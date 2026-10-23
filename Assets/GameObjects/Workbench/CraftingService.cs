using System.Collections.Generic;
using UnityEngine;

public readonly struct CraftOutcome
{
    public readonly int batches;
    public readonly bool pickaxeUpgraded, energyUpgraded, carryUpgraded;
    public CraftOutcome(int batches, bool pickaxe, bool energy, bool carry)
    { this.batches = batches; pickaxeUpgraded = pickaxe; energyUpgraded = energy; carryUpgraded = carry; }
}

public static class CraftingService
{
    public static event System.Action<CraftingRecipe, CraftOutcome> Crafted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Crafted = null;

    // GPS Werkbank → Lager: materials in the storage huts count towards recipes. By default the
    // backpack is used up first; "Lager zuerst" keeps the backpack and empties the storage first.
    public static bool UsesStorage => GpsSettings.Preferences.workbenchUsesStorage;
    public static bool StorageFirst => GpsSettings.Preferences.workbenchStorageFirst;

    public static bool IsOwnedPowerup(CraftingRecipe recipe, InventoryManager inventory) =>
        recipe && recipe.output && recipe.output.category == ItemCategory.Powerup &&
        inventory && inventory.IsPowerupUnlocked(recipe.output);

    public static int GetStoredCount(ItemSO item)
    {
        if (!item || !UsesStorage) return 0;
        long total = 0;
        foreach (var storage in SurfaceStorageBuilding.Active)
            if (storage) total += storage.GetStoredCount(item);
        return (int)System.Math.Min(int.MaxValue, total);
    }

    public static int GetAvailableCount(ItemSO item, InventoryManager inventory) =>
        (int)System.Math.Min(int.MaxValue, (long)(inventory ? inventory.GetCount(item) : 0) + GetStoredCount(item));

    public static int GetMaxCraftable(CraftingRecipe recipe, InventoryManager inventory)
    {
        if (!recipe || !inventory || !RecipeUnlocks.IsUnlocked(recipe) || IsOwnedPowerup(recipe, inventory) ||
            !recipe.TryGetCosts(out var costs)) return 0;
        int maximum = (int.MaxValue - inventory.GetCount(recipe.output)) / recipe.outputAmount;
        if (recipe.output.category == ItemCategory.Powerup) maximum = Mathf.Min(maximum, 1);
        foreach (var cost in costs)
            maximum = Mathf.Min(maximum, GetAvailableCount(cost.Key, inventory) / cost.Value);
        if (maximum <= 0 || FitsWeight(recipe, costs, inventory, maximum)) return Mathf.Max(0, maximum);
        // The weight change is monotonic until the limit is crossed, so the largest fitting prefix is found by bisection.
        int fits = 0, fails = maximum;
        while (fails - fits > 1)
        {
            int middle = fits + (fails - fits) / 2;
            if (FitsWeight(recipe, costs, inventory, middle)) fits = middle;
            else fails = middle;
        }
        return fits;
    }

    // With "Lager zuerst" a medium quantity can be heavier than the maximum, so the exact quantity is checked.
    public static bool CanCraft(CraftingRecipe recipe, InventoryManager inventory, int batches) =>
        batches > 0 && batches <= GetMaxCraftable(recipe, inventory) && recipe.TryGetCosts(out var costs) &&
        FitsWeight(recipe, costs, inventory, batches);

    // Splits the amount needed for the given batches into the backpack and the storage part.
    public static void SplitCost(ItemSO item, int needed, InventoryManager inventory, out int fromInventory, out int fromStorage)
    {
        int owned = inventory ? inventory.GetCount(item) : 0;
        int stored = GetStoredCount(item);
        fromInventory = StorageFirst ? Mathf.Clamp(needed - stored, 0, owned) : Mathf.Min(owned, needed);
        fromStorage = Mathf.Min(needed - fromInventory, stored);
    }

