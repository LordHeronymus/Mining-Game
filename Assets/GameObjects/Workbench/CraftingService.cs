using System.Collections.Generic;
using UnityEngine;

public static class CraftingService
{
    public static bool IsOwnedPowerup(CraftingRecipe recipe, InventoryManager inventory) =>
        recipe && recipe.output && recipe.output.category == ItemCategory.Powerup &&
        inventory && inventory.IsPowerupUnlocked(recipe.output);

    public static int GetMaxCraftable(CraftingRecipe recipe, InventoryManager inventory)
    {
        if (!recipe || !inventory || !RecipeUnlocks.IsUnlocked(recipe) || IsOwnedPowerup(recipe, inventory) ||
            !recipe.TryGetCosts(out var costs)) return 0;
        int maximum = (int.MaxValue - inventory.GetCount(recipe.output)) / recipe.outputAmount;
        if (recipe.output.category == ItemCategory.Powerup) maximum = Mathf.Min(maximum, 1);
        foreach (var cost in costs)
            maximum = Mathf.Min(maximum, inventory.GetCount(cost.Key) / cost.Value);
        double weightPerBatch = recipe.output.EffectiveWeight * (double)recipe.outputAmount;
        foreach (var cost in costs)
            weightPerBatch -= cost.Key.EffectiveWeight * (double)cost.Value;
        if (weightPerBatch > 0d && !GameplayTestSettings.NoWeight)
            maximum = (int)System.Math.Min(maximum, System.Math.Max(0d,
                System.Math.Floor((inventory.MaximumWeight - inventory.TotalWeight + 0.00001d) / weightPerBatch)));
        return Mathf.Max(0, maximum);
    }

    public static bool TryCraft(CraftingRecipe recipe, InventoryManager inventory, int batches)
    {
        if (batches <= 0 || batches > GetMaxCraftable(recipe, inventory)) return false;
        if (!recipe.TryGetCosts(out var perBatch)) return false;
        var costs = new Dictionary<ItemSO, int>();
        foreach (var cost in perBatch) costs.Add(cost.Key, checked(cost.Value * batches));
        return inventory.TryExchange(costs, recipe.output, checked(recipe.outputAmount * batches));
    }
}
