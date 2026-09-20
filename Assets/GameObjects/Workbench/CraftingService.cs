using System.Collections.Generic;
using UnityEngine;

public static class CraftingService
{
    public static int GetMaxCraftable(CraftingRecipe recipe, InventoryManager inventory)
    {
        if (!recipe || !inventory || !recipe.TryGetCosts(out var costs)) return 0;
        int maximum = (int.MaxValue - inventory.GetCount(recipe.output)) / recipe.outputAmount;
        foreach (var cost in costs)
            maximum = Mathf.Min(maximum, inventory.GetCount(cost.Key) / cost.Value);
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
