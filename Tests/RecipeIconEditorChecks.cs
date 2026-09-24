using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class RecipeIconEditorChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static object Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var debug = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>(FindObjectsInactive.Include);
        Check(workbench && debug, "Workbench or debug panel is missing.");
        var recipe = workbench.recipes.FirstOrDefault(candidate => candidate && candidate.output &&
            candidate.output.item == Item.Torche);
        Check(recipe, "Torch recipe is missing.");
        bool wasOpen = GameplayDebugPanel.IsOpen;
        bool hadCustomLayout = new SerializedObject(recipe).FindProperty("customCardIconLayout").boolValue;
        var original = recipe.CardIconLayout;
        int previousSelection = 0;
        try
        {
            if (!wasOpen) debug.Toggle();
            var window = debug.GetComponent<GameplayDebugWindow>();
            window.SwitchTab("Icons");
            var content = debug.transform.Find("Card/WindowViewport/WindowContent");
            var dropdown = content.Find("IconRecipeDropdown").GetComponent<TMP_Dropdown>();
            Check(dropdown.gameObject.activeInHierarchy && dropdown.options.Count == workbench.recipes.Length,
                "Icon tab or recipe list is incomplete.");
            previousSelection = dropdown.value;
            int index = dropdown.options.FindIndex(option => option.text.EndsWith(" · " + recipe.output.displayName));
            Check(index >= 0, "Torch is missing from the icon editor.");
            dropdown.value = index;

            var inputs = Enumerable.Range(0, 4).Select(i => content.Find("IconInput" + i)
                .GetComponent<TMP_InputField>()).ToArray();
            inputs[0].text = "1.25";
            inputs[1].text = "1.5";
            inputs[2].text = "10";
            inputs[3].text = "-7";
            var flipX = content.Find("IconFlipX").GetComponent<Toggle>();
            var flipY = content.Find("IconFlipY").GetComponent<Toggle>();
            flipX.isOn = false;
            flipY.isOn = true;
            Check(window.CommitIconInputs(), "Icon values could not be saved.");

            var actual = recipe.CardIconLayout;
            Check(actual.scale == new Vector2(1.25f, 1.5f) && actual.offset == new Vector2(10f, -7f) &&
                !actual.flipX && actual.flipY, "Recipe did not receive the edited values.");
            var preview = (RectTransform)content.Find("IconPreviewCard/Icon");
            var card = workbench.transform.Find("Layout/Recipes/Content/" + recipe.name + "/Icon") as RectTransform;
            Check(card && preview.anchoredPosition == new Vector2(97f, -72f) &&
                card.anchoredPosition == preview.anchoredPosition &&
                card.localScale == preview.localScale && card.localScale == new Vector3(1.25f, -1.5f, 1f),
                "Preview and existing recipe card did not update together.");
            string path = AssetDatabase.GetAssetPath(recipe);
            Check(File.ReadAllText(path).Contains("customCardIconLayout: 1"), "Icon settings were not written to the recipe asset.");

            content.Find("IconReset").GetComponent<Button>().onClick.Invoke();
            Check(!new SerializedObject(recipe).FindProperty("customCardIconLayout").boolValue,
                "Reset did not restore the recipe default.");
            Check(recipe.CardIconLayout.flipX && card.localScale.x == -1f,
                "Torch's original mirrored appearance was not restored.");
            return "PASS: icon tab, live preview/card update, X/Y scale and offset, flips, asset persistence and reset.";
        }
        finally
        {
            if (hadCustomLayout) recipe.SetCardIconLayout(original);
            else recipe.ResetCardIconLayout();
            recipe.SaveCardIconLayout();
            workbench.RefreshRecipeIcons();
            if (GameplayDebugPanel.IsOpen)
            {
                var dropdown = debug.transform.Find("Card/WindowViewport/WindowContent/IconRecipeDropdown")
                    .GetComponent<TMP_Dropdown>();
                dropdown.value = previousSelection;
                if (!wasOpen) debug.Close();
            }
        }
    }
}
