using System;
using System.Collections.Generic;
using UnityEngine;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    public event Action<ItemSO, int, int> OnItemSold;
    public event Action<int> OnSellAll;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool CanSell(ItemSO item, int qty = 1)
    {
        if (!item || item.worth <= 0 || qty <= 0) return false;
        var inv = InventoryManager.Instance;
        if (!inv) return false;
        return inv.GetCount(item) >= qty;
    }

    public int GetMaxSellable(ItemSO item)
    {
        if (!item || item.worth <= 0) return 0;
        var inv = InventoryManager.Instance;
        return inv ? inv.GetCount(item) : 0;
    }

    public int GetSellValue(ItemSO item, int qty = 1)
    {
        if (!item || item.worth <= 0 || qty <= 0) return 0;
        return item.worth * qty;
    }

    /// <summary> Verkauft eine Menge eines Items. </summary>
    public bool TrySell(ItemSO item, int qty = 1)
    {
        if (!CanSell(item, qty)) return false;

        var inv = InventoryManager.Instance;
        int value = GetSellValue(item, qty);

        // Bestand wirklich entfernen
        if (!inv.TryRemove(item, qty)) return false;

        StatsManager.Instance.AddMoney(value);
        OnItemSold?.Invoke(item, qty, value);
        return true;
    }

    /// <summary> Verkauft alle Items mit worth > 0. </summary>
    public int SellAll()
    {
        var inv = InventoryManager.Instance;
        if (!inv) return 0;

        int total = 0;
        // Snapshot holen (direkt iterierbar)
        IReadOnlyDictionary<ItemSO, int> snap = inv.GetSnapshot();

        // Liste bauen, damit wir während des Iterierens entfernen dürfen
        var buffer = new List<(ItemSO item, int count)>();
        foreach (var kv in snap)
            if (kv.Key && kv.Key.worth > 0 && kv.Value > 0)
                buffer.Add((kv.Key, kv.Value));

        foreach (var entry in buffer)
        {
            if (inv.TryRemove(entry.item, entry.count))
                total += entry.item.worth * entry.count;
        }

        if (total > 0)
        {
            StatsManager.Instance.AddMoney(total);
            OnSellAll?.Invoke(total);
        }

        return total;
    }
}
