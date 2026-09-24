using System;
using System.Linq;
using UnityEngine;

public static class OneTimePowerupChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>();
        var inventory = InventoryManager.Instance;
        Check(panel && inventory, "Workbench or inventory is missing.");
        var previous = inventory.GetSnapshot().ToArray();
        var axe = panel.recipes.Single(recipe => recipe.output.item == Item.Axe);
        var scythe = panel.recipes.Single(recipe => recipe.output.item == Item.Scythe);
        var trees = UnityEngine.Object.FindFirstObjectByType<SurfaceTrees>();
        var grass = UnityEngine.Object.FindFirstObjectByType<SurfaceTallGrass>();
        Check(trees && grass, "Surface powerup consumers are missing.");
        try
        {
            inventory.ResetAll();
            panel.SetSearch("");
            panel.SetCategory(CraftingRecipe.RecipeCategory.Automatic);
            panel.SetFilter(false);
            panel.SetFavoritesOnly(false);
            panel.ShowPanel(true);
            Check(panel.VisibleRecipeCount == 16, "Both powerups must appear before crafting.");
            panel.SelectRecipe(panel.recipes.Single(recipe => recipe.output.item == Item.Rope));
            Check(panel.transform.Find("Layout/Details/QuantityControls").gameObject.activeSelf,
                "Ordinary recipes must retain quantity controls.");

            foreach (var recipe in new[] { axe, scythe })
            {
                Check(recipe.TryGetCosts(out var costs), "Invalid powerup recipe.");
                foreach (var cost in costs) inventory.Add(cost.Key, cost.Value * 2);
                panel.SelectRecipe(recipe);
                Check(!panel.transform.Find("Layout/Details/QuantityControls").gameObject.activeSelf,
                    "Powerups must not show quantity controls.");
                Check(CraftingService.GetMaxCraftable(recipe, inventory) == 1, "Powerup must allow one batch at most.");
                panel.SetQuantity(2);
                Check(panel.Quantity == 1, "Powerup quantity must stay at one.");
                int before = panel.VisibleRecipeCount;
                Check(panel.CraftSelected(), "First powerup craft failed.");
                Check(inventory.GetCount(recipe.output) == 1, "Powerup output must be one.");
                Check(panel.VisibleRecipeCount == before - 1, "Crafted powerup must disappear immediately.");
                Check(!panel.transform.Find("Layout/Recipes/Content/" + recipe.name).gameObject.activeSelf,
                    "Crafted powerup card is still visible.");
                Check(CraftingService.GetMaxCraftable(recipe, inventory) == 0 &&
                    !CraftingService.TryCraft(recipe, inventory, 1), "Second powerup craft was allowed.");
                Check(inventory.TryRemove(recipe.output) && inventory.GetCount(recipe.output) == 0,
                    "Powerup test could not clear the inventory count.");
                Check(inventory.IsPowerupUnlocked(recipe.output) &&
                    (recipe == axe ? trees.HasAxe : grass.HasScythe),
                    "Powerup must remain active after its count is removed.");
                Check(panel.VisibleRecipeCount == before - 1 &&
                    !CraftingService.TryCraft(recipe, inventory, 1),
                    "Permanent unlock must keep the recipe hidden and prevent recrafting.");
                panel.SelectRecipe(recipe);
                Check(panel.SelectedRecipe != recipe, "Hidden recipe remained selectable.");
            }
            Check(panel.VisibleRecipeCount == 14, "Both crafted powerups must be hidden.");
            return "PASS: powerup quantity controls hidden, one craft each, permanent run unlock, hidden cards, and no recrafting.";
        }
        finally
        {
            panel.ShowPanel(false);
            inventory.ResetAll();
            foreach (var entry in previous) inventory.Add(entry.Key, entry.Value);
        }
    }
}
