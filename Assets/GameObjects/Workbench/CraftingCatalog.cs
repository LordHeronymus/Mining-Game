using UnityEngine;

public sealed class CraftingCatalog : ScriptableObject
{
    public CraftingRecipe[] recipes = System.Array.Empty<CraftingRecipe>();
}
