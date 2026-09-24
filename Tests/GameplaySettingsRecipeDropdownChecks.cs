using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GameplaySettingsRecipeDropdownChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static object Main()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        CraftingRecipe invalid = null;
        try
        {
            var assets = AssetDatabase.FindAssets("t:CraftingRecipe")
                .Select(guid => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToArray();
            Check(assets.Length > 0, "No recipe assets found.");

            invalid = UnityEngine.Object.Instantiate(assets[0]);
            invalid.name = "Invalid test recipe";
            invalid.ingredients = Array.Empty<CraftingIngredient>();

            var sorted = GameplaySettingsWindow.SortCraftableRecipes(assets.Concat(new[] { invalid }));
            Check(sorted.Length == assets.Length, "Invalid recipes must be excluded.");
            Check(sorted.All(recipe => recipe.TryGetCosts(out _)), "Dropdown contains an invalid recipe.");

            var expected = sorted
                .OrderBy(recipe => recipe.Category switch
                {
                    CraftingRecipe.RecipeCategory.Building => 0,
                    CraftingRecipe.RecipeCategory.Materials => 1,
                    CraftingRecipe.RecipeCategory.Tools => 2,
                    _ => 3
                })
                .ThenBy(recipe => recipe.output.displayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(recipe => recipe.name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            Check(sorted.SequenceEqual(expected), "Recipes are not grouped by category and sorted by name.");

            var labels = GameplaySettingsWindow.GetRecipeLabels(sorted);
            Check(labels.Length == sorted.Length && labels.All(label => label.Contains(" · ")),
                "Each dropdown label must show its category and item name.");
            return $"PASS: {sorted.Length} valid recipes, invalid recipe excluded, category/name ordering and labels verified.";
        }
        finally
        {
            if (invalid) UnityEngine.Object.DestroyImmediate(invalid);
        }
    }
}
