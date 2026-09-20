using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct CraftingIngredient
{
    public ItemSO item;
    [Min(1)] public int amount;
    public CraftingIngredient(ItemSO item, int amount) { this.item = item; this.amount = amount; }
}

[CreateAssetMenu(menuName = "Crafting/Recipe")]
public sealed class CraftingRecipe : ScriptableObject
{
    public ItemSO output;
    [Min(1)] public int outputAmount = 1;
    public string pluralName;
    public CraftingIngredient[] ingredients = Array.Empty<CraftingIngredient>();

    public string ResultName(long count) => count == 1 || string.IsNullOrEmpty(pluralName)
        ? (output ? output.displayName : "") : pluralName;

    public bool TryGetCosts(out Dictionary<ItemSO, int> costs)
    {
        costs = new Dictionary<ItemSO, int>();
        if (!output || outputAmount <= 0 || ingredients == null || ingredients.Length == 0) return false;
        foreach (var ingredient in ingredients)
        {
            if (!ingredient.item || ingredient.item == output || ingredient.amount <= 0) return false;
            costs.TryGetValue(ingredient.item, out int previous);
            long total = (long)previous + ingredient.amount;
            if (total > int.MaxValue) return false;
            costs[ingredient.item] = (int)total;
        }
        return true;
    }
}
