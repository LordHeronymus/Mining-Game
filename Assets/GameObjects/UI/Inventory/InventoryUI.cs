using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class InventoryUI : MonoBehaviour
{
    public Sprite frameSprite, slotSprite, selectionSprite, rowSprite, actionSprite, badgeSprite;
    public TMP_FontAsset font;
    public Material fontMaterial;
    public bool IsOpen { get; private set; }
    public int Filter { get; private set; }
    public ItemSO SelectedItem { get; private set; }
    public int VisibleItemCount { get; private set; }
    CanvasGroup group;
    RectTransform layout, content;
    ScrollRect scroll;
    InventoryManager inventory;
    Image footerIcon;
    TextMeshProUGUI footerName, footerCount;
    readonly List<Image> tabs = new();
    readonly List<Cell> cells = new();
    bool alphabetical;
    Coroutine fade;
    static readonly Color Cream = new Color32(255, 245, 229, 255);
    sealed class Cell
    {
        public RectTransform rect, badge;
        public Image icon, selection;
        public TextMeshProUGUI count;
        public Button button;
        public ItemSO item;
    }
    void Awake()
    {
        group = GetComponent<CanvasGroup>(); BuildView();
        group.alpha = 0; group.blocksRaycasts = group.interactable = false;
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) { if (IsOpen) HidePanel(); else ShowPanel(); }
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) HidePanel();
        if (IsOpen && inventory != InventoryManager.Instance) Subscribe();
    }
    void OnDisable()
    {
        if (inventory) inventory.OnInventoryChanged -= Refresh;
        inventory = null; GameplayInputBlocker.SetBlocked(this, false);
        if (IsOpen) InfoPanel.Instance?.ShowPanel(true);
        IsOpen = false;
        if (group) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
        fade = null;
    }
    void Subscribe()
    {
        if (inventory) inventory.OnInventoryChanged -= Refresh;
        inventory = InventoryManager.Instance;
        if (inventory) inventory.OnInventoryChanged += Refresh;
        Refresh();
    }
    public void ShowPanel()
    {
        if (!IsOpen && GameplayInputBlocker.IsBlocked) return;
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (selected && selected.GetComponent<TMP_InputField>()) return;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadePanel(true));
    }
    public void HidePanel()
    {
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadePanel(false));
    }
    public IEnumerator FadePanel(bool show)
    {
        IsOpen = show;
        if (show)
        {
            transform.SetAsLastSibling(); Subscribe();
            GameplayInputBlocker.SetBlocked(this, true); InfoPanel.Instance?.ShowPanel(false);
        }
        group.blocksRaycasts = group.interactable = show;
        float start = group.alpha;
        for (float t = 0; t < .12f; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(start, show ? 1 : 0, t / .12f); yield return null;
        }
        group.alpha = show ? 1 : 0;
        if (!show)
        {
            GameplayInputBlocker.SetBlocked(this, false); InfoPanel.Instance?.ShowPanel(true);
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        fade = null;
    }
    public void SetFilter(int filter) { Filter = Mathf.Clamp(filter, 0, 3); scroll.verticalNormalizedPosition = 1; Refresh(); }
    public void SortItems() { alphabetical = !alphabetical; Refresh(); }
    public void SelectItem(ItemSO item) { SelectedItem = item; Refresh(); }
    static int Category(ItemSO item) => item.category == ItemCategory.Ore ? 1 : item.category == ItemCategory.Misc ? 2 : 3;
    static int ItemOrder(ItemSO item) => item.item switch
    {
        Item.Coal => 0, Item.Iron => 1, Item.Copper => 2, Item.Silver => 3,
        Item.Gold => 4, Item.Platinum => 5, Item.Wood => 6, Item.PlantFiber => 7,
        Item.Rope => 8, Item.Nails => 9, Item.Torche => 10, Item.Dynamite => 11,
        Item.Ladder => 12, Item.BridgePart => 13, _ => 14 + (int)item.item
    };
    static string Count(int amount)
    {
        if (amount < 100000) return amount.ToString();
        double divisor = amount >= 1000000000 ? 1e9 : amount >= 1000000 ? 1e6 : 1e3;
        return (Math.Floor(amount / divisor * 10) / 10).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + (divisor == 1e9 ? "G" : divisor == 1e6 ? "M" : "k");
    }
    void Refresh()
    {
        if (!IsOpen || !content) return;
        var items = inventory ? inventory.GetSnapshot().Where(p => p.Key && p.Value > 0 && (Filter == 0 || Category(p.Key) == Filter)).ToList()
            : new List<KeyValuePair<ItemSO, int>>();
        items = alphabetical ? items.OrderBy(p => p.Key.displayName).ToList()
            : items.OrderBy(p => Category(p.Key)).ThenBy(p => ItemOrder(p.Key)).ToList();
        VisibleItemCount = items.Count;
        if (!items.Any(p => p.Key == SelectedItem)) SelectedItem = items.Count > 0 ? items[0].Key : null;
        int capacity = Mathf.Max(21, Mathf.CeilToInt(items.Count / 7f) * 7);
        while (cells.Count < capacity) cells.Add(CreateCell(cells.Count));
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i]; c.rect.gameObject.SetActive(i < capacity);
            c.item = i < items.Count ? items[i].Key : null;
            c.icon.enabled = c.item; c.icon.sprite = c.item ? c.item.icon : null;
            c.selection.enabled = c.item && c.item == SelectedItem;
            c.badge.gameObject.SetActive(c.item); c.button.interactable = c.item;
            if (c.item)
            {
                c.count.text = Count(items[i].Value);
                c.badge.sizeDelta = new Vector2(Mathf.Clamp(c.count.GetPreferredValues(c.count.text).x + 22, 52, 142), 40);
            }
        }
        content.sizeDelta = new Vector2(1230, Mathf.Max(492, capacity / 7 * 168 - 12));
        for (int i = 0; i < tabs.Count; i++) tabs[i].sprite = Filter == i ? actionSprite : rowSprite;
        footerIcon.enabled = SelectedItem; footerIcon.sprite = SelectedItem ? SelectedItem.icon : null;
        footerName.text = SelectedItem ? SelectedItem.displayName : "";
        footerCount.text = SelectedItem ? "Bestand <color=#FFBC4F>" + Count(inventory.GetCount(SelectedItem)) + "</color>" : "";
    }
    void OnRectTransformDimensionsChange() => Fit();
    void Fit()
    {
        if (!layout) return;
        var size = ((RectTransform)transform).rect.size;
        layout.localScale = Vector3.one * Mathf.Min(size.x / 1672f, size.y / 941f);
    }
    RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = gameObject.layer;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
    }
    Image Panel(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.type = sprite && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
        image.raycastTarget = false; return image;
    }
    TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h, float size, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var label = Rect("Text", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSharedMaterial = fontMaterial; label.fontStyle = FontStyles.Bold;
        label.text = text; label.fontSize = size; label.color = Cream; label.alignment = align;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false; return label;
    }
    Button Button(string name, Transform parent, string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        var image = Panel(name, parent, x, y, w, h, rowSprite); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        var colors = button.colors; colors.highlightedColor = new Color(1, .92f, .77f); colors.pressedColor = new Color(.8f, .7f, .55f); button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (text.Length > 0) Label(image.transform, text, 0, 0, w, h, 30, TextAlignmentOptions.Midline);
        return button;
    }
    void BuildView()
    {
        var shade = Panel("Gameplay Dim", transform, 0, 0, 0, 0, null);
        shade.color = new Color(0, 0, 0, .42f); shade.raycastTarget = true;
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        layout = Rect("Inventory Layout", transform, 0, 0, 1672, 941);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        Panel("Frame", layout, 0, 0, 1672, 941, frameSprite);
        Label(layout, "Inventar", 568, 32, 536, 85, 58, TextAlignmentOptions.Midline);
        var close = Button("Close", layout, "×", 1430, 85, 72, 72, HidePanel);
        close.GetComponent<Image>().sprite = actionSprite; close.GetComponentInChildren<TextMeshProUGUI>().fontSize = 60;
        string[] names = { "Alle", "Erze", "Materialien", "Werkzeuge" };
        for (int i = 0; i < 4; i++)
        {
            int filter = i;
            tabs.Add(Button("Filter " + names[i], layout, names[i], 258 + i * 296, 162, 270, 60, () => SetFilter(filter)).GetComponent<Image>());
        }
        var viewport = Rect("Items Viewport", layout, 222, 237, 1230, 496);
        viewport.gameObject.AddComponent<RectMask2D>(); content = Rect("Items", viewport, 0, 0, 1230, 496);
        scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var footer = Panel("Selection Footer", layout, 222, 754, 1230, 96, rowSprite);
        footerIcon = Panel("Item Icon", footer.transform, 20, 10, 76, 76, null); footerIcon.preserveAspect = true;
        footerName = Label(footer.transform, "", 110, 0, 440, 96, 36);
        footerCount = Label(footer.transform, "", 590, 0, 300, 96, 30);
        var sort = Button("Sort", footer.transform, "Sortieren", 910, 14, 300, 68, SortItems); sort.GetComponent<Image>().sprite = actionSprite;
        Fit();
    }
    Cell CreateCell(int index)
    {
        var button = Button("Slot " + index, content, "", index % 7 * 177.5f, index / 7 * 168, 164, 156, () => { });
        button.GetComponent<Image>().sprite = slotSprite;
        var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
        var c = new Cell { rect = (RectTransform)button.transform, button = button };
        c.selection = Panel("Selection", c.rect, 0, 0, 164, 156, selectionSprite);
        c.icon = Panel("Item", c.rect, 15, 12, 134, 128, null); c.icon.preserveAspect = true;
        c.badge = Panel("Count Badge", c.rect, 0, 0, 52, 40, badgeSprite).rectTransform;
        c.badge.anchorMin = c.badge.anchorMax = new Vector2(1, 0); c.badge.pivot = new Vector2(1, 0); c.badge.anchoredPosition = new Vector2(-7, 8);
        c.count = Label(c.badge, "", 0, 0, 52, 40, 27, TextAlignmentOptions.Midline);
        c.count.rectTransform.anchorMin = Vector2.zero; c.count.rectTransform.anchorMax = Vector2.one;
        c.count.rectTransform.offsetMin = c.count.rectTransform.offsetMax = Vector2.zero;
        button.onClick.AddListener(() => { if (c.item) SelectItem(c.item); }); return c;
    }
}
