using System.Collections.Generic;
using UnityEngine;

public static class RecipeUnlocks
{
    const string MedkitKey = "workbench.recipe.medkit.unlocked";
    const string CopperPickaxeKey = "workbench.recipe.copper-pickaxe.unlocked";
    const string IronPickaxeKey = "workbench.recipe.iron-pickaxe.unlocked";
    const string SteelPickaxeKey = "workbench.recipe.steel-pickaxe.unlocked";
    const string TitaniumPickaxeKey = "workbench.recipe.titanium-pickaxe.unlocked";
    const string TungstenPickaxeKey = "workbench.recipe.tungsten-pickaxe.unlocked";
    const string ObsidianPickaxeKey = "workbench.recipe.obsidian-pickaxe.unlocked";
    const string MythrilPickaxeKey = "workbench.recipe.mythril-pickaxe.unlocked";
    const string DiamondPickaxeKey = "workbench.recipe.diamond-pickaxe.unlocked";
    public static bool IsShopRecipe(CraftingRecipe recipe) => recipe && recipe.output && recipe.output.item switch
    {
        Item.Medkit or Item.IronPickaxe or Item.SteelPickaxe or Item.TitaniumPickaxe or
            Item.TungstenPickaxe or Item.ObsidianPickaxe or Item.MythrilPickaxe or Item.DiamondPickaxe => true,
        _ => false
    };

    public static bool IsUnlocked(CraftingRecipe recipe) =>
        !recipe || !recipe.output || recipe.output.item switch
        {
            Item.Medkit => IsMedkitUnlocked,
            Item.IronPickaxe => IsIronPickaxeUnlocked,
            Item.SteelPickaxe => IsSteelPickaxeUnlocked,
            Item.TitaniumPickaxe => IsTitaniumPickaxeUnlocked,
            Item.TungstenPickaxe => IsTungstenPickaxeUnlocked,
            Item.ObsidianPickaxe => IsObsidianPickaxeUnlocked,
            Item.MythrilPickaxe => IsMythrilPickaxeUnlocked,
            Item.DiamondPickaxe => IsDiamondPickaxeUnlocked,
            Item.Steel => IsRequiredByUnlockedRecipe(recipe.output),
            _ => true
        };

    static bool IsRequiredByUnlockedRecipe(ItemSO item)
    {
        if (!item) return false;
        var recipes = new HashSet<CraftingRecipe>();
        foreach (var panel in Object.FindObjectsByType<WorkbenchPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (panel && panel.recipes != null)
                foreach (var recipe in panel.recipes)
                    if (recipe) recipes.Add(recipe);
        foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes"))
            if (recipe) recipes.Add(recipe);

        foreach (var candidate in recipes)
        {
            if (!candidate || !candidate.output || candidate.output == item || candidate.ingredients == null) continue;
            bool consumesItem = false;
            foreach (var ingredient in candidate.ingredients)
                if (ingredient.item == item) { consumesItem = true; break; }
            if (consumesItem && IsUnlocked(candidate)) return true;
        }
        return false;
    }

    public static bool IsMedkitUnlocked => PlayerPrefs.GetInt(MedkitKey, 0) == 1;
    public static bool IsIronPickaxeUnlocked => PlayerPrefs.GetInt(IronPickaxeKey, 0) == 1;
    public static bool IsSteelPickaxeUnlocked => PlayerPrefs.GetInt(SteelPickaxeKey, 0) == 1;
    public static bool IsTitaniumPickaxeUnlocked => PlayerPrefs.GetInt(TitaniumPickaxeKey, 0) == 1;
    public static bool IsTungstenPickaxeUnlocked => PlayerPrefs.GetInt(TungstenPickaxeKey, 0) == 1;
    public static bool IsObsidianPickaxeUnlocked => PlayerPrefs.GetInt(ObsidianPickaxeKey, 0) == 1;
    public static bool IsMythrilPickaxeUnlocked => PlayerPrefs.GetInt(MythrilPickaxeKey, 0) == 1;
    public static bool IsDiamondPickaxeUnlocked => PlayerPrefs.GetInt(DiamondPickaxeKey, 0) == 1;

    public static void UnlockMedkit()
    {
        PlayerPrefs.SetInt(MedkitKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockIronPickaxe()
    {
        PlayerPrefs.SetInt(IronPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockSteelPickaxe()
    {
        PlayerPrefs.SetInt(SteelPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockTitaniumPickaxe()
    {
        PlayerPrefs.SetInt(TitaniumPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockTungstenPickaxe()
    {
        PlayerPrefs.SetInt(TungstenPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockObsidianPickaxe()
    {
        PlayerPrefs.SetInt(ObsidianPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockMythrilPickaxe()
    {
        PlayerPrefs.SetInt(MythrilPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockDiamondPickaxe()
    {
        PlayerPrefs.SetInt(DiamondPickaxeKey, 1);
        PlayerPrefs.Save();
    }

    public static bool Unlock(CraftingRecipe recipe)
    {
        if (!IsShopRecipe(recipe) || IsUnlocked(recipe)) return false;
        switch (recipe.output.item)
        {
            case Item.Medkit: UnlockMedkit(); break;
            case Item.IronPickaxe: UnlockIronPickaxe(); break;
            case Item.SteelPickaxe: UnlockSteelPickaxe(); break;
            case Item.TitaniumPickaxe: UnlockTitaniumPickaxe(); break;
            case Item.TungstenPickaxe: UnlockTungstenPickaxe(); break;
            case Item.ObsidianPickaxe: UnlockObsidianPickaxe(); break;
            case Item.MythrilPickaxe: UnlockMythrilPickaxe(); break;
            case Item.DiamondPickaxe: UnlockDiamondPickaxe(); break;
            default: return false;
        }
        return true;
    }

    public static void ResetRun()
    {
        PlayerPrefs.DeleteKey(MedkitKey);
        PlayerPrefs.DeleteKey(CopperPickaxeKey);
        PlayerPrefs.DeleteKey(IronPickaxeKey);
        PlayerPrefs.DeleteKey(SteelPickaxeKey);
        PlayerPrefs.DeleteKey(TitaniumPickaxeKey);
        PlayerPrefs.DeleteKey(TungstenPickaxeKey);
        PlayerPrefs.DeleteKey(ObsidianPickaxeKey);
        PlayerPrefs.DeleteKey(MythrilPickaxeKey);
        PlayerPrefs.DeleteKey(DiamondPickaxeKey);
        PlayerPrefs.Save();
    }
}
