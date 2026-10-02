using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-90)]
[RequireComponent(typeof(CanvasGroup))]
public sealed class WorkbenchPanel : MonoBehaviour
{
    public CraftingRecipe[] recipes;
    public TMP_FontAsset font;
    public Material fontMaterial;
    public Sprite rowSprite, selectedRowSprite, actionSprite;
    public int initialRecipe = 2;
    public bool allowKeyboardOpen = true;

    public bool IsOpen { get; private set; }
    public CraftingRecipe SelectedRecipe { get; private set; }
    public int Quantity { get; private set; } = 1;
    public bool OnlyCraftable { get; private set; }
    public bool OnlyFavorites { get; private set; }
    public string SearchQuery { get; private set; } = "";
    public CraftingRecipe.RecipeCategory SelectedCategory { get; private set; }
    public int VisibleRecipeCount { get; private set; }

    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Muted = new Color32(224, 207, 187, 255);
    static readonly Color Enough = new Color32(150, 224, 109, 255);
    static readonly Color Missing = new Color32(232, 134, 101, 255);
    const float CraftingPickupLeadTime = .3f;
    const float LayoutWidth = 1640, LayoutHeight = 924;
    const float GridWidth = 758, GridHeight = 570, CardWidth = 178, CardHeight = 180, ColumnPitch = 192, RowPitch = 195;
    const float IngredientWidth = 450, IngredientHeight = 134;
    readonly List<Sprite> styleSprites = new();
    Material ownedFontMaterial, glowMaterial;
    Sprite backgroundSprite;
    readonly List<RecipeRow> rows = new();
    readonly List<IngredientRow> ingredientRows = new();
    CanvasGroup group;
    RectTransform layout, recipeContent, ingredientContent, details, quantityControls;
    Image outputIcon, favoriteFilter;
    readonly List<Image> categoryTabs = new();
    WorkbenchGlyph craftableCheck, favoriteFilterStar;
    TMP_InputField searchField;
    ScrollRect recipeScroll, ingredientScroll;
    TextMeshProUGUI outputName, resultText, quantityText, craftText, emptyText;
    Button minus, plus, maximum, craft;
    InventoryManager inventory;
    CraftingRecipe renderedRecipe;
    bool craftingPending;

    sealed class RecipeRow { public CraftingRecipe recipe; public RectTransform rect, icon; public Image image; public WorkbenchGlyph star; public bool favorite; }
    sealed class IngredientRow { public ItemSO item; public int amount; public TextMeshProUGUI count; }

    void Awake()
    {
        LoadResourceRecipes();
        LoadVisualStyle();
        group = GetComponent<CanvasGroup>();
        BuildView();
        group.alpha = 0;
        group.blocksRaycasts = group.interactable = false;
        if (layout) layout.gameObject.SetActive(false);
    }

    void LoadResourceRecipes()
    {
        var combined = new List<CraftingRecipe>();
        var seen = new HashSet<CraftingRecipe>();
        if (recipes != null)
            foreach (var recipe in recipes)
                if (recipe && seen.Add(recipe)) combined.Add(recipe);

        foreach (var recipe in Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes"))
            if (recipe && seen.Add(recipe)) combined.Add(recipe);

        recipes = combined.ToArray();
    }

    void Update()
    {
        if (IsOpen)
        {
            if (glowMaterial) glowMaterial.SetFloat("_AnimationTime", Time.unscaledTime);
            if (GameBindings.Down(GameAction.Settings) || (GameBindings.Down(GameAction.Workbench) && !searchField.isFocused)) ShowPanel(false);
            else if (SubscribeInventory()) Refresh();
        }
        else if (allowKeyboardOpen && GameBindings.Down(GameAction.Workbench) && !GameplayInputBlocker.IsBlocked)
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (!selected || !selected.GetComponent<TMP_InputField>()) ShowPanel(true);
        }
    }

    void OnRectTransformDimensionsChange() => FitLayout();
    void FitLayout()
    {
        if (!layout) return;
        var size = ((RectTransform)transform).rect.size;
        float scale = Mathf.Min(size.x / LayoutWidth, size.y / LayoutHeight);
        layout.localScale = new Vector3(scale, scale, 1);
    }

