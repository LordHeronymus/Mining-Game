using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuyPage : MonoBehaviour
{
    const float CardWidth = 455, CardHeight = 222, Gap = 18, ListX = 378;
    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Gold = new Color32(255, 198, 86, 255);
    static readonly Color Muted = new Color32(189, 164, 134, 255);
    static readonly Color Unaffordable = new Color32(229, 145, 128, 255);
    static readonly CompareInfo NameComparison = CultureInfo.GetCultureInfo("de-DE").CompareInfo;
    readonly List<RecipeCard> cards = new();
    readonly List<Sprite> generatedSprites = new();
    readonly List<GameObject> materialRows = new();
    RectTransform content, materialContent;
    ScrollRect scroll, materialScroll;
    TMP_InputField searchField;
    TMP_FontAsset font;
    Material fontMaterial;
    Sprite normalFrame, selectedFrame, actionFrame, coin, buyBackground, previousBackground;
    Image shopBackground, preview;
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
        var source = GetComponentInChildren<TextMeshProUGUI>(true);
        var workbench = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        font = workbench && workbench.font ? workbench.font : source ? source.font : TMP_Settings.defaultFontAsset;
        fontMaterial = workbench ? workbench.fontMaterial : source ? source.fontSharedMaterial : null;
        recipes = (workbench && workbench.recipes != null ? workbench.recipes : Array.Empty<CraftingRecipe>())
            .Concat(Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes")).Where(r => r && r.output).Distinct().ToArray();
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        shopBackground = transform.parent.GetComponent<Image>();
        moneyText = transform.parent.Find("CurrentMoney/Amount")?.GetComponent<TextMeshProUGUI>();
        buyBackground = Resources.Load<Sprite>("Shop/BuyBackground");
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames");
        normalFrame = Slice(sheet, "Card", 60, 135, 1140, 340);
        selectedFrame = Slice(sheet, "Selected", 55, 487, 1150, 370);
        actionFrame = Slice(sheet, "Purchase", 60, 875, 1140, 245);
        coin = Resources.LoadAll<Sprite>("GameOverCoin").FirstOrDefault();
        Build();
        Add(Item.Medkit, "MedkitRecipe");
        Add(Item.IronPickaxe, "IronPickaxeRecipe");
        Add(Item.SteelPickaxe, "SteelPickaxeRecipe");
        Add(Item.TitaniumPickaxe, "TitaniumPickaxeRecipe");
        Add(Item.TungstenPickaxe, "TungstenPickaxeRecipe");
        Add(Item.ObsidianPickaxe, "ObsidianPickaxeRecipe");
        Add(Item.MythrilPickaxe, "MythrilPickaxeRecipe");
        Add(Item.DiamondPickaxe, "DiamondPickaxeRecipe");
        selected = cards.OrderBy(c => c.isUnlocked()).ThenBy(c => c.price).FirstOrDefault();
    }

    void OnEnable()
    {
        if (shopBackground && buyBackground) { previousBackground = shopBackground.sprite; shopBackground.sprite = buyBackground; }
        ObserveStats(); Refresh(); RefreshMaterials();
    }
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
        if (shopBackground && shopBackground.sprite == buyBackground) shopBackground.sprite = previousBackground;
    }
    void OnDestroy() { foreach (var sprite in generatedSprites) if (sprite) Destroy(sprite); }
    void OnMoneyChanged(int _) => Refresh();

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
        var button = card.rect.gameObject.AddComponent<Button>();
        button.targetGraphic = card.background;
        button.onClick.AddListener(() => Select(card));
        var colors = button.colors; colors.highlightedColor = new Color(1f, .94f, .8f);
        colors.pressedColor = new Color(.8f, .7f, .52f); button.colors = colors;
        card.icon = Panel("Blueprint", card.rect, 18, 22, 170, 178, card.sprite);
        card.icon.type = Image.Type.Simple; card.icon.preserveAspect = true;
        card.name = Label(card.rect, recipe.output.displayName, 204, 39, 230, 66, 28);
        card.name.enableAutoSizing = true; card.name.fontSizeMin = 19; card.name.fontSizeMax = 28;
        card.priceText = Label(card.rect, "", 204, 123, 160, 53, 37);
        card.coin = Panel("Price Coin", card.rect, 365, 125, 45, 45, coin);
        card.coin.type = Image.Type.Simple; card.coin.preserveAspect = true;
        card.check = Rect("Unlocked Check", card.rect, 199, 130, 34, 34).gameObject.AddComponent<WorkbenchGlyph>();
        card.check.shape = WorkbenchGlyph.Shape.Check; card.check.color = Gold; card.check.raycastTarget = false;
        cards.Add(card);
    }
    void Select(RecipeCard card)
    {
        selected = card; AudioManager.Instance?.Play(SoundType.UI_Click); Refresh(); RefreshMaterials();
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
            Place(card.rect, i % 2 * (CardWidth + Gap), i / 2 * (CardHeight + Gap), CardWidth, CardHeight);
            card.background.sprite = card == selected ? selectedFrame : normalFrame;
            card.icon.color = unlocked ? new Color(.72f, .66f, .57f, 1f) : Color.white;
            card.name.color = unlocked ? Muted : Cream;
            card.check.gameObject.SetActive(unlocked); card.coin.gameObject.SetActive(!unlocked);
            card.priceText.text = unlocked ? "Freigeschaltet" : ShopMoneyFormatter.Format(card.price);
            bool canAfford = observedStats && observedStats.CanAffordMoney(card.price);
            card.priceText.fontSize = unlocked ? 24 : 37;
            card.priceText.color = unlocked ? Muted : canAfford ? Cream : Unaffordable;
            Place(card.priceText.rectTransform, unlocked ? 239 : 204, 123, unlocked ? 195 : 160, 53);
            float priceWidth = card.priceText.GetPreferredValues(card.priceText.text).x;
            card.coin.rectTransform.anchoredPosition = new Vector2(211 + Mathf.Min(145, priceWidth), -126);
        }
        if (content) content.sizeDelta = new Vector2(928, Mathf.Max(702, Mathf.Ceil(ordered.Length / 2f) * (CardHeight + Gap) - Gap));
        if (selected == null)
        {
            preview.sprite = null; detailName.text = ""; purchaseLabel.text = "";
            purchaseButton.interactable = false;
            return;
        }
        preview.sprite = selected.sprite; detailName.text = selected.recipe.output.displayName;
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
        foreach (var row in materialRows) Destroy(row);
        materialRows.Clear();
        if (selected == null || !selected.recipe.TryGetCosts(out var costs)) return;
        int i = 0;
        foreach (var cost in costs)
        {
            var row = Panel("Material " + cost.Key.name, materialContent, 0, i++ * 82, 445, 74, normalFrame);
            materialRows.Add(row.gameObject);
            var icon = Panel("Icon", row.transform, 14, 9, 58, 55, cost.Key.icon);
            icon.type = Image.Type.Simple; icon.preserveAspect = true;
            var name = Label(row.transform, cost.Key.displayName, 86, 7, 274, 60, 28);
            name.enableAutoSizing = true; name.fontSizeMin = 20; name.fontSizeMax = 28;
            Label(row.transform, cost.Value.ToString(), 360, 7, 64, 60, 31).alignment = TextAlignmentOptions.MidlineRight;
        }
        materialContent.sizeDelta = new Vector2(445, Mathf.Max(324, i * 82 - 8));
        materialScroll.verticalNormalizedPosition = 1f;
    }
    void Build()
    {
        var root = Rect("Blueprint Shop", transform, 0, 0, 2560, 1440);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.anchoredPosition = Vector2.zero;
        BuildSearch(root);
        scroll = ScrollArea(root, "Blueprints", ListX, 450, 928, 702, out content);
        AddScrollbar(scroll, root, ListX + 945, 450, 24, 702);
        preview = Panel("Selected Blueprint", root, 1410, 455, 290, 390, null);
        preview.type = Image.Type.Simple; preview.preserveAspect = true;
        detailName = Label(root, "", 1730, 465, 490, 90, 43);
        detailName.enableAutoSizing = true; detailName.fontSizeMin = 29; detailName.fontSizeMax = 43;
        Label(root, "Bauplan", 1730, 555, 450, 46, 29);
        Panel("Detail Divider", root, 1730, 622, 455, 3, null).color = Gold;
        Label(root, "Herstellungsmaterialien", 1730, 642, 470, 65, 30);
        materialScroll = ScrollArea(root, "Materials", 1730, 713, 445, 324, out materialContent);
        AddScrollbar(materialScroll, root, 2188, 713, 14, 324);
        var action = Panel("Buy Blueprint", root, 1405, 1038, 805, 118, actionFrame);
        action.raycastTarget = true;
        purchaseButton = action.gameObject.AddComponent<Button>(); purchaseButton.targetGraphic = action;
        purchaseButton.onClick.AddListener(Purchase);
        var colors = purchaseButton.colors; colors.disabledColor = new Color(.48f, .44f, .38f, 1f);
        colors.highlightedColor = new Color(1f, .95f, .78f); colors.pressedColor = new Color(.8f, .65f, .45f);
        purchaseButton.colors = colors;
        purchaseLabel = Label(action.transform, "", 35, 0, 735, 118, 40);
        purchaseLabel.alignment = TextAlignmentOptions.Center; purchaseLabel.enableAutoSizing = true;
        purchaseLabel.rectTransform.anchoredPosition += Vector2.down * 7f;
        purchaseLabel.fontSizeMin = 28; purchaseLabel.fontSizeMax = 40;
    }
    void BuildSearch(Transform root)
    {
        var panel = Panel("Search", root, ListX, 350, 430, 75, normalFrame);
        panel.raycastTarget = true;
        var glyph = Rect("Search Icon", panel.transform, 19, 17, 40, 40).gameObject.AddComponent<WorkbenchGlyph>();
        glyph.shape = WorkbenchGlyph.Shape.Search; glyph.color = Cream; glyph.raycastTarget = false;
        var viewport = Rect("Text Area", panel.transform, 72, 8, 335, 58);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, "", 0, 0, 335, 58, 30);
        text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Overflow;
        var placeholder = Label(viewport, "Bauplan suchen …", 0, 0, 335, 58, 28);
        placeholder.fontStyle = FontStyles.Italic; placeholder.color = Muted;
        searchField = panel.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = viewport; searchField.textComponent = text;
        searchField.placeholder = placeholder; searchField.targetGraphic = panel;
        searchField.characterLimit = 80; searchField.lineType = TMP_InputField.LineType.SingleLine;
        searchField.customCaretColor = true; searchField.caretColor = Cream;
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
        var slidingArea = Rect("Sliding Area", track.transform, 0, 0, w, h);
        slidingArea.anchorMin = Vector2.zero; slidingArea.anchorMax = Vector2.one;
        slidingArea.pivot = new Vector2(.5f, .5f); slidingArea.offsetMin = slidingArea.offsetMax = Vector2.zero;
        var handle = Panel("Handle", slidingArea, 0, 0, w, h, actionFrame); handle.raycastTarget = true;
        handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = Vector2.one;
        handle.rectTransform.pivot = new Vector2(.5f, .5f);
        handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        target.verticalScrollbar = bar; target.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }
    Sprite Slice(Texture2D texture, string name, float x, float y, float w, float h)
    {
        if (!texture) return null;
        float sx = texture.width / 1280f, sy = texture.height / 1280f;
        var sprite = Sprite.Create(texture, new Rect(x * sx, (1280 - y - h) * sy, w * sx, h * sy),
            new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 70 * sy, 100 * sx, 70 * sy));
        sprite.name = name; generatedSprites.Add(sprite); return sprite;
    }
    Image Panel(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); image.sprite = sprite;
        image.type = sprite && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 3f; image.raycastTarget = false; return image;
    }
    TextMeshProUGUI Label(Transform parent, string value, float x, float y, float w, float h, float size)
    {
        var label = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; if (fontMaterial) label.fontSharedMaterial = fontMaterial;
        label.text = value; label.fontSize = size; label.fontStyle = FontStyles.Bold; label.color = Cream;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false; return label;
    }
    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); Place(rect, x, y, w, h); return rect;
    }
    static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
    }
}
