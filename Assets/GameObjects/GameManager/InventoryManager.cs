using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private readonly Dictionary<ItemSO, int> _counts = new();

    public event Action<ItemSO, int> OnItemChanged;
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

        OnItemChanged?.Invoke(item, _counts[item]);
        OnInventoryChanged?.Invoke();
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

    public IReadOnlyDictionary<ItemSO, int> GetSnapshot() => _counts;

    public void ResetAll()
    {
        _counts.Clear();
        OnInventoryChanged?.Invoke();
    }
}
