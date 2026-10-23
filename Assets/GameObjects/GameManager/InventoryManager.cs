using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private readonly Dictionary<ItemSO, int> _counts = new();
    private readonly HashSet<ItemSO> _ownedThisRun = new();
    private readonly HashSet<Item> _unlockedPowerups = new();

    UpgradeSettings upgradeSettings;
    int carryingCapacityLevel = 1;
    int energyCapacityLevel = 1;
    public int EnergyCapacityLevel => energyCapacityLevel;
    public float EnergyCapacityMultiplier => upgradeSettings
        ? upgradeSettings.GetEnergyCapacityMultiplier(energyCapacityLevel) : 1f;
    public int CarryingCapacityLevel
    {
        get => carryingCapacityLevel;
        set => SetCarryingCapacityLevel(value, true);
    }
    public float CarryingCapacity => (upgradeSettings ? upgradeSettings.GetCarryingCapacity(carryingCapacityLevel) : 30f) + MetaProgression.CurrentLoadout.carryCapacityBonus;
    public float MaximumWeight => CarryingCapacity * OverloadMaxMultiplier;
    // GPS Bewegung → Traglast: up to the capacity the player moves freely; beyond it the speed falls linearly
    // until the maximum load (multiple of the capacity), where only the minimum speed factor is left.
    public static float OverloadMaxMultiplier => Sanitize(GpsSettings.Preferences.carryOverloadMaxMultiplier, 2f, 1f, 10f);
    public static float OverloadMinSpeed => Sanitize(GpsSettings.Preferences.carryOverloadMinSpeed, 1f / 3f, .05f, 1f);
    static float Sanitize(float value, float fallback, float min, float max) =>
        float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : Mathf.Clamp(value, min, max);
    public ItemSO EquippedPickaxe { get; private set; }
    public int EquippedPickaxeLevel
    {
        get
        {
            if (!upgradeSettings || !EquippedPickaxe || upgradeSettings.pickaxeLevels == null) return 0;
            for (int i = 0; i < upgradeSettings.pickaxeLevels.Length; i++)
                if (upgradeSettings.pickaxeLevels[i] != null &&
                    upgradeSettings.pickaxeLevels[i].pickaxe == EquippedPickaxe)
                    return i + 1;
            return 0;
        }
    }
    public float EquippedPickaxeProgressMultiplier => EquippedPickaxeLevel > 0
        ? upgradeSettings.GetPickaxeProgressMultiplier(EquippedPickaxeLevel) : 1f;
    public float EquippedPickaxeMaximumHardness => EquippedPickaxeLevel > 0
        ? upgradeSettings.GetPickaxeMaximumHardness(EquippedPickaxeLevel) : 1f;
    public double TotalWeight
    {
        get
        {
            double total = 0d;
            foreach (var entry in _counts)
                if (entry.Key && entry.Value > 0) total += entry.Key.EffectiveWeight * (double)entry.Value;
            return total;
        }
    }
    public float MovementWeightFactor => GameplayTestSettings.NoWeight
        ? 1f : CalculateMovementWeightFactor(TotalWeight, CarryingCapacity);

    public static int CarryStage(double weight, float capacity) =>
        capacity <= 0f ? 0 : weight > 2d * capacity ? 2 : weight > capacity ? 1 : 0;

    public static float CalculateMovementWeightFactor(double totalWeight, float carryingCapacity) =>
        CalculateMovementWeightFactor(totalWeight, carryingCapacity, OverloadMaxMultiplier, OverloadMinSpeed);

    public static float CalculateMovementWeightFactor(double totalWeight, float carryingCapacity, float maxMultiplier, float minSpeed)
    {
        double overload = Math.Max(0d, totalWeight / Math.Max(0.01f, carryingCapacity) - 1d);
        if (overload <= 0d) return 1f;
        double t = Math.Min(1d, overload / Math.Max(0.0001d, maxMultiplier - 1d));
        return (float)(1d + (Mathf.Clamp(minSpeed, 0f, 1f) - 1d) * t);
    }

    public bool CanFitWeight(double additionalWeight) => GameplayTestSettings.NoWeight || additionalWeight <= 0d ||
        TotalWeight + additionalWeight <= MaximumWeight + 0.00001d;

    public bool CanAdd(ItemSO item, int amount = 1) => item && amount > 0 &&
        (item.category == ItemCategory.Powerup
            ? amount == 1 && !IsPowerupUnlocked(item)
            : GetCount(item) <= int.MaxValue - amount && CanFitWeight(item.EffectiveWeight * (double)amount));

    public void NotifyWeightChanged() => OnInventoryChanged?.Invoke();

    public event Action<ItemSO, int> OnItemChanged;
    public event Action<ItemSO, int> OnItemGained;
    public event Action OnInventoryChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Instance = null;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        upgradeSettings = Resources.Load<UpgradeSettings>("UpgradeSettings");
        UnlockDefaultPickaxe();
        DontDestroyOnLoad(gameObject);
    }

    public bool EquipPickaxe(ItemSO pickaxe)
    {
        if (!IsPickaxe(pickaxe) || !_unlockedPowerups.Contains(pickaxe.item)) return false;
        SelectBestUnlockedPickaxe();
        OnInventoryChanged?.Invoke();
        return EquippedPickaxe == pickaxe;
    }

    static bool IsPickaxe(ItemSO item)
    {
        if (!item) return false;
        int id = (int)item.item;
        return id >= (int)Item.CopperPickaxe && id <= (int)Item.DiamondPickaxe;
    }

    static ItemSO FindPickaxe(Item item)
    {
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!catalog || catalog.items == null) return null;
        foreach (var candidate in catalog.items)
            if (candidate && candidate.item == item) return candidate;
        return null;
    }

    void UnlockDefaultPickaxe()
    {
        var copper = FindPickaxe(Item.CopperPickaxe);
        if (copper && copper.category == ItemCategory.Powerup)
            _unlockedPowerups.Add(copper.item);
        SelectBestUnlockedPickaxe();
    }

    void SelectBestUnlockedPickaxe()
    {
        ItemSO best = null;
        float bestHardness = float.NegativeInfinity;
        if (upgradeSettings && upgradeSettings.pickaxeLevels != null)
        {
            foreach (var level in upgradeSettings.pickaxeLevels)
            {
                if (level == null || !level.pickaxe || !_unlockedPowerups.Contains(level.pickaxe.item)) continue;
                if (level.maximumHardness > bestHardness)
                {
                    best = level.pickaxe;
                    bestHardness = level.maximumHardness;
                }
            }
        }

        if (!best)
            for (int id = (int)Item.DiamondPickaxe; id >= (int)Item.CopperPickaxe; id--)
            {
                var candidate = FindPickaxe((Item)id);
                if (candidate && _unlockedPowerups.Contains(candidate.item)) { best = candidate; break; }
            }
        EquippedPickaxe = best ? best : FindPickaxe(Item.CopperPickaxe);
    }

    public bool IsPickaxeUnlocked(ItemSO item) => IsPickaxe(item) && _unlockedPowerups.Contains(item.item);

    bool SetCarryingCapacityLevel(int value, bool notify)
    {
        int next = Mathf.Clamp(value, 1, upgradeSettings ? upgradeSettings.CarryingCapacityLevelCount : 1);
        if (carryingCapacityLevel == next) return false;
        carryingCapacityLevel = next;
        if (notify) OnInventoryChanged?.Invoke();
        return true;
    }

    bool UnlockPowerup(ItemSO item, bool notify = true, bool announce = true)
    {
        if (!item || item.category != ItemCategory.Powerup || !_unlockedPowerups.Add(item.item)) return false;
        if (IsPickaxe(item)) SelectBestUnlockedPickaxe();
        if (item.carryingCapacityUpgradeLevel > 0)
            SetCarryingCapacityLevel(Mathf.Max(carryingCapacityLevel, item.carryingCapacityUpgradeLevel), false);
        if (item.energyCapacityUpgradeLevel > 0)
        {
            energyCapacityLevel = Mathf.Clamp(Mathf.Max(energyCapacityLevel, item.energyCapacityUpgradeLevel),
                1, upgradeSettings ? upgradeSettings.EnergyCapacityLevelCount : 1);
            StatsManager.Instance?.RefreshEnergyCapacity();
        }
        if (notify)
        {
            OnInventoryChanged?.Invoke();
            if (announce) OnItemGained?.Invoke(item, 1);
        }
        return true;
    }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool Add(ItemSO item, int amount = 1)
    {
        return AddInternal(item, amount, enforceWeightLimit: true);
    }

    // Explicitly configured starting resources are granted even when their weight exceeds
    // the normal carrying limit. The player can still be overweight after the run begins.
    // A powerup is unlocked once; a starting entry copied in F1 keeps the amount of the entry it came from.
    // Without announce the item skips the item-gained feed, e.g. while a saved run replaces the inventory anyway.
    public bool AddStartingItem(ItemSO item, int amount = 1, bool announce = true)
    {
        if (item && item.category == ItemCategory.Powerup) amount = Mathf.Min(1, amount);
        return AddInternal(item, amount, enforceWeightLimit: false, announce: announce);
    }

    // Moving items out of the storage hut is a transfer, not a find, so it skips the item-gained feed.
    public bool AddTransferred(ItemSO item, int amount = 1)
    {
        return AddInternal(item, amount, enforceWeightLimit: true, announce: false);
    }

    bool AddInternal(ItemSO item, int amount, bool enforceWeightLimit, bool announce = true)
    {
        if (item && item.category == ItemCategory.Powerup)
            return amount == 1 && UnlockPowerup(item, announce: announce);
        if (!item || amount <= 0 || GetCount(item) > int.MaxValue - amount ||
            (enforceWeightLimit && !CanFitWeight(item.EffectiveWeight * (double)amount))) return false;

        _ownedThisRun.Add(item);
        _counts[item] = _counts.TryGetValue(item, out var cur) ? cur + amount : amount;
        OnItemChanged?.Invoke(item, _counts[item]);
        OnInventoryChanged?.Invoke();
        if (announce) OnItemGained?.Invoke(item, amount);
        return true;
    }

    public bool TryRemove(ItemSO item, int amount = 1)
    {
        if (item && item.category == ItemCategory.Powerup) return false;
        if (!item || amount <= 0) return false;
        if (!_counts.TryGetValue(item, out var cur) || cur < amount) return false;

        int newVal = cur - amount;
        if (newVal <= 0) _counts.Remove(item);
        else _counts[item] = newVal;

        OnItemChanged?.Invoke(item, GetCount(item));
        OnInventoryChanged?.Invoke();
        return true;
    }

    public int GetCount(ItemSO item) =>
        (!item) ? 0 : (_counts.TryGetValue(item, out var cur) ? cur : 0);

    public bool WasOwnedThisRun(ItemSO item) => item && _ownedThisRun.Contains(item);

    public bool IsPowerupUnlocked(ItemSO item) => item &&
        item.category == ItemCategory.Powerup && _unlockedPowerups.Contains(item.item);

    // Apply a crafting transaction completely before notifying inventory listeners.
    // The costs may be empty when the workbench takes every material from the storage hut.
    // outputToInventory false: the output went elsewhere (crafted components into the storage hut); only the
    // costs leave the backpack, but the gain is still announced for the item feed.
    public bool TryExchange(IReadOnlyDictionary<ItemSO, int> costs, ItemSO output, int amount, bool outputToInventory = true)
    {
        if (!output || amount <= 0 || costs == null) return false;
        bool outputIsPowerup = output.category == ItemCategory.Powerup;
        if (outputIsPowerup && (amount != 1 || IsPowerupUnlocked(output))) return false;
        bool addsOutput = outputToInventory && !outputIsPowerup;
        foreach (var cost in costs)
            if (!cost.Key || cost.Value <= 0 || GetCount(cost.Key) < cost.Value) return false;
        costs.TryGetValue(output, out int outputCost);
        long finalOutput = addsOutput ? (long)GetCount(output) - outputCost + amount : 0;
        if (addsOutput && finalOutput > int.MaxValue) return false;
        double changeInWeight = addsOutput ? output.EffectiveWeight * (double)amount : 0d;
        foreach (var cost in costs) changeInWeight -= cost.Key.EffectiveWeight * (double)cost.Value;
        if (!CanFitWeight(changeInWeight)) return false;
        if (outputIsPowerup && !UnlockPowerup(output, false)) return false;

        var changed = new HashSet<ItemSO>();
        foreach (var cost in costs)
        {
            int remaining = GetCount(cost.Key) - cost.Value;
            if (remaining == 0) _counts.Remove(cost.Key);
            else _counts[cost.Key] = remaining;
            changed.Add(cost.Key);
        }
        if (addsOutput)
        {
            _counts[output] = (int)finalOutput;
            _ownedThisRun.Add(output);
            changed.Add(output);
        }
        foreach (var item in changed) OnItemChanged?.Invoke(item, GetCount(item));
        OnInventoryChanged?.Invoke();
        OnItemGained?.Invoke(output, amount);
        return true;
    }

    public IReadOnlyDictionary<ItemSO, int> GetSnapshot() => _counts;

    public SavedInventory CaptureRunState()
    {
        var items = new List<SavedItem>();
        foreach (var entry in _counts) if (entry.Key) items.Add(new SavedItem { id = (int)entry.Key.item, count = entry.Value });
        var owned = new List<int>(); foreach (var item in _ownedThisRun) if (item) owned.Add((int)item.item);
        var powerups = new List<int>(); foreach (var item in _unlockedPowerups) powerups.Add((int)item);
        return new SavedInventory { items = items.ToArray(), owned = owned.ToArray(), powerups = powerups.ToArray(),
            carryingLevel = carryingCapacityLevel, energyLevel = energyCapacityLevel };
    }

    public void RestoreRunState(SavedInventory state)
    {
        _counts.Clear(); _ownedThisRun.Clear(); _unlockedPowerups.Clear();
        foreach (var entry in state.items ?? Array.Empty<SavedItem>())
        { var item = StartingResourcesSettings.Resolve(entry.id); if (item && entry.count > 0) _counts[item] = entry.count; }
        foreach (int id in state.owned ?? Array.Empty<int>())
        { var item = StartingResourcesSettings.Resolve(id); if (item) _ownedThisRun.Add(item); }
        foreach (int id in state.powerups ?? Array.Empty<int>()) _unlockedPowerups.Add((Item)id);
        carryingCapacityLevel = Mathf.Clamp(state.carryingLevel, 1, upgradeSettings ? upgradeSettings.CarryingCapacityLevelCount : 1);
        energyCapacityLevel = Mathf.Clamp(state.energyLevel, 1, upgradeSettings ? upgradeSettings.EnergyCapacityLevelCount : 1);
        UnlockDefaultPickaxe(); StatsManager.Instance?.RefreshEnergyCapacity();
        OnInventoryChanged?.Invoke();
    }

    public void ResetAll()
    {
        _counts.Clear();
        _ownedThisRun.Clear();
        _unlockedPowerups.Clear();
        carryingCapacityLevel = 1;
        energyCapacityLevel = 1;
        StatsManager.Instance?.RefreshEnergyCapacity();
        UnlockDefaultPickaxe();
        OnInventoryChanged?.Invoke();
    }
}
