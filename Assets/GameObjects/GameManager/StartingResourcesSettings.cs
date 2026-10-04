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
    public StartingItemAmount[] items =
    {
        new StartingItemAmount((int)Item.Torche, 5),
        new StartingItemAmount((int)Item.Ladder, 10)
    };
}

public static class StartingResourcesSettings
{
    const string Key = "debug.starting-resources.v1";

    public static StartingResourcesData Load()
    {
        return JsonUtility.FromJson<StartingResourcesData>(JsonUtility.ToJson(GpsSettings.Preferences.startingResources));
    }

    public static void Save(int money, StartingItemAmount[] items)
    {
        var data=new StartingResourcesData { money=Mathf.Max(0,money),items=items??Array.Empty<StartingItemAmount>() };
        var value=GpsCodec.Read("startingResources",typeof(StartingResourcesData),data,GpsSettings.Profile.Key);
        GpsSettings.SetValue("preferences",value,out _);
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
