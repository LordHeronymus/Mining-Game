using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WorkbenchChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static object Main()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        var go = new GameObject("Crafting checks");
        go.SetActive(false);
        var inventory = go.AddComponent<InventoryManager>();
        CraftingRecipe duplicate = null;
        try
        {
            var recipes = AssetDatabase.FindAssets("t:CraftingRecipe", new[] { "Assets/GameObjects/Workbench/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Check(recipes.Length == 16, "Expected original, pickaxe, scythe, and axe recipes.");
            var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
            Check(panel && panel.recipes.Length == 16, "Workbench panel is missing recipes.");
            Check(recipes.All(recipe => panel.recipes.Contains(recipe)), "Workbench panel does not list every recipe.");
            var pickaxes = recipes.Where(recipe => recipe.name.EndsWith("Pickaxe")).ToArray();
            Check(pickaxes.Length == 8 && pickaxes.All(recipe => recipe.output && recipe.output.icon &&
                recipe.output.category == ItemCategory.Tool), "Pickaxe items or icons are incomplete.");
            Check(pickaxes.Select(recipe => recipe.output.item).Distinct().Count() == 8,
                "Pickaxe item IDs must be unique.");
            var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
            Check(catalog && pickaxes.All(recipe => catalog.items.Contains(recipe.output)),
                "Pickaxes are missing from the item catalog.");
            var axe = recipes.Single(recipe => recipe.output.item == Item.Axe);
            Check(axe.output.category == ItemCategory.Powerup && axe.output.icon &&
                axe.Category == CraftingRecipe.RecipeCategory.Tools && catalog.items.Contains(axe.output),
                "Axe powerup is not available in the workshop and catalog.");
            foreach (var recipe in recipes)
            {
                bool powerup = recipe.output.category == ItemCategory.Powerup;
                int batches = powerup ? 1 : 2;
                inventory.ResetAll();
                Check(recipe.TryGetCosts(out var costs), "Invalid recipe: " + recipe.name);
                Check(!CraftingService.TryCraft(recipe, inventory, 1), "Crafted without materials.");
                Check(!CraftingService.TryCraft(recipe, inventory, 0), "Accepted zero quantity.");
                foreach (var cost in costs) inventory.Add(cost.Key, cost.Value * 2);
                Check(CraftingService.GetMaxCraftable(recipe, inventory) == batches, "Wrong maximum: " + recipe.name);
                int notifications = 0;
                Action listener = () => {
                    notifications++;
                    Check(costs.All(c => inventory.GetCount(c.Key) == c.Value * (2 - batches)), "Listener saw partial ingredient consumption.");
                    Check(inventory.GetCount(recipe.output) == recipe.outputAmount * batches, "Listener saw missing output.");
                };
                inventory.OnInventoryChanged += listener;
                Check(CraftingService.TryCraft(recipe, inventory, batches), "Craft failed: " + recipe.name);
                inventory.OnInventoryChanged -= listener;
                Check(notifications == 1, "Expected one atomic inventory notification.");
                Check(!CraftingService.TryCraft(recipe, inventory, 1), "Spent ingredients twice.");
                Check(inventory.GetCount(recipe.output) == recipe.outputAmount * batches, "Failed craft modified output.");

                inventory.ResetAll();
                foreach (var cost in costs) inventory.Add(cost.Key, cost.Value * 2);
                inventory.Add(recipe.output, int.MaxValue);
                Check(CraftingService.GetMaxCraftable(recipe, inventory) == 0, "Output overflow not prevented.");
                Check(!CraftingService.TryCraft(recipe, inventory, 1), "Overflow craft accepted.");
                Check(costs.All(c => inventory.GetCount(c.Key) == c.Value * 2), "Failed craft consumed materials.");
            }
            inventory.ResetAll();
            var source = recipes.First(r => r.name == "Nails");
            duplicate = UnityEngine.Object.Instantiate(source);
            var ingredient = source.ingredients[0].item;
            duplicate.ingredients = new[] { new CraftingIngredient(ingredient, 2), new CraftingIngredient(ingredient, 3) };
            inventory.Add(ingredient, 9);
            Check(CraftingService.GetMaxCraftable(duplicate, inventory) == 1, "Duplicate ingredient rows not aggregated.");
            Check(CraftingService.TryCraft(duplicate, inventory, 1), "Duplicate ingredient recipe failed.");
            Check(inventory.GetCount(ingredient) == 4, "Wrong aggregated consumption.");
            return "PASS: sixteen recipes including the axe powerup, materials, batches, atomic inventory updates, and overflow.";
        }
        finally
        {
            if (duplicate) UnityEngine.Object.DestroyImmediate(duplicate);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
