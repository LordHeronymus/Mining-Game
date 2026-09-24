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
    [Serializable] sealed class SavedRecipeSettings { public int version = 1; public int category, outputAmount; public int[] ingredientItems, ingredientAmounts; }
    [Serializable]
    public struct RecipeIconLayout
    {
        public Vector2 scale;
        public Vector2 offset;
        public bool flipX;
        public bool flipY;
    }

    public enum RecipeCategory { Automatic, Tools, Building, Materials }
    public RecipeCategory category;
    [SerializeField, HideInInspector] string persistentId;
    [SerializeField, HideInInspector] bool customCardIconLayout;
    [SerializeField, HideInInspector] RecipeIconLayout cardIconLayout;
    bool runtimeCardIconLayout;
    public string FavoriteKey => "workbench.favorite." + (string.IsNullOrEmpty(persistentId)
        ? (output ? ((int)output.item).ToString() : "none") + "." + name : persistentId);
    public RecipeCategory Category => category != RecipeCategory.Automatic ? category :
        output && (output.item == Item.Ladder || output.item == Item.BridgePart) ? RecipeCategory.Building :
        output && (output.category == ItemCategory.Tool || output.category == ItemCategory.Consumable)
            ? RecipeCategory.Tools : RecipeCategory.Materials;
    string SavedSettingsKey => "workbench.recipe." + (string.IsNullOrEmpty(persistentId)
        ? (output ? ((int)output.item).ToString() : name) : persistentId);

    public bool SetRecipeSettings(RecipeCategory nextCategory, int nextOutputAmount, CraftingIngredient[] nextIngredients)
    {
        if (!Enum.IsDefined(typeof(RecipeCategory), nextCategory) || nextOutputAmount <= 0 || nextIngredients == null || nextIngredients.Length == 0) return false;
        var previousCategory = category;
        int previousOutputAmount = outputAmount;
        var previousIngredients = ingredients;
        category = nextCategory;
        outputAmount = nextOutputAmount;
        ingredients = (CraftingIngredient[])nextIngredients.Clone();
        if (TryGetCosts(out _)) return true;
        category = previousCategory; outputAmount = previousOutputAmount; ingredients = previousIngredients;
        return false;
    }

    public void SaveRecipeSettings()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
        var data = new SavedRecipeSettings {
            category = (int)category,
            outputAmount = outputAmount,
            ingredientItems = Array.ConvertAll(ingredients, ingredient => (int)ingredient.item.item),
            ingredientAmounts = Array.ConvertAll(ingredients, ingredient => ingredient.amount)
        };
        PlayerPrefs.SetString(SavedSettingsKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
#endif
    }

#if !UNITY_EDITOR
    void LoadSavedRecipeSettings()
    {
        string key = SavedSettingsKey;
        if (!PlayerPrefs.HasKey(key)) return;
        var data = JsonUtility.FromJson<SavedRecipeSettings>(PlayerPrefs.GetString(key));
        if (data == null || data.version != 1 || data.outputAmount <= 0 || data.ingredientItems == null ||
            data.ingredientAmounts == null || data.ingredientItems.Length == 0 ||
            data.ingredientItems.Length != data.ingredientAmounts.Length || !Enum.IsDefined(typeof(RecipeCategory), data.category)) return;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!catalog) return;
        var loadedIngredients = new CraftingIngredient[data.ingredientItems.Length];
        for (int i = 0; i < loadedIngredients.Length; i++)
        {
            ItemSO item = Array.Find(catalog.items, candidate => candidate && (int)candidate.item == data.ingredientItems[i]);
            if (!item || item == output || data.ingredientAmounts[i] <= 0) return;
            loadedIngredients[i] = new CraftingIngredient(item, data.ingredientAmounts[i]);
        }
        SetRecipeSettings((RecipeCategory)data.category, data.outputAmount, loadedIngredients);
    }
#endif

    void OnEnable()
    {
#if !UNITY_EDITOR
        if (Application.isPlaying) LoadSavedRecipeSettings();
#endif
    }

    public RecipeIconLayout CardIconLayout
    {
        get
        {
            if (runtimeCardIconLayout) return cardIconLayout;
#if !UNITY_EDITOR
            string key = FavoriteKey + ".icon";
            if (PlayerPrefs.HasKey(key))
            {
                var saved = JsonUtility.FromJson<RecipeIconLayout>(PlayerPrefs.GetString(key));
                if (saved.scale.x > 0f && saved.scale.y > 0f) return saved;
            }
#endif
            return customCardIconLayout ? cardIconLayout : DefaultCardIconLayout(output ? output.item : default);
        }
    }

    public void SetCardIconLayout(RecipeIconLayout layout)
    {
        cardIconLayout = layout;
        customCardIconLayout = true;
        runtimeCardIconLayout = true;
    }

    public void ResetCardIconLayout()
    {
        customCardIconLayout = false;
        cardIconLayout = default;
        runtimeCardIconLayout = false;
#if !UNITY_EDITOR
        PlayerPrefs.DeleteKey(FavoriteKey + ".icon");
#endif
    }

    public void SaveCardIconLayout()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
        string key = FavoriteKey + ".icon";
        if (customCardIconLayout) PlayerPrefs.SetString(key, JsonUtility.ToJson(cardIconLayout));
        else PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
#endif
    }

    static RecipeIconLayout DefaultCardIconLayout(Item item)
    {
        var layout = new RecipeIconLayout { scale = Vector2.one };
        Vector2 opaqueOffset;
        switch (item)
        {
            case Item.Torche: opaqueOffset = new Vector2(5.34f, 3.21f); break;
            case Item.Dynamite: opaqueOffset = new Vector2(4.42f, -2.27f); break;
            case Item.Ladder: opaqueOffset = new Vector2(12.34f, -1.74f); break;
            case Item.CopperPickaxe: opaqueOffset = new Vector2(-1.70f, 9.53f); break;
            default: return layout;
        }
        layout.offset = new Vector2(4f + opaqueOffset.x, -opaqueOffset.y);
        layout.flipX = true;
        return layout;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(path)) return;
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
        if (persistentId == guid) return;
        persistentId = guid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

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
