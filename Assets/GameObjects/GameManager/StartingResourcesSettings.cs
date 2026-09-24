using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct StartingItemAmount
{
    public int itemId;
    public int amount;

    public StartingItemAmount(int itemId, int amount)
    {
        this.itemId = itemId;
        this.amount = amount;
    }
}

[Serializable]
public sealed class StartingResourcesData
{
    public int version = 1;
    public int money = 100;
    public StartingItemAmount[] items = { new StartingItemAmount((int)Item.Torche, 5) };
}

public static class StartingResourcesSettings
{
    const string Key = "debug.starting-resources.v1";

    public static StartingResourcesData Load()
    {
        if (!PlayerPrefs.HasKey(Key)) return Defaults();
        try
        {
            var data = JsonUtility.FromJson<StartingResourcesData>(PlayerPrefs.GetString(Key));
            if (data == null || data.version != 1 || data.money < 0 || data.items == null)
                return Defaults();
            var clean = new List<StartingItemAmount>();
            var totals = new Dictionary<int, long>();
            foreach (var entry in data.items)
            {
                if (entry.amount <= 0 || !Resolve(entry.itemId)) continue;
                long total = (totals.TryGetValue(entry.itemId, out var previous) ? previous : 0) + entry.amount;
                if (total > int.MaxValue) total = int.MaxValue;
                totals[entry.itemId] = total;
            }
            foreach (var entry in totals) clean.Add(new StartingItemAmount(entry.Key, (int)entry.Value));
            data.items = clean.ToArray();
            return data;
        }
        catch { return Defaults(); }
    }

    public static void Save(int money, StartingItemAmount[] items)
    {
        var data = new StartingResourcesData
        {
            money = Mathf.Max(0, money),
            items = items ?? Array.Empty<StartingItemAmount>()
        };
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    public static ItemSO Resolve(int itemId)
    {
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!catalog || catalog.items == null) return null;
        foreach (var item in catalog.items)
            if (item && (int)item.item == itemId) return item;
        return null;
    }

    static StartingResourcesData Defaults() => new StartingResourcesData();
}
