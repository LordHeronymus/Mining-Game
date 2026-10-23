using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu. Runs in an isolated save/meta profile; result in Temp/StorageUiChecks.txt.
public static class StorageUiChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play from MainMenu.";
        var go = new GameObject("Storage UI Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<StorageUiProbe>();
        return "Running storage entry sign, chest layout, moves and triple-capacity withdraw checks; result in Temp/StorageUiChecks.txt.";
    }
}

public sealed class StorageUiProbe : MonoBehaviour
{
    const string Report = "Temp/StorageUiChecks.txt";
    string save, meta, gps;
    bool clean;
    int checks;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory; gps = JsonUtility.ToJson(GpsSettings.Tests);
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/StorageUi-" + DateTime.UtcNow.Ticks);
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
        if (StoragePanel.Instance && StoragePanel.Instance.IsOpen) StoragePanel.Instance.Hide();
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
        RunNavigation.NewGame("Lager-Test"); yield return Ready();
        var storage = Object.FindFirstObjectByType<SurfaceStorageBuilding>();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var inventory = InventoryManager.Instance;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        Check(storage && player && inventory && catalog, "Storage hut, player, inventory and item catalog exist");
        var iron = catalog.items.First(x => x && x.item == Item.Iron);
        var coal = catalog.items.First(x => x && x.item == Item.Coal);
        var gold = catalog.items.First(x => x && x.item == Item.Gold);
        var home = player.transform.position;

