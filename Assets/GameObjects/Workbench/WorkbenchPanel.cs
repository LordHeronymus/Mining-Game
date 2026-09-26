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
    readonly List<RecipeRow> rows = new();
    readonly List<IngredientRow> ingredientRows = new();
    CanvasGroup group;
    RectTransform layout, recipeContent, ingredientContent, details, quantityControls;
    Image outputIcon, favoriteFilter;
    readonly List<Image> categoryTabs = new();
    WorkbenchGlyph craftableCheck, favoriteFilterStar;
    TMP_InputField searchField;
    ScrollRect recipeScroll;
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
        group = GetComponent<CanvasGroup>();
        BuildView();
        group.alpha = 0;
        group.blocksRaycasts = group.interactable = false;
    }

    void LoadResourceRecipes()
    {
        var resourceRecipes = Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes");
        if (resourceRecipes.Length == 0) return;

        var combined = new List<CraftingRecipe>(recipes ?? System.Array.Empty<CraftingRecipe>());
        foreach (var recipe in resourceRecipes)
            if (recipe && !combined.Contains(recipe)) combined.Add(recipe);
        recipes = combined.ToArray();
    }

    void Update()
    {
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetKeyDown(KeyCode.B) && !searchField.isFocused)) ShowPanel(false);
            else SubscribeInventory();
        }
        else if (allowKeyboardOpen && Input.GetKeyDown(KeyCode.B) && !GameplayInputBlocker.IsBlocked)
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
        float scale = Mathf.Min(size.x / 1640f, size.y / 960f);
        layout.localScale = new Vector3(scale, scale, 1);
    }

    void OnDisable()
    {
        if (inventory) inventory.OnInventoryChanged -= Refresh;
        inventory = null;
        GameplayInputBlocker.SetBlocked(this, false);
        if (IsOpen) InfoPanel.Instance?.ShowPanel(true);
        IsOpen = false;
        if (group) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
    }

    void SubscribeInventory()
    {
        if (inventory == InventoryManager.Instance) return;
        if (inventory) inventory.OnInventoryChanged -= Refresh;
        inventory = InventoryManager.Instance;
        if (inventory) inventory.OnInventoryChanged += Refresh;
        Refresh();
    }

    public void ShowPanel(bool show)
    {
        if (show && !IsOpen && GameplayInputBlocker.IsBlocked) return;
        IsOpen = show;
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
            row.rect.anchoredPosition = new Vector2(visible % 4 * 196, -(visible / 4) * 174);
            visible++;
        }
        VisibleRecipeCount = visible;
        recipeContent.sizeDelta = new Vector2(770, Mathf.Max(508, ((visible + 3) / 4) * 174 - 14));
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
                    var row = Panel("Ingredient_" + cost.Key.name, ingredientContent, 0, i * 68, 490, 61, rowSprite);
                    Icon(row.transform, cost.Key.icon, 10, 6, 68, 49);
                    Label(row.transform, cost.Key.displayName, 87, 0, 187, 61, 25);
                    var count = Label(row.transform, "", 278, 0, 199, 61, 25, TextAlignmentOptions.Right);
                    ingredientRows.Add(new IngredientRow { item = cost.Key, amount = cost.Value, count = count });
                    i++;
                }
                ingredientContent.sizeDelta = new Vector2(490, Mathf.Max(215, i * 68 - 7));
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

    void BuildView()
    {
        layout = Rect("Layout", transform, 0, 0, 1640, 960);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        var backdrop = GetComponent<Image>();
        if (backdrop)
        {
            var background = Panel("Background", layout, 0, 0, 1640, 960, backdrop.sprite);
            background.type = Image.Type.Simple;
            backdrop.sprite = null; backdrop.color = new Color32(15, 9, 5, 255);
        }
        Label(layout, "Werkbank", 550, 75, 535, 80, 57, TextAlignmentOptions.Center);
        Label(layout, "Rezepte", 170, 201, 228, 50, 38);
        BuildSearch();
        favoriteFilter = Button("Favorites", layout, "", 744, 204, 50, 48, () => SetFavoritesOnly(!OnlyFavorites)).GetComponent<Image>();
        favoriteFilterStar = Glyph(favoriteFilter.transform, WorkbenchGlyph.Shape.Star, 9, 7, 32);
        favoriteFilterStar.color = new Color32(255, 199, 70, 255);
        var craftable = Button("Craftable", layout, "", 812, 204, 153, 48, () => SetFilter(!OnlyCraftable));
        craftable.GetComponent<Image>().color = Color.clear;
        Panel("Checkbox", craftable.transform, 0, 8, 31, 31, rowSprite);
        craftableCheck = Glyph(craftable.transform, WorkbenchGlyph.Shape.Check, 2, 10, 27);
        Label(craftable.transform, "Herstellbar", 39, 0, 114, 48, 21);
        string[] categories = { "Alle", "Werkzeuge", "Bauen", "Materialien" };
        for (int i = 0; i < categories.Length; i++)
        {
            var category = (CraftingRecipe.RecipeCategory)i;
            var tab = Button("Category_" + category, layout, categories[i], 170 + i * 200, 264, 190, 44, () => SetCategory(category));
            tab.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 24;
            categoryTabs.Add(tab.GetComponent<Image>());
        }
        recipeContent = ScrollArea("Recipes", layout, 170, 323, 774, 508, 770);
        recipeScroll = recipeContent.parent.GetComponent<ScrollRect>();
        AddScrollbar(recipeScroll, layout, 955, 323, 18, 508);
        if (recipes != null) foreach (var recipe in recipes)
        {
            if (!recipe || !recipe.output) continue;
            var button = Button(recipe.name, recipeContent, "", 0, 0, 182, 160, () => SelectRecipe(recipe));
            var icon = Icon(button.transform, recipe.output.icon, 20, 12, 134, 106);
            ApplyRecipeIconLayout(recipe, icon.rectTransform);
            var name = Label(button.transform, recipe.output.displayName, 8, 123, 166, 31, 22, TextAlignmentOptions.Center);
            name.fontSizeMin = 15;
            var favorite = Button("Favorite", button.transform, "", 139, 3, 40, 40, () => ToggleFavorite(recipe));
            favorite.GetComponent<Image>().color = Color.clear;
            var star = Glyph(favorite.transform, WorkbenchGlyph.Shape.Star, 5, 5, 30);
            rows.Add(new RecipeRow { recipe = recipe, rect = (RectTransform)button.transform, icon = icon.rectTransform,
                image = button.GetComponent<Image>(), star = star, favorite = IsFavorite(recipe) });
        }
        emptyText = Label(layout, "Keine Rezepte", 170, 440, 774, 60, 28, TextAlignmentOptions.Center);
        details = Rect("Details", layout, 0, 0, 1640, 960);
        var preview = Panel("OutputFrame", details, 1026, 210, 195, 187, rowSprite);
        outputIcon = Icon(preview.transform, null, 12, 10, 171, 167);
        outputName = Label(details, "", 1236, 225, 284, 68, 35);
        outputName.textWrappingMode = TextWrappingModes.Normal;
        resultText = Label(details, "", 1236, 302, 284, 62, 24);
        resultText.textWrappingMode = TextWrappingModes.Normal;
        resultText.color = Muted;
        var line = Panel("Separator", details, 1026, 410, 494, 2, null);
        line.color = new Color32(142, 99, 57, 255);
        Label(details, "Materialien", 1026, 422, 230, 40, 31);
        Label(details, "Vorhanden / Benötigt", 1260, 422, 256, 40, 22, TextAlignmentOptions.Right);
        ingredientContent = ScrollArea("Ingredients", details, 1026, 469, 494, 215, 490);
        quantityControls = Rect("QuantityControls", details, 0, 0, 1640, 960);
        Label(quantityControls, "Menge", 1026, 701, 106, 52, 27);
        minus = Button("Minus", quantityControls, "−", 1140, 700, 48, 52, () => SetQuantity(Quantity - 1));
        Panel("QuantityField", quantityControls, 1195, 700, 100, 52, rowSprite);
        quantityText = Label(quantityControls, "1", 1197, 700, 96, 52, 30, TextAlignmentOptions.Center);
        plus = Button("Plus", quantityControls, "+", 1302, 700, 48, 52, () => SetQuantity(Quantity + (Quantity < int.MaxValue ? 1 : 0)));
        maximum = Button("Max", quantityControls, "Max", 1370, 700, 146, 52,
            () => SetQuantity(CraftingService.GetMaxCraftable(SelectedRecipe, inventory)));
        craft = Button("Craft", details, "Herstellen", 1026, 775, 494, 65, () => CraftSelected());
        craft.GetComponent<Image>().sprite = actionSprite;
        craftText = craft.GetComponentInChildren<TextMeshProUGUI>();
        SelectedRecipe = recipes != null && recipes.Length > 0 ? recipes[Mathf.Clamp(initialRecipe, 0, recipes.Length - 1)] : null;
        FitLayout();
        Refresh();
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
        var panel = Panel("Search", layout, 410, 204, 314, 48, rowSprite);
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
        track.raycastTarget = true;
        var area = Rect("Sliding Area", track.transform, 2, 2, width - 4, height - 4);
        var handle = Panel("Handle", area, 0, 0, width - 4, height - 4, selectedRowSprite);
        handle.raycastTarget = true;
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
        text.text = caption; text.color = Cream; text.fontStyle = FontStyles.Bold;
        text.fontSize = size; text.enableAutoSizing = true; text.fontSizeMin = size * .65f; text.fontSizeMax = size;
        text.alignment = alignment; text.textWrappingMode = TextWrappingModes.NoWrap;
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
