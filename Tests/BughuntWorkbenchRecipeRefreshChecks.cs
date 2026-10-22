using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Bug hunt 22.10.2026: recipe ingredients changed in F1/GPS while the workbench was closed must be
// shown with the new items and amounts when the workbench opens again (rows used to stay cached).
public static class BughuntWorkbenchRecipeRefreshChecks
{
    const string ReportPath = "Temp/BughuntWorkbenchRecipeRefreshChecks.txt";
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);

    static ItemSO Probe(Item item, ItemCategory category, string displayName)
    {
        var so = ScriptableObject.CreateInstance<ItemSO>();
        so.hideFlags = HideFlags.HideAndDontSave; so.name = "Probe " + displayName;
        so.item = item; so.category = category; so.displayName = displayName;
        return so;
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var created = new List<Object>();
        GameObject root = null, trash = null;
        WorkbenchPanel panel = null;
        try
        {
            var output = Probe(Item.Torche, ItemCategory.Consumable, "Probe-Fackel");
            var coal = Probe(Item.Coal, ItemCategory.Ore, "Probe-Kohle");
            var copper = Probe(Item.Copper, ItemCategory.Ore, "Probe-Kupfer");
            var iron = Probe(Item.Iron, ItemCategory.Ore, "Probe-Eisen");
            var recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.hideFlags = HideFlags.HideAndDontSave; recipe.name = "Probe Recipe";
            recipe.output = output; recipe.outputAmount = 1;
            recipe.ingredients = new[] { new CraftingIngredient(coal, 2) };
            created.AddRange(new Object[] { output, coal, copper, iron, recipe });

            root = new GameObject("Bughunt Workbench Probe", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            trash = new GameObject("Bughunt Workbench Trash") { hideFlags = HideFlags.HideAndDontSave };
            panel = root.AddComponent<WorkbenchPanel>();
            panel.recipes = new[] { recipe };
            // Edit Mode does not run Awake for runtime scripts; build the panel like Play Mode would.
            typeof(WorkbenchPanel).GetMethod("Awake", Instance).Invoke(panel, null);
            var content = Field<RectTransform>(panel, "ingredientContent");
            var rows = Field<IList>(panel, "ingredientRows");
            // Destroy() is unavailable in Edit Mode: move old rows away first, as Play Mode would destroy them.
            void Detach() { while (content.childCount > 0) content.GetChild(0).SetParent(trash.transform, false); }
            string Rows() => string.Join(", ", rows.Cast<object>().Select(row =>
                Field<ItemSO>(row, "item").displayName + " " + Field<TextMeshProUGUI>(row, "count").text));
            bool Shows(ItemSO item, int amount) => rows.Cast<object>().Any(row => Field<ItemSO>(row, "item") == item &&
                Field<TextMeshProUGUI>(row, "count").text.EndsWith(" / " + amount));

            Detach(); panel.ShowPanel(true);
            Detach(); panel.SelectRecipe(recipe);
            Check(panel.IsOpen && panel.SelectedRecipe == recipe, "Werkbank offen, Probe-Rezept gewählt");
            Check(rows.Count == 1 && Shows(coal, 2) && content.childCount == 1, "Zutaten vor der Änderung: 2 Kohle (" + Rows() + ")");

            panel.ShowPanel(false);
            Check(recipe.SetRecipeSettings(recipe.category, 1, new[] { new CraftingIngredient(copper, 5), new CraftingIngredient(iron, 3) }),
                "Rezept bei geschlossener Werkbank geändert (wie F1/GPS)");
            Detach(); panel.ShowPanel(true);
            Check(panel.SelectedRecipe == recipe, "Auswahl bleibt nach dem erneuten Öffnen erhalten");
            Check(rows.Count == 2 && Shows(copper, 5) && Shows(iron, 3) && !Shows(coal, 2),
                "Neu geöffnete Werkbank zeigt die neuen Zutaten und Mengen (" + Rows() + ")");
            Check(content.childCount == 2, "Zutatenzeilen wurden neu aufgebaut");

            panel.ShowPanel(false);
            Detach(); panel.ShowPanel(true);
            Check(rows.Count == 2 && Shows(copper, 5) && Shows(iron, 3), "Erneutes Öffnen ohne Änderung bleibt korrekt");

            string report = string.Join("\n", checks);
            File.WriteAllText(ReportPath, report); return checks[checks.Count - 1];
        }
        catch (Exception ex)
        {
            var error = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;
            File.WriteAllText(ReportPath, "FAIL " + error.Message + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            if (panel)
            {
                if (panel.IsOpen) panel.ShowPanel(false);
                GameplayInputBlocker.SetBlocked(panel, false);
                foreach (var sprite in Field<List<Sprite>>(panel, "styleSprites")) if (sprite) Object.DestroyImmediate(sprite);
                var fontMaterial = Field<Material>(panel, "ownedFontMaterial"); if (fontMaterial) Object.DestroyImmediate(fontMaterial);
                var glowMaterial = Field<Material>(panel, "glowMaterial"); if (glowMaterial) Object.DestroyImmediate(glowMaterial);
            }
            if (root) Object.DestroyImmediate(root);
            if (trash) Object.DestroyImmediate(trash);
            foreach (var obj in created) if (obj) Object.DestroyImmediate(obj);
        }
    }
}