        inventory.ResetAll();
        storage.RestoreRunState(Array.Empty<SavedItem>());
        Teleport(player, storage.transform.position + Vector3.up * 1.2f);
        float started = Time.realtimeSinceStartup;
        while (!storage.HasVisitor && Time.realtimeSinceStartup - started < 2) yield return new WaitForFixedUpdate();
        Check(storage.HasVisitor, "Player inside the storage trigger");
        started = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - started < .4f) yield return null;
        var entry = storage.EntryButton;
        var entryGroup = storage.EntryGroup;
        Check(entryGroup && entryGroup.alpha > .99f && entryGroup.interactable, "Entry sign fades in near the hut");
        Check(entry.GetComponentInParent<Canvas>().renderMode == RenderMode.WorldSpace, "Entry sign is a world-space button");
        Check(storage.EntrySign.Label.text == "Lager öffnen", "Entry label");
        var workshopSign = Object.FindFirstObjectByType<WorkbenchBuilding>().EntrySign;
        var shopSign = Object.FindFirstObjectByType<ShopBuilding>().EntrySign;
        var energySign = Object.FindFirstObjectByType<EnergyMonolyth>().EntrySign;
        Check(workshopSign && shopSign && workshopSign.Label.text == "Werkbank öffnen" && shopSign.Label.text == "Shop öffnen", "Workshop and shop use the same gold entry sign");
        Check(energySign && energySign.Label.text == "Energie aufladen", "Energy monolith uses the same gold entry sign");
        foreach (var sign in new[] { storage.EntrySign, workshopSign, shopSign, energySign })
        {
            Check(sign.Button.GetComponent<GoldButtonFeedback>() && Mathf.Approximately(sign.Label.fontSize, storage.EntrySign.Label.fontSize), sign.Label.text + ": same style and font size");
            Check(sign.Label.GetPreferredValues(sign.Label.text).x <= sign.Label.rectTransform.rect.width + .5f, sign.Label.text + ": text fits between the end caps");
            System.Func<EntrySign, float> baseScale = s => s.transform.lossyScale.x / (.92f + .08f * s.Group.alpha);
            Check(Mathf.Abs(baseScale(sign) - baseScale(storage.EntrySign)) < .0001f, sign.Label.text + ": same world size on scaled buildings");
            var main = EntrySign.MainSprite(sign.transform.parent);
            var signRect = (RectTransform)sign.transform;
            var visible = EntrySign.VisibleBounds(main);
            Check(signRect.TransformPoint(signRect.rect.min).y > visible.max.y + .3f, sign.Label.text + ": floats above the roof");
            float signCentre = (signRect.TransformPoint(signRect.rect.min).x + signRect.TransformPoint(signRect.rect.max).x) / 2;
            Check(Mathf.Abs(signCentre - visible.center.x) < .02f && visible.size.x < main.bounds.size.x + .001f,
                sign.Label.text + $": centred over the visible building (off by {signCentre - visible.center.x:0.000}, sprite-rect centre {main.bounds.center.x - visible.center.x:0.000})");
        }
        {
            var workshopMain = EntrySign.MainSprite(workshopSign.transform.parent);
            Check(EntrySign.VisibleBounds(workshopMain).center.x - workshopMain.bounds.center.x > .1f, "Workshop: visible art sits right of its image centre and the sign follows it");
        }
        Check(!Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c => c.name == "EnterButtonCanvas" && c.gameObject.activeInHierarchy), "Old Enter signs are hidden");
        Check(entry.GetComponent<GoldButtonFeedback>(), "Entry sign uses the gold wood button style");

        entry.onClick.Invoke();
        var panel = StoragePanel.Instance;
        Check(panel && panel.IsOpen && storage.IsOpen && GameplayInputBlocker.IsBlocked, "Entry opens the storage panel and blocks gameplay");
        yield return null; yield return null;
        Check(entryGroup.alpha < 1 && !entryGroup.blocksRaycasts, "Entry sign fades out and stops taking clicks while the panel is open");
        var layout = panel.transform.Find("Layout");
        var backpackFrame = (RectTransform)layout.Find("Backpack Frame");
        var storageFrame = (RectTransform)layout.Find("Storage Frame");
        Check(backpackFrame.anchoredPosition.x < storageFrame.anchoredPosition.x, "Backpack on the left, storage on the right");
        Check(layout.Find("Background").GetComponent<Image>().sprite.texture.name == "BuyBackground-v2", "Shop background artwork");
        for (int frame = 0; frame < 4; frame++) yield return null;
        var bottom =new[] { "Deposit Ores", "Deposit All", "Withdraw All" }.Select(n => layout.Find(n).GetComponentInChildren<TextMeshProUGUI>()).ToArray();
        Check(bottom.All(x => Mathf.Abs(x.fontSize - bottom[0].fontSize) < .01f) && ((RectTransform)layout.Find("Value")).rect.height == ((RectTransform)layout.Find("Withdraw All")).rect.height,
            "Bottom buttons share size and font size");
        var slotIcon = panel.BackpackSlots[0];
        Check(Vector2.Distance(slotIcon.iconCenter, new Vector2(slotIcon.rect.rect.width / 2, -slotIcon.rect.rect.height / 2)) < .5f &&
            Mathf.Abs(slotIcon.iconRect.rect.width / slotIcon.iconRect.rect.height - 134f / 106f) < .01f, "Storage icons are centred like inventory icons");
        var hud = Object.FindFirstObjectByType<CompactHud>();
        var hotbarIcon = hud.GetComponentsInChildren<Image>(true).First(x => x.name == "Item");
        Check(Mathf.Abs(hotbarIcon.rectTransform.sizeDelta.x / hotbarIcon.rectTransform.sizeDelta.y - 134f / 106f) < .01f, "Hotbar icons use the inventory icon box");

        inventory.Add(iron, 10); inventory.Add(coal, 7); inventory.Add(gold, 3);
        yield return null;
        panel.Click(panel.BackpackSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, false);
        Check(storage.GetStoredCount(iron) == 1 && inventory.GetCount(iron) == 9, "Click deposits one");
        panel.Click(panel.BackpackSlots.First(x => x.item == iron), PointerEventData.InputButton.Right, false);
        Check(storage.GetStoredCount(iron) == 6 && inventory.GetCount(iron) == 4, "Right click deposits half (rounded up)");
        panel.Click(panel.BackpackSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, true);
        Check(storage.GetStoredCount(iron) == 10 && inventory.GetCount(iron) == 0, "Shift click deposits the stack");
        Check(panel.StorageSlots.Any(x => x.item == iron && x.amount == 10) && panel.BackpackSlots.All(x => x.item != iron), "Both grids refresh after moves");
        panel.Click(panel.StorageSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, false);
        Check(storage.GetStoredCount(iron) == 9 && inventory.GetCount(iron) == 1, "Click on the storage side withdraws one");

        panel.SetCategory(1);
        Check(panel.BackpackSlots.Where(x => x.item).All(x => x.item.category == ItemCategory.Ore), "Ore tab filters the grids");
        panel.SetCategory(0); panel.SetSearch(coal.displayName.ToUpperInvariant());
        Check(panel.BackpackSlots.Count(x => x.item) == 1 && panel.BackpackSlots[0].item == coal, "Search filters items");
        panel.SetSearch("");

        Canvas.ForceUpdateCanvases();
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var drop = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, storageFrame.TransformPoint(storageFrame.rect.center))
        };
        panel.BeginDrag(panel.BackpackSlots.First(x => x.item == coal), drop); panel.Drop(drop);
        Check(storage.GetStoredCount(coal) == 7 && inventory.GetCount(coal) == 0, "Dragging onto the storage side moves the whole stack");

        panel.DepositOres();
        Check(inventory.GetCount(iron) == 0 && inventory.GetCount(gold) == 0, "'Erze einlagern' stores all ores");
        panel.WithdrawAll();
        Check(storage.StoredItems.Count == 0 && inventory.GetCount(coal) == 7 && inventory.GetCount(iron) == 10, "'Alles entnehmen' empties the storage when the weight fits");
        panel.DepositAll();
        Check(inventory.GetCount(coal) == 0 && inventory.GetCount(iron) == 0 && inventory.GetCount(gold) == 0, "'Alles einlagern' stores resources");

        if (iron.EffectiveWeight > 0)
        {
            inventory.ResetAll();
            double weight = iron.EffectiveWeight;
            int fit = (int)Math.Floor(inventory.MaximumWeight / weight + 1e-6);
            storage.RestoreRunState(new[] { new SavedItem { id = (int)Item.Iron, count = fit + 25 } });
            yield return null;
            panel.Click(panel.StorageSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, true);
            Check(inventory.GetCount(iron) == fit && storage.GetStoredCount(iron) == 25, $"Withdraw stops exactly at three times the carrying capacity ({fit} iron)");
            Check(inventory.TotalWeight > inventory.CarryingCapacity && inventory.TotalWeight <= inventory.MaximumWeight + 1e-4,
                "Player may carry above the capacity (slower) but not above the maximum");
            Check(Mathf.Abs(inventory.MaximumWeight - inventory.CarryingCapacity * 3) < .001f, "Maximum is three times the capacity");
            Check(storage.WithdrawUpTo(iron, 1) == 0, "No further withdraw at the maximum");
            yield return null;
            var slower = (RectTransform)layout.Find("Weight Bar/Slower");
            var normal = (RectTransform)layout.Find("Weight Bar/Normal");
            Check(normal.sizeDelta.x > 1 && slower.sizeDelta.x > 1, "Weight bar shows the slower zone above the capacity");
        }

        storage.Close();
        Check(!panel.IsOpen && !GameplayInputBlocker.IsBlocked, "Close unblocks gameplay");
        entry.onClick.Invoke();
        Check(panel.IsOpen, "Panel reopens");
        Teleport(player, home);
        started = Time.realtimeSinceStartup;
        while (panel.IsOpen && Time.realtimeSinceStartup - started < 2) yield return new WaitForFixedUpdate();
        Check(!panel.IsOpen && !GameplayInputBlocker.IsBlocked, "Walking away closes the storage");
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
    }

    static void Teleport(PlayerMovement player, Vector3 position)
    {
        var body = player.GetComponent<Rigidbody2D>();
        player.transform.position = position;
        if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta; GameSaveSystem.TestDirectory = save;
        JsonUtility.FromJsonOverwrite(gps, GpsSettings.Document.tests);
        Object.Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
