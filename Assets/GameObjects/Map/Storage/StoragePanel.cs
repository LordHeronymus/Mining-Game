using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Chest-style storage window: backpack on the left, storage hut on the right, in the shop/workbench style.
[RequireComponent(typeof(CanvasGroup))]
public sealed class StoragePanel : MonoBehaviour
{
    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Muted = new Color32(224, 207, 187, 255);
    static readonly Color Gold = new Color32(255, 199, 70, 255);
    static readonly Color Overload = new Color32(232, 134, 101, 255);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    const float LayoutWidth = 1640, LayoutHeight = 924;
    const float Cell = 128, ColumnPitch = 140, RowPitch = 138, GridWidth = 548, GridHeight = 404;
    const int Columns = 4, VisibleSlots = 12;
    const float WeightBarWidth = 572;
    static readonly string[] Tabs = { "Alle", "Erze", "Werkzeuge", "Verbrauch", "Material" };
    static readonly int[] TabCategories = { 0, 1, 2, 3, 5 };
    public const string ControlsHint = "Klick: 1 Stück  ·  Shift+Klick: ganzer Stapel  ·  Rechtsklick: Hälfte  ·  Ziehen: ganzer Stapel";

    public static StoragePanel Instance { get; private set; }
    public bool IsOpen { get; private set; }
    public SurfaceStorageBuilding Storage { get; private set; }
    public int Category { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public IReadOnlyList<Slot> BackpackSlots => backpackSlots;
    public IReadOnlyList<Slot> StorageSlots => storageSlots;

    public sealed class Slot
    {
        public bool fromStorage;
        public RectTransform rect, iconRect, badge;
        public Image frame, icon;
        public Button button;
        public TextMeshProUGUI count;
        public Vector2 iconCenter;
        public ItemSO item;
        public int amount;
    }

    TMP_FontAsset font;
    Material fontMaterial;
    Sprite rowSprite, selectedRowSprite, backgroundSprite;
    readonly List<Sprite> styleSprites = new();
    readonly List<Slot> backpackSlots = new(), storageSlots = new();
    readonly List<Image> tabs = new();
    CanvasGroup group;
    RectTransform layout, backpackContent, storageContent, backpackFrame, storageFrame;
    RectTransform normalFill, overloadFill, capacityMarker;
    ScrollRect backpackScroll, storageScroll;
    TextMeshProUGUI weightText, weightHint, storageSummary, valueText, hintText;
    TMP_InputField searchField;
    Image dragGhost;
    Slot dragged;
    Vector3 dragGrabOffset;
    InventoryManager inventory;
    SurfaceStorageBuilding subscribedStorage;
    string hoverHint;
    float weightWarning;
    bool dirty;
    readonly List<TextMeshProUGUI> actionLabels = new();
    int actionLabelFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => Instance = null;

    public static StoragePanel Ensure()
    {
        if (Instance) return Instance;
        var host = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var parent = host ? host.transform.parent : null;
        if (!parent)
        {
            var canvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace);
            parent = canvas ? canvas.transform : null;
        }
        if (!parent) return null;
        var rect = new GameObject("StoragePanel", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var panel = rect.gameObject.AddComponent<StoragePanel>();
        UiScaleExempt.Attach(panel);
        return panel;
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        group = GetComponent<CanvasGroup>();
        LoadVisualStyle();
        var backdrop = gameObject.AddComponent<Image>();
        backdrop.color = new Color32(15, 9, 5, 255);
        BuildView();
        group.alpha = 0;
        group.blocksRaycasts = group.interactable = false;
        layout.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var sprite in styleSprites) if (sprite) Destroy(sprite);
        if (fontMaterial) Destroy(fontMaterial);
    }

    void OnDisable()
    {
        Unsubscribe();
        GameplayInputBlocker.SetBlocked(this, false);
        if (IsOpen) InfoPanel.Instance?.ShowPanel(true);
        IsOpen = false; Storage = null;
        EndDrag();
        if (layout) layout.gameObject.SetActive(false);
        if (group) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
    }

    void Update()
    {
        if (!IsOpen) return;
        if (!Storage || GameBindings.CloseDown()) { Hide(); return; }
        if (inventory != InventoryManager.Instance) Subscribe(Storage);
        weightWarning = Mathf.Max(0, weightWarning - Time.unscaledDeltaTime);
    }

