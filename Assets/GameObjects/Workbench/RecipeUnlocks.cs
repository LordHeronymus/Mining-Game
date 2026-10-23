using System.Collections.Generic;
using UnityEngine;

public static class RecipeUnlocks
{
    static readonly HashSet<int> exoticLearned = new();
    // Shop blueprints bought in this run (item ids). Kept in memory, not in PlayerPrefs: those are shared by
    // every editor of the project, so a parallel run would wipe or overwrite them. Saves use CaptureRunState.
    static readonly HashSet<int> shopUnlocked = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { exoticLearned.Clear(); shopUnlocked.Clear(); }
    public static bool LearnExotic(CraftingRecipe recipe)
    {
        if (!recipe || !recipe.exotic || !recipe.output) return false;
        return exoticLearned.Add((int)recipe.output.item);
    }
    static readonly Item[] EnergyUpgradeItems = {
        Item.CrystalPendant, Item.CopperEnergyBracelet, Item.EnergyStorageVial, Item.RuneBelt,
        Item.CrystalHeart, Item.CrystalHarness, Item.TravelMonolith
    };
    static bool IsEnergyUpgrade(Item item) => System.Array.IndexOf(EnergyUpgradeItems, item) >= 0;
    public static bool IsShopRecipe(CraftingRecipe recipe) => recipe && recipe.output && !recipe.exotic &&
        (IsEnergyUpgrade(recipe.output.item) || recipe.output.item switch
    {
        Item.Medkit or Item.Backpack or Item.LoadBelt or Item.HeavyDutyBoots or Item.ReinforcedBackpack or Item.SpringGreaves or Item.LoadFrame or Item.Exoskeleton or Item.IronPickaxe or Item.SteelPickaxe or Item.TitaniumPickaxe or
            Item.TungstenPickaxe or Item.ObsidianPickaxe or Item.MythrilPickaxe or Item.DiamondPickaxe => true,
        _ => false
    });

    public static bool IsUnlocked(CraftingRecipe recipe) =>
        !recipe || !recipe.output || (recipe.exotic ? exoticLearned.Contains((int)recipe.output.item) : IsEnergyUpgrade(recipe.output.item)
            ? IsShopUnlocked(recipe.output.item) : recipe.output.item switch
        {
            Item.Medkit => IsMedkitUnlocked,
            Item.Backpack => IsBackpackUnlocked,
            Item.LoadBelt => IsLoadBeltUnlocked,
            Item.HeavyDutyBoots => IsHeavyDutyBootsUnlocked,
            Item.ReinforcedBackpack => IsReinforcedBackpackUnlocked,
            Item.SpringGreaves => IsSpringGreavesUnlocked,
            Item.LoadFrame => IsLoadFrameUnlocked,
            Item.Exoskeleton => IsExoskeletonUnlocked,
            Item.IronPickaxe => IsIronPickaxeUnlocked,
            Item.SteelPickaxe => IsSteelPickaxeUnlocked,
            Item.TitaniumPickaxe => IsTitaniumPickaxeUnlocked,
            Item.TungstenPickaxe => IsTungstenPickaxeUnlocked,
            Item.ObsidianPickaxe => IsObsidianPickaxeUnlocked,
            Item.MythrilPickaxe => IsMythrilPickaxeUnlocked,
            Item.DiamondPickaxe => IsDiamondPickaxeUnlocked,
            Item.Steel => IsRequiredByUnlockedRecipe(recipe.output),
            _ => true
        });

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

    static bool IsShopUnlocked(Item item) => shopUnlocked.Contains((int)item);

    // Upgrade lines in the shop, lowest step first. With GPS Shop → Baupläne → "Nur nächste Stufe" only the first
    // step and the step after a bought blueprint are offered. Unlock itself stays unrestricted, so saves that
    // already hold higher steps keep them; a gap simply offers the missing lower step again.
    static readonly Item[][] ShopLines = {
        new[] { Item.Backpack, Item.LoadBelt, Item.HeavyDutyBoots, Item.ReinforcedBackpack, Item.SpringGreaves, Item.LoadFrame, Item.Exoskeleton },
        new[] { Item.CrystalPendant, Item.CopperEnergyBracelet, Item.EnergyStorageVial, Item.RuneBelt, Item.CrystalHeart, Item.CrystalHarness, Item.TravelMonolith },
        new[] { Item.IronPickaxe, Item.SteelPickaxe, Item.TitaniumPickaxe, Item.TungstenPickaxe, Item.ObsidianPickaxe, Item.MythrilPickaxe, Item.DiamondPickaxe },
    };

