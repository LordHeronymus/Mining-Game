using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu. Runs in an isolated save/meta profile.
// Checks that the dragged icon in storage, inventory and hotbar matches its slot icon (size, flip, scale, grab point).
// Result in Temp/ItemDragGhostChecks.txt, side-by-side screenshots in Temp/ItemDragGhost/.
public static class ItemDragGhostChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play from MainMenu.";
        var go = new GameObject("Item Drag Ghost Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ItemDragGhostProbe>();
        return "Running drag ghost checks; result in Temp/ItemDragGhostChecks.txt.";
    }
}

public sealed class ItemDragGhostProbe : MonoBehaviour
{
    const string Report = "Temp/ItemDragGhostChecks.txt";
    const string Shots = "Temp/ItemDragGhost";
    static readonly Item[] Pictured = { Item.Ladder, Item.Torche, Item.Dynamite };
    string save, meta, gps;
    bool clean;
    int checks;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
    }

    void Log(string line) => File.AppendAllText(Report, line + "\n");

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory; gps = JsonUtility.ToJson(GpsSettings.Tests);
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/ItemDragGhost-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        GpsSettings.Document.tests.testModeDisabled = true;
        File.WriteAllText(Report, "");
        Directory.CreateDirectory(Shots);
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { Log("FAILED " + error); break; }
            if (!more) break;
            yield return next;
        }
        if (StoragePanel.Instance && StoragePanel.Instance.IsOpen) StoragePanel.Instance.Hide();
        var inventoryUi = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUi && inventoryUi.IsOpen) inventoryUi.HidePanel();
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
        RunNavigation.NewGame("Drag-Ghost-Test"); yield return Ready();
        var storage = Object.FindFirstObjectByType<SurfaceStorageBuilding>();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var inventory = InventoryManager.Instance;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        var hud = Object.FindFirstObjectByType<CompactHud>();
        var inventoryUi = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        Check(storage && player && inventory && catalog && hud && inventoryUi, "Storage, player, inventory, HUD and catalog exist");

        var custom = catalog.items.Where(x => x && x.category != ItemCategory.Powerup && x.icon &&
            (x.iconFlipX || x.iconFlipY || x.iconScale != Vector2.one || x.iconOffset != Vector2.zero)).ToList();
        var pictured = Pictured.Select(id => catalog.items.First(x => x && x.item == id)).ToList();
        Check(pictured.All(x => x.icon), "Ladder, torch and dynamite have icons");
        inventory.ResetAll();
        foreach (var item in pictured.Concat(custom).Distinct()) inventory.Add(item, item.item == Item.Ladder ? 10 : 3);
        Log("Items with own icon layout: " + string.Join(", ", custom.Select(x => x.displayName +
            (x.iconFlipX ? " [flipX]" : "") + (x.iconFlipY ? " [flipY]" : ""))));

        // Storage
        var home = player.transform.position;
        Teleport(player, storage.transform.position + Vector3.up * 1.2f);
        float started = Time.realtimeSinceStartup;
        while (!storage.HasVisitor && Time.realtimeSinceStartup - started < 2) yield return new WaitForFixedUpdate();
        storage.EntryButton.onClick.Invoke();
        var panel = StoragePanel.Instance;
        Check(panel && panel.IsOpen, "Storage panel opens");
        yield return null; Canvas.ForceUpdateCanvases();
        var storageGhost = Field<Image>(panel, "dragGhost");
        foreach (var slot in panel.BackpackSlots.Where(x => x.item).ToList())
        {
            if (pictured.Contains(slot.item)) { Field<ScrollRect>(panel, "backpackScroll").verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases(); }
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            data.position = Screen(slot.icon.rectTransform, ScreenCenter(slot.icon.rectTransform)) + new Vector2(9, -6);
            var grab = data.position;
            panel.BeginDrag(slot, data);
            storageGhost = Field<Image>(panel, "dragGhost");
            Compare("Storage", slot.item, slot.icon, storageGhost, grab, Vector2.zero);
            data.position += new Vector2(190, 10);
            panel.Drag(data);
            Compare("Storage", slot.item, slot.icon, storageGhost, grab, new Vector2(190, 10));
            if (pictured.Contains(slot.item))
            {
                yield return Pop(storageGhost); PopChecks("Storage", slot.item, storageGhost);
                Compare("Storage", slot.item, slot.icon, storageGhost, grab, new Vector2(190, 10));
                yield return Shot("Storage", slot.item, slot.icon.rectTransform, storageGhost.rectTransform);
            }
            data.position = Screen(slot.icon.rectTransform, ScreenCenter(slot.icon.rectTransform));
            panel.Drop(data);
            Check(!storageGhost.gameObject.activeSelf, "Storage ghost hides after drop");
        }
        Log("PASS Storage: " + panel.BackpackSlots.Count(x => x.item) + " items");
        {
            var slot = panel.BackpackSlots.First(x => x.item);
            var item = slot.item; int count = inventory.GetCount(item);
            yield return Hold("Storage", slot.frame.gameObject, Screen(slot.icon.rectTransform, ScreenCenter(slot.icon.rectTransform)),
                () => Field<Image>(panel, "dragGhost"));
            HoldChecks();
            Check(inventory.GetCount(item) == count, "Storage hold: releasing a held item on its own slot moves nothing (click swallowed)");
        }
        storage.Close();
        Teleport(player, home);
        yield return new WaitForFixedUpdate(); yield return null;

        // Inventory
        inventoryUi.ShowPanel();
        yield return null; Canvas.ForceUpdateCanvases();
        Check(inventoryUi.IsOpen, "Inventory opens");
        int inventoryCount = 0;
        foreach (var item in pictured.Concat(custom).Distinct())
        {
            var icon = inventoryUi.GetComponentsInChildren<Image>().FirstOrDefault(x => x.name == "Item" && x.enabled && x.sprite == item.icon &&
                x.transform.parent.GetComponent<InventoryCellDrag>());
            if (!icon) { Check(!pictured.Contains(item), "Inventory shows " + item.displayName); continue; }
            var position = Screen(icon.rectTransform, ScreenCenter(icon.rectTransform)) + new Vector2(-7, 5);
            inventoryUi.BeginItemDrag(item, position, icon);
            var ghost = Field<Image>(inventoryUi, "draggedIcon");
            Compare("Inventory", item, icon, ghost, position, Vector2.zero);
            inventoryUi.MoveDraggedIcon(position + new Vector2(200, -12));
            Compare("Inventory", item, icon, ghost, position, new Vector2(200, -12));
            if (pictured.Contains(item))
            {
                pin = () => inventoryUi.MoveDraggedIcon(position + new Vector2(200, -12));
                yield return Pop(ghost); PopChecks("Inventory", item, ghost);
                inventoryUi.MoveDraggedIcon(position + new Vector2(200, -12));
                Compare("Inventory", item, icon, ghost, position, new Vector2(200, -12));
                yield return Shot("Inventory", item, icon.rectTransform, ghost.rectTransform);
                pin = null;
            }
            inventoryUi.CancelItemDrag();
            inventoryCount++;
        }
        Log("PASS Inventory: " + inventoryCount + " items");
        {
            var cell = inventoryUi.GetComponentsInChildren<InventoryCellDrag>().First(x => x.item?.Invoke());
            yield return Hold("Inventory", cell.gameObject, Screen(cell.icon.rectTransform, ScreenCenter(cell.icon.rectTransform)),
                () => Field<Image>(inventoryUi, "draggedIcon"));
            HoldChecks();
        }

        // Hotbar (while the inventory is open the slots may be reordered); slots are set directly so no layout is saved.
        var originalSlots = (ItemSO[])hud.slots.Clone();
        try
        {
            var hotbarItems = pictured.Concat(custom).Where(CompactHud.IsHotbarItem).Distinct().Take(8).ToList();
            for (int i = 0; i < 8; i++) hud.slots[i] = i < hotbarItems.Count ? hotbarItems[i] : null;
            hud.RefreshHotbarIconLayouts();
            yield return null; Canvas.ForceUpdateCanvases();
            for (int i = 0; i < hotbarItems.Count; i++)
            {
                var icon = Field<Image[]>(hud, "icons")[i];
                var position = Screen(icon.rectTransform, ScreenCenter(icon.rectTransform)) + new Vector2(6, 8);
                Check(hud.BeginHotbarDrag(i + 1, position), "Hotbar drag starts for " + hotbarItems[i].displayName);
                var ghost = Field<Image>(hud, "dragIcon");
                Compare("Hotbar", hotbarItems[i], icon, ghost, position, Vector2.zero);
                var moved = position + new Vector2(0, 230);
                hud.GetType().GetMethod("MoveDraggedIcon", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { moved });
                Compare("Hotbar", hotbarItems[i], icon, ghost, position, new Vector2(0, 230));
                if (pictured.Contains(hotbarItems[i]))
                {
                    pin = () => hud.GetType().GetMethod("MoveDraggedIcon", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { moved });
                    yield return Pop(ghost); PopChecks("Hotbar", hotbarItems[i], ghost);
                    hud.GetType().GetMethod("MoveDraggedIcon", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { moved });
                    Compare("Hotbar", hotbarItems[i], icon, ghost, position, new Vector2(0, 230));
                    yield return Shot("Hotbar", hotbarItems[i], icon.rectTransform, ghost.rectTransform);
                    pin = null;
                }
                hud.CancelHotbarDrag();
            }
            Log("PASS Hotbar: " + hotbarItems.Count + " items");
            {
                var drag = Field<HotbarSlotDrag[]>(hud, "slotDrags")[0];
                var icon = Field<Image[]>(hud, "icons")[0];
                int selected = hud.SelectedSlot;
                yield return Hold("Hotbar", drag.gameObject, Screen(icon.rectTransform, ScreenCenter(icon.rectTransform)),
                    () => Field<Image>(hud, "dragIcon"));
                HoldChecks();
                Check(hud.slots[0] == hotbarItems[0] && hud.SelectedSlot == selected, "Hotbar hold: release on its own slot keeps the slot and selection");
            }

            // GPS (UI → Item ziehen) drives the pop; values are changed in memory only and restored.
            var prefs = GpsSettings.Preferences;
            float popScale = prefs.dragPopScale, carryScale = prefs.dragCarryScale, popDuration = prefs.dragPopDuration;
            try
            {
                prefs.dragPopScale = 1.6f; prefs.dragCarryScale = 1.3f; prefs.dragPopDuration = .45f;
                Check(Mathf.Abs(ItemDragGhost.Lift(.15f) - 1.6f) < .001f, "GPS: pop peaks at the configured strength after a third of the duration");
                Check(Mathf.Abs(ItemDragGhost.Lift(.45f) - 1.3f) < .001f, "GPS: pop ends at the configured carry size after the duration");
                var icon = Field<Image[]>(hud, "icons")[0];
                var position = Screen(icon.rectTransform, ScreenCenter(icon.rectTransform));
                Check(hud.BeginHotbarDrag(1, position), "GPS: hotbar drag starts");
                var ghost = Field<Image>(hud, "dragIcon");
                pin = () => hud.GetType().GetMethod("MoveDraggedIcon", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { position });
                yield return Pop(ghost);
                pin = null;
                float factor = ghost.GetComponent<ItemDragGhost>().Factor;
                Check(popPeak > 1.5f && popPeak <= 1.6001f, $"GPS: configured pop strength reached (peak {popPeak:0.000})");
                Check(Mathf.Abs(factor - 1.3f) < .001f, $"GPS: configured carry size reached ({factor:0.000})");
                hud.CancelHotbarDrag();
                prefs.dragPopDuration = 0f;
                Check(Mathf.Abs(ItemDragGhost.Lift(.01f) - 1.3f) < .001f, "GPS: duration 0 carries at once without pop");
                Log($"PASS GPS: peak {popPeak:0.000}, carried at {factor:0.000}");
            }
            finally { prefs.dragPopScale = popScale; prefs.dragCarryScale = carryScale; prefs.dragPopDuration = popDuration; }

            // Right-click takes an item off the hotbar; the saved layout is restored afterwards.
            var layout = Enumerable.Range(0, 8).Select(i => PlayerPrefs.GetInt("CompactHud.Slot." + i, int.MinValue)).ToArray();
            int layoutVersion = PlayerPrefs.GetInt("CompactHud.SlotLayoutVersion", 0);
            try
            {
                var drags = Field<HotbarSlotDrag[]>(hud, "slotDrags");
                PointerEventData Click(PointerEventData.InputButton button) => new PointerEventData(EventSystem.current) { button = button };
                var before = (ItemSO[])hud.slots.Clone();
                ExecuteEvents.Execute(drags[1].gameObject, Click(PointerEventData.InputButton.Left), ExecuteEvents.pointerClickHandler);
                Check(hud.slots[1] == before[1], "Right-click: left click keeps the item in the hotbar");
                ExecuteEvents.Execute(drags[1].gameObject, Click(PointerEventData.InputButton.Right), ExecuteEvents.pointerClickHandler);
                Check(!hud.slots[1], "Right-click: item leaves the hotbar");
                for (int i = 0; i < 8; i++) if (i != 1) Check(hud.slots[i] == before[i], "Right-click: other slot " + (i + 1) + " unchanged");
                Check(PlayerPrefs.GetInt("CompactHud.Slot.1", 0) == int.MinValue, "Right-click: removal is saved in the hotbar layout");
                yield return null; Canvas.ForceUpdateCanvases();
                Check(!Field<Image[]>(hud, "icons")[1].enabled, "Right-click: slot shows no icon");
                ExecuteEvents.Execute(drags[1].gameObject, Click(PointerEventData.InputButton.Right), ExecuteEvents.pointerClickHandler);
                Check(!hud.slots[1], "Right-click: empty slot stays empty");
                Check(InventoryManager.Instance.GetCount(before[1]) > 0, "Right-click: item stays in the inventory");
                Log("PASS Right-click: " + before[1].displayName + " removed from slot 2");
            }
            finally
            {
                for (int i = 0; i < 8; i++)
                    if (layout[i] == int.MinValue) PlayerPrefs.DeleteKey("CompactHud.Slot." + i); else PlayerPrefs.SetInt("CompactHud.Slot." + i, layout[i]);
                if (layoutVersion == 0) PlayerPrefs.DeleteKey("CompactHud.SlotLayoutVersion"); else PlayerPrefs.SetInt("CompactHud.SlotLayoutVersion", layoutVersion);
                PlayerPrefs.Save();
            }
        }
        finally
        {
            for (int i = 0; i < 8; i++) hud.slots[i] = originalSlots[i];
            hud.RefreshHotbarIconLayouts();
        }
        inventoryUi.HidePanel();
        Log("COMPLETE " + checks + " checks");
    }

    // The ghost must cover the slot icon shifted by the pointer movement, with the same mirroring. While it pops it is
    // enlarged by the current factor around the grab point, so the grabbed spot stays under the cursor.
    void Compare(string ui, ItemSO item, Image source, Image ghost, Vector2 grab, Vector2 pointerDelta)
    {
        Check(ghost && ghost.gameObject.activeInHierarchy, ui + ": ghost visible for " + item.displayName);
        Check(ghost.sprite == source.sprite && ghost.preserveAspect == source.preserveAspect, ui + ": same sprite for " + item.displayName);
        var state = ghost.GetComponent<ItemDragGhost>();
        Check(state && ghost.GetComponent<Shadow>(), ui + ": ghost has pop state and shadow for " + item.displayName);
        float factor = state.Factor;
        var a = Corners(source.rectTransform); var b = Corners(ghost.rectTransform);
        for (int i = 0; i < 4; i++)
        {
            var expected = grab + pointerDelta + (a[i] - grab) * factor;
            Check(Vector2.Distance(expected, b[i]) < 1.5f,
                $"{ui}: corner {i} of {item.displayName} differs (expected {expected}, ghost {b[i]}, factor {factor:0.000})");
        }
        Check(Mathf.Sign(source.rectTransform.lossyScale.x) == Mathf.Sign(ghost.rectTransform.lossyScale.x) &&
              Mathf.Sign(source.rectTransform.lossyScale.y) == Mathf.Sign(ghost.rectTransform.lossyScale.y),
            ui + ": same mirroring for " + item.displayName);
    }

    // Press-and-hold (GPS hotbarHoldDuration, used by all three UIs): no drag before the hold time, drag after it,
    // gone after release; with hold 0 a press alone starts nothing, moving starts the drag. Nested coroutines bypass
    // the error wrapper in Start, so Hold only records and HoldChecks throws.
    readonly List<(bool ok, string label)> holdResults = new();
    IEnumerator Hold(string ui, GameObject target, Vector2 position, Func<Image> ghost)
    {
        holdResults.Clear();
        bool Carried() { var image = ghost(); return image && image.gameObject.activeInHierarchy; }
        void Send<T>(Vector2 at, ExecuteEvents.EventFunction<T> handler) where T : IEventSystemHandler =>
            ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = at }, handler);
        IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        var prefs = GpsSettings.Preferences;
        float original = prefs.hotbarHoldDuration;
        try
        {
            prefs.hotbarHoldDuration = .3f;
            Send(position, ExecuteEvents.pointerDownHandler);
            yield return Wait(.12f);
            holdResults.Add((!Carried(), $"{ui} hold: no drag before the hold time"));
            yield return Wait(.3f);
            holdResults.Add((Carried(), $"{ui} hold: drag mode starts after the hold time without moving"));
            Send(position, ExecuteEvents.pointerUpHandler);
            Send(position, ExecuteEvents.pointerClickHandler);
            holdResults.Add((!Carried(), $"{ui} hold: ghost hides after release"));

            prefs.hotbarHoldDuration = 0f;
            Send(position, ExecuteEvents.pointerDownHandler);
            yield return Wait(.12f);
            holdResults.Add((!Carried(), $"{ui} hold 0: a press alone starts no drag"));
            Send(position + new Vector2(30, 0), ExecuteEvents.dragHandler);
            holdResults.Add((Carried(), $"{ui} hold 0: moving starts the drag at once"));
            Send(position, ExecuteEvents.pointerUpHandler);
            Send(position, ExecuteEvents.pointerClickHandler);
            holdResults.Add((!Carried(), $"{ui} hold 0: ghost hides after release"));
        }
        finally { prefs.hotbarHoldDuration = original; }
    }

    void HoldChecks()
    {
        foreach (var (ok, label) in holdResults) Check(ok, label);
        Log("PASS " + holdResults[0].label.Split(' ')[0] + " hold: " + holdResults.Count + " checks");
    }

    // Drag start pops: the item grows quickly past the carry size, then settles at CarryScale.
    // Nested coroutines bypass the error wrapper in Start, so Pop only measures and PopChecks throws.
    float popPeak;
    // Inventory and hotbar move their ghost to the real mouse every frame (Update/LateUpdate); the test pins its own
    // pointer right before the canvases render, after all of those.
    Action pin;
    void OnEnable() => Canvas.willRenderCanvases += Pin;
    void OnDisable() => Canvas.willRenderCanvases -= Pin;
    void Pin() => pin?.Invoke();
    IEnumerator Pop(Image ghost)
    {
        var state = ghost.GetComponent<ItemDragGhost>();
        float end = Time.realtimeSinceStartup + .6f;
        popPeak = 0f;
        while (Time.realtimeSinceStartup < end) { yield return null; popPeak = Mathf.Max(popPeak, state.Factor); }
    }

    void PopChecks(string ui, ItemSO item, Image ghost)
    {
        float factor = ghost.GetComponent<ItemDragGhost>().Factor;
        Check(popPeak > ItemDragGhost.CarryScale + .05f, $"{ui}: {item.displayName} pops on drag start (peak {popPeak:0.000})");
        Check(Mathf.Abs(factor - ItemDragGhost.CarryScale) < .001f, $"{ui}: {item.displayName} settles at the carry size ({factor:0.000})");
        Log($"{ui} {item.displayName}: peak {popPeak:0.000}, carried at {factor:0.000}");
    }

    IEnumerator Shot(string ui, ItemSO item, RectTransform slot, RectTransform ghost)
    {
        yield return new WaitForEndOfFrame();
        var full = ScreenCapture.CaptureScreenshotAsTexture();
        var points = Corners(slot).Concat(Corners(ghost)).ToList();
        var gi = ghost.GetComponent<Image>(); var gc = gi.canvas;
        Log($"{ui} {item.item} shot: slot {string.Join(" ", Corners(slot))} ghost {string.Join(" ", Corners(ghost))} parent {ghost.parent.name} sib {ghost.GetSiblingIndex()}/{ghost.parent.childCount} canvas {gc.name}/{gc.renderMode}/{gc.sortingOrder} root {gc.rootCanvas.name} active {gi.isActiveAndEnabled} alpha {gi.color.a} cull {gi.canvasRenderer.cull} scale {ghost.lossyScale} frame {Time.frameCount}");
        float scaleX = full.width / (float)UnityEngine.Screen.width, scaleY = full.height / (float)UnityEngine.Screen.height;
        int xMin = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.x) * scaleX) - 30, 0, full.width - 1);
        int yMin = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.y) * scaleY) - 30, 0, full.height - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(points.Max(p => p.x) * scaleX) + 30, xMin + 1, full.width);
        int yMax = Mathf.Clamp(Mathf.CeilToInt(points.Max(p => p.y) * scaleY) + 30, yMin + 1, full.height);
        var crop = new Texture2D(xMax - xMin, yMax - yMin, TextureFormat.RGBA32, false);
        crop.SetPixels(full.GetPixels(xMin, yMin, xMax - xMin, yMax - yMin));
        crop.Apply();
        File.WriteAllBytes(Path.Combine(Shots, ui + "-" + item.item + ".png"), crop.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(Shots, ui + "-" + item.item + "-full.png"), full.EncodeToPNG());
        Destroy(crop); Destroy(full);
    }

    static Camera EventCamera(Transform t)
    {
        var canvas = t.GetComponentInParent<Canvas>().rootCanvas;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    static Vector3 ScreenCenter(RectTransform rect) => rect.TransformPoint(rect.rect.center);
    static Vector2 Screen(RectTransform rect, Vector3 world) => RectTransformUtility.WorldToScreenPoint(EventCamera(rect), world);

    static Vector2[] Corners(RectTransform rect)
    {
        var world = new Vector3[4];
        rect.GetWorldCorners(world);
        var camera = EventCamera(rect);
        return world.Select(w => RectTransformUtility.WorldToScreenPoint(camera, w)).ToArray();
    }

    static T Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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
