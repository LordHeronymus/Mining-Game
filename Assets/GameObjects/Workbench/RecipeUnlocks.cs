using System.Collections.Generic;
using UnityEngine;

public static class RecipeUnlocks
{
    static readonly HashSet<int> exoticLearned = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetExotics() => exoticLearned.Clear();
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
    static string EnergyUpgradeKey(Item item) => "workbench.recipe.energy." + (int)item;
    const string MedkitKey = "workbench.recipe.medkit.unlocked";
    const string BackpackKey = "workbench.recipe.backpack.unlocked";
    const string LoadBeltKey = "workbench.recipe.load-belt.unlocked";
    const string HeavyDutyBootsKey = "workbench.recipe.heavy-duty-boots.unlocked";
    const string ReinforcedBackpackKey = "workbench.recipe.reinforced-backpack.unlocked";
    const string LoadFrameKey = "workbench.recipe.load-frame.unlocked";
    const string ExoskeletonKey = "workbench.recipe.exoskeleton.unlocked";
    const string SpringGreavesKey = "workbench.recipe.spring-greaves.unlocked";
    const string CopperPickaxeKey = "workbench.recipe.copper-pickaxe.unlocked";
    const string IronPickaxeKey = "workbench.recipe.iron-pickaxe.unlocked";
    const string SteelPickaxeKey = "workbench.recipe.steel-pickaxe.unlocked";
    const string TitaniumPickaxeKey = "workbench.recipe.titanium-pickaxe.unlocked";
    const string TungstenPickaxeKey = "workbench.recipe.tungsten-pickaxe.unlocked";
    const string ObsidianPickaxeKey = "workbench.recipe.obsidian-pickaxe.unlocked";
    const string MythrilPickaxeKey = "workbench.recipe.mythril-pickaxe.unlocked";
    const string DiamondPickaxeKey = "workbench.recipe.diamond-pickaxe.unlocked";
    public static bool IsShopRecipe(CraftingRecipe recipe) => recipe && recipe.output && !recipe.exotic &&
        (IsEnergyUpgrade(recipe.output.item) || recipe.output.item switch
    {
        Item.Medkit or Item.Backpack or Item.LoadBelt or Item.HeavyDutyBoots or Item.ReinforcedBackpack or Item.SpringGreaves or Item.LoadFrame or Item.Exoskeleton or Item.IronPickaxe or Item.SteelPickaxe or Item.TitaniumPickaxe or
            Item.TungstenPickaxe or Item.ObsidianPickaxe or Item.MythrilPickaxe or Item.DiamondPickaxe => true,
        _ => false
    });

    public static bool IsUnlocked(CraftingRecipe recipe) =>
        !recipe || !recipe.output || (recipe.exotic ? exoticLearned.Contains((int)recipe.output.item) : IsEnergyUpgrade(recipe.output.item)
            ? PlayerPrefs.GetInt(EnergyUpgradeKey(recipe.output.item), 0) == 1 : recipe.output.item switch
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

    public static bool IsMedkitUnlocked => PlayerPrefs.GetInt(MedkitKey, 0) == 1;
    public static bool IsBackpackUnlocked => PlayerPrefs.GetInt(BackpackKey, 0) == 1;
    public static bool IsLoadBeltUnlocked => PlayerPrefs.GetInt(LoadBeltKey, 0) == 1;
    public static bool IsHeavyDutyBootsUnlocked => PlayerPrefs.GetInt(HeavyDutyBootsKey, 0) == 1;
    public static bool IsReinforcedBackpackUnlocked => PlayerPrefs.GetInt(ReinforcedBackpackKey, 0) == 1;
    public static bool IsLoadFrameUnlocked => PlayerPrefs.GetInt(LoadFrameKey, 0) == 1;
    public static bool IsExoskeletonUnlocked => PlayerPrefs.GetInt(ExoskeletonKey, 0) == 1;
    public static bool IsSpringGreavesUnlocked => PlayerPrefs.GetInt(SpringGreavesKey, 0) == 1;
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

    public static void UnlockBackpack()
    {
        PlayerPrefs.SetInt(BackpackKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockLoadBelt()
    {
        PlayerPrefs.SetInt(LoadBeltKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockHeavyDutyBoots()
    {
        PlayerPrefs.SetInt(HeavyDutyBootsKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockReinforcedBackpack()
    {
        PlayerPrefs.SetInt(ReinforcedBackpackKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockLoadFrame() { PlayerPrefs.SetInt(LoadFrameKey, 1); PlayerPrefs.Save(); }

    public static void UnlockExoskeleton()
    {
        PlayerPrefs.SetInt(ExoskeletonKey, 1);
        PlayerPrefs.Save();
    }

    public static void UnlockSpringGreaves()
    {
        PlayerPrefs.SetInt(SpringGreavesKey, 1);
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
        if (IsEnergyUpgrade(recipe.output.item))
        {
            PlayerPrefs.SetInt(EnergyUpgradeKey(recipe.output.item), 1);
            PlayerPrefs.Save();
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
        foreach (var item in EnergyUpgradeItems) PlayerPrefs.DeleteKey(EnergyUpgradeKey(item));
        PlayerPrefs.DeleteKey(MedkitKey);
        PlayerPrefs.DeleteKey(BackpackKey);
        PlayerPrefs.DeleteKey(LoadBeltKey);
        PlayerPrefs.DeleteKey(HeavyDutyBootsKey);
        PlayerPrefs.DeleteKey(ReinforcedBackpackKey);
        PlayerPrefs.DeleteKey(SpringGreavesKey);
        PlayerPrefs.DeleteKey(LoadFrameKey);
        PlayerPrefs.DeleteKey(ExoskeletonKey);
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

    public static int[] CaptureRunState()
    {
        var ids = new List<int>(exoticLearned);
        foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes"))
            if (IsShopRecipe(recipe) && IsUnlocked(recipe) && !ids.Contains((int)recipe.output.item)) ids.Add((int)recipe.output.item);
        return ids.ToArray();
    }
    public static void RestoreRunState(int[] ids)
    {
        ResetRun();
        var set = new HashSet<int>(ids ?? System.Array.Empty<int>());
        foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes"))
            if (recipe && recipe.output && set.Contains((int)recipe.output.item))
            {
                if (recipe.exotic) LearnExotic(recipe);
                else Unlock(recipe);
            }
    }
}
