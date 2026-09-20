using System.Collections.Generic;
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

    public bool IsOpen { get; private set; }
    public CraftingRecipe SelectedRecipe { get; private set; }
    public int Quantity { get; private set; } = 1;
    public bool OnlyCraftable { get; private set; }

    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Muted = new Color32(224, 207, 187, 255);
    static readonly Color Enough = new Color32(150, 224, 109, 255);
    static readonly Color Missing = new Color32(232, 134, 101, 255);
    readonly List<RecipeRow> rows = new();
    readonly List<IngredientRow> ingredientRows = new();
    CanvasGroup group;
    RectTransform layout, recipeContent, ingredientContent, details;
    Image allTab, craftableTab, outputIcon;
    TextMeshProUGUI outputName, resultText, quantityText, craftText, emptyText;
    Button minus, plus, maximum, craft;
    InventoryManager inventory;
    CraftingRecipe renderedRecipe;

    sealed class RecipeRow { public CraftingRecipe recipe; public RectTransform rect; public Image image; }
    sealed class IngredientRow { public ItemSO item; public int amount; public TextMeshProUGUI count; }

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        BuildView();
        group.alpha = 0;
        group.blocksRaycasts = group.interactable = false;
    }

    void Update()
    {
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Escape)) ShowPanel(false);
            else SubscribeInventory();
        }
        else if (Input.GetKeyDown(KeyCode.B) && !GameplayInputBlocker.IsBlocked)
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
        layout.localScale = new Vector3(size.x / 1640f, size.y / 960f, 1);
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

    public void SetFilter(bool onlyCraftable)
    {
        OnlyCraftable = onlyCraftable;
        Refresh();
    }

    public void SetQuantity(int quantity)
    {
        Quantity = Mathf.Clamp(quantity, 1, Mathf.Max(1, CraftingService.GetMaxCraftable(SelectedRecipe, inventory)));
        Refresh();
    }

    public bool CraftSelected()
    {
        bool success = CraftingService.TryCraft(SelectedRecipe, inventory, Quantity);
        if (success) AudioManager.Instance?.Play(SoundType.UI_Click);
        Refresh();
        return success;
    }

    void Refresh()
    {
        if (!layout) return;
        allTab.sprite = OnlyCraftable ? rowSprite : actionSprite;
        craftableTab.sprite = OnlyCraftable ? actionSprite : rowSprite;
        int visible = 0;
        CraftingRecipe first = null;
        bool selectionVisible = false;
        foreach (var row in rows)
        {
            bool show = !OnlyCraftable || CraftingService.GetMaxCraftable(row.recipe, inventory) > 0;
            row.rect.gameObject.SetActive(show);
            if (!show) continue;
            if (!first) first = row.recipe;
            if (row.recipe == SelectedRecipe) selectionVisible = true;
            row.rect.anchoredPosition = new Vector2(0, -visible * 84);
            visible++;
        }
        recipeContent.sizeDelta = new Vector2(470, Mathf.Max(500, visible * 84 - 8));
        emptyText.gameObject.SetActive(visible == 0);
        if (!selectionVisible) { SelectedRecipe = first; Quantity = 1; }
        foreach (var row in rows) row.image.sprite = row.recipe == SelectedRecipe ? selectedRowSprite : rowSprite;
        details.gameObject.SetActive(SelectedRecipe);
        if (!SelectedRecipe) return;

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
                    var row = Panel("Ingredient_" + cost.Key.name, ingredientContent, 0, i * 63, 620, 57, rowSprite);
                    Icon(row.transform, cost.Key.icon, 12, 5, 72, 47);
                    Label(row.transform, cost.Key.displayName, 100, 0, 235, 57, 26);
                    var count = Label(row.transform, "", 343, 0, 254, 57, 27, TextAlignmentOptions.Right);
                    ingredientRows.Add(new IngredientRow { item = cost.Key, amount = cost.Value, count = count });
                    i++;
                }
                ingredientContent.sizeDelta = new Vector2(620, Mathf.Max(185, i * 63 - 6));
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
        craft.interactable = max >= Quantity;
    }

    void BuildView()
    {
        layout = Rect("Layout", transform, 0, 0, 1640, 960);
        Label(layout, "Werkbank", 548, 85, 535, 82, 57, TextAlignmentOptions.Center);
        Label(layout, "Rezepte", 251, 198, 465, 58, 40);
        allTab = Button("All", layout, "Alle", 252, 254, 225, 48, () => SetFilter(false)).GetComponent<Image>();
        craftableTab = Button("Craftable", layout, "Herstellbar", 491, 254, 232, 48, () => SetFilter(true)).GetComponent<Image>();
        recipeContent = ScrollArea("Recipes", layout, 252, 315, 480, 500, 470);
        if (recipes != null) foreach (var recipe in recipes)
        {
            if (!recipe || !recipe.output) continue;
            var button = Button(recipe.name, recipeContent, "", 0, rows.Count * 84, 470, 76, () => SelectRecipe(recipe));
            Icon(button.transform, recipe.output.icon, 7, 4, 88, 68);
            Label(button.transform, recipe.output.displayName, 120, 0, 340, 76, 28);
            rows.Add(new RecipeRow { recipe = recipe, rect = (RectTransform)button.transform, image = button.GetComponent<Image>() });
        }
        emptyText = Label(layout, "Keine Rezepte", 253, 380, 470, 60, 28, TextAlignmentOptions.Center);
        details = Rect("Details", layout, 0, 0, 1640, 960);
        var preview = Panel("OutputFrame", details, 782, 213, 210, 180, rowSprite);
        outputIcon = Icon(preview.transform, null, 16, 9, 178, 162);
        outputName = Label(details, "", 1020, 222, 385, 64, 40);
        resultText = Label(details, "", 1020, 287, 385, 56, 28);
        resultText.color = Muted;
        var line = Panel("Separator", details, 782, 403, 620, 2, null);
        line.color = new Color32(142, 99, 57, 255);
        Label(details, "Materialien", 782, 413, 350, 41, 33);
        Label(details, "Vorhanden / Benötigt", 1140, 413, 261, 41, 23, TextAlignmentOptions.Right);
        ingredientContent = ScrollArea("Ingredients", details, 782, 456, 630, 185, 620);
        Label(details, "Menge", 782, 659, 155, 52, 28);
        minus = Button("Minus", details, "−", 951, 660, 57, 50, () => SetQuantity(Quantity - 1));
        Panel("QuantityField", details, 1013, 660, 115, 50, rowSprite);
        quantityText = Label(details, "1", 1016, 660, 109, 50, 30, TextAlignmentOptions.Center);
        plus = Button("Plus", details, "+", 1135, 660, 56, 50, () => SetQuantity(Quantity + (Quantity < int.MaxValue ? 1 : 0)));
        maximum = Button("Max", details, "Max", 1220, 660, 149, 50,
            () => SetQuantity(CraftingService.GetMaxCraftable(SelectedRecipe, inventory)));
        craft = Button("Craft", details, "Herstellen", 782, 730, 621, 71, () => CraftSelected());
        craft.GetComponent<Image>().sprite = actionSprite;
        craftText = craft.GetComponentInChildren<TextMeshProUGUI>();
        SelectedRecipe = recipes != null && recipes.Length > 0 ? recipes[Mathf.Clamp(initialRecipe, 0, recipes.Length - 1)] : null;
        FitLayout();
        Refresh();
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
