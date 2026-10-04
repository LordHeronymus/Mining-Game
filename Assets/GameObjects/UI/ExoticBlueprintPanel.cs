using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ExoticBlueprintPanel : MonoBehaviour
{
    ExoticWorldContent owner;
    RectTransform layout;
    float previousScale;
    bool pauseHeld;
    public static bool IsOpen => FindFirstObjectByType<ExoticBlueprintPanel>() != null;
    public static bool Show(ExoticWorldContent world)
    {
        if (!Application.isPlaying || !world || IsOpen || GameplayInputBlocker.IsBlocked) return false;
        var canvas = FindFirstObjectByType<CompactHud>()?.GetComponentInParent<Canvas>();
        if (!canvas) return false;
        var root = HomeUi.Panel("Exotic Blueprint Choice", canvas.transform);
        root.SetAsLastSibling();
        var panel = root.gameObject.AddComponent<ExoticBlueprintPanel>();
        panel.owner = world; panel.Build(); return true;
    }
    void Build()
    {
        GameplayInputBlocker.SetBlocked(this, true);
        previousScale = Time.timeScale; Time.timeScale = 0; pauseHeld = true;
        var shade = HomeUi.Image("Shade", transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(shade.rectTransform); shade.color = new Color(0, 0, 0, .8f); shade.raycastTarget = true;
        layout = HomeUi.Rect("Blueprint Layout", transform, Vector2.zero, new Vector2(1180, 690));
        HomeUi.Image("Board", layout, Vector2.zero, new Vector2(1160, 670), "Panel");
        HomeUi.Label("Title", layout, "Exotischer Bauplan", new Vector2(0, 247), new Vector2(920, 70), 46);
        var choices = owner.PendingChoices;
        for (int i = 0; i < choices.Length; i++)
        {
            var recipe = choices[i]; if (!recipe) continue;
            float x = (i - (choices.Length - 1) * .5f) * 340;
            var card = HomeUi.Image("Blueprint " + recipe.exoticId, layout, new Vector2(x, -8), new Vector2(306, 366));
            card.color = ExoticDesign.Navy;
            for (int line = -2; line <= 2; line++)
            {
                var grid = HomeUi.Image("Diagram", card.transform, new Vector2(line * 48, 38), new Vector2(1, 190));
                grid.color = new Color(.38f, .88f, .89f, .12f);
                grid = HomeUi.Image("Diagram", card.transform, new Vector2(0, 38 + line * 38), new Vector2(250, 1));
                grid.color = new Color(.38f, .88f, .89f, .12f);
            }
            ExoticDesign.AddSeal(card.transform, new Vector2(-120, 151), 27);
            var tag = HomeUi.Label("Exotic", card.transform, "EXOTISCH", new Vector2(12, 150), new Vector2(205, 33), 21);
            tag.color = ExoticDesign.Cyan;
            var icon = HomeUi.Image("Item", card.transform, new Vector2(0, 39), new Vector2(184, 174));
            icon.sprite = recipe.output.icon; icon.preserveAspect = true;
            HomeUi.Label("Name", card.transform, recipe.output.displayName, new Vector2(0, -89), new Vector2(276, 54), 29);
            HomeUi.Button("Learn " + recipe.exoticId, card.transform, "Lernen", new Vector2(0, -145), new Vector2(264, 55), () => Choose(recipe));
        }
        HomeUi.Fit(layout, new Vector2(1280, 760));
    }
    void Choose(CraftingRecipe recipe)
    {
        if (owner && owner.LearnPending(recipe)) Destroy(gameObject);
    }
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(1280, 760));
    void OnDestroy()
    {
        GameplayInputBlocker.SetBlocked(this, false);
        if (pauseHeld && !RunNavigation.IsTransitioning) Time.timeScale = previousScale;
        EventSystem.current?.SetSelectedGameObject(null);
    }
}
