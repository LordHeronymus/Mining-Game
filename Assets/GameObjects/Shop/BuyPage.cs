using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuyPage : MonoBehaviour
{
    const int Columns = 4;
    const float CardWidth = 296, CardHeight = 322, GapX = 22, GapY = 18;
    const float ListWidth = 1250, ListHeight = 1002;
    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Gold = new Color32(255, 198, 86, 255);
    static readonly Color Muted = new Color32(189, 164, 134, 255);
    static readonly Color Unaffordable = new Color32(229, 145, 128, 255);
    static readonly CompareInfo NameComparison = CultureInfo.GetCultureInfo("de-DE").CompareInfo;
    readonly List<RecipeCard> cards = new();
    readonly List<GameObject> materialRows = new();
    RectTransform content, materialContent;
    ScrollRect scroll, materialScroll;
    TMP_InputField searchField;
    TMP_FontAsset font;
    Material fontMaterial;
    Sprite normalFrame, selectedFrame, actionFrame, coin;
    Image preview;
    Vector2 previewCenter;
    GameObject detailPanel;
    TextMeshProUGUI detailName, purchaseLabel, moneyText;
    Button purchaseButton;
    RecipeCard selected;
    StatsManager observedStats;
    CraftingRecipe[] recipes;
    public string SearchQuery { get; private set; } = "";

    sealed class RecipeCard
    {
        public int price;
        public Func<bool> isUnlocked, purchase;
        public CraftingRecipe recipe;
        public Sprite sprite;
        public RectTransform rect;
        public Image background, icon, coin;
        public TextMeshProUGUI name, priceText;
        public WorkbenchGlyph check;
    }
    void Awake()
    {
        var workbench = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var theme = ShopVisualTheme.Ensure(transform.parent);
        font = theme.Font; fontMaterial = theme.FontMaterial;
        normalFrame = theme.CardFrame; selectedFrame = theme.SelectedFrame; actionFrame = theme.ActionFrame;
        recipes = (workbench && workbench.recipes != null ? workbench.recipes : Array.Empty<CraftingRecipe>())
            .Concat(Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes")).Where(r => r && r.output).Distinct().ToArray();
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        moneyText = transform.parent.Find("CurrentMoney/Amount")?.GetComponent<TextMeshProUGUI>();
        coin = Resources.LoadAll<Sprite>("GameOverCoin").FirstOrDefault();
        Build();
        Add(Item.Medkit, "MedkitRecipe");
        Add(Item.Backpack, "BackpackRecipe");
        Add(Item.LoadBelt, "LoadBeltRecipe");
        Add(Item.HeavyDutyBoots, "HeavyDutyBootsRecipe");
        Add(Item.ReinforcedBackpack, "ReinforcedBackpackRecipe");
        Add(Item.SpringGreaves, "SpringGreavesRecipe");
        Add(Item.LoadFrame, "LoadFrameRecipe");
        Add(Item.Exoskeleton, "ExoskeletonRecipe");
        Add(Item.CrystalPendant, "CrystalPendantRecipe");
        Add(Item.CopperEnergyBracelet, "CopperEnergyBraceletRecipe");
        Add(Item.EnergyStorageVial, "EnergyStorageVialRecipe");
        Add(Item.RuneBelt, "RuneBeltRecipe");
        Add(Item.CrystalHeart, "CrystalHeartRecipe");
        Add(Item.CrystalHarness, "CrystalHarnessRecipe");
        Add(Item.TravelMonolith, "TravelMonolithRecipe");
        Add(Item.IronPickaxe, "IronPickaxeRecipe");
        Add(Item.SteelPickaxe, "SteelPickaxeRecipe");
        Add(Item.TitaniumPickaxe, "TitaniumPickaxeRecipe");
        Add(Item.TungstenPickaxe, "TungstenPickaxeRecipe");
        Add(Item.ObsidianPickaxe, "ObsidianPickaxeRecipe");
        Add(Item.MythrilPickaxe, "MythrilPickaxeRecipe");
        Add(Item.DiamondPickaxe, "DiamondPickaxeRecipe");
        selected = cards.OrderBy(c => c.isUnlocked()).ThenBy(c => c.price).FirstOrDefault();
    }
    void OnEnable() { ShopVisualTheme.Ensure(transform.parent).UseBuyLayout(true); ObserveStats(); Refresh(); RefreshMaterials(); }
    void Update() { if (observedStats != StatsManager.Instance) { ObserveStats(); Refresh(); } }
    void ObserveStats()
    {
        if (observedStats) observedStats.OnMoneyChanged -= OnMoneyChanged;
        observedStats = StatsManager.Instance;
        if (observedStats) observedStats.OnMoneyChanged += OnMoneyChanged;
    }
    void OnDisable()
    {
        if (observedStats) observedStats.OnMoneyChanged -= OnMoneyChanged;
        observedStats = null;
    }
    void OnMoneyChanged(int _) => Refresh();
    public void RefreshIconLayouts() { if (isActiveAndEnabled) { Refresh(); RefreshMaterials(); } }
    public void SetSearch(string query)
    {
        SearchQuery = query ?? "";
        if (searchField && searchField.text != SearchQuery) searchField.SetTextWithoutNotify(SearchQuery);
        var previous = selected;
        Refresh();
        if (selected != previous) RefreshMaterials();
        if (scroll) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }
    void Add(Item item, string spriteName)
    {
        var recipe = recipes.FirstOrDefault(r => r.output.item == item);
        if (!recipe) return;
        var card = new RecipeCard { recipe = recipe, price = recipe.ShopPrice,
            isUnlocked = () => RecipeUnlocks.IsUnlocked(recipe),
            purchase = () => ShopManager.Instance && ShopManager.Instance.TryBuyRecipe(recipe),
            sprite = Resources.Load<Sprite>("Shop/" + spriteName) };
        card.background = Panel("Blueprint " + item, content, 0, 0, CardWidth, CardHeight, normalFrame);
        card.rect = card.background.rectTransform; card.background.raycastTarget = true;
        var button = card.rect.gameObject.AddComponent<Button>(); button.targetGraphic = card.background;
        GoldButtonFeedback.Apply(button, normalFrame, selectedFrame);
        HomeClickAudio.Bind(button);
        button.onClick.AddListener(() => Select(card));
        var colors = button.colors; colors.highlightedColor = new Color(1f, .94f, .8f);
        colors.pressedColor = new Color(.8f, .7f, .52f); button.colors = colors;
        card.icon = Panel("Blueprint", card.rect, 18, 22, 260, 176, card.sprite);
        ShopVisualTheme.CenterImage(card.icon);
        card.name = Label(card.rect, recipe.output.displayName, 30, 202, 236, 38, 31);
        card.name.alignment = TextAlignmentOptions.Midline;
        card.name.textWrappingMode = TextWrappingModes.NoWrap;
        card.name.overflowMode = TextOverflowModes.Ellipsis;
        card.name.enableAutoSizing = true; card.name.fontSizeMin = 18; card.name.fontSizeMax = 31;
        card.priceText = Label(card.rect, "", 30, 272, 185, 38, 36);
        card.coin = Panel("Price Coin", card.rect, 215, 272, 38, 38, coin);
        ShopVisualTheme.CenterImage(card.coin);
        card.check = Rect("Unlocked Check", card.rect, 20, 277, 28, 28).gameObject.AddComponent<WorkbenchGlyph>();
        card.check.shape = WorkbenchGlyph.Shape.Check; card.check.color = Gold; card.check.raycastTarget = false;
        cards.Add(card);
    }
    void Select(RecipeCard card)
    {
        selected = card; Refresh(); RefreshMaterials();
    }
    void Purchase()
    {
        if (selected == null || selected.isUnlocked()) return;
        if (selected.purchase()) { Refresh(); RefreshMaterials(); }
    }
    void Refresh()
    {
        if (moneyText) moneyText.text = ShopMoneyFormatter.Format(observedStats ? observedStats.Money : 0);
        string query = SearchQuery.Trim();
        var ordered = cards.Where(c => query.Length == 0 || NameComparison.IndexOf(
                c.recipe.output.displayName, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .OrderBy(c => c.isUnlocked()).ThenBy(c => c.price).ToArray();
        var visible = new HashSet<RecipeCard>(ordered);
        foreach (var card in cards) card.rect.gameObject.SetActive(visible.Contains(card));
        if (selected == null || !visible.Contains(selected)) selected = ordered.FirstOrDefault();
        for (int i = 0; i < ordered.Length; i++)
        {
            var card = ordered[i]; bool unlocked = card.isUnlocked();
            ItemIconLayout.Apply(card.recipe.output, card.icon.rectTransform, new Vector2(148, -110));
            Place(card.rect, i % Columns * (CardWidth + GapX), i / Columns * (CardHeight + GapY), CardWidth, CardHeight);
            GoldButtonFeedback.Select(card.background, card == selected);
            card.icon.color = unlocked ? new Color(.72f, .66f, .57f, 1f) : Color.white;
            card.name.color = unlocked ? Muted : Cream;
            card.check.gameObject.SetActive(unlocked); card.coin.gameObject.SetActive(!unlocked);
            card.priceText.text = unlocked ? "Freigeschaltet" : ShopMoneyFormatter.Format(card.price);
            bool canAfford = observedStats && observedStats.CanAffordMoney(card.price);
            card.priceText.fontSize = unlocked ? 25 : 32;
            card.priceText.color = unlocked ? Muted : canAfford ? Cream : Unaffordable;
            float priceWidth = Mathf.Min(210, card.priceText.GetPreferredValues(card.priceText.text).x);
            float start = (CardWidth - priceWidth - (unlocked ? 35 : 45)) * .5f;
            Place(card.priceText.rectTransform, start + (unlocked ? 35 : 0), 247, priceWidth + 2, 34);
            Place(card.coin.rectTransform, start + priceWidth + 7, 247, 34, 34);
            ShopVisualTheme.CenterImage(card.coin);
            Place(card.check.rectTransform, start, 250, 28, 28);
        }
        content.sizeDelta = new Vector2(ListWidth, Mathf.Max(ListHeight, Mathf.Ceil(ordered.Length / (float)Columns) * (CardHeight + GapY) - GapY));
        detailPanel.SetActive(selected != null);
        if (selected == null)
        {
            preview.sprite = null; detailName.text = ""; purchaseLabel.text = "";
            purchaseButton.interactable = false; return;
        }
        preview.sprite = selected.sprite; detailName.text = selected.recipe.output.displayName;
        ItemIconLayout.Apply(selected.recipe.output, preview.rectTransform, previewCenter);
        bool owned = selected.isUnlocked();
        bool canAffordSelected = observedStats && observedStats.CanAffordMoney(selected.price);
        string formattedPrice = ShopMoneyFormatter.Format(selected.price);
        purchaseLabel.text = owned ? "Freigeschaltet" : canAffordSelected
            ? "Bauplan für " + formattedPrice + " kaufen"
            : "Bauplan für <color=#E59180>" + formattedPrice + "</color> kaufen";
        purchaseButton.interactable = !owned && canAffordSelected;
    }
    void RefreshMaterials()
    {
        foreach (var row in materialRows) { row.SetActive(false); Destroy(row); }
        materialRows.Clear();
        if (selected == null || !selected.recipe.TryGetCosts(out var costs)) return;
        int i = 0;
        foreach (var cost in costs)
        {
            var row = Panel("Material " + cost.Key.name, materialContent, i % 2 * 302, i / 2 * 102, 286, 90, normalFrame);
            i++; row.pixelsPerUnitMultiplier = 5; materialRows.Add(row.gameObject);
            var icon = Panel("Icon", row.transform, 14, 16, 56, 58, cost.Key.icon);
            ShopVisualTheme.CenterImage(icon);
            ItemIconLayout.Apply(cost.Key, icon.rectTransform, icon.rectTransform.anchoredPosition);
            var name = Label(row.transform, cost.Key.displayName, 80, 10, 142, 70, 28);
            name.enableAutoSizing = true; name.fontSizeMin = 19; name.fontSizeMax = 28;
            name.textWrappingMode = TextWrappingModes.Normal;
            Label(row.transform, cost.Value.ToString(), 223, 10, 48, 70, 34).alignment = TextAlignmentOptions.MidlineRight;
        }
        materialContent.sizeDelta = new Vector2(588, Mathf.Max(192, Mathf.Ceil(i / 2f) * 102 - 12));
        materialScroll.verticalNormalizedPosition = 1f;
    }
    void Build()
    {
        var root = Rect("Blueprint Shop", transform, 0, 0, 2560, 1440);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.anchoredPosition = Vector2.zero;
        BuildSearch(root);
        scroll = ScrollArea(root, "Blueprints", 290, 297, ListWidth, ListHeight, out content);
        AddScrollbar(scroll, root, 1574, 297, 26, ListHeight);
        var detail = Panel("Blueprint Details", root, 1640, 295, 644, 1008, normalFrame).rectTransform;
        detailPanel = detail.gameObject;
        preview = Panel("Selected Blueprint", detail, 97, 38, 450, 405, null);
        ShopVisualTheme.CenterImage(preview);
        previewCenter = preview.rectTransform.anchoredPosition;
        detailName = Label(detail, "", 27, 443, 590, 69, 50);
        detailName.alignment = TextAlignmentOptions.Midline;
        detailName.enableAutoSizing = true; detailName.fontSizeMin = 32; detailName.fontSizeMax = 50;
        Label(detail, "Bauplan", 27, 510, 590, 44, 31).alignment = TextAlignmentOptions.Midline;
        Panel("Detail Divider", detail, 65, 573, 514, 2, null).color = Gold;
        Label(detail, "Herstellungsmaterialien", 27, 589, 590, 52, 34).alignment = TextAlignmentOptions.Midline;
        materialScroll = ScrollArea(detail, "Materials", 28, 655, 588, 192, out materialContent);
        AddScrollbar(materialScroll, detail, 619, 655, 10, 192);
        var action = Panel("Buy Blueprint", detail, 40, 881, 564, 98, actionFrame); action.raycastTarget = true;
        purchaseButton = action.gameObject.AddComponent<Button>(); purchaseButton.targetGraphic = action;
        GoldButtonFeedback.Apply(purchaseButton, normalFrame, selectedFrame);
        HomeClickAudio.Bind(purchaseButton);
        purchaseButton.onClick.AddListener(Purchase);
        var colors = purchaseButton.colors; colors.disabledColor = new Color(.48f, .44f, .38f, 1f);
        colors.highlightedColor = new Color(1f, .95f, .78f); colors.pressedColor = new Color(.8f, .65f, .45f);
        purchaseButton.colors = colors;
        purchaseLabel = Label(action.transform, "", 20, 0, 524, 98, 37);
        purchaseLabel.alignment = TextAlignmentOptions.Midline; purchaseLabel.enableAutoSizing = true;
        purchaseLabel.fontSizeMin = 26; purchaseLabel.fontSizeMax = 37;
    }
    void BuildSearch(Transform root)
    {
        var panel = Panel("Search", root, 306, 184, 552, 86, normalFrame); panel.raycastTarget = true;
        var glyph = Rect("Search Icon", panel.transform, 25, 24, 38, 38).gameObject.AddComponent<WorkbenchGlyph>();
        glyph.shape = WorkbenchGlyph.Shape.Search; glyph.color = Cream; glyph.raycastTarget = false;
        var viewport = Rect("Text Area", panel.transform, 82, 9, 440, 68); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, "", 0, 0, 440, 68, 36);
        text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Overflow;
        var placeholder = Label(viewport, "Bauplan suchen …", 0, 0, 440, 68, 35);
        placeholder.fontStyle = FontStyles.Italic; placeholder.color = Muted;
        searchField = panel.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = viewport; searchField.textComponent = text;
        searchField.placeholder = placeholder; searchField.targetGraphic = panel;
        searchField.characterLimit = 80; searchField.lineType = TMP_InputField.LineType.SingleLine;
        searchField.customCaretColor = true; searchField.caretColor = Cream;
        HomeUi.StyleInputField(searchField, true);
        searchField.onValueChanged.AddListener(SetSearch);
    }
    ScrollRect ScrollArea(Transform parent, string name, float x, float y, float w, float h, out RectTransform items)
    {
        var viewport = Rect(name, parent, x, y, w, h); viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        items = Rect("Content", viewport, 0, 0, w, h);
        var result = viewport.gameObject.AddComponent<ScrollRect>();
        result.viewport = viewport; result.content = items; result.horizontal = false; result.vertical = true;
        result.movementType = ScrollRect.MovementType.Clamped; result.scrollSensitivity = 45; return result;
    }
    void AddScrollbar(ScrollRect target, Transform parent, float x, float y, float w, float h)
    {
        var track = Panel("Scrollbar", parent, x, y, w, h, normalFrame); track.raycastTarget = true;
        track.pixelsPerUnitMultiplier = 12;
        var slidingArea = Rect("Sliding Area", track.transform, 0, 0, w, h);
        slidingArea.anchorMin = Vector2.zero; slidingArea.anchorMax = Vector2.one;
        slidingArea.pivot = new Vector2(.5f, .5f);
        slidingArea.offsetMin = new Vector2(4, 8); slidingArea.offsetMax = new Vector2(-4, -8);
        var handle = Panel("Handle", slidingArea, 0, 0, w, h, actionFrame); handle.raycastTarget = true;
        handle.pixelsPerUnitMultiplier = 14;
        handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = Vector2.one;
        handle.rectTransform.pivot = new Vector2(.5f, .5f);
        handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        target.verticalScrollbar = bar; target.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }
    Image Panel(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); image.sprite = sprite;
        image.type = sprite && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 3f; image.raycastTarget = false; HomeUi.StylePanelWood(image); return image;
    }
    TextMeshProUGUI Label(Transform parent, string value, float x, float y, float w, float h, float size)
    {
        var label = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; if (fontMaterial) label.fontSharedMaterial = fontMaterial;
        label.text = value; label.fontSize = size; label.fontStyle = FontStyles.Normal; label.color = Cream;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false; return label;
    }
    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); Place(rect, x, y, w, h); return rect;
    }
    static void Place(RectTransform rect, float x, float y, float w, float h) => ShopVisualTheme.Place(rect, x, y, w, h);
}