    void LateUpdate()
    {
        if (!IsOpen) return;
        if (dirty) Refresh();
        MatchActionLabels();
        if (weightText) weightText.color = weightWarning > 0
            ? Color.Lerp(WeightColor(), Color.red, Mathf.PingPong(weightWarning * 6, 1))
            : WeightColor();
    }

    void OnRectTransformDimensionsChange() => FitLayout();
    void FitLayout()
    {
        if (!layout) return;
        var size = ((RectTransform)transform).rect.size;
        float scale = Mathf.Min(size.x / LayoutWidth, size.y / LayoutHeight);
        layout.localScale = new Vector3(scale, scale, 1);
    }

    public void Show(SurfaceStorageBuilding storage)
    {
        if (!storage || IsOpen && Storage == storage) return;
        if (!IsOpen && GameplayInputBlocker.IsBlocked) return;
        Storage = storage;
        Subscribe(storage);
        IsOpen = true;
        layout.gameObject.SetActive(true);
        GameplayInputBlocker.SetBlocked(this, true);
        group.alpha = 1;
        group.interactable = group.blocksRaycasts = true;
        InfoPanel.Instance?.ShowPanel(false);
        transform.SetAsLastSibling();
        FitLayout();
        hoverHint = null;
        Refresh();
        ResetScroll();
        AudioManager.Instance?.Play(SoundType.UI_Click);
        AudioManager.Instance?.Play(SoundType.DoorOpen);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Storage = null;
        EndDrag();
        Unsubscribe();
        layout.gameObject.SetActive(false);
        GameplayInputBlocker.SetBlocked(this, false);
        group.alpha = 0;
        group.interactable = group.blocksRaycasts = false;
        InfoPanel.Instance?.ShowPanel(true);
        if (EventSystem.current && EventSystem.current.currentSelectedGameObject &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    void Subscribe(SurfaceStorageBuilding storage)
    {
        Unsubscribe();
        inventory = InventoryManager.Instance;
        if (inventory) inventory.OnInventoryChanged += MarkDirty;
        subscribedStorage = storage;
        if (storage) storage.StorageChanged += MarkDirty;
        dirty = true;
    }

    void Unsubscribe()
    {
        if (inventory) inventory.OnInventoryChanged -= MarkDirty;
        if (subscribedStorage) subscribedStorage.StorageChanged -= MarkDirty;
        inventory = null; subscribedStorage = null;
    }

    void MarkDirty() => dirty = true;

    public void SetCategory(int category)
    {
        Category = Mathf.Clamp(category, 0, Tabs.Length - 1);
        Refresh(); ResetScroll();
    }

    public void SetSearch(string query)
    {
        SearchQuery = query ?? "";
        if (searchField && searchField.text != SearchQuery) searchField.SetTextWithoutNotify(SearchQuery);
        Refresh(); ResetScroll();
    }

    void ResetScroll()
    {
        foreach (var scroll in new[] { backpackScroll, storageScroll })
            if (scroll) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
    }

    // ---- Moving items ----

    public int Move(ItemSO item, int amount, bool withdraw)
    {
        if (!Storage || !item || amount <= 0) return 0;
        int moved = withdraw ? Storage.WithdrawUpTo(item, amount) : Storage.TryDeposit(item, amount) ? amount : 0;
        if (moved > 0) HomeClickAudio.PlayUi();
        if (withdraw && moved < amount) WarnWeight();
        Refresh();
        return moved;
    }

    public void Click(Slot slot, PointerEventData.InputButton button, bool shift)
    {
        if (slot == null || !slot.item || slot.amount <= 0) return;
        int amount = shift ? slot.amount : button == PointerEventData.InputButton.Right ? (slot.amount + 1) / 2 : 1;
        Move(slot.item, amount, slot.fromStorage);
    }

    public void DepositOres() => DepositWhere(item => item.category == ItemCategory.Ore);

    // Hotbar items stay with the player so tools, torches and ladders remain ready for the next trip.
    public void DepositAll()
    {
        var hud = FindFirstObjectByType<CompactHud>();
        var kept = new HashSet<ItemSO>(hud && hud.slots != null ? hud.slots.Where(x => x) : Array.Empty<ItemSO>());
        DepositWhere(item => !kept.Contains(item));
    }

    void DepositWhere(Func<ItemSO, bool> predicate)
    {
        var inventory = InventoryManager.Instance;
        if (!Storage || !inventory) return;
        foreach (var entry in inventory.GetSnapshot().Where(e => e.Key && e.Value > 0 && Storable(e.Key) && predicate(e.Key)).ToArray())
            Storage.TryDeposit(entry.Key, entry.Value);
        Refresh();
    }

    public void WithdrawAll()
    {
        if (!Storage) return;
        bool blocked = false;
        foreach (var entry in Sorted(Storage.StoredItems))
        {
            int taken = Storage.WithdrawUpTo(entry.Key, entry.Value);
            blocked |= taken < entry.Value;
        }
        if (blocked) WarnWeight();
        Refresh();
    }

    void WarnWeight()
    {
        weightWarning = .6f;
        AudioManager.Instance?.Play(SoundType.UI_Alert);
    }

    static bool Storable(ItemSO item) => item && item.category != ItemCategory.Powerup;

    // ---- Drag and drop ----

    public void BeginDrag(Slot slot, PointerEventData data)
    {
        if (slot == null || !slot.item) return;
        dragged = slot;
        if (!dragGhost)
        {
            dragGhost = Rect("Drag Ghost", layout, 0, 0, ItemIconLayout.CellIconSize(new Vector2(Cell, Cell)).x, ItemIconLayout.CellIconSize(new Vector2(Cell, Cell)).y).gameObject.AddComponent<Image>();
            dragGhost.raycastTarget = false; dragGhost.preserveAspect = true;
            dragGhost.rectTransform.anchorMin = dragGhost.rectTransform.anchorMax = dragGhost.rectTransform.pivot = new Vector2(.5f, .5f);
        }
        dragGhost.sprite = slot.item.icon;
        dragGrabOffset = ItemDragGhost.Begin(dragGhost, slot.icon, data.position, EventCamera);
        dragGhost.color = Color.white;
        dragGhost.transform.SetAsLastSibling();
        dragGhost.gameObject.SetActive(true);
        Drag(data);
    }

    public void Drag(PointerEventData data)
    {
        if (dragGhost && dragged != null) ItemDragGhost.Move(dragGhost, dragGrabOffset, data.position, EventCamera);
    }

    public void Drop(PointerEventData data)
    {
        var slot = dragged;
        EndDrag();
        if (slot == null || !slot.item) return;
        var target = slot.fromStorage ? backpackFrame : storageFrame;
        if (RectTransformUtility.RectangleContainsScreenPoint(target, data.position, EventCamera))
            Move(slot.item, slot.amount, slot.fromStorage);
    }

    Camera EventCamera
    {
        get
        {
            var canvas = GetComponentInParent<Canvas>();
            canvas = canvas ? canvas.rootCanvas : null;
            return canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }

    public void CancelDrag() => EndDrag();

    void EndDrag()
    {
        dragged = null;
        if (dragGhost) dragGhost.gameObject.SetActive(false);
    }

    // ---- Hover hints ----

    public void Hover(Slot slot, bool entered)
    {
        if (!entered || slot == null || !slot.item) { SetHint(null); return; }
        var item = slot.item;
        string weight = item.EffectiveWeight > 0 ? $" · {Kg(item.EffectiveWeight)} kg je Stück" : "";
        SetHint($"{item.displayName} · {slot.amount} Stück{weight}{WorthHint(item)}");
    }

    // Value is what the shop pays, so unsellable items (tools, placeables) show none and add nothing.
    internal static string WorthHint(ItemSO item) => ShopManager.IsSellable(item) ? $" · Wert {ShopMoneyFormatter.Format(item.worth)} je Stück" : "";
    internal static long SellableWorth(IEnumerable<KeyValuePair<ItemSO, int>> items) => items.Where(e => ShopManager.IsSellable(e.Key)).Sum(e => (long)e.Key.worth * e.Value);

    public void SetHint(string hint)
    {
        hoverHint = hint;
        if (hintText) hintText.text = string.IsNullOrEmpty(hint) ? ControlsHint : hint;
    }

    // ---- Refresh ----

    IEnumerable<KeyValuePair<ItemSO, int>> Sorted(IEnumerable<KeyValuePair<ItemSO, int>> items) => items
        .Where(e => e.Key && e.Value > 0 && Storable(e.Key) && ItemIconLayout.Matches(e.Key, SearchQuery, TabCategories[Category]))
        .OrderBy(e => (int)e.Key.category).ThenBy(e => e.Key.displayName, StringComparer.Create(German, true)).ToArray();

    void Refresh()
    {
        dirty = false;
        if (!layout || !IsOpen) return;
        for (int i = 0; i < tabs.Count; i++) GoldButtonFeedback.Select(tabs[i], i == Category);
        var inventory = InventoryManager.Instance;
        var pack = inventory ? Sorted(inventory.GetSnapshot()).ToArray() : Array.Empty<KeyValuePair<ItemSO, int>>();
        var stored = Storage ? Sorted(Storage.StoredItems).ToArray() : Array.Empty<KeyValuePair<ItemSO, int>>();
        Populate(backpackSlots, backpackContent, pack, false);
        Populate(storageSlots, storageContent, stored, true);
        int kinds = Storage ? Storage.StoredItems.Count(e => e.Key && e.Value > 0) : 0;
        storageSummary.text = kinds == 0 ? "leer" : kinds == 1 ? "1 Sorte" : $"{kinds} Sorten";
        long worth = Storage ? SellableWorth(Storage.StoredItems) : 0;
        valueText.text = $"Wert <color=#{ColorUtility.ToHtmlStringRGB(Gold)}>{ShopMoneyFormatter.Format(worth)}</color>";
        RefreshWeight(inventory);
        if (hoverHint == null) SetHint(null);
    }

    void RefreshWeight(InventoryManager inventory)
    {
        double total = inventory ? inventory.TotalWeight : 0;
        float capacity = inventory ? inventory.CarryingCapacity : 0, maximum = inventory ? inventory.MaximumWeight : 0;
        bool unlimited = GameplayTestSettings.NoWeight || maximum <= 0;
        weightText.text = unlimited ? $"{Kg(total)} kg" : $"{Kg(total)} / {Kg(maximum)} kg";
        weightHint.text = unlimited ? "Kein Gewichtslimit (Testmodus)"
            : $"bis {Kg(capacity)} kg normal schnell  ·  darüber langsamer  ·  höchstens {Kg(maximum)} kg";
        float scale = unlimited ? 0 : WeightBarWidth / maximum;
        float normal = (float)Math.Min(total, capacity) * scale;
        float over = (float)Math.Max(0, Math.Min(total, maximum) - capacity) * scale;
        normalFill.sizeDelta = new Vector2(normal, normalFill.sizeDelta.y);
        overloadFill.anchoredPosition = new Vector2(capacity * scale, overloadFill.anchoredPosition.y);
        overloadFill.sizeDelta = new Vector2(over, overloadFill.sizeDelta.y);
        capacityMarker.gameObject.SetActive(!unlimited);
        capacityMarker.anchoredPosition = new Vector2(capacity * scale - 1, capacityMarker.anchoredPosition.y);
    }

    Color WeightColor()
    {
        var inventory = InventoryManager.Instance;
        return inventory && !GameplayTestSettings.NoWeight && inventory.TotalWeight > inventory.CarryingCapacity ? Overload : Cream;
    }

    void Populate(List<Slot> slots, RectTransform content, KeyValuePair<ItemSO, int>[] items, bool fromStorage)
    {
        int shown = Mathf.Max(VisibleSlots, (items.Length + Columns - 1) / Columns * Columns);
        while (slots.Count < shown) slots.Add(CreateSlot(content, fromStorage));
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.rect.gameObject.SetActive(i < shown);
            if (i >= shown) { slot.item = null; slot.amount = 0; continue; }
            slot.rect.anchoredPosition = new Vector2(i % Columns * ColumnPitch, -(i / Columns) * RowPitch);
            var item = i < items.Length ? items[i].Key : null;
            slot.amount = i < items.Length ? items[i].Value : 0;
            slot.button.interactable = item;
            slot.icon.enabled = item;
            slot.badge.gameObject.SetActive(item);
            if (item && slot.item != item)
            {
                slot.icon.sprite = item.icon;
                ItemIconLayout.Apply(item, slot.iconRect, slot.iconCenter);
            }
            slot.item = item;
            if (item) slot.count.text = slot.amount < 10000 ? slot.amount.ToString(CultureInfo.InvariantCulture) : ShopMoneyFormatter.Format(slot.amount);
        }
        content.sizeDelta = new Vector2(GridWidth, Mathf.Max(GridHeight, (shown / Columns - 1) * RowPitch + Cell));
    }

    static string Kg(double value) => value.ToString("0.##", German);

    // ---- View ----

    void LoadVisualStyle()
    {
        backgroundSprite = Resources.Load<Sprite>("Shop/BuyBackground-v2");
        font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (font)
        {
            fontMaterial = new Material(font.material);
            fontMaterial.SetFloat("_OutlineWidth", .065f);
            fontMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
        }
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames-v2");
        if (!sheet) return;
        rowSprite = SliceFrame(sheet, "Storage Card", 40, 110, 1175, 325);
        selectedRowSprite = SliceFrame(sheet, "Storage Selected", 35, 490, 1185, 340);
    }

    Sprite SliceFrame(Texture2D sheet, string name, float x, float top, float width, float height)
    {
        float sx = sheet.width / 1280f, sy = sheet.height / 1280f;
        var sprite = Sprite.Create(sheet, new UnityEngine.Rect(x * sx, (1280 - top - height) * sy, width * sx, height * sy),
            Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 80 * sy, 100 * sx, 80 * sy));
        sprite.name = name; styleSprites.Add(sprite); return sprite;
    }

    void BuildView()
    {
        layout = Rect("Layout", transform, 0, 0, LayoutWidth, LayoutHeight);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        layout.anchoredPosition = Vector2.zero;
        var background = Panel("Background", layout, 0, 0, LayoutWidth, LayoutHeight, backgroundSprite);
        background.type = Image.Type.Simple;
        Label(layout, "Lager", 550, 24, 540, 80, 64, TextAlignmentOptions.Midline);
        BuildSearch();
        for (int i = 0; i < Tabs.Length; i++)
        {
            int category = i;
            var tab = Button("Category_" + Tabs[i], layout, Tabs[i], 524 + i * 150, 137, 140, 49, () => SetCategory(category));
            tab.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 25;
            tabs.Add(tab.GetComponent<Image>());
        }
        Button("Close", layout, "Schliessen", 1310, 137, 150, 49, Hide).GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 25;

        backpackFrame = Panel("Backpack Frame", layout, 176, 206, 640, 600, rowSprite).rectTransform;
        storageFrame = Panel("Storage Frame", layout, 832, 206, 640, 600, rowSprite).rectTransform;

        Label(layout, "Rucksack", 204, 218, 260, 40, 32);
        weightText = Label(layout, "", 528, 218, 260, 40, 26, TextAlignmentOptions.Right);
        var track = Panel("Weight Track", layout, 204, 262, 584, 24, rowSprite);
        track.pixelsPerUnitMultiplier = 16;
        var bar = Rect("Weight Bar", layout, 210, 268, WeightBarWidth, 12);
        bar.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .35f);
        normalFill = Fill(bar, "Normal", Gold);
        overloadFill = Fill(bar, "Slower", Overload);
        capacityMarker = Rect("Capacity Marker", bar, 0, -4, 2, 20);
        capacityMarker.gameObject.AddComponent<Image>().color = Cream;
        weightHint = Label(layout, "", 204, 290, 584, 30, 19, TextAlignmentOptions.Midline);
        weightHint.color = Muted;

        Label(layout, "Lager", 860, 218, 260, 40, 32);
        storageSummary = Label(layout, "", 1180, 218, 264, 40, 24, TextAlignmentOptions.Right);
        storageSummary.color = Muted;

        backpackContent = ScrollArea("Backpack", layout, 220, 330, out backpackScroll);
        AddScrollbar(backpackScroll, 780, 330);
        storageContent = ScrollArea("Storage", layout, 876, 330, out storageScroll);
        AddScrollbar(storageScroll, 1436, 330);

        Hint(Button("Deposit Ores", layout, "Erze einlagern", 204, 745, 282, 56, DepositOres), "Legt alle Erze aus dem Rucksack ins Lager");
        Hint(Button("Deposit All", layout, "Alles einlagern", 506, 745, 282, 56, DepositAll), "Legt alles ins Lager – Gegenstände in der Schnellleiste bleiben im Rucksack");
        var value = Panel("Value", layout, 860, 745, 282, 56, rowSprite);
        valueText = Label(value.transform, "", 20, 0, 242, 56, 26, TextAlignmentOptions.Midline);
        valueText.richText = true;
        Hint(Button("Withdraw All", layout, "Alles entnehmen", 1162, 745, 282, 56, WithdrawAll), "Nimmt so viel aus dem Lager, wie der Rucksack tragen kann");
        foreach (var name in new[] { "Deposit Ores", "Deposit All", "Withdraw All" })
            actionLabels.Add(layout.Find(name).GetComponentInChildren<TextMeshProUGUI>());
        hintText = Label(layout, ControlsHint, 176, 810, 1296, 32, 21, TextAlignmentOptions.Midline);
        hintText.color = Muted;
        FitLayout();
    }