    void OnDisable()
    {
        if (inventory) inventory.OnInventoryChanged -= OnInventoryChanged;
        inventory = null;
        GameplayInputBlocker.SetBlocked(this, false);
        if (IsOpen) InfoPanel.Instance?.ShowPanel(true);
        IsOpen = false;
        if (layout) layout.gameObject.SetActive(false);
        if (group) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
    }

    bool SubscribeInventory()
    {
        if (inventory == InventoryManager.Instance) return false;
        if (inventory) inventory.OnInventoryChanged -= OnInventoryChanged;
        inventory = InventoryManager.Instance;
        if (inventory) inventory.OnInventoryChanged += OnInventoryChanged;
        return true;
    }

    void OnInventoryChanged()
    {
        // Opening always refreshes from the current inventory; hidden cards need no updates.
        if (IsOpen) Refresh();
    }

    public void ShowPanel(bool show)
    {
        if (show && !IsOpen && GameplayInputBlocker.IsBlocked) return;
        IsOpen = show;
        if (layout) layout.gameObject.SetActive(show);
        GameplayInputBlocker.SetBlocked(this, show);
        group.alpha = show ? 1 : 0;
        group.interactable = group.blocksRaycasts = show;
        InfoPanel.Instance?.ShowPanel(!show);
        if (show)
        {
            transform.SetAsLastSibling();
            SubscribeInventory();
            Refresh();
        }
        else if (EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                 EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void SelectRecipe(CraftingRecipe recipe)
    {
        SelectedRecipe = recipe;
        Quantity = 1;
        Refresh();
    }

    public void RefreshRecipeSettings(CraftingRecipe recipe)
    {
        if (renderedRecipe == recipe) renderedRecipe = null;
        Refresh();
    }
    public void RefreshRecipeIcons()
    {
        foreach (var row in rows) ApplyRecipeIconLayout(row.recipe, row.icon);
    }

    public void SetFilter(bool onlyCraftable)
    {
        OnlyCraftable = onlyCraftable;
        FilterChanged();
    }

    public void SetSearch(string query)
    {
        SearchQuery = query ?? "";
        if (searchField && searchField.text != SearchQuery) searchField.SetTextWithoutNotify(SearchQuery);
        FilterChanged();
    }

    public void SetCategory(CraftingRecipe.RecipeCategory category)
    {
        SelectedCategory = category;
        FilterChanged();
    }

    public void SetFavoritesOnly(bool favoritesOnly)
    {
        OnlyFavorites = favoritesOnly;
        FilterChanged();
    }

    public bool IsFavorite(CraftingRecipe recipe) => recipe && PlayerPrefs.GetInt(recipe.FavoriteKey, 0) == 1;

    public void ToggleFavorite(CraftingRecipe recipe)
    {
        if (!recipe) return;
        bool favorite = !IsFavorite(recipe);
        if (favorite) PlayerPrefs.SetInt(recipe.FavoriteKey, 1);
        else PlayerPrefs.DeleteKey(recipe.FavoriteKey);
        PlayerPrefs.Save();
        foreach (var row in rows) if (row.recipe == recipe) row.favorite = favorite;
        Refresh();
    }

    void FilterChanged()
    {
        Refresh();
        if (recipeScroll) { recipeScroll.StopMovement(); recipeScroll.verticalNormalizedPosition = 1; }
    }

    public void SetQuantity(int quantity)
    {
        Quantity = Mathf.Clamp(quantity, 1, Mathf.Max(1, CraftingService.GetMaxCraftable(SelectedRecipe, inventory)));
        Refresh();
    }

    public bool CraftSelected()
    {
        if (craftingPending || !SelectedRecipe || !inventory || Quantity <= 0 ||
            CraftingService.GetMaxCraftable(SelectedRecipe, inventory) < Quantity) return false;
        craftingPending = true;
        StartCoroutine(CompleteCraftAfterSound(SelectedRecipe, Quantity));
        Refresh();
        return true;
    }

    System.Collections.IEnumerator CompleteCraftAfterSound(CraftingRecipe recipe, int batches)
    {
        var audio = AudioManager.Instance;
        if (audio && audio.HasSound(SoundType.ItemInBag))
        {
            AudioSource source = null;
            while (audio && !source)
            {
                source = audio.TryPlayCraftingSound();
                if (!source) yield return null;
            }
            while (source && source.isPlaying && source.clip &&
                   source.clip.length - source.time > CraftingPickupLeadTime) yield return null;
        }

        if (inventory && recipe) CraftingService.TryCraft(recipe, inventory, batches);
        craftingPending = false;
        Refresh();
    }

    void Refresh()
    {
        if (!layout) return;
        for (int i = 0; i < categoryTabs.Count; i++) categoryTabs[i].sprite = i == (int)SelectedCategory ? actionSprite : rowSprite;
        craftableCheck.filled = OnlyCraftable;
        craftableCheck.SetVerticesDirty();
        favoriteFilter.sprite = OnlyFavorites ? selectedRowSprite : rowSprite;
        favoriteFilterStar.filled = OnlyFavorites;
        favoriteFilterStar.SetVerticesDirty();
        string query = SearchQuery.Trim();
        int visible = 0;
        CraftingRecipe first = null;
        bool selectionVisible = false;
        foreach (var row in rows)
        {
            row.star.filled = row.favorite;
            row.star.color = row.favorite ? new Color32(255, 199, 70, 255) : Muted;
            row.star.SetVerticesDirty();
            bool show = RecipeUnlocks.IsUnlocked(row.recipe) && !CraftingService.IsOwnedPowerup(row.recipe, inventory)
                && (SelectedCategory == CraftingRecipe.RecipeCategory.Automatic || row.recipe.Category == SelectedCategory)
                && (!OnlyFavorites || row.favorite)
                && (query.Length == 0 || CultureInfo.GetCultureInfo("de-DE").CompareInfo.IndexOf(
                    row.recipe.output.displayName, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                && (!OnlyCraftable || CraftingService.GetMaxCraftable(row.recipe, inventory) > 0);
            row.rect.gameObject.SetActive(show);
            if (!show) continue;
            if (!first) first = row.recipe;
            if (row.recipe == SelectedRecipe) selectionVisible = true;
            row.rect.anchoredPosition = new Vector2(visible % 4 * ColumnPitch, -(visible / 4) * RowPitch);
            visible++;
        }
        VisibleRecipeCount = visible;
        recipeContent.sizeDelta = new Vector2(GridWidth, Mathf.Max(GridHeight, ((visible + 3) / 4) * RowPitch - (RowPitch - CardHeight)));
        emptyText.gameObject.SetActive(visible == 0);
        if (!selectionVisible) { SelectedRecipe = first; Quantity = 1; }
        foreach (var row in rows) row.image.sprite = row.recipe == SelectedRecipe ? selectedRowSprite : rowSprite;
        details.gameObject.SetActive(SelectedRecipe);
        if (!SelectedRecipe) return;
        quantityControls.gameObject.SetActive(SelectedRecipe.output.category != ItemCategory.Powerup);

        if (renderedRecipe != SelectedRecipe)
        {
            renderedRecipe = SelectedRecipe;
            foreach (Transform child in ingredientContent) Destroy(child.gameObject);
            ingredientRows.Clear();
            if (SelectedRecipe.TryGetCosts(out var costs))
            {
                int i = 0;
                foreach (var cost in costs)
                {
                    var row = Panel("Ingredient_" + cost.Key.name, ingredientContent, i % 2 * 230, i / 2 * 72, 220, 62, rowSprite);
                    Icon(row.transform, cost.Key.icon, 9, 10, 43, 42);
                    Label(row.transform, cost.Key.displayName, 60, 5, 146, 25, 23);
                    var count = Label(row.transform, "", 60, 30, 146, 25, 22, TextAlignmentOptions.MidlineLeft);
                    ingredientRows.Add(new IngredientRow { item = cost.Key, amount = cost.Value, count = count });
                    i++;
                }
                ingredientContent.sizeDelta = new Vector2(IngredientWidth, Mathf.Max(IngredientHeight, ((i + 1) / 2) * 72 - 10));
                ingredientScroll.StopMovement(); ingredientScroll.verticalNormalizedPosition = 1;
            }
            outputIcon.sprite = SelectedRecipe.output.icon;
            outputName.text = SelectedRecipe.output.displayName;
        }
        int max = CraftingService.GetMaxCraftable(SelectedRecipe, inventory);
        Quantity = Mathf.Clamp(Quantity, 1, Mathf.Max(1, max));
        foreach (var row in ingredientRows)
        {
            int owned = inventory ? inventory.GetCount(row.item) : 0;
            long needed = (long)row.amount * Quantity;
            string color = ColorUtility.ToHtmlStringRGB(owned >= needed ? Enough : Missing);
            row.count.text = $"<color=#{color}>{owned}</color> / {needed}";
        }
        long output = (long)SelectedRecipe.outputAmount * Quantity;
        resultText.text = $"Ergebnis: {output} {SelectedRecipe.ResultName(output)}";
        quantityText.text = Quantity.ToString();
        craftText.text = $"{output} {SelectedRecipe.ResultName(output)} herstellen";
        minus.interactable = Quantity > 1;
        plus.interactable = Quantity < max;
        maximum.interactable = max > 0 && Quantity != max;
        craft.interactable = !craftingPending && max >= Quantity;
    }

    void LoadVisualStyle()
    {
        backgroundSprite = Resources.Load<Sprite>("Workbench/WorkshopBackground-v2");
        var titleFont = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (titleFont)
        {
            font = titleFont;
            ownedFontMaterial = new Material(font.material);
            ownedFontMaterial.SetFloat("_OutlineWidth", .065f);
            ownedFontMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
            fontMaterial = ownedFontMaterial;
        }
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames-v2");
        if (sheet)
        {
            rowSprite = SliceFrame(sheet, "Workbench Card", 40, 110, 1175, 325);
            selectedRowSprite = SliceFrame(sheet, "Workbench Selected", 35, 490, 1185, 340);
            actionSprite = SliceFrame(sheet, "Workbench Action", 38, 890, 1178, 275);
        }
    }

    Sprite SliceFrame(Texture2D sheet, string name, float x, float top, float width, float height)
    {
        float sx = sheet.width / 1280f, sy = sheet.height / 1280f;
        var sprite = Sprite.Create(sheet, new UnityEngine.Rect(x * sx, (1280 - top - height) * sy, width * sx, height * sy),
            Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 80 * sy, 100 * sx, 80 * sy));
        sprite.name = name; styleSprites.Add(sprite); return sprite;
    }

    void OnDestroy()
    {
        foreach (var sprite in styleSprites) if (sprite) Destroy(sprite);
        if (ownedFontMaterial) Destroy(ownedFontMaterial);
        if (glowMaterial) Destroy(glowMaterial);
    }

    void BuildView()
    {
        layout = Rect("Layout", transform, 0, 0, LayoutWidth, LayoutHeight);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        var backdrop = GetComponent<Image>();
        var background = Panel("Background", layout, 0, 0, LayoutWidth, LayoutHeight, backgroundSprite ? backgroundSprite : backdrop ? backdrop.sprite : null);
        background.type = Image.Type.Simple;
        if (backdrop) { backdrop.sprite = null; backdrop.color = new Color32(15, 9, 5, 255); }
        Label(layout, "Werkbank", 550, 24, 540, 80, 64, TextAlignmentOptions.Midline);
        BuildSearch();
        string[] categories = { "Alle", "Werkzeuge", "Bauen", "Materialien" };
        for (int i = 0; i < categories.Length; i++)
        {
            var category = (CraftingRecipe.RecipeCategory)i;
            var tab = Button("Category_" + category, layout, categories[i], 524 + i * 158, 137, 148, 49, () => SetCategory(category));
            tab.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 25;
            categoryTabs.Add(tab.GetComponent<Image>());
        }
        favoriteFilter = Button("Favorites", layout, "", 1166, 137, 49, 49, () => SetFavoritesOnly(!OnlyFavorites)).GetComponent<Image>();
        favoriteFilterStar = Glyph(favoriteFilter.transform, WorkbenchGlyph.Shape.Star, 11, 11, 27);
        favoriteFilterStar.color = new Color32(255, 199, 70, 255);
        var craftable = Button("Craftable", layout, "", 1230, 137, 222, 49, () => SetFilter(!OnlyCraftable));
        craftable.GetComponent<Image>().color = Color.clear;
        Panel("Checkbox", craftable.transform, 8, 10, 29, 29, rowSprite);
        craftableCheck = Glyph(craftable.transform, WorkbenchGlyph.Shape.Check, 10, 12, 25);
        Label(craftable.transform, "Herstellbar", 47, 0, 165, 49, 28);
        recipeContent = ScrollArea("Recipes", layout, 179, 230, GridWidth, GridHeight, GridWidth);
        recipeScroll = recipeContent.parent.GetComponent<ScrollRect>();
        AddScrollbar(recipeScroll, layout, 944, 230, 13, GridHeight);
        if (recipes != null) foreach (var recipe in recipes)
        {
            if (!recipe || !recipe.output) continue;
            var button = Button(recipe.name, recipeContent, "", 0, 0, CardWidth, CardHeight, () => SelectRecipe(recipe));
            var iconArea = Rect("Icon Area", button.transform, 2, 10, 174, 130);
            var icon = Icon(iconArea, recipe.output.icon, 20, 12, 134, 106);
            ApplyRecipeIconLayout(recipe, icon.rectTransform);
            var name = Label(button.transform, recipe.output.displayName, 10, 140, CardWidth - 20, 30, 23, TextAlignmentOptions.Midline);
            name.fontSizeMin = 15;
            var favorite = Button("Favorite", button.transform, "", CardWidth - 36, 5, 32, 32, () => ToggleFavorite(recipe));
            favorite.GetComponent<Image>().color = Color.clear;
            var star = Glyph(favorite.transform, WorkbenchGlyph.Shape.Star, 5, 5, 22);
            rows.Add(new RecipeRow { recipe = recipe, rect = (RectTransform)button.transform, icon = icon.rectTransform,
                image = button.GetComponent<Image>(), star = star, favorite = IsFavorite(recipe) });
        }
        emptyText = Label(layout, "Keine Rezepte", 179, 445, GridWidth, 60, 30, TextAlignmentOptions.Midline);
        details = Rect("Details", layout, 0, 0, LayoutWidth, LayoutHeight);
        var glow = Panel("Item Glow", details, 1010, 233, 470, 286, null);
        var shader = Resources.Load<Shader>("Workbench/ItemGlow");
        if (shader) { glowMaterial = new Material(shader); glow.material = glowMaterial; }
        else glow.color = Color.clear;
        var ornament = Rect("Preview Ornament", details, 1050, 240, 390, 252).gameObject.AddComponent<WorkbenchPreviewOrnament>();
        ornament.color = new Color(1, .72f, .28f, .52f); ornament.raycastTarget = false;
        outputIcon = Icon(details, null, 1110, 235, 270, 215);
        outputName = Label(details, "", 1000, 455, 490, 44, 39, TextAlignmentOptions.Midline);
        resultText = Label(details, "", 1000, 499, 490, 34, 24, TextAlignmentOptions.Midline);
        resultText.color = Muted;
        var line = Panel("Separator", details, 1030, 539, 430, 1, null);
        line.color = new Color32(169, 123, 65, 255);
        Label(details, "Materialien", 1030, 550, 225, 36, 30);
        Label(details, "Vorhanden / Benötigt", 1235, 550, 225, 36, 20, TextAlignmentOptions.MidlineRight);
        ingredientContent = ScrollArea("Ingredients", details, 1020, 592, IngredientWidth, IngredientHeight, IngredientWidth);
        ingredientScroll = ingredientContent.parent.GetComponent<ScrollRect>();
        AddScrollbar(ingredientScroll, details, 1475, 592, 10, IngredientHeight);
        quantityControls = Rect("QuantityControls", details, 0, 0, LayoutWidth, LayoutHeight);
        Label(quantityControls, "Menge", 1030, 739, 105, 38, 27);
        minus = Button("Minus", quantityControls, "−", 1140, 739, 38, 38, () => SetQuantity(Quantity - 1));
        Panel("QuantityField", quantityControls, 1185, 739, 82, 38, rowSprite);
        quantityText = Label(quantityControls, "1", 1185, 739, 82, 38, 27, TextAlignmentOptions.Midline);
        plus = Button("Plus", quantityControls, "+", 1274, 739, 38, 38, () => SetQuantity(Quantity + (Quantity < int.MaxValue ? 1 : 0)));
        maximum = Button("Max", quantityControls, "Max", 1330, 739, 130, 38,
            () => SetQuantity(CraftingService.GetMaxCraftable(SelectedRecipe, inventory)));
        craft = Button("Craft", details, "Herstellen", 1020, 787, 450, 49, () => CraftSelected());
        craft.GetComponent<Image>().sprite = actionSprite;
        craftText = craft.GetComponentInChildren<TextMeshProUGUI>();
        craftText.fontSizeMax = 27;
        SelectedRecipe = recipes != null && recipes.Length > 0 ? recipes[Mathf.Clamp(initialRecipe, 0, recipes.Length - 1)] : null;
        FitLayout(); Refresh();
    }

    public static void ApplyRecipeIconLayout(CraftingRecipe recipe, RectTransform icon)
    {
        if (!recipe || !icon) return;
        var settings = recipe.CardIconLayout;
        icon.pivot = new Vector2(.5f, .5f);
        icon.anchoredPosition = new Vector2(87f + settings.offset.x, -65f + settings.offset.y);
        icon.localScale = new Vector3(settings.scale.x * (settings.flipX ? -1f : 1f),
            settings.scale.y * (settings.flipY ? -1f : 1f), 1f);
    }

    public CraftingRecipe FindRecipeForOutput(ItemSO item)
    {
        if (!item || recipes == null) return null;
        foreach (var recipe in recipes)
            if (recipe && recipe.output == item) return recipe;
        return null;
    }

    WorkbenchGlyph Glyph(Transform parent, WorkbenchGlyph.Shape shape, float x, float y, float size)
    {
        var glyph = Rect(shape.ToString(), parent, x, y, size, size).gameObject.AddComponent<WorkbenchGlyph>();
        glyph.shape = shape; glyph.color = Cream; glyph.raycastTarget = false;
        return glyph;
    }

    void BuildSearch()
    {
        var panel = Panel("Search", layout, 180, 137, 327, 49, rowSprite);
        panel.raycastTarget = true;
        Glyph(panel.transform, WorkbenchGlyph.Shape.Search, 12, 10, 28);
        var viewport = Rect("Text Area", panel.transform, 48, 4, 254, 40);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, "", 0, 0, 254, 40, 23);
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Overflow;
        var placeholder = Label(viewport, "Rezept suchen …", 0, 0, 254, 40, 22);
        placeholder.fontStyle = FontStyles.Italic; placeholder.color = Muted;
        searchField = panel.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = viewport;
        searchField.textComponent = text; searchField.placeholder = placeholder;
        searchField.targetGraphic = panel;
        searchField.characterLimit = 80;
        searchField.lineType = TMP_InputField.LineType.SingleLine;
        searchField.customCaretColor = true; searchField.caretColor = Cream;
        searchField.onValueChanged.AddListener(SetSearch);
    }

    void AddScrollbar(ScrollRect scroll, Transform parent, float x, float y, float width, float height)
    {
        var track = Panel("RecipeScrollbar", parent, x, y, width, height, rowSprite);
        track.raycastTarget = true; track.pixelsPerUnitMultiplier = 16;
        var area = Rect("Sliding Area", track.transform, 2, 2, width - 4, height - 4);
        var handle = Panel("Handle", area, 0, 0, width - 4, height - 4, selectedRowSprite);
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

    RectTransform ScrollArea(string name, Transform parent, float x, float y, float width, float height, float contentWidth)
    {
        var viewport = Rect(name, parent, x, y, width, height);
        var background = viewport.gameObject.AddComponent<Image>();
        background.color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, 0, 0, contentWidth, height);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35;
        return content;
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
        return image;
    }

    Image Icon(Transform parent, Sprite sprite, float x, float y, float width, float height)
    {
        var image = Panel("Icon", parent, x, y, width, height, sprite);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
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
            TextAlignmentOptions.Left => TextAlignmentOptions.MidlineLeft,
            TextAlignmentOptions.Center => TextAlignmentOptions.Midline,
            TextAlignmentOptions.Right => TextAlignmentOptions.MidlineRight,
            _ => alignment
        }; text.textWrappingMode = TextWrappingModes.NoWrap;
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
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.highlightedColor = new Color(1.12f, 1.08f, 1f);
        colors.pressedColor = new Color(.8f, .7f, .6f);
        colors.disabledColor = new Color(.45f, .45f, .45f);
        button.colors = colors;
        button.onClick.AddListener(action);
        Label(image.transform, caption, 8, 0, width - 16, height, 29, TextAlignmentOptions.Center);
        return button;
    }
}
