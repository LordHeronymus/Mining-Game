using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class SurfaceStorageBuilding : MonoBehaviour
{
    readonly Dictionary<ItemSO, int> stored = new();
    readonly HashSet<Collider2D> visitors = new();
    Vector2 inventoryScroll;
    Vector2 storageScroll;
    bool open;

    public bool IsOpen => open;
    public IReadOnlyDictionary<ItemSO, int> StoredItems => stored;
    public event Action StorageChanged;

    public static SurfaceStorageBuilding CreateNearShop(ShopBuilding shop, Sprite sprite)
    {
        if (!shop || !sprite) return null;
        var root = new GameObject("Storage Hut");
        root.transform.SetParent(shop.transform.parent, false);
        root.transform.position = new Vector3(-13.5f, 0f, 0f);
        var visual = new GameObject("Sprite", typeof(SpriteRenderer));
        visual.transform.SetParent(root.transform, false);
        var renderer = visual.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        var shopRenderer = shop.GetComponentInChildren<SpriteRenderer>(true);
        if (shopRenderer)
        {
            renderer.sharedMaterial = shopRenderer.sharedMaterial;
            renderer.sortingLayerID = shopRenderer.sortingLayerID;
            renderer.sortingOrder = shopRenderer.sortingOrder;
            root.transform.position += Vector3.up * (shopRenderer.bounds.min.y - renderer.bounds.min.y);
        }
        var trigger = root.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(sprite.bounds.size.x + .5f, sprite.bounds.size.y + .5f);
        trigger.offset = new Vector2(0f, sprite.bounds.center.y + .2f);
        return root.AddComponent<SurfaceStorageBuilding>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMovement>()) visitors.Add(other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        visitors.Remove(other);
        if (visitors.Count == 0) Close();
    }

    void Update()
    {
        visitors.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
        if (open && (visitors.Count == 0 || Input.GetKeyDown(KeyCode.Escape))) Close();
    }

    public void Open()
    {
        if (open || visitors.Count == 0 || GameplayInputBlocker.IsBlocked) return;
        open = true;
        GameplayInputBlocker.SetBlocked(this, true);
    }

    public void Close()
    {
        open = false;
        GameplayInputBlocker.SetBlocked(this, false);
    }

    void OnDisable()
    {
        Close();
        visitors.Clear();
    }

    public int GetStoredCount(ItemSO item) => item && stored.TryGetValue(item, out int count) ? count : 0;

    public SavedItem[] CaptureRunState() => stored.Where(e => e.Key && e.Value > 0)
        .Select(e => new SavedItem { id = (int)e.Key.item, count = e.Value }).ToArray();
    public void RestoreRunState(SavedItem[] state)
    {
        stored.Clear();
        foreach (var entry in state ?? System.Array.Empty<SavedItem>())
        { var item = StartingResourcesSettings.Resolve(entry.id); if (item && entry.count > 0) stored[item] = entry.count; }
        StorageChanged?.Invoke();
    }

    public bool TryDeposit(ItemSO item, int amount)
    {
        var inventory = InventoryManager.Instance;
        if (!item || item.category == ItemCategory.Powerup || amount <= 0 || !inventory ||
            inventory.GetCount(item) < amount || GetStoredCount(item) > int.MaxValue - amount ||
            !inventory.TryRemove(item, amount)) return false;
        stored[item] = GetStoredCount(item) + amount;
        StorageChanged?.Invoke();
        return true;
    }

    public bool TryWithdraw(ItemSO item, int amount)
    {
        var inventory = InventoryManager.Instance;
        int count = GetStoredCount(item);
        if (amount <= 0 || count < amount || !inventory || !inventory.CanAdd(item, amount)) return false;
        if (!inventory.Add(item, amount)) return false;
        if (count == amount) stored.Remove(item);
        else stored[item] = count - amount;
        StorageChanged?.Invoke();
        return true;
    }

    public void ResetForNewRun()
    {
        if (stored.Count == 0) return;
        stored.Clear();
        StorageChanged?.Invoke();
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!open)
        {
            if (visitors.Count == 0 || GameplayInputBlocker.IsBlocked || !Camera.main) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3.8f);
            if (screen.z > 0f && GUI.Button(new Rect(screen.x - 75f, Screen.height - screen.y - 22f, 150f, 44f), "Lager öffnen"))
                Open();
            return;
        }

        float width = Mathf.Min(660f, Screen.width - 24f);
        float height = Mathf.Min(600f, Screen.height - 24f);
        GUILayout.BeginArea(new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f,
            width, height), GUI.skin.window);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Lager", GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Schließen", GUILayout.Width(100f))) Close();
        GUILayout.EndHorizontal();

        var inventory = InventoryManager.Instance;
        GUILayout.Label("Inventar");
        inventoryScroll = GUILayout.BeginScrollView(inventoryScroll, GUILayout.Height((height - 100f) * .5f));
        if (inventory)
            foreach (var entry in inventory.GetSnapshot().Where(e => e.Key && e.Value > 0 &&
                         e.Key.category != ItemCategory.Powerup).OrderBy(e => e.Key.displayName).ToArray())
                DrawRow(entry.Key, entry.Value, false);
        GUILayout.EndScrollView();

        GUILayout.Label("Eingelagert");
        storageScroll = GUILayout.BeginScrollView(storageScroll, GUILayout.Height((height - 100f) * .5f));
        foreach (var entry in stored.Where(e => e.Key && e.Value > 0)
                     .OrderBy(e => e.Key.displayName).ToArray())
            DrawRow(entry.Key, entry.Value, true);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawRow(ItemSO item, int count, bool withdraw)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(item.displayName + "  × " + count, GUILayout.ExpandWidth(true));
        if (GUILayout.Button(withdraw ? "Entnehmen 1" : "Einlagern 1", GUILayout.Width(112f)))
        {
            if (withdraw) TryWithdraw(item, 1);
            else TryDeposit(item, 1);
        }
        if (GUILayout.Button("Alle", GUILayout.Width(58f)))
        {
            if (withdraw) TryWithdraw(item, count);
            else TryDeposit(item, count);
        }
        GUILayout.EndHorizontal();
    }
}