    public static bool IsShopAvailable(CraftingRecipe recipe)
    {
        if (!IsShopRecipe(recipe)) return false;
        if (IsUnlocked(recipe) || !GpsSettings.Preferences.shopNextStepOnly) return true;
        foreach (var line in ShopLines)
        {
            int index = System.Array.IndexOf(line, recipe.output.item);
            if (index >= 0) return index == 0 || IsShopUnlocked(line[index - 1]);
        }
        return true;
    }
    public static bool IsMedkitUnlocked => IsShopUnlocked(Item.Medkit);
    public static bool IsBackpackUnlocked => IsShopUnlocked(Item.Backpack);
    public static bool IsLoadBeltUnlocked => IsShopUnlocked(Item.LoadBelt);
    public static bool IsHeavyDutyBootsUnlocked => IsShopUnlocked(Item.HeavyDutyBoots);
    public static bool IsReinforcedBackpackUnlocked => IsShopUnlocked(Item.ReinforcedBackpack);
    public static bool IsLoadFrameUnlocked => IsShopUnlocked(Item.LoadFrame);
    public static bool IsExoskeletonUnlocked => IsShopUnlocked(Item.Exoskeleton);
    public static bool IsSpringGreavesUnlocked => IsShopUnlocked(Item.SpringGreaves);
    public static bool IsIronPickaxeUnlocked => IsShopUnlocked(Item.IronPickaxe);
    public static bool IsSteelPickaxeUnlocked => IsShopUnlocked(Item.SteelPickaxe);
    public static bool IsTitaniumPickaxeUnlocked => IsShopUnlocked(Item.TitaniumPickaxe);
    public static bool IsTungstenPickaxeUnlocked => IsShopUnlocked(Item.TungstenPickaxe);
    public static bool IsObsidianPickaxeUnlocked => IsShopUnlocked(Item.ObsidianPickaxe);
    public static bool IsMythrilPickaxeUnlocked => IsShopUnlocked(Item.MythrilPickaxe);
    public static bool IsDiamondPickaxeUnlocked => IsShopUnlocked(Item.DiamondPickaxe);

    public static void UnlockMedkit() => shopUnlocked.Add((int)Item.Medkit);
    public static void UnlockBackpack() => shopUnlocked.Add((int)Item.Backpack);
    public static void UnlockLoadBelt() => shopUnlocked.Add((int)Item.LoadBelt);
    public static void UnlockHeavyDutyBoots() => shopUnlocked.Add((int)Item.HeavyDutyBoots);
    public static void UnlockReinforcedBackpack() => shopUnlocked.Add((int)Item.ReinforcedBackpack);
    public static void UnlockLoadFrame() => shopUnlocked.Add((int)Item.LoadFrame);
    public static void UnlockExoskeleton() => shopUnlocked.Add((int)Item.Exoskeleton);
    public static void UnlockSpringGreaves() => shopUnlocked.Add((int)Item.SpringGreaves);
    public static void UnlockIronPickaxe() => shopUnlocked.Add((int)Item.IronPickaxe);
    public static void UnlockSteelPickaxe() => shopUnlocked.Add((int)Item.SteelPickaxe);
    public static void UnlockTitaniumPickaxe() => shopUnlocked.Add((int)Item.TitaniumPickaxe);
    public static void UnlockTungstenPickaxe() => shopUnlocked.Add((int)Item.TungstenPickaxe);
    public static void UnlockObsidianPickaxe() => shopUnlocked.Add((int)Item.ObsidianPickaxe);
    public static void UnlockMythrilPickaxe() => shopUnlocked.Add((int)Item.MythrilPickaxe);
    public static void UnlockDiamondPickaxe() => shopUnlocked.Add((int)Item.DiamondPickaxe);

    public static bool Unlock(CraftingRecipe recipe)
    {
        if (!IsShopRecipe(recipe) || IsUnlocked(recipe)) return false;
        if (IsEnergyUpgrade(recipe.output.item))
        {
            shopUnlocked.Add((int)recipe.output.item);
            return true;
        }
        switch (recipe.output.item)
        {
            case Item.Medkit: UnlockMedkit(); break;
            case Item.Backpack: UnlockBackpack(); break;
            case Item.LoadBelt: UnlockLoadBelt(); break;
            case Item.HeavyDutyBoots: UnlockHeavyDutyBoots(); break;
            case Item.ReinforcedBackpack: UnlockReinforcedBackpack(); break;
            case Item.SpringGreaves: UnlockSpringGreaves(); break;
            case Item.LoadFrame: UnlockLoadFrame(); break;
            case Item.Exoskeleton: UnlockExoskeleton(); break;
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
        exoticLearned.Clear();
        shopUnlocked.Clear();
    }

    // Pickaxe recipes live outside Resources/WorkbenchRecipes and are only listed in the CraftingCatalog.
    static HashSet<CraftingRecipe> SavedRecipes()
    {
        var recipes = new HashSet<CraftingRecipe>();
        foreach (var recipe in ExoticCatalog.AllRecipes) if (recipe) recipes.Add(recipe);
        foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes")) if (recipe) recipes.Add(recipe);
        return recipes;
    }

    public static int[] CaptureRunState()
    {
        var ids = new List<int>(exoticLearned);
        foreach (var recipe in SavedRecipes())
            if (IsShopRecipe(recipe) && IsUnlocked(recipe) && !ids.Contains((int)recipe.output.item)) ids.Add((int)recipe.output.item);
        return ids.ToArray();
    }
    public static void RestoreRunState(int[] ids)
    {
        ResetRun();
        var set = new HashSet<int>(ids ?? System.Array.Empty<int>());
        foreach (var recipe in SavedRecipes())
            if (recipe && recipe.output && set.Contains((int)recipe.output.item))
            {
                if (recipe.exotic) LearnExotic(recipe);
                else Unlock(recipe);
            }
    }
}
