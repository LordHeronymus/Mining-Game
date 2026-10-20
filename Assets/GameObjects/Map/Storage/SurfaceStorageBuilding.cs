using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class SurfaceStorageBuilding : MonoBehaviour
{
    const float EntryFadeDuration = .12f;
    readonly Dictionary<ItemSO, int> stored = new();
    readonly HashSet<Collider2D> visitors = new();
    CanvasGroup entryGroup;
    Button entryButton;
    RectTransform entryCanvas;
    Material entryFontMaterial;
    int entryLayer = -1;
    float entryBaseY;

    public bool IsOpen => StoragePanel.Instance && StoragePanel.Instance.IsOpen && StoragePanel.Instance.Storage == this;
    public bool HasVisitor => visitors.Count > 0;
    public Button EntryButton => entryButton;
    public CanvasGroup EntryGroup => entryGroup;
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

    void Start() => BuildEntryButton();

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
        if (IsOpen && visitors.Count == 0) Close();
    }

    void LateUpdate()
    {
        if (!entryGroup) return;
        bool visible = visitors.Count > 0 && !GameplayInputBlocker.IsBlocked;
        entryGroup.alpha = Mathf.MoveTowards(entryGroup.alpha, visible ? 1 : 0, Time.unscaledDeltaTime / EntryFadeDuration);
        // Stay interactable while fading out so GoldButtonFeedback does not grey the sign; only clicks stop.
        entryGroup.blocksRaycasts = visible;
        entryGroup.interactable = visible || entryGroup.alpha > 0;
        // Pops in slightly and floats gently so the sign reads as an inviting interaction.
        float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * .04f * entryGroup.alpha;
        entryCanvas.localScale = Vector3.one * (.0092f + .0008f * entryGroup.alpha);
        entryCanvas.localPosition = new Vector3(entryCanvas.localPosition.x, entryBaseY + bob, 0);
    }

    void BuildEntryButton()
    {
        if (entryGroup) return;
        var sprite = GetComponentInChildren<SpriteRenderer>();
        if (!sprite) return;
        if (entryLayer < 0)
        {
            var shop = FindFirstObjectByType<ShopBuilding>();
            var shopCanvas = shop ? shop.GetComponentInChildren<Canvas>(true) : null;
            entryLayer = shopCanvas ? shopCanvas.gameObject.layer : gameObject.layer;
        }
        var canvasObject = new GameObject("EnterButtonCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.layer = entryLayer;
        entryCanvas = (RectTransform)canvasObject.transform;
        entryCanvas.SetParent(transform, false);
        entryCanvas.sizeDelta = new Vector2(330, 92);
        entryBaseY = sprite.bounds.max.y - transform.position.y + .45f;
        entryCanvas.localPosition = new Vector3(sprite.bounds.center.x - transform.position.x, entryBaseY, 0);
        entryCanvas.localScale = Vector3.one * .01f;
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingLayerID = sprite.sortingLayerID;
        canvas.sortingOrder = sprite.sortingOrder + 2;

        var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.layer = canvasObject.layer;
        var rect = (RectTransform)buttonObject.transform;
        rect.SetParent(entryCanvas, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var button = entryButton = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        GoldButtonFeedback.Apply(button, null, null);
        button.onClick.AddListener(Open);

        // The label sits beside the button so GoldButtonFeedback does not shrink it to the narrow centre of the plaque.
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.gameObject.layer = canvasObject.layer;
        label.rectTransform.SetParent(entryCanvas, false);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(44, 8); label.rectTransform.offsetMax = new Vector2(-44, -8);
        var font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (font)
        {
            label.font = font;
            entryFontMaterial = new Material(font.material);
            entryFontMaterial.SetFloat("_OutlineWidth", .065f);
            entryFontMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
            label.fontSharedMaterial = entryFontMaterial;
        }
        label.text = "Lager öffnen";
        label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = 44; label.color = new Color32(255, 245, 229, 255);
        label.alignment = TextAlignmentOptions.Midline;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        entryGroup = canvasObject.GetComponent<CanvasGroup>();
        entryGroup.alpha = 0;
        entryGroup.interactable = entryGroup.blocksRaycasts = false;
    }

    public void Open()
    {
        if (IsOpen || visitors.Count == 0 || GameplayInputBlocker.IsBlocked) return;
        var panel = StoragePanel.Ensure();
        if (panel) panel.Show(this);
    }

    public void Close()
    {
        if (IsOpen) StoragePanel.Instance.Hide();
    }

    void OnDisable()
    {
        Close();
        visitors.Clear();
        if (entryGroup) { entryGroup.alpha = 0; entryGroup.interactable = entryGroup.blocksRaycasts = false; }
    }

    void OnDestroy()
    {
        if (entryFontMaterial) Destroy(entryFontMaterial);
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
        if (!inventory.AddTransferred(item, amount)) return false;
        if (count == amount) stored.Remove(item);
        else stored[item] = count - amount;
        StorageChanged?.Invoke();
        return true;
    }

    // The player can carry up to the maximum weight (three times the carrying capacity); above the
    // capacity they only move slower, so withdrawals are limited by the maximum, not the capacity.
    public int MaxWithdrawable(ItemSO item)
    {
        var inventory = InventoryManager.Instance;
        int count = GetStoredCount(item);
        if (!inventory || count <= 0) return 0;
        int amount = Mathf.Min(count, int.MaxValue - inventory.GetCount(item));
        double weight = item.EffectiveWeight;
        if (!GameplayTestSettings.NoWeight && weight > 0)
            amount = (int)Math.Min(amount, Math.Max(0, Math.Floor((inventory.MaximumWeight - inventory.TotalWeight + .00001d) / weight)));
        // Floating point rounding can leave the estimate one item above what CanAdd accepts.
        for (int i = 0; i < 3 && amount > 0 && !inventory.CanAdd(item, amount); i++) amount--;
        return amount > 0 && inventory.CanAdd(item, amount) ? amount : 0;
    }

    public int WithdrawUpTo(ItemSO item, int amount)
    {
        amount = Mathf.Min(amount, MaxWithdrawable(item));
        return amount > 0 && TryWithdraw(item, amount) ? amount : 0;
    }

    public void ResetForNewRun()
    {
        if (stored.Count == 0) return;
        stored.Clear();
        StorageChanged?.Invoke();
    }
}
