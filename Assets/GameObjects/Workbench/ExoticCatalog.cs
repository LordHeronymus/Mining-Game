using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class ExoticCatalog
{
    public static IReadOnlyList<CraftingRecipe> AllRecipes => Resources.Load<CraftingCatalog>("CraftingCatalog")?.recipes ?? System.Array.Empty<CraftingRecipe>();
    static List<CraftingRecipe> recipes;
    public static IReadOnlyList<CraftingRecipe> Recipes
    {
        get
        {
            if (recipes == null)
            {
                recipes = new List<CraftingRecipe>();
                foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes"))
                    if (recipe && recipe.exotic && recipe.output) recipes.Add(recipe);
                recipes.Sort((a, b) => a.metaUnlockLevel.CompareTo(b.metaUnlockLevel));
            }
            return recipes;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache() => recipes = null;
    public static bool IsAvailable(CraftingRecipe recipe) => recipe && (!recipe.exotic || MetaProgression.Level >= recipe.metaUnlockLevel);
    public static CraftingRecipe Find(string id)
    {
        foreach (var recipe in Recipes) if (recipe.exoticId == id) return recipe;
        return null;
    }
    public static CraftingRecipe[] Offer(string siteId)
    {
        var pool = new List<CraftingRecipe>();
        foreach (var recipe in Recipes)
            if (IsAvailable(recipe) && !RecipeUnlocks.IsUnlocked(recipe)) pool.Add(recipe);
        // Stable choices survive saving and reopening. Never consume global Unity randomness.
        uint hash = 2166136261;
        foreach (char character in siteId ?? "") hash = (hash ^ character) * 16777619;
        var random = new System.Random(unchecked((int)hash));
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        // One locally affordable recipe biases the offer without excluding unfamiliar choices.
        var inventory = InventoryManager.Instance;
        if (pool.Count > 3 && inventory)
        {
            int best = 0, bestScore = -1;
            for (int i = 0; i < pool.Count; i++)
            {
                int score = 0;
                foreach (var cost in pool[i].ingredients)
                    if (cost.item && inventory.GetCount(cost.item) >= cost.amount) score++;
                if (score > bestScore) { best = i; bestScore = score; }
            }
            (pool[0], pool[best]) = (pool[best], pool[0]);
        }
        if (pool.Count > 3) pool.RemoveRange(3, pool.Count - 3);
        return pool.ToArray();
    }
}

public static class ExoticDesign
{
    public static readonly Color Cyan = new Color32(98, 225, 227, 255);
    public static readonly Color Navy = new Color32(13, 34, 48, 255);
    static Sprite crystal;
    public static Sprite Crystal
    {
        get
        {
            if (crystal) return crystal;
            var texture = Resources.Load<Texture2D>("Exotics/ExoticCrystal");
            if (!texture) return null;
            crystal = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            crystal.name = "Exotic Crystal V1";
            return crystal;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCrystal()
    {
        if (crystal) Object.Destroy(crystal);
        crystal = null;
    }
    public static void AddSeal(Transform parent, Vector2 position, float size)
    {
        var rect = HomeUi.Rect("Exotic Seal", parent, position, Vector2.one * size);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var seal = rect.gameObject.AddComponent<ExoticSealGraphic>();
        seal.raycastTarget = false; seal.color = Color.white;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ExoticSealGraphic : Image
{
    protected override void Awake()
    {
        base.Awake();
        sprite = ExoticDesign.Crystal;
        preserveAspect = true;
        raycastTarget = false;
    }
}
