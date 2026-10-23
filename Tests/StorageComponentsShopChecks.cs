using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Crafted building components (nails, rope) go straight into the storage hut, placeable items stay in the backpack,
// the shop sells ores from backpack and storage, and 100 energy cost 25 coins.
// Runs in Edit Mode on a temporary inventory, StatsManager and storage hut; scene, assets and the GPS profile stay untouched.
public static class StorageComponentsShopChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var prefs = GpsSettings.Preferences;
        bool usesStorage = prefs.workbenchUsesStorage, storageFirst = prefs.workbenchStorageFirst;
        bool componentsToStorage = prefs.workbenchComponentsToStorage, sellsFromStorage = prefs.shopSellsFromStorage;
        var instanceProperty = typeof(InventoryManager).GetProperty(nameof(InventoryManager.Instance));
        var previousInventory = InventoryManager.Instance; var previousStats = StatsManager.Instance; var previousAudio = AudioManager.Instance;
        var activeHuts = (List<SurfaceStorageBuilding>)typeof(SurfaceStorageBuilding).GetField("active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var previousHuts = activeHuts.ToList();
        var host = new GameObject("StorageComponentsShopChecks") { hideFlags = HideFlags.HideAndDontSave };
        var hutObject = new GameObject("StorageComponentsShopChecks Hut", typeof(BoxCollider2D)) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            AudioManager.Instance = null;
            var inventory = host.AddComponent<InventoryManager>();
            instanceProperty.SetValue(null, inventory);
            var stats = host.AddComponent<StatsManager>();
            StatsManager.Instance = stats;
            var shop = host.AddComponent<ShopManager>();
            var hut = hutObject.AddComponent<SurfaceStorageBuilding>();
            activeHuts.Clear(); activeHuts.Add(hut);
            prefs.workbenchUsesStorage = true; prefs.workbenchStorageFirst = false;
            prefs.workbenchComponentsToStorage = true; prefs.shopSellsFromStorage = true;

            var recipes = AssetDatabase.FindAssets("t:CraftingRecipe", new[] { "Assets/GameObjects/Workbench/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            var nails = recipes.Single(r => r.output.item == Item.Nails);
            var rope = recipes.Single(r => r.output.item == Item.Rope);
            var torch = recipes.Single(r => r.output.item == Item.Torche);
            Check(CraftingService.IsComponent(nails.output) && CraftingService.IsComponent(rope.output) &&
                !CraftingService.IsComponent(torch.output), "Nägel und Seil sind Baukomponenten, die Fackel nicht");
            Check(recipes.Where(r => r.output.category == ItemCategory.Tool || r.output.category == ItemCategory.Powerup || r.output.category == ItemCategory.Consumable)
                .All(r => !CraftingService.IsComponent(r.output)), "Werkzeuge, Powerups und Verbrauchsgüter bleiben im Rucksack");
            bool nailRecipe = nails.TryGetCosts(out var nailCosts), torchRecipe = torch.TryGetCosts(out var torchCosts);
            Check(nailRecipe && torchRecipe, "Rezeptkosten gelesen");

            void Fill(CraftingRecipe recipe, Dictionary<ItemSO, int> costs, int batches, bool inStorage)
            {
                inventory.ResetAll();
                hut.RestoreRunState(Array.Empty<SavedItem>());
                foreach (var cost in costs)
                    if (inStorage) hut.ReturnFromCrafting(cost.Key, cost.Value * batches);
                    else inventory.AddStartingItem(cost.Key, cost.Value * batches);
            }

            // Nails from backpack materials land in the storage.
            int gained = 0;
            Action<ItemSO, int> onGained = (item, amount) => { if (item == nails.output) gained += amount; };
            inventory.OnItemGained += onGained;
            Fill(nails, nailCosts, 2, false);
            Check(CraftingService.OutputStorage(nails) == hut, "Nägel gehen ins Lager");
            Check(CraftingService.TryCraft(nails, inventory, 2), "Nägel aus dem Rucksack hergestellt");
            Check(inventory.GetCount(nails.output) == 0 && hut.GetStoredCount(nails.output) == 2 * nails.outputAmount,
                "Nägel liegen im Lager, nicht im Rucksack");
            Check(nailCosts.All(c => inventory.GetCount(c.Key) == 0), "Zutaten aus dem Rucksack verbraucht");
            Check(gained == 2 * nails.outputAmount, "Hinweis „+Nägel“ erscheint trotzdem");
            inventory.OnItemGained -= onGained;
            Check(hut.CaptureRunState().Any(x => x.id == (int)Item.Nails && x.count == 2 * nails.outputAmount), "Spielstand des Lagers enthält die Nägel");

            // Nails from storage materials stay in the storage.
            Fill(nails, nailCosts, 3, true);
            Check(CraftingService.TryCraft(nails, inventory, 3) && hut.GetStoredCount(nails.output) == 3 * nails.outputAmount &&
                nailCosts.All(c => hut.GetStoredCount(c.Key) == 0) && inventory.GetCount(nails.output) == 0, "Nägel aus Lager-Material bleiben im Lager");

            // Rope too.
            Check(rope.TryGetCosts(out var ropeCosts), "Seilrezept gelesen");
            Fill(rope, ropeCosts, 1, false);
            Check(CraftingService.TryCraft(rope, inventory, 1) && hut.GetStoredCount(rope.output) == rope.outputAmount && inventory.GetCount(rope.output) == 0,
                "Seil geht ins Lager");

            // A full backpack does not block components (their weight is not carried).
            var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
            var gold = catalog.items.First(x => x && x.item == Item.Gold);
            var copper = catalog.items.First(x => x && x.item == Item.Copper);
            if (!GameplayTestSettings.NoWeight && gold.EffectiveWeight > 0 && torch.output.EffectiveWeight > 0)
            {
                Fill(nails, nailCosts, 4, true);
                foreach (var cost in torchCosts) hut.ReturnFromCrafting(cost.Key, cost.Value * 4);
                int fill = (int)Math.Floor((inventory.MaximumWeight - inventory.TotalWeight) / gold.EffectiveWeight);
                if (fill > 0) inventory.AddStartingItem(gold, fill);
                Check(CraftingService.GetMaxCraftable(nails, inventory) == 4, "Voller Rucksack: Nägel trotzdem herstellbar");
                int fitting = (int)Math.Floor((inventory.MaximumWeight - inventory.TotalWeight + .00001d) / (torch.output.EffectiveWeight * (double)torch.outputAmount));
                int torches = CraftingService.GetMaxCraftable(torch, inventory);
                Check(torches == Mathf.Min(4, fitting) && torches < 4 || fitting >= 4,
                    "Voller Rucksack: Fackeln weiterhin durch das Gewicht begrenzt (" + torches + " von 4)");
            }

            // Torches stay in the backpack.
            Fill(torch, torchCosts, 1, false);
            Check(CraftingService.OutputStorage(torch) == null && CraftingService.TryCraft(torch, inventory, 1) &&
                inventory.GetCount(torch.output) == torch.outputAmount && hut.GetStoredCount(torch.output) == 0, "Fackeln landen im Rucksack");

            // Switches and missing hut fall back to the backpack.
            prefs.workbenchComponentsToStorage = false;
            Fill(nails, nailCosts, 1, false);
            Check(CraftingService.TryCraft(nails, inventory, 1) && inventory.GetCount(nails.output) == nails.outputAmount, "GPS-Schalter aus: Nägel in den Rucksack");
            prefs.workbenchComponentsToStorage = true;
            prefs.workbenchUsesStorage = false;
            Fill(nails, nailCosts, 1, false);
            Check(CraftingService.TryCraft(nails, inventory, 1) && inventory.GetCount(nails.output) == nails.outputAmount,
                "Werkbank ohne Lager: Nägel in den Rucksack");
            prefs.workbenchUsesStorage = true;
            activeHuts.Clear();
            Fill(nails, nailCosts, 1, false);
            Check(CraftingService.OutputStorage(nails) == null && CraftingService.TryCraft(nails, inventory, 1) &&
                inventory.GetCount(nails.output) == nails.outputAmount, "Ohne Lagerhütte: Nägel in den Rucksack");
            activeHuts.Add(hut);

            // A storage pile at the int limit refuses the craft without consuming anything.
            Fill(nails, nailCosts, 1, false);
            hut.ReturnFromCrafting(nails.output, int.MaxValue);
            Check(CraftingService.GetMaxCraftable(nails, inventory) == 0 && !CraftingService.TryCraft(nails, inventory, 1) &&
                nailCosts.All(c => inventory.GetCount(c.Key) == c.Value), "Volles Lager (Zahlengrenze): nichts verbraucht");

            // Shop: backpack and storage combined.
            inventory.ResetAll();
            hut.RestoreRunState(new[] { new SavedItem { id = (int)Item.Copper, count = 30 }, new SavedItem { id = (int)Item.Nails, count = 7 } });
            inventory.AddStartingItem(copper, 5);
            var stock = ShopManager.OreStock();
            Check(stock.TryGetValue(copper, out int copperStock) && copperStock == 35 && !stock.ContainsKey(nails.output),
                "Shop zeigt Erze aus Rucksack und Lager, keine Nägel");
            Check(ShopManager.StockCount(copper) == 35 && ShopManager.StoredCount(copper) == 30, "Bestand 35, davon 30 im Lager");
            int money = stats.Money;
            Check(shop.TrySell(copper, 8), "8 Kupfer verkauft");
            Check(inventory.GetCount(copper) == 0 && hut.GetStoredCount(copper) == 27, "Zuerst der Rucksack, dann das Lager");
            Check(stats.Money == money + 8 * copper.worth, "Erlös gutgeschrieben");
            Check(!shop.TrySell(copper, 28) && hut.GetStoredCount(copper) == 27, "Mehr als vorhanden wird abgelehnt");

            prefs.shopSellsFromStorage = false;
            Check(ShopManager.StockCount(copper) == 0 && !shop.TrySell(copper, 1) && !ShopManager.OreStock().ContainsKey(copper),
                "GPS-Schalter aus: Shop ignoriert das Lager");
            prefs.shopSellsFromStorage = true;

            inventory.AddStartingItem(copper, 3);
            money = stats.Money;
            int total = shop.SellAll();
            Check(total == 30 * copper.worth && stats.Money == money + total, "Alles verkaufen erfasst auch das Lager");
            Check(inventory.GetCount(copper) == 0 && hut.GetStoredCount(copper) == 0 && hut.GetStoredCount(nails.output) == 7,
                "Kupfer weg, Nägel bleiben im Lager");

            // Energy: 0.25 coins per unit, i.e. 100 energy for 25 coins.
            var monolithObject = new GameObject("StorageComponentsShopChecks Monolith") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                monolithObject.SetActive(false);
                var monolith = monolithObject.AddComponent<EnergyMonolyth>();
                GpsSettings.ApplyComponent(monolith);
                float price = (float)typeof(EnergyMonolyth).GetField("rechargeCost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(monolith);
                Check(Mathf.Approximately(price * 100f, 25f), "100 Energie kosten 25 Coins (" + price + " je Einheit)");
            }
            finally { Object.DestroyImmediate(monolithObject); }

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/StorageComponentsShopChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/StorageComponentsShopChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            prefs.workbenchUsesStorage = usesStorage; prefs.workbenchStorageFirst = storageFirst;
            prefs.workbenchComponentsToStorage = componentsToStorage; prefs.shopSellsFromStorage = sellsFromStorage;
            activeHuts.Clear(); activeHuts.AddRange(previousHuts);
            instanceProperty.SetValue(null, previousInventory);
            StatsManager.Instance = previousStats; AudioManager.Instance = previousAudio;
            Object.DestroyImmediate(hutObject); Object.DestroyImmediate(host);
        }
    }
}
