using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    public event Action<ItemSO, int, int> OnItemSold;
    public event Action<int> OnSellAll;
    public static event Action<int, double, bool> Sold; // (money, soldWeight, sellAll)
    public static event Action<CraftingRecipe> RecipeBought;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() { Instance = null; Sold = null; RecipeBought = null; }

    public bool TryBuyRecipe(CraftingRecipe recipe)
    {
        var stats = StatsManager.Instance;
        if (!stats || !RecipeUnlocks.IsShopAvailable(recipe) || RecipeUnlocks.IsUnlocked(recipe) ||
            !stats.CanAffordMoney(recipe.ShopPrice)) return false;
        if (!RecipeUnlocks.Unlock(recipe)) return false;
        stats.AddMoney(-recipe.ShopPrice);
        ItemFeed.Instance?.ShowRecipe(recipe);
        AudioManager.Instance?.PlayShopPaperSound();
        RecipeBought?.Invoke(recipe);
        return true;
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // The sell page lists ores only; every sell path uses the same rule so nothing hidden is sold.
    public static bool IsSellable(ItemSO item) => item && item.category == ItemCategory.Ore && item.worth > 0;

    // GPS Shop → Lager: ores in the storage huts are listed and sold together with the backpack.
    public static bool SellsFromStorage => GpsSettings.Preferences.shopSellsFromStorage;
    public static int StoredCount(ItemSO item) => SellsFromStorage ? SurfaceStorageBuilding.TotalStored(item) : 0;

    public static int StockCount(ItemSO item)
    {
        var inv = InventoryManager.Instance;
        return inv ? (int)Math.Min(int.MaxValue, (long)inv.GetCount(item) + StoredCount(item)) : 0;
    }

    // Ores in the backpack and (with the GPS switch) in the storage huts, combined per item.
    public static Dictionary<ItemSO, int> OreStock()
    {
        var stock = new Dictionary<ItemSO, int>();
        var inv = InventoryManager.Instance;
        if (!inv) return stock;
        foreach (var kv in inv.GetSnapshot())
            if (kv.Key && kv.Key.category == ItemCategory.Ore && kv.Value > 0) stock[kv.Key] = kv.Value;
        if (SellsFromStorage)
            foreach (var storage in SurfaceStorageBuilding.Active)
                if (storage)
                    foreach (var kv in storage.StoredItems)
                        if (kv.Key && kv.Key.category == ItemCategory.Ore && kv.Value > 0)
                            stock[kv.Key] = (int)Math.Min(int.MaxValue, (long)(stock.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value);
        return stock;
    }

    public bool CanSell(ItemSO item, int qty = 1)
    {
        if (!IsSellable(item) || qty <= 0) return false;
        return StockCount(item) >= qty;
    }

    public int GetMaxSellable(ItemSO item)
    {
        if (!item || item.worth <= 0) return 0;
        return StockCount(item);
    }

    // The backpack is sold first (it lightens the load), then the storage huts in order.
    bool TryRemoveStock(ItemSO item, int qty, out double backpackWeight)
    {
        backpackWeight = 0d;
        var inv = InventoryManager.Instance;
        int fromInventory = Mathf.Min(qty, inv.GetCount(item));
        int fromStorage = qty - fromInventory;
        if (fromStorage > StoredCount(item)) return false;
        if (fromInventory > 0 && !inv.TryRemove(item, fromInventory)) return false;
        if (fromStorage > 0 && SurfaceStorageBuilding.TakeFromAll(item, fromStorage) != fromStorage) return false;
        backpackWeight = item.EffectiveWeight * (double)fromInventory;
        return true;
    }

    public int GetSellValue(ItemSO item, int qty = 1)
    {
        if (!item || item.worth <= 0 || qty <= 0) return 0;
        return item.worth * qty;
    }

    public bool TrySell(ItemSO item, int qty = 1)
    {
        if (!CanSell(item, qty)) return false;

        int value = GetSellValue(item, qty);

        // Bestand wirklich entfernen
        if (!TryRemoveStock(item, qty, out double soldWeight)) return false;

        StatsManager.Instance.AddMoney(value);
        if (MetaProgressionRuntime.RewardsAllowed) MetaProgression.RecordSale(value);
        OnItemSold?.Invoke(item, qty, value);
        Sold?.Invoke(value, soldWeight, false);

        PlaySellSound(qty);

        return true;
    }

    public int SellAll()
    {
        var inv = InventoryManager.Instance;
        if (!inv) return 0;

        int total = 0;
        int soldCount = 0;
        double soldWeight = 0d;

        foreach (var entry in OreStock())
        {
            if (IsSellable(entry.Key) && TryRemoveStock(entry.Key, entry.Value, out double weight))
            {
                total += entry.Key.worth * entry.Value;
                soldCount += entry.Value;
                soldWeight += weight;
            }
        }

        if (total > 0)
        {
            StatsManager.Instance.AddMoney(total);
            if (MetaProgressionRuntime.RewardsAllowed) MetaProgression.RecordSale(total);
            OnSellAll?.Invoke(total);
            Sold?.Invoke(total, soldWeight, true);
        }

        if (soldCount > 0) PlaySellSound(soldCount);
        return total;
    }


    void PlaySellSound(int count)
    {
        if (!AudioManager.Instance) return;
        if (count > 1 && isActiveAndEnabled) StartCoroutine(SellSound(count));
        else AudioManager.Instance.Play(SoundType.SellItem);
    }

    IEnumerator SellSound(int count)
    {
        int iterations = Mathf.FloorToInt(Mathf.Sqrt(count) * 2 + 1);

        for (int i = 0; i < iterations; i++)
        {
            AudioManager.Instance.Play(SoundType.SellItem);

            float waitTime = UnityEngine.Random.Range(0.005f, 0.1f);
            yield return new WaitForSeconds(waitTime);
        }
    }
}
