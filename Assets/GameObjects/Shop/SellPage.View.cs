using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class SellPage
{
    const float OreCardWidth = 608, OreCardHeight = 232, ListHeight = 1002;
    readonly Dictionary<ItemSO, ShopSlot> oreCards = new();
    static readonly CompareInfo OreNameComparison = CultureInfo.GetCultureInfo("de-DE").CompareInfo;
    ShopVisualTheme theme;
    ScrollRect oreScroll;
    TMP_InputField searchField;
    Image unitPriceCoin, proceedsCoin;
    Material itemGlowMaterial;
    Sprite coin;
    public string SearchQuery { get; private set; } = "";
    public ItemSO SelectedItem => _selected;

    public void SetSearch(string query)
    {
        SearchQuery = query ?? "";
        if (searchField && searchField.text != SearchQuery) searchField.SetTextWithoutNotify(SearchQuery);
        Rebuild();
        if (oreScroll) { oreScroll.StopMovement(); oreScroll.verticalNormalizedPosition = 1; }
    }
    bool MatchesSearch(ItemSO item) => SearchQuery.Trim().Length == 0 || OreNameComparison.IndexOf(
        item.displayName, SearchQuery.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    int CurrentSaleCount(int count) => GameBindings.Held(GameAction.SellAll) ? count :
        GameBindings.Held(GameAction.SellTen) ? Mathf.Min(10, count) : Mathf.Min(1, count);

    void BuildSellView()
    {
        theme = ShopVisualTheme.Ensure(transform.parent);
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        moneyText = transform.parent.Find("CurrentMoney/Amount")?.GetComponent<TextMeshProUGUI>();
        coin = Resources.LoadAll<Sprite>("GameOverCoin").FirstOrDefault();
        var root = SellRect("Ore Shop", transform, 0, 0, 2560, 1440);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f; root.anchoredPosition = Vector2.zero;
        var search = SellFrame("Search", root, 306, 184, 552, 86, theme.CardFrame);
        search.raycastTarget = true;
        var glyph = SellRect("Search Icon", search.transform, 25, 24, 38, 38).gameObject.AddComponent<WorkbenchGlyph>();
        glyph.shape = WorkbenchGlyph.Shape.Search; glyph.raycastTarget = false;
        var textArea = SellRect("Text Area", search.transform, 82, 9, 440, 68);
        textArea.gameObject.AddComponent<RectMask2D>();
        var text = SellLabel(textArea, "", 0, 0, 440, 68, 36);
        text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Overflow;
        var placeholder = SellLabel(textArea, "Erz suchen …", 0, 0, 440, 68, 35);
        placeholder.fontStyle = FontStyles.Italic; placeholder.color = new Color32(189, 164, 134, 255);
        searchField = search.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = textArea; searchField.textComponent = text;
        searchField.placeholder = placeholder; searchField.targetGraphic = search;
        searchField.characterLimit = 80; searchField.lineType = TMP_InputField.LineType.SingleLine;
        searchField.customCaretColor = true; searchField.caretColor = new Color32(255, 245, 229, 255);
        HomeUi.StyleInputField(searchField, true);
        searchField.onValueChanged.AddListener(SetSearch);
        var viewport = SellRect("Ores", root, 290, 297, 1250, ListHeight);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        content = SellRect("Content", viewport, 0, 0, 1250, ListHeight);
        oreScroll = viewport.gameObject.AddComponent<ScrollRect>();
        oreScroll.content = (RectTransform)content; oreScroll.viewport = viewport;
        oreScroll.horizontal = false; oreScroll.movementType = ScrollRect.MovementType.Clamped; oreScroll.scrollSensitivity = 45;
        BuildOreScrollbar(root);
        var details = SellFrame("Ore Details", root, 1640, 295, 644, 1008, theme.CardFrame).rectTransform;
        detailsPanel = details.gameObject;
        var glow = SellFrame("Item Glow", details, 12, -10, 620, 520, null);
        var shader = Resources.Load<Shader>("Workbench/ItemGlow");
        if (shader) { itemGlowMaterial = new Material(shader); glow.material = itemGlowMaterial; } else glow.color = Color.clear;
        selectedOreIcon = SellFrame("Selected Ore", details, 97, 38, 450, 405, null);
        ShopVisualTheme.CenterImage(selectedOreIcon);
        oreNameText = SellLabel(details, "", 27, 443, 590, 69, 50);
        oreNameText.alignment = TextAlignmentOptions.Midline;
        SellLabel(details, "Erz", 27, 510, 590, 44, 31).alignment = TextAlignmentOptions.Midline;
        SellFrame("Divider", details, 65, 573, 514, 2, null).color = new Color32(255, 198, 86, 255);
        countText = BuildStat(details, "Stock", "Bestand", 24, false, out _);
        oreWorthText = BuildStat(details, "Unit Price", "Stückpreis", 227, true, out unitPriceCoin);
        totalWorthText = BuildStat(details, "Proceeds", "Erlös", 430, true, out proceedsCoin);
        sell1Button = sell10Button = null;
        sellMaxButton = SellButton("Sell Selected", details, "Erz verkaufen", 32, 752, 580, 98, theme.ActionFrame);
        sellAllButton = SellButton("Sell All", details, "Alles verkaufen", 32, 870, 580, 98, theme.CardFrame);
    }

    TextMeshProUGUI BuildStat(Transform parent, string name, string caption, float x, bool currency, out Image moneyIcon)
    {
        var field = SellFrame(name, parent, x, 607, 190, 126, theme.CardFrame); field.pixelsPerUnitMultiplier = 5;
        SellLabel(field.transform, caption, 8, 12, 174, 38, 29).alignment = TextAlignmentOptions.Midline;
        var value = SellLabel(field.transform, "", 10, 53, 170, 60, 43);
        value.alignment = currency ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
        moneyIcon = currency ? SellFrame("Coin", field.transform, 135, 63, 42, 42, coin) : null;
        if (moneyIcon) { ShopVisualTheme.CenterImage(moneyIcon); }
        return value;
    }
    void CenterStatValue(TextMeshProUGUI value, Image moneyIcon)
    {
        if (!value || !moneyIcon) return;
        float width = Mathf.Min(128, value.GetPreferredValues(value.text).x);
        float left = (190 - width - 49) * .5f;
        ShopVisualTheme.Place(value.rectTransform, left, 53, width + 2, 60);
        ShopVisualTheme.Place(moneyIcon.rectTransform, left + width + 7, 62, 42, 42);
        ShopVisualTheme.CenterImage(moneyIcon);
    }

    void SyncOreCards()
    {
        foreach (var slot in oreCards.Values) if (slot) slot.gameObject.SetActive(false);
        for (int i = 0; i < _buffer.Count; i++)
        {
            var entry = _buffer[i];
            if (!oreCards.TryGetValue(entry.item, out var slot) || !slot)
            {
                var frame = SellFrame("Slot_" + entry.item.displayName, content, 0, 0, OreCardWidth, OreCardHeight, theme.CardFrame);
                frame.raycastTarget = true;
                var button = frame.gameObject.AddComponent<Button>(); button.targetGraphic = frame;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors; colors.highlightedColor = new Color(1, .94f, .8f);
                colors.pressedColor = new Color(.8f, .7f, .52f); button.colors = colors;
                var icon = SellFrame("Icon", frame.transform, 20, 18, 260, 194, entry.item.icon);
                ShopVisualTheme.CenterImage(icon);
                var name = SellLabel(frame.transform, "", 300, 28, 282, 72, 43);
                var badge = SellFrame("Count Badge", frame.transform, 0, 0, 100, 56, theme.CardFrame);
                badge.pixelsPerUnitMultiplier = 8;
                badge.rectTransform.pivot = new Vector2(1, 1); badge.rectTransform.anchoredPosition = new Vector2(OreCardWidth - 26, -155);
                var count = SellLabel(frame.transform, "", 0, 0, 100, 56, 41);
                count.alignment = TextAlignmentOptions.Midline;
                count.rectTransform.pivot = new Vector2(1, 1); count.rectTransform.anchoredPosition = badge.rectTransform.anchoredPosition;
                slot = frame.gameObject.AddComponent<ShopSlot>();
                slot.ConfigureWideCard(icon, name, count, badge, frame, button, theme.CardFrame, theme.SelectedFrame);
                oreCards[entry.item] = slot;
            }
            slot.gameObject.SetActive(true);
            ShopVisualTheme.Place((RectTransform)slot.transform, i % 2 * 642, i / 2 * 256, OreCardWidth, OreCardHeight);
            slot.Bind(entry.item, entry.count, this);
        }
        ((RectTransform)content).sizeDelta = new Vector2(1250, Mathf.Max(ListHeight, Mathf.Ceil(_buffer.Count / 2f) * 256 - 24));
    }

    void BuildOreScrollbar(Transform root)
    {
        var track = SellFrame("Scrollbar", root, 1574, 297, 26, ListHeight, theme.CardFrame);
        track.raycastTarget = true; track.pixelsPerUnitMultiplier = 12;
        var sliding = SellRect("Sliding Area", track.transform, 0, 0, 26, ListHeight);
        sliding.anchorMin = Vector2.zero; sliding.anchorMax = Vector2.one; sliding.pivot = Vector2.one * .5f;
        sliding.offsetMin = new Vector2(4, 8); sliding.offsetMax = new Vector2(-4, -8);
        var handle = SellFrame("Handle", sliding, 0, 0, 26, ListHeight, theme.ActionFrame);
        handle.raycastTarget = true; handle.pixelsPerUnitMultiplier = 14;
        handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = Vector2.one;
        handle.rectTransform.pivot = Vector2.one * .5f; handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        oreScroll.verticalScrollbar = bar; oreScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }
    Button SellButton(string name, Transform parent, string caption, float x, float y, float w, float h, Sprite sprite)
    {
        var image = SellFrame(name, parent, x, y, w, h, sprite); image.raycastTarget = true;
        var result = image.gameObject.AddComponent<Button>(); result.targetGraphic = image;
        GoldButtonFeedback.Apply(result, theme.CardFrame, theme.SelectedFrame);
        HomeClickAudio.Bind(result);
        result.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = result.colors; colors.disabledColor = new Color(.48f, .44f, .38f);
        colors.highlightedColor = new Color(1, .95f, .78f); colors.pressedColor = new Color(.8f, .65f, .45f); result.colors = colors;
        SellLabel(image.transform, caption, 18, 0, w - 36, h, 39).alignment = TextAlignmentOptions.Midline;
        return result;
    }
    Image SellFrame(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
    {
        var image = SellRect(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); image.sprite = sprite;
        image.type = sprite && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 3; image.raycastTarget = false; HomeUi.StylePanelWood(image); return image;
    }
    TextMeshProUGUI SellLabel(Transform parent, string value, float x, float y, float w, float h, float size)
    {
        var text = SellRect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        theme.Style(text); text.text = value; text.fontSize = size;
        text.enableAutoSizing = true; text.fontSizeMin = size * .7f; text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; return text;
    }
    static RectTransform SellRect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
        ShopVisualTheme.Place(rect, x, y, w, h); return rect;
    }
    void OnDestroy() { if (itemGlowMaterial) Destroy(itemGlowMaterial); }
}
