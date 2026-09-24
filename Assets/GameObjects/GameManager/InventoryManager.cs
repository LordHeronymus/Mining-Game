using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private readonly Dictionary<ItemSO, int> _counts = new();
    private readonly HashSet<Item> _unlockedPowerups = new();

    public event Action<ItemSO, int> OnItemChanged;
    public event Action<ItemSO, int> OnItemGained;
    public event Action OnInventoryChanged;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Add(ItemSO item, int amount = 1)
    {
        if (!item || amount <= 0) return;

        _counts[item] = _counts.TryGetValue(item, out var cur) ? cur + amount : amount;
        if (item.category == ItemCategory.Powerup) _unlockedPowerups.Add(item.item);

        OnItemChanged?.Invoke(item, _counts[item]);
        OnInventoryChanged?.Invoke();
        OnItemGained?.Invoke(item, amount);
    }

    public bool TryRemove(ItemSO item, int amount = 1)
    {
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

    public bool IsPowerupUnlocked(ItemSO item) => item &&
        item.category == ItemCategory.Powerup && _unlockedPowerups.Contains(item.item);

    // Apply a crafting transaction completely before notifying inventory listeners.
    public bool TryExchange(IReadOnlyDictionary<ItemSO, int> costs, ItemSO output, int amount)
    {
        if (!output || amount <= 0 || costs == null || costs.Count == 0) return false;
        foreach (var cost in costs)
            if (!cost.Key || cost.Value <= 0 || GetCount(cost.Key) < cost.Value) return false;
        costs.TryGetValue(output, out int outputCost);
        long finalOutput = (long)GetCount(output) - outputCost + amount;
        if (finalOutput > int.MaxValue) return false;

        var changed = new HashSet<ItemSO>();
        foreach (var cost in costs)
        {
            int remaining = GetCount(cost.Key) - cost.Value;
            if (remaining == 0) _counts.Remove(cost.Key);
            else _counts[cost.Key] = remaining;
            changed.Add(cost.Key);
        }
        _counts[output] = (int)finalOutput;
        if (output.category == ItemCategory.Powerup) _unlockedPowerups.Add(output.item);
        changed.Add(output);
        foreach (var item in changed) OnItemChanged?.Invoke(item, GetCount(item));
        OnInventoryChanged?.Invoke();
        OnItemGained?.Invoke(output, amount);
        return true;
    }

    public IReadOnlyDictionary<ItemSO, int> GetSnapshot() => _counts;

    public void ResetAll()
    {
        _counts.Clear();
        _unlockedPowerups.Clear();
        OnInventoryChanged?.Invoke();
    }
}