    static bool FitsWeight(CraftingRecipe recipe, Dictionary<ItemSO, int> costs, InventoryManager inventory, int batches)
    {
        double change = recipe.output.category == ItemCategory.Powerup ? 0d
            : recipe.output.EffectiveWeight * (double)recipe.outputAmount * batches;
        foreach (var cost in costs)
        {
            SplitCost(cost.Key, (int)System.Math.Min(int.MaxValue, (long)cost.Value * batches), inventory, out int fromInventory, out _);
            change -= cost.Key.EffectiveWeight * (double)fromInventory;
        }
        return inventory.CanFitWeight(change);
    }

    public static bool TryCraft(CraftingRecipe recipe, InventoryManager inventory, int batches)
    {
        if (!CanCraft(recipe, inventory, batches)) return false;
        if (!recipe.TryGetCosts(out var perBatch)) return false;
        var inventoryCosts = new Dictionary<ItemSO, int>();
        var storageCosts = new Dictionary<ItemSO, int>();
        foreach (var cost in perBatch)
        {
            SplitCost(cost.Key, checked(cost.Value * batches), inventory, out int fromInventory, out int fromStorage);
            if (fromInventory + fromStorage != cost.Value * batches) return false;
            if (fromInventory > 0) inventoryCosts.Add(cost.Key, fromInventory);
            if (fromStorage > 0) storageCosts.Add(cost.Key, fromStorage);
        }
        int previousPickaxe = inventory.EquippedPickaxeLevel;
        int previousEnergy = inventory.EnergyCapacityLevel;
        int previousCarrying = inventory.CarryingCapacityLevel;
        // Storage materials go first so the inventory notification already sees the final storage contents.
        var taken = TakeFromStorage(storageCosts);
        if (taken == null) return false;
        if (!inventory.TryExchange(inventoryCosts, recipe.output, checked(recipe.outputAmount * batches)))
        {
            foreach (var entry in taken) entry.storage.ReturnFromCrafting(entry.item, entry.amount);
            return false;
        }
        if (MetaProgressionRuntime.RewardsAllowed)
        {
            MetaProgression.RecordCraftUpgrade("pickaxe-upgrades", previousPickaxe, inventory.EquippedPickaxeLevel);
            MetaProgression.RecordCraftUpgrade("energy-upgrades", previousEnergy, inventory.EnergyCapacityLevel);
            MetaProgression.RecordCraftUpgrade("carrying-upgrades", previousCarrying, inventory.CarryingCapacityLevel);
            MetaProgression.RecordCraft(recipe.output.item);
            MetaProgression.RecordCraftedAmount(recipe.output.item, checked(recipe.outputAmount * batches), recipe.exotic, batches);
            if (recipe.exotic) MetaProgression.RecordExoticCraft(recipe.output.item);
        }
        Crafted?.Invoke(recipe, new CraftOutcome(batches, inventory.EquippedPickaxeLevel > previousPickaxe,
            inventory.EnergyCapacityLevel > previousEnergy, inventory.CarryingCapacityLevel > previousCarrying));
        return true;
    }

    readonly struct StorageTake
    {
        public readonly SurfaceStorageBuilding storage; public readonly ItemSO item; public readonly int amount;
        public StorageTake(SurfaceStorageBuilding storage, ItemSO item, int amount) { this.storage = storage; this.item = item; this.amount = amount; }
    }

    static List<StorageTake> TakeFromStorage(Dictionary<ItemSO, int> costs)
    {
        var taken = new List<StorageTake>();
        foreach (var cost in costs)
        {
            int remaining = cost.Value;
            foreach (var storage in SurfaceStorageBuilding.Active)
            {
                if (remaining <= 0) break;
                if (!storage) continue;
                int amount = storage.TakeForCrafting(cost.Key, remaining);
                if (amount > 0) { taken.Add(new StorageTake(storage, cost.Key, amount)); remaining -= amount; }
            }
            if (remaining > 0)
            {
                foreach (var entry in taken) entry.storage.ReturnFromCrafting(entry.item, entry.amount);
                return null;
            }
        }
        return taken;
    }
}
