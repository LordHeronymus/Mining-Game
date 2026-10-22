using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu. Runs in an isolated save/meta profile; result in Temp/WorkbenchStorageChecks.txt.
public static class WorkbenchStorageChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play from MainMenu.";
        var go = new GameObject("Workbench Storage Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WorkbenchStorageProbe>();
        return "Running workbench crafting with storage materials; result in Temp/WorkbenchStorageChecks.txt.";
    }
}

public sealed class WorkbenchStorageProbe : MonoBehaviour
{
    const string Report = "Temp/WorkbenchStorageChecks.txt";
    string save, meta, gps;
    bool usesStorage, storageFirst, clean;
    int checks;
    GameObject extraStorage;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory; gps = JsonUtility.ToJson(GpsSettings.Tests);
        usesStorage = GpsSettings.Preferences.workbenchUsesStorage; storageFirst = GpsSettings.Preferences.workbenchStorageFirst;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/WorkbenchStorage-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        GpsSettings.Document.tests.testModeDisabled = true;
        File.WriteAllText(Report, "");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { File.AppendAllText(Report, "FAILED " + error + "\n"); break; }
            if (!more) break;
            yield return next;
        }
        if (extraStorage) Destroy(extraStorage);
        var workbench = Object.FindFirstObjectByType<WorkbenchPanel>();
        if (workbench && workbench.IsOpen) workbench.ShowPanel(false);
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    IEnumerator Ready()
    {
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        yield return null; yield return null;
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Werkbank-Lager-Test"); yield return Ready();
        var prefs = GpsSettings.Preferences;
        prefs.workbenchUsesStorage = true; prefs.workbenchStorageFirst = false;
        var storage = Object.FindFirstObjectByType<SurfaceStorageBuilding>();
        var inventory = InventoryManager.Instance;
        var panel = Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        Check(storage && inventory && panel, "Storage hut, inventory and workbench exist");
        Check(SurfaceStorageBuilding.Active.Count == 1 && SurfaceStorageBuilding.Active[0] == storage, "The generated hut is the only active storage");
        var nails = panel.recipes.First(r => r && r.name == "Nails");
        bool validCosts = nails.TryGetCosts(out var costs);
        Check(RecipeUnlocks.IsUnlocked(nails) && validCosts && costs.Count == 1, "Nails recipe is unlocked with one ingredient");
        var material = costs.Keys.First();
        int cost = costs[material], output = nails.outputAmount;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        var filler = catalog.items.First(x => x && x.item == Item.Gold);

        int crafted = 0, gained = 0;
        Action<CraftingRecipe, CraftOutcome> onCrafted = (r, o) => crafted++;
        Action<ItemSO, int> onGained = (item, amount) => { if (item == nails.output) gained += amount; };
        CraftingService.Crafted += onCrafted; inventory.OnItemGained += onGained;
        try
        {
            // Only the storage holds materials.
            Reset(inventory, storage, 0, material, 10 * cost);
            Check(CraftingService.GetAvailableCount(material, inventory) == 10 * cost, "Available count is backpack plus storage");
            Check(CraftingService.GetMaxCraftable(nails, inventory) == 10, "Ten batches craftable from the storage alone");
            Check(CraftingService.TryCraft(nails, inventory, 1), "Craft with an empty backpack succeeds");
            Check(storage.GetStoredCount(material) == 9 * cost && inventory.GetCount(material) == 0 && inventory.GetCount(nails.output) == output,
                "Material leaves the storage, nails land in the backpack");
            Check(crafted == 1 && gained == output, "One craft event and one item-gained notice");

            // Backpack first (default).
            Reset(inventory, storage, cost, material, 3 * cost);
            Check(CraftingService.TryCraft(nails, inventory, 2), "Backpack-first craft of two batches");
            Check(inventory.GetCount(material) == 0 && storage.GetStoredCount(material) == 2 * cost, "Backpack is used up before the storage");

            // Storage first.
            prefs.workbenchStorageFirst = true;
            Reset(inventory, storage, 2 * cost, material, 3 * cost);
            Check(CraftingService.TryCraft(nails, inventory, 2), "Storage-first craft of two batches");
            Check(inventory.GetCount(material) == 2 * cost && storage.GetStoredCount(material) == cost, "Storage is used up before the backpack");
            Check(CraftingService.TryCraft(nails, inventory, 2), "Storage-first craft continues with the backpack");
            Check(inventory.GetCount(material) == cost && storage.GetStoredCount(material) == 0, "Remainder comes from the backpack");
            prefs.workbenchStorageFirst = false;

            // Not enough in total: nothing changes.
            Reset(inventory, storage, cost, material, cost);
            crafted = 0;
            Check(CraftingService.GetMaxCraftable(nails, inventory) == 2 && !CraftingService.TryCraft(nails, inventory, 3), "Three batches refused with two available");
            Check(inventory.GetCount(material) == cost && storage.GetStoredCount(material) == cost && crafted == 0, "Refused craft changes nothing");

            // Storage use switched off in GPS.
            prefs.workbenchUsesStorage = false;
            Reset(inventory, storage, 0, material, 10 * cost);
            Check(CraftingService.GetMaxCraftable(nails, inventory) == 0 && !CraftingService.TryCraft(nails, inventory, 1) &&
                storage.GetStoredCount(material) == 10 * cost, "GPS switch off ignores the storage");
            prefs.workbenchUsesStorage = true;

            // No storage hut.
            storage.gameObject.SetActive(false);
            Check(SurfaceStorageBuilding.Active.Count == 0 && CraftingService.GetMaxCraftable(nails, inventory) == 0, "Without a storage hut only the backpack counts");
            storage.gameObject.SetActive(true);
            Check(SurfaceStorageBuilding.Active.Contains(storage), "Re-enabled hut counts again");

            // Two storage huts.
            extraStorage = new GameObject("Second Storage Probe", typeof(BoxCollider2D));
            extraStorage.GetComponent<BoxCollider2D>().isTrigger = true;
            extraStorage.transform.position = new Vector3(-400, 400, 0);
            var second = extraStorage.AddComponent<SurfaceStorageBuilding>();
            Reset(inventory, storage, 0, material, cost);
            second.RestoreRunState(new[] { new SavedItem { id = (int)material.item, count = 2 * cost } });
            Check(SurfaceStorageBuilding.Active.Count == 2 && CraftingService.GetMaxCraftable(nails, inventory) == 3, "Both huts count towards the sum");
            Check(CraftingService.TryCraft(nails, inventory, 2), "Craft across two huts");
            Check(storage.GetStoredCount(material) == 0 && second.GetStoredCount(material) == cost, "Huts are emptied in order, nothing taken twice");
            Destroy(extraStorage); extraStorage = null;
            yield return null;
            Check(SurfaceStorageBuilding.Active.Count == 1, "Destroyed hut leaves the list");

            // One atomic inventory notification that already sees the final storage contents.
            Reset(inventory, storage, cost, material, 2 * cost);
            int notifications = 0;
            Action listener = () =>
            {
                notifications++;
                Check(inventory.GetCount(material) == 0 && storage.GetStoredCount(material) == cost && inventory.GetCount(nails.output) == 2 * output,
                    "Inventory listener sees backpack and storage after the craft");
            };
            inventory.OnInventoryChanged += listener;
            try { Check(CraftingService.TryCraft(nails, inventory, 2), "Mixed craft succeeds"); }
            finally { inventory.OnInventoryChanged -= listener; }
            Check(notifications == 1, "Exactly one inventory notification");

            // Save state reflects the craft.
            var savedStorage = storage.CaptureRunState();
            var savedInventory = inventory.CaptureRunState();
            Check(savedStorage.Single(x => x.id == (int)material.item).count == cost && savedInventory.items.All(x => x.id != (int)material.item) &&
                savedInventory.items.Single(x => x.id == (int)nails.output.item).count == 2 * output, "Captured run state has the new backpack and storage contents");

            // Weight: storage materials add the output weight without lightening the backpack.
            if (nails.output.EffectiveWeight > 0 && filler.EffectiveWeight > 0)
                foreach (bool first in new[] { false, true })
                {
                    prefs.workbenchStorageFirst = first;
                    foreach (int carried in new[] { 0, 3, 40 })
                    {
                        Reset(inventory, storage, carried * cost, material, 200 * cost);
                        double slack = nails.output.EffectiveWeight * output * 5.5;
                        int fill = (int)Math.Floor((inventory.MaximumWeight - inventory.TotalWeight - slack) / filler.EffectiveWeight);
                        if (fill > 0) inventory.AddStartingItem(filler, fill);
                        int expected = 0;
                        for (int n = 1; n <= carried + 200; n++) if (Fits(nails, inventory, n, cost, output, material)) expected = n; else if (!first) break;
                        int max = CraftingService.GetMaxCraftable(nails, inventory);
                        Check(max > 0 && max <= carried + 200 && CraftingService.CanCraft(nails, inventory, max) && !CraftingService.CanCraft(nails, inventory, max + 1) &&
                            (first || max == expected), $"Weight limit respected (storage first {first}, {carried} carried, max {max})");
                        double before = inventory.TotalWeight;
                        Check(CraftingService.TryCraft(nails, inventory, max), "Craft at the weight limit succeeds");
                        Check(inventory.TotalWeight <= inventory.MaximumWeight + 1e-4 || inventory.TotalWeight <= before + 1e-9,
                            "Craft at the weight limit stays within the maximum");
                    }
                }
            prefs.workbenchStorageFirst = false;

            // Workbench panel shows the storage share and crafts from it.
            Reset(inventory, storage, cost, material, 4 * cost);
            panel.ShowPanel(true);
            panel.SetCategory(CraftingRecipe.RecipeCategory.Automatic); panel.SetSearch(""); panel.SetFavoritesOnly(false); panel.SetFilter(true);
            panel.SelectRecipe(nails);
            yield return null;
            Check(panel.IsOpen && panel.SelectedRecipe == nails, "Workbench opens with the nails recipe under 'Herstellbar'");
            var details = panel.transform.Find("Layout/Details");
            var countLabel = details.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.text.Contains(" / " + cost));
            Check(countLabel && countLabel.text.Contains("+" + 4 * cost + " Lager"), "Ingredient row shows the storage share: " + (countLabel ? countLabel.text : "missing"));
            panel.SetQuantity(99);
            Check(panel.Quantity == 5, "Max quantity uses backpack plus storage");
            var craftButton = details.GetComponentsInChildren<Button>(true).First(b => b.name == "Craft");
            Check(craftButton.interactable, "Craft button active");
            Check(panel.CraftSelected(), "Craft from the workbench starts");
            float end = Time.realtimeSinceStartup + 10;
            while (inventory.GetCount(nails.output) == 0 && Time.realtimeSinceStartup < end) yield return null;
            Check(inventory.GetCount(nails.output) == 5 * output && inventory.GetCount(material) == 0 && storage.GetStoredCount(material) == 0,
                "Workbench craft consumes backpack and storage");
            panel.ShowPanel(false);
        }
        finally
        {
            CraftingService.Crafted -= onCrafted; inventory.OnItemGained -= onGained;
            prefs.workbenchUsesStorage = usesStorage; prefs.workbenchStorageFirst = storageFirst;
        }
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
    }

    static bool Fits(CraftingRecipe recipe, InventoryManager inventory, int batches, int cost, int output, ItemSO material)
    {
        int needed = cost * batches, owned = inventory.GetCount(material), stored = CraftingService.GetStoredCount(material);
        if (needed > owned + stored) return false;
        int fromInventory = CraftingService.StorageFirst ? Mathf.Clamp(needed - stored, 0, owned) : Mathf.Min(owned, needed);
        return inventory.CanFitWeight(recipe.output.EffectiveWeight * (double)output * batches - material.EffectiveWeight * (double)fromInventory);
    }

    static void Reset(InventoryManager inventory, SurfaceStorageBuilding storage, int carried, ItemSO material, int stored)
    {
        inventory.ResetAll();
        if (carried > 0) inventory.AddStartingItem(material, carried);
        storage.RestoreRunState(stored > 0 ? new[] { new SavedItem { id = (int)material.item, count = stored } } : Array.Empty<SavedItem>());
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        GpsSettings.Preferences.workbenchUsesStorage = usesStorage; GpsSettings.Preferences.workbenchStorageFirst = storageFirst;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta; GameSaveSystem.TestDirectory = save;
        JsonUtility.FromJsonOverwrite(gps, GpsSettings.Document.tests);
        Object.Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