    // GoldButtonFeedback auto-sizes each caption to its own length; once that has run, all action buttons
    // take the smallest size so the bottom row reads as one set.
    void MatchActionLabels()
    {
        if (actionLabelFrames > 2 || ++actionLabelFrames <= 2) return;
        foreach (var label in actionLabels) label.ForceMeshUpdate();
        float size = actionLabels.Min(label => label.fontSize);
        foreach (var label in actionLabels) { label.enableAutoSizing = false; label.fontSize = size; }
    }

    RectTransform Fill(RectTransform bar, string name, Color color)
    {
        var fill = Rect(name, bar, 0, 0, 0, 12);
        fill.gameObject.AddComponent<Image>().color = color;
        return fill;
    }

    void Hint(Button button, string text)
    {
        var hint = button.gameObject.AddComponent<StorageHoverHint>();
        hint.panel = this; hint.text = text;
    }

    Slot CreateSlot(RectTransform content, bool fromStorage)
    {
        var frame = Panel(fromStorage ? "Stored Slot" : "Backpack Slot", content, 0, 0, Cell, Cell, rowSprite);
        frame.raycastTarget = true;
        var button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        GoldButtonFeedback.Apply(button, rowSprite, selectedRowSprite);
        var iconSize = ItemIconLayout.CellIconSize(new Vector2(Cell, Cell));
        var icon = Panel("Icon", frame.transform, (Cell - iconSize.x) / 2, (Cell - iconSize.y) / 2, iconSize.x, iconSize.y, null);
        icon.preserveAspect = true;
        ShopVisualTheme.CenterImage(icon);
        var badge = Panel("Count", frame.transform, 60, 90, 62, 32, rowSprite);
        badge.pixelsPerUnitMultiplier = 9;
        var count = Label(badge.transform, "", 4, 0, 54, 32, 23, TextAlignmentOptions.Midline);
        var slot = new Slot { fromStorage = fromStorage, rect = frame.rectTransform, frame = frame, icon = icon, iconRect = icon.rectTransform,
            iconCenter = icon.rectTransform.anchoredPosition, badge = badge.rectTransform, button = button, count = count };
        var input = frame.gameObject.AddComponent<StorageSlotInput>();
        input.panel = this; input.slot = slot;
        return slot;
    }

