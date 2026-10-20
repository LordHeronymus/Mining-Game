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
            panel.BeginDrag(slot, data);
            storageGhost = Field<Image>(panel, "dragGhost");
            Compare("Storage", slot.item, slot.icon, storageGhost, Vector2.zero);
            data.position += new Vector2(190, 10);
            panel.Drag(data);
            Compare("Storage", slot.item, slot.icon, storageGhost, new Vector2(190, 10));
            if (pictured.Contains(slot.item)) yield return Shot("Storage", slot.item, slot.icon.rectTransform, storageGhost.rectTransform);
            data.position = Screen(slot.icon.rectTransform, ScreenCenter(slot.icon.rectTransform));
            panel.Drop(data);
            Check(!storageGhost.gameObject.activeSelf, "Storage ghost hides after drop");
        }
        Log("PASS Storage: " + panel.BackpackSlots.Count(x => x.item) + " items");
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
            Compare("Inventory", item, icon, ghost, Vector2.zero);
            inventoryUi.MoveDraggedIcon(position + new Vector2(200, -12));
            Compare("Inventory", item, icon, ghost, new Vector2(200, -12));
            if (pictured.Contains(item)) yield return Shot("Inventory", item, icon.rectTransform, ghost.rectTransform);
            inventoryUi.CancelItemDrag();
            inventoryCount++;
        }
        Log("PASS Inventory: " + inventoryCount + " items");

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
                Compare("Hotbar", hotbarItems[i], icon, ghost, Vector2.zero);
                var moved = position + new Vector2(0, 230);
                hud.GetType().GetMethod("MoveDraggedIcon", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { moved });
                Compare("Hotbar", hotbarItems[i], icon, ghost, new Vector2(0, 230));
                if (pictured.Contains(hotbarItems[i])) yield return Shot("Hotbar", hotbarItems[i], icon.rectTransform, ghost.rectTransform);
                hud.CancelHotbarDrag();
            }
            Log("PASS Hotbar: " + hotbarItems.Count + " items");
        }
        finally
        {
            for (int i = 0; i < 8; i++) hud.slots[i] = originalSlots[i];
            hud.RefreshHotbarIconLayouts();
        }
        inventoryUi.HidePanel();
        Log("COMPLETE " + checks + " checks");
    }

    // The ghost must cover the slot icon shifted by the pointer movement, with the same mirroring.
    void Compare(string ui, ItemSO item, Image source, Image ghost, Vector2 pointerDelta)
    {
        Check(ghost && ghost.gameObject.activeInHierarchy, ui + ": ghost visible for " + item.displayName);
        Check(ghost.sprite == source.sprite && ghost.preserveAspect == source.preserveAspect, ui + ": same sprite for " + item.displayName);
        var a = Corners(source.rectTransform); var b = Corners(ghost.rectTransform);
        for (int i = 0; i < 4; i++)
            Check(Vector2.Distance(a[i] + pointerDelta, b[i]) < 1.5f,
                $"{ui}: corner {i} of {item.displayName} differs (slot {a[i] + pointerDelta}, ghost {b[i]})");
        Check(Mathf.Sign(source.rectTransform.lossyScale.x) == Mathf.Sign(ghost.rectTransform.lossyScale.x) &&
              Mathf.Sign(source.rectTransform.lossyScale.y) == Mathf.Sign(ghost.rectTransform.lossyScale.y),
            ui + ": same mirroring for " + item.displayName);
    }

    IEnumerator Shot(string ui, ItemSO item, RectTransform slot, RectTransform ghost)
    {
        yield return new WaitForEndOfFrame();
        var full = ScreenCapture.CaptureScreenshotAsTexture();
        var points = Corners(slot).Concat(Corners(ghost)).ToList();
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