    void BuildSearch()
    {
        var panel = Panel("Search", layout, 180, 137, 327, 49, rowSprite);
        panel.raycastTarget = true;
        var glyph = Rect("Search", panel.transform, 12, 10, 28, 28).gameObject.AddComponent<WorkbenchGlyph>();
        glyph.shape = WorkbenchGlyph.Shape.Search; glyph.color = Cream; glyph.raycastTarget = false;
        var viewport = Rect("Text Area", panel.transform, 48, 4, 254, 40);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, "", 0, 0, 254, 40, 23);
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Overflow;
        var placeholder = Label(viewport, "Item suchen …", 0, 0, 254, 40, 22);
        placeholder.fontStyle = FontStyles.Italic; placeholder.color = Muted;
        searchField = panel.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = viewport;
        searchField.textComponent = text; searchField.placeholder = placeholder;
        searchField.targetGraphic = panel;
        searchField.characterLimit = 80;
        searchField.lineType = TMP_InputField.LineType.SingleLine;
        searchField.customCaretColor = true; searchField.caretColor = Cream;
        HomeUi.StyleInputField(searchField, true);
        searchField.onValueChanged.AddListener(SetSearch);
    }

    RectTransform ScrollArea(string name, Transform parent, float x, float y, out ScrollRect scroll)
    {
        var viewport = Rect(name, parent, x, y, GridWidth, GridHeight);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, 0, 0, GridWidth, GridHeight);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35;
        return content;
    }

    void AddScrollbar(ScrollRect scroll, float x, float y)
    {
        var track = Panel("Scrollbar", layout, x, y, 13, GridHeight, rowSprite);
        track.raycastTarget = true; track.pixelsPerUnitMultiplier = 16;
        var area = Rect("Sliding Area", track.transform, 2, 2, 9, GridHeight - 4);
        var handle = Panel("Handle", area, 0, 0, 9, GridHeight - 4, selectedRowSprite);
        handle.raycastTarget = true; handle.pixelsPerUnitMultiplier = 16;
        handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = Vector2.one;
        handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    Image Panel(string name, Transform parent, float x, float y, float width, float height, Sprite sprite)
    {
        var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 5;
        image.raycastTarget = false;
        HomeUi.StylePanelWood(image);
        return image;
    }

    TextMeshProUGUI Label(Transform parent, string caption, float x, float y, float width, float height,
        float size, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var text = Rect("Label", parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        if (fontMaterial) text.fontSharedMaterial = fontMaterial;
        text.text = caption; text.color = Cream; text.fontStyle = FontStyles.Normal;
        text.fontSize = size; text.enableAutoSizing = true; text.fontSizeMin = size * .65f; text.fontSizeMax = size;
        text.alignment = alignment switch {
            TextAlignmentOptions.MidlineLeft => TextAlignmentOptions.Left,
            TextAlignmentOptions.Midline => TextAlignmentOptions.Center,
            TextAlignmentOptions.MidlineRight => TextAlignmentOptions.Right,
            _ => alignment
        };
        // Middle (line-box) alignment, not Midline: Midline centres the visible glyphs, so a descender such as
        // the g in "einlagern" pushes the whole caption up compared with captions without descenders.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    Button Button(string name, Transform parent, string caption, float x, float y, float width, float height, UnityAction action)
    {
        var image = Panel(name, parent, x, y, width, height, rowSprite);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        GoldButtonFeedback.Apply(button, rowSprite, selectedRowSprite);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        HomeClickAudio.Bind(button);
        button.onClick.AddListener(action);
        Label(image.transform, caption, 8, 0, width - 16, height, 27, TextAlignmentOptions.Center);
        return button;
    }
}

// Left click moves one, Shift moves the stack, right click moves half; holding an item picks it up (see ItemDragHold)
// and dropping it on the other side moves the stack.
public sealed class StorageSlotInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public StoragePanel panel;
    public StoragePanel.Slot slot;
    readonly ItemDragHold hold = new();

    public void OnPointerClick(PointerEventData data)
    {
        // A drag released on its own slot still reports a click; the drop already decided what moves.
        if (hold.ConsumeClick() || data.dragging || data.button == PointerEventData.InputButton.Middle) return;
        panel.Click(slot, data.button, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        panel.Hover(slot, slot.rect.gameObject.activeInHierarchy);
    }
    public void OnPointerEnter(PointerEventData data) => panel.Hover(slot, true);
    public void OnPointerExit(PointerEventData data) => panel.Hover(slot, false);
    public void OnPointerDown(PointerEventData data) { hold.Press(); if (data.button == PointerEventData.InputButton.Left && slot?.item) hold.Down(data.position); }
    public void OnDrag(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left) return;
        hold.Pointer = data.position;
        if (hold.Dragging) panel.Drag(data);
        else if (hold.ShouldStart(true)) Begin();
    }
    public void OnPointerUp(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && hold.Up(data.position)) panel.Drop(data);
    }
    void Update()
    {
        if (hold.ShouldStart(false)) Begin();
    }
    void Begin()
    {
        if (!panel || slot == null || !slot.item) { hold.Cancel(); return; }
        hold.Dragging = true;
        panel.BeginDrag(slot, Pointer(hold.PressPosition));
        panel.Drag(Pointer(hold.Pointer));
    }
    static PointerEventData Pointer(Vector2 position) =>
        new(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position };
    void OnDisable()
    {
        if (hold.Dragging && panel) panel.CancelDrag();
        hold.Cancel();
    }
}

public sealed class StorageHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public StoragePanel panel;
    public string text;
    public void OnPointerEnter(PointerEventData data) => panel.SetHint(text);
    public void OnPointerExit(PointerEventData data) => panel.SetHint(null);
    void OnDisable() { if (panel) panel.SetHint(null); }
}
