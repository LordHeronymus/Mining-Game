using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runtime debug controls are assembled from the existing canvas styles.
public sealed class GameplayDebugWindow : MonoBehaviour
{
    enum DebugTab { Gameplay, Tests, Misc, StartingResources, Items, World, Audio, Icons, Recipes }

    RectTransform window, bounds, content, tooltip;
    TextMeshProUGUI tooltipLabel;
    ScrollRect scroll;
    readonly Dictionary<string, RectTransform> items = new Dictionary<string, RectTransform>();
    Vector2 lastSize, lastBounds, startPointer, startPosition, startSize;
    Vector2 resizeDirection;
    readonly List<string> gameplayItems = new List<string>();
    readonly List<string> testItems = new List<string>();
    readonly List<string> miscItems = new List<string>();
    readonly List<string> giftingItems = new List<string>();
    readonly List<string> worldItems = new List<string>();
    readonly List<string> audioItems = new List<string>();
    readonly List<string> iconItems = new List<string>();
    readonly List<string> recipeItems = new List<string>();
    readonly List<string> startingResourceItems = new List<string>();
    readonly List<string> layerStoneAudioItems = new List<string>();
    readonly List<LayerStoneAudioControl> layerStoneAudioControls = new List<LayerStoneAudioControl>();
    readonly Dictionary<int, string> layerStoneAudioLayerHeaders = new Dictionary<int, string>();
    readonly Dictionary<int, string> layerStoneAudioActionHeaders = new Dictionary<int, string>();
    bool layerStoneAudioControlsBuilt;
    readonly Dictionary<DebugTab, string> tabNames = new Dictionary<DebugTab, string>();
    TMP_InputField testMultiplier;
    TMP_InputField movementMultiplier;
    TMP_InputField healthInput;
    TMP_InputField miningHitOffsetInput;
    MinerPlayerVisual minerVisual;
    bool miningHitOffsetListenerBound;
    TMP_InputField giftAmount;
    ItemSO[] giftItems = System.Array.Empty<ItemSO>();
    readonly List<(string header, List<string> cards)> giftSections = new List<(string, List<string>)>();
    readonly Dictionary<ItemSO, Button> giftCards = new Dictionary<ItemSO, Button>();
    readonly Image[] giftPresetBackgrounds = new Image[4];
    static readonly int[] GiftPresets = { 1, 10, 64, 999 };
    static string lastGiftAmount = "10";
    TextMeshProUGUI testStatus;
    readonly Dictionary<GameplayTestMode, Toggle> modeToggles = new Dictionary<GameplayTestMode, Toggle>();
    [SerializeField] RectTransform[] dayNightButtons = new RectTransform[3];
    [SerializeField] Image[] dayNightBackgrounds = new Image[3];
    static readonly (string label, AudioVolumeSetting setting)[] AudioSettings =
    {
        ("Gesamt-Ambience (%)", AudioVolumeSetting.Ambience),
        ("Oberfläche (%)", AudioVolumeSetting.Surface),
        ("Untergrund (%)", AudioVolumeSetting.Underground),
        ("Höhle (%)", AudioVolumeSetting.Cave),
        ("Abbausounds (%)", AudioVolumeSetting.DigSounds),
    };
    static readonly (string label, AudioVolumeSetting setting, AudioTimeOffsetSetting offsetSetting, string tooltip)[] ConcreteAudioSettings =
    {
        ("Bling (%)", AudioVolumeSetting.DingLight, AudioTimeOffsetSetting.DingLight,
            "Wird abgespielt, wenn ein Item eingesammelt wird. Ein positiver Versatz verzögert den Klang; ein negativer überspringt den Clipanfang."),
    };
    [SerializeField] TMP_InputField[] audioInputs = new TMP_InputField[AudioSettings.Length];
    [SerializeField] TMP_InputField[] concreteAudioInputs = new TMP_InputField[ConcreteAudioSettings.Length];
    [SerializeField] TMP_InputField[] concreteAudioOffsetInputs = new TMP_InputField[ConcreteAudioSettings.Length];
    MapGenerator layerStoneAudioMap;
    FirstLayerAmbience detailAmbience;
    TMP_Dropdown detailClipDropdown;
    TMP_InputField detailVolumeInput;
    Button detailPreviewButton;
    int detailClipCount;
    CraftingRecipe[] iconRecipes = System.Array.Empty<CraftingRecipe>();
    CraftingRecipe editingIconRecipe;
    TMP_Dropdown iconRecipeDropdown;
    readonly TMP_InputField[] iconInputs = new TMP_InputField[4];
    TMP_InputField hotbarVerticalOffsetInput;
    readonly Toggle[] iconFlips = new Toggle[2];
    RectTransform iconPreview;
    Image iconPreviewImage;
    TextMeshProUGUI iconPreviewName, iconStatus;
    CraftingRecipe[] editableRecipes = System.Array.Empty<CraftingRecipe>();
    ItemSO[] recipeIngredientChoices = System.Array.Empty<ItemSO>();
    CraftingRecipe editingRecipe;
    TMP_Dropdown recipeDropdown, recipeCategoryDropdown;
    TMP_InputField recipeOutputAmount;
    RectTransform recipeIngredientRows;
    TextMeshProUGUI recipeEditorStatus;
    Button recipeAddIngredient;
    readonly List<RecipeIngredientEditorRow> recipeRows = new List<RecipeIngredientEditorRow>();
    TMP_InputField startingMoneyInput;
    RectTransform startingResourceRows;
    Button startingResourceAdd;
    TextMeshProUGUI startingResourceStatus;
    readonly List<StartingResourceEditorRow> startingResourceRowsData = new List<StartingResourceEditorRow>();
    int nextStartingResourceRowId;
    DebugTab currentTab;
    public bool IsTestTab => currentTab == DebugTab.Tests;
    public bool IsMiscTab => currentTab == DebugTab.Misc;
    public bool IsIconTab => currentTab == DebugTab.Icons;
    public bool IsRecipeTab => currentTab == DebugTab.Recipes;
    public bool IsStartingResourcesTab => currentTab == DebugTab.StartingResources;

    sealed class RecipeIngredientEditorRow
    {
        public RectTransform rect;
        public TMP_Dropdown item;
        public TextMeshProUGUI amountLabel;
        public TMP_InputField amount;
        public Button remove;
    }

    sealed class StartingResourceEditorRow
    {
        public RectTransform rect;
        public TMP_Dropdown item;
        public TextMeshProUGUI amountLabel;
        public TMP_InputField amount;
        public Button remove;
    }

    sealed class LayerStoneAudioControl
    {
        public int layerIndex;
        public bool breaking;
        public SoundType soundType;
        public int clipIndex;
        public string labelName;
        public TMP_InputField volume, pitch, pitchSpread;
    }

    void Awake()
    {
        window = (RectTransform)transform.Find("Card");
        bounds = (RectTransform)transform;
        var backdrop = GetComponent<Image>();
        if (backdrop) { backdrop.color = new Color(0f, 0f, 0f, .72f); backdrop.raycastTarget = true; }
        window.anchorMin = Vector2.zero; window.anchorMax = Vector2.one;
        window.offsetMin = window.offsetMax = Vector2.zero;
        window.pivot = new Vector2(.5f, .5f);
        if (window.TryGetComponent<Image>(out var windowImage)) windowImage.color = Color.clear;
        foreach (RectTransform child in window) items[child.name] = child;

        var viewport = MakeRect("WindowViewport", window);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(310, 34); viewport.offsetMax = new Vector2(-38, -104);
        viewport.gameObject.AddComponent<RectMask2D>();
        var background = viewport.gameObject.AddComponent<Image>(); background.color = Color.clear;
        content = MakeRect("WindowContent", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        foreach (var entry in items)
            if (entry.Key != "Title" && entry.Key != "Close" && entry.Key != "Accent")
                entry.Value.SetParent(content, false);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35; scroll.inertia = false;
        var track = MakeRect("WindowScrollbar", window);
        track.anchorMin = new Vector2(1,0); track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1,.5f);
        track.offsetMin = new Vector2(-24,38); track.offsetMax = new Vector2(-16,-108);
        var trackImage = track.gameObject.AddComponent<Image>(); trackImage.color = new Color(.12f,.16f,.21f);
        var thumb = MakeRect("Thumb", track);
        thumb.anchorMin = Vector2.zero; thumb.anchorMax = Vector2.one; thumb.sizeDelta = Vector2.zero;
        var thumbImage = thumb.gameObject.AddComponent<Image>(); thumbImage.color = new Color(.45f,.52f,.6f);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = thumb; bar.targetGraphic = thumbImage; bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        items["Title"].GetComponent<TextMeshProUGUI>().raycastTarget = false;
        items["Title"].GetComponent<TextMeshProUGUI>().text = "DEBUG PANEL";
        items["Close"].SetAsLastSibling();
        var sidebar = MakeRect("SidebarBackground", window);
        sidebar.anchorMin = Vector2.zero; sidebar.anchorMax = new Vector2(0, 1);
        sidebar.pivot = new Vector2(0, .5f); sidebar.sizeDelta = new Vector2(282, 0);
        sidebar.gameObject.AddComponent<Image>().color = new Color(.035f, .055f, .08f, .82f);
        sidebar.SetAsFirstSibling();
        var divider = MakeRect("SidebarDivider", window);
        divider.anchorMin = new Vector2(0, 0); divider.anchorMax = new Vector2(0, 1);
        divider.pivot = new Vector2(0, .5f); divider.anchoredPosition = new Vector2(282, 0);
        divider.sizeDelta = new Vector2(2, 0);
        divider.gameObject.AddComponent<Image>().color = new Color(.35f, .41f, .5f, .55f);
        CreateTooltip();
        AttachTooltip("MakeDefaults", "Übernimmt beide Sektionen nach dem Play-Stopp dauerhaft in die Gameplay Settings.");
        CreateLightingInfo();
        CreateItemGifting();
        CreateStartingResourcesEditor();
        CreateIconEditor();
        CreateRecipeEditor();
        CreateAudioSettings();
        CreateLayer1DetailSettings();
        foreach (var entry in items) if (entry.Value.parent == content) gameplayItems.Add(entry.Key);
        CreateTabs();
        SetTab(false);
        Layout();
    }

    void CreateStartingResourcesEditor()
    {
        var catalogOrder = giftItems.Where(item => item)
            .OrderBy(item => item.category == ItemCategory.Ore ? 0 : 1)
            .ThenBy(item => item.displayName, System.StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        CloneItem("Section", "StartingResourcesSection", content, "SPIELSTART");
        CloneItem("SpeedLabel", "StartingMoneyLabel", content, "Startgeld");
        startingMoneyInput = CloneItem("DiggingSpeed", "StartingMoneyInput", content).GetComponent<TMP_InputField>();
        startingMoneyInput.onValueChanged = new TMP_InputField.OnChangeEvent();
        startingMoneyInput.onEndEdit = new TMP_InputField.SubmitEvent();
        startingMoneyInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        startingMoneyInput.characterLimit = 10;
        DisableInputChildRaycasts(startingMoneyInput);
        startingMoneyInput.onEndEdit.AddListener(_ => SaveStartingResources());
        startingResourceRows = MakeRect("StartingResourceRows", content);
        items[startingResourceRows.name] = startingResourceRows;
        startingResourceAdd = CloneItem("Defaults", "StartingResourceAdd", content, "Item hinzufügen").GetComponent<Button>();
        startingResourceAdd.onClick = new Button.ButtonClickedEvent();
        startingResourceAdd.onClick.AddListener(AddStartingResourceRow);
        startingResourceStatus = CloneItem("Status", "StartingResourceStatus", content, "").GetComponent<TextMeshProUGUI>();
        startingResourceItems.AddRange(new[] { "StartingResourcesSection", "StartingMoneyLabel", "StartingMoneyInput",
            "StartingResourceRows", "StartingResourceAdd", "StartingResourceStatus" });

        var data = StartingResourcesSettings.Load();
        startingMoneyInput.SetTextWithoutNotify(data.money.ToString(CultureInfo.InvariantCulture));
        foreach (var entry in data.items)
        {
            var item = System.Array.Find(catalogOrder, candidate => (int)candidate.item == entry.itemId);
            if (item) CreateStartingResourceRow(item, entry.amount, catalogOrder);
        }
        if (startingResourceRowsData.Count == 0) CreateStartingResourceRow(null, 1, catalogOrder);
    }

    void AddStartingResourceRow()
    {
        var catalogOrder = giftItems.Where(item => item)
            .OrderBy(item => item.category == ItemCategory.Ore ? 0 : 1)
            .ThenBy(item => item.displayName, System.StringComparer.CurrentCultureIgnoreCase).ToArray();
        CreateStartingResourceRow(null, 1, catalogOrder);
        Layout();
    }

    void CreateStartingResourceRow(ItemSO selected, int amount, ItemSO[] choices)
    {
        int index = nextStartingResourceRowId++;
        var rowRect = MakeRect("StartingResourceRow_" + index, startingResourceRows);
        items[rowRect.name] = rowRect;
        startingResourceItems.Add(rowRect.name);
        var row = new StartingResourceEditorRow { rect = rowRect };
        row.item = CreateStyledDropdown("StartingResourceDropdown_" + index, rowRect);
        row.item.ClearOptions();
        row.item.AddOptions(new List<string> { "Item auswählen" }.Concat(choices.Select(item => item.displayName)).ToList());
        row.item.SetValueWithoutNotify(selected ? System.Array.IndexOf(choices, selected) + 1 : 0);
        row.item.onValueChanged = new TMP_Dropdown.DropdownEvent();
        row.item.onValueChanged.AddListener(_ => SaveStartingResources());
        startingResourceItems.Add(row.item.name);

        row.amountLabel = CloneItem("SpeedLabel", "StartingResourceAmountLabel_" + index, rowRect, "Menge")
            .GetComponent<TextMeshProUGUI>();
        row.amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        row.amountLabel.fontSize = 22;
        row.amountLabel.enableWordWrapping = false;
        row.amountLabel.raycastTarget = false;
        startingResourceItems.Add(row.amountLabel.name);

        row.amount = CloneItem("DiggingSpeed", "StartingResourceAmount_" + index, rowRect).GetComponent<TMP_InputField>();
        row.amount.onValueChanged = new TMP_InputField.OnChangeEvent();
        row.amount.onEndEdit = new TMP_InputField.SubmitEvent();
        row.amount.contentType = TMP_InputField.ContentType.IntegerNumber;
        row.amount.characterLimit = 10;
        DisableInputChildRaycasts(row.amount);
        row.amount.targetGraphic.color = new Color(.2f, .25f, .32f);
        row.amount.SetTextWithoutNotify(Mathf.Max(1, amount).ToString(CultureInfo.InvariantCulture));
        row.amount.onEndEdit.AddListener(_ => SaveStartingResources());
        startingResourceItems.Add(row.amount.name);

        row.remove = CloneItem("Defaults", "StartingResourceRemove_" + index, rowRect, "−").GetComponent<Button>();
        row.remove.onClick = new Button.ButtonClickedEvent();
        row.remove.onClick.AddListener(() => RemoveStartingResourceRow(row));
        startingResourceItems.Add(row.remove.name);
        startingResourceRowsData.Add(row);
    }

    void RemoveStartingResourceRow(StartingResourceEditorRow row)
    {
        startingResourceRowsData.Remove(row);
        foreach (var name in new[] { row.rect.name, row.item.name, row.amountLabel.name, row.amount.name, row.remove.name })
        {
            startingResourceItems.Remove(name);
            items.Remove(name);
        }
        Destroy(row.rect.gameObject);
        SaveStartingResources();
        Layout();
    }

    public bool CommitStartingResources() => SaveStartingResources();

    bool SaveStartingResources()
    {
        if (!startingMoneyInput || !startingResourceStatus) return true;
        if (!int.TryParse(startingMoneyInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int money) || money < 0)
        {
            startingResourceStatus.text = "Startgeld muss 0 oder größer sein.";
            return false;
        }
        var choices = giftItems.Where(item => item)
            .OrderBy(item => item.category == ItemCategory.Ore ? 0 : 1)
            .ThenBy(item => item.displayName, System.StringComparer.CurrentCultureIgnoreCase).ToArray();
        var entries = new List<StartingItemAmount>();
        foreach (var row in startingResourceRowsData)
        {
            int choice = row.item.value - 1;
            if (choice < 0 || choice >= choices.Length) continue;
            if (!int.TryParse(row.amount.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                startingResourceStatus.text = "Itemmenge muss größer als 0 sein.";
                return false;
            }
            entries.Add(new StartingItemAmount((int)choices[choice].item, amount));
        }
        StartingResourcesSettings.Save(money, entries.ToArray());
        startingResourceStatus.text = "Gespeichert";
        return true;
    }

    RectTransform CloneItem(string source, string name, Transform parent, string label = null)
    {
        var rect = Instantiate(items[source], parent);
        rect.name = name; items[name] = rect;
        rect.gameObject.SetActive(true);
        if (label != null) rect.GetComponentInChildren<TextMeshProUGUI>().text = label;
        var trigger = rect.GetComponent<GameplayButtonTooltip>();
        if (trigger) Destroy(trigger);
        return rect;
    }

    void CreateItemGifting()
    {
        CloneItem("Section", "ItemsSection", content, "MENGE");
        CloneItem("SpeedLabel", "GiftAmountLabel", content, "Menge");
        giftAmount = CloneItem("DiggingSpeed", "GiftAmount", content).GetComponent<TMP_InputField>();
        giftAmount.onEndEdit = new TMP_InputField.SubmitEvent();
        giftAmount.onValueChanged = new TMP_InputField.OnChangeEvent();
        giftAmount.contentType = TMP_InputField.ContentType.IntegerNumber;
        giftAmount.characterLimit = 10;
        DisableInputChildRaycasts(giftAmount);
        giftAmount.SetTextWithoutNotify(lastGiftAmount);
        giftAmount.onValueChanged.AddListener(value => { lastGiftAmount = value; RefreshGifting(); });
        for (int i = 0; i < GiftPresets.Length; i++)
        {
            int amount = GiftPresets[i];
            string name = "GiftPreset" + i;
            var rect = CloneItem("Defaults", name, content, amount.ToString(CultureInfo.InvariantCulture));
            var button = rect.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => giftAmount.text = amount.ToString(CultureInfo.InvariantCulture));
            giftPresetBackgrounds[i] = rect.GetComponent<Image>();
        }

        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (catalog) giftItems = System.Array.FindAll(catalog.items, item => item);
        var preferred = new[] { ItemCategory.Ore, ItemCategory.Misc, ItemCategory.Tool, ItemCategory.Consumable };
        foreach (var category in preferred)
            AddGiftSection(GiftCategoryLabel(category), category);
        foreach (ItemCategory category in System.Enum.GetValues(typeof(ItemCategory)))
            if (System.Array.IndexOf(preferred, category) < 0)
                AddGiftSection(GiftCategoryLabel(category), category);
        RefreshGifting();
    }

    static string GiftCategoryLabel(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.Ore: return "Erze";
            case ItemCategory.Misc: return "Materialien";
            case ItemCategory.Tool: return "Werkzeuge";
            case ItemCategory.Consumable: return "Verbrauchsgegenstände";
            case ItemCategory.Powerup: return "Powerups";
            default: return category.ToString();
        }
    }

    TMP_Dropdown CreateStyledDropdown(string name, Transform parent = null)
    {
        var dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObject.name = name;
        dropdownObject.transform.SetParent(parent ? parent : content, false);
        items[name] = (RectTransform)dropdownObject.transform;
        var dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        var font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        var arrow = dropdownObject.transform.Find("Arrow");
        arrow.GetComponent<Image>().enabled = false;
        var arrowLabel = MakeRect("Caption", arrow);
        arrowLabel.anchorMin = Vector2.zero; arrowLabel.anchorMax = Vector2.one;
        arrowLabel.offsetMin = arrowLabel.offsetMax = Vector2.zero;
        var arrowText = arrowLabel.gameObject.AddComponent<TextMeshProUGUI>();
        arrowText.text = "v";
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.raycastTarget = false;
        foreach (var label in dropdownObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.font = font;
            label.fontSize = label == arrowText ? 18 : 22;
            label.color = Color.white;
        }
        dropdownObject.GetComponent<Image>().color = new Color(.2f, .25f, .32f);
        dropdown.template.GetComponent<Image>().color = new Color(.2f, .25f, .32f);
        var optionToggle = dropdown.itemText.GetComponentInParent<Toggle>(true);
        optionToggle.targetGraphic.color = new Color(.3f, .37f, .45f);
        ((RectTransform)optionToggle.transform).sizeDelta = new Vector2(0, 36);
        var sourceCheck = optionToggle.graphic;
        if (sourceCheck)
        {
            var sourceRect = (RectTransform)sourceCheck.transform;
            var checkRect = MakeRect("SelectedCheckGlyph", sourceCheck.transform.parent);
            checkRect.anchorMin = sourceRect.anchorMin; checkRect.anchorMax = sourceRect.anchorMax;
            checkRect.pivot = sourceRect.pivot; checkRect.anchoredPosition = sourceRect.anchoredPosition;
            checkRect.sizeDelta = sourceRect.sizeDelta;
            checkRect.localRotation = sourceRect.localRotation; checkRect.localScale = sourceRect.localScale;
            checkRect.SetSiblingIndex(sourceRect.GetSiblingIndex() + 1);
            var check = checkRect.gameObject.AddComponent<WorkbenchGlyph>();
            check.shape = WorkbenchGlyph.Shape.Check; check.filled = true;
            check.color = new Color(1f, .65f, .2f); check.raycastTarget = false;
            sourceCheck.enabled = false;
            sourceCheck.raycastTarget = false;
            optionToggle.graphic = check;
            optionToggle.SetIsOnWithoutNotify(optionToggle.isOn);
        }
        return dropdown;
    }

    void AddGiftSection(string label, ItemCategory category)
    {
        var matching = System.Array.FindAll(giftItems, item => item.category == category);
        if (matching.Length == 0) return;
        string headerName = "GiftSection" + category;
        CloneItem("Section", headerName, content, label.ToUpperInvariant());
        var cards = new List<string>();
        foreach (var item in matching)
        {
            string name = "GiftCard" + (int)item.item;
            var rect = MakeRect(name, content);
            items[name] = rect;
            cards.Add(name);
            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(.12f, .17f, .23f, .92f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => GiveItem(item));
            giftCards[item] = button;

            var iconRect = MakeRect("Icon", rect);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, .5f);
            iconRect.pivot = new Vector2(0, .5f);
            iconRect.anchoredPosition = new Vector2(12, 0);
            iconRect.sizeDelta = new Vector2(52, 52);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = item.icon;
            icon.enabled = item.icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var labelRect = MakeRect("Label", rect);
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(72, 4);
            labelRect.offsetMax = new Vector2(-8, -4);
            var text = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
            text.fontSize = 23;
            text.color = Color.white;
            text.text = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
        }
        giftSections.Add((headerName, cards));
    }

    void CreateAudioSettings()
    {
        CloneItem("Section", "AudioSection", content, "SOUND");
        for (int i = 0; i < AudioSettings.Length; i++)
        {
            int index = i;
            CreateAudioSettingRow(AudioSettings[i].label, "AudioLabel" + i, "AudioInput" + i,
                input => ApplyAudioInput(index, input), out audioInputs[i]);
        }

        CloneItem("Section", "ConcreteAudioSection", content, "KONKRETE SOUNDCLIPS");
        for (int i = 0; i < ConcreteAudioSettings.Length; i++)
        {
            int index = i;
            CreateAudioSettingRow(ConcreteAudioSettings[i].label, "ConcreteAudioLabel" + i,
                "ConcreteAudioInput" + i, input => ApplyConcreteAudioInput(index, input),
                out concreteAudioInputs[i]);
            AttachTooltip("ConcreteAudioLabel" + i, ConcreteAudioSettings[i].tooltip);
            var label = items["ConcreteAudioLabel" + i].GetComponentInChildren<TextMeshProUGUI>();
            if (label) label.raycastTarget = true;
            CloneItem("SpeedLabel", "ConcreteAudioOffsetLabel" + i, content, "Versatz (Sek.)");
            var offsetInput = CloneItem("DiggingSpeed", "ConcreteAudioOffsetInput" + i, content)
                .GetComponent<TMP_InputField>();
            offsetInput.onEndEdit = new TMP_InputField.SubmitEvent();
            offsetInput.onValueChanged = new TMP_InputField.OnChangeEvent();
            offsetInput.contentType = TMP_InputField.ContentType.DecimalNumber;
            DisableInputChildRaycasts(offsetInput);
            int offsetIndex = i;
            offsetInput.onEndEdit.AddListener(_ => ApplyConcreteAudioOffset(offsetIndex));
            concreteAudioOffsetInputs[i] = offsetInput;
        }
        CreateLayerStoneAudioControls();
        RefreshAudioSettings();
    }

    void CreateLayerStoneAudioControls()
    {
        if (!items.ContainsKey("LayerStoneAudioSection"))
        {
            var backdrop = MakeRect("LayerStoneAudioBackdrop", content);
            items[backdrop.name] = backdrop;
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = new Color(.045f, .06f, .09f, .82f);
            backdropImage.raycastTarget = false;
            backdrop.SetAsFirstSibling();
            StyleLayerAudioLabel(CloneItem("Section", "LayerStoneAudioSection", content,
                "ABBAU · HIEB & BRUCH"), 25);
            StyleLayerAudioLabel(CloneItem("SpeedLabel", "LayerStoneAudioHeaderVolume", content,
                "Lautstärke %"), 23);
            StyleLayerAudioLabel(CloneItem("SpeedLabel", "LayerStoneAudioHeaderPitch", content,
                "Pitch %"), 23);
            StyleLayerAudioLabel(CloneItem("SpeedLabel", "LayerStoneAudioHeaderSpread", content,
                "Spread ± %"), 23);
        }
        if (layerStoneAudioControlsBuilt) return;

        layerStoneAudioMap = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var layers = layerStoneAudioMap ? layerStoneAudioMap.layers : null;
        var audio = AudioManager.Instance;
        if (layers == null || !audio) return;

        for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            var layer = layers[layerIndex];
            if (layer == null || !layer.stone) continue;

            SoundType hitType = layerIndex >= 2 ? SoundType.DigDeepStone : layer.stone.digSound;
            SoundType breakType = layerIndex >= 2 ? SoundType.StoneBreak : SoundType.ClayBreak;
            int firstRow = layerStoneAudioControls.Count;
            AddLayerStoneAudioRows(audio, layerIndex, false, hitType);
            AddLayerStoneAudioRows(audio, layerIndex, true, breakType);
            if (layerStoneAudioControls.Count == firstRow) continue;
            string headerName = "LayerStoneAudioLayer" + layerIndex;
            var header = CloneItem("Section", headerName, content, layer.name.ToUpperInvariant());
            StyleLayerAudioLabel(header, 25);
            layerStoneAudioLayerHeaders.Add(layerIndex, headerName);
            layerStoneAudioItems.Add(headerName);
        }
        layerStoneAudioControlsBuilt = true;
        foreach (string name in layerStoneAudioItems)
        {
            gameplayItems.Remove(name);
            if (!audioItems.Contains(name)) audioItems.Add(name);
            if (items.TryGetValue(name, out var rect)) rect.gameObject.SetActive(currentTab == DebugTab.Audio);
        }
    }

    void AddLayerStoneAudioRows(AudioManager audio, int layerIndex, bool breaking, SoundType soundType)
    {
        int count = audio.GetSoundClipCount(soundType);
        for (int clipIndex = 0; clipIndex < count; clipIndex++)
        {
            var clip = audio.GetSoundClip(soundType, clipIndex);
            if (!clip) continue;

            int actionKey = layerIndex * 2 + (breaking ? 1 : 0);
            if (!layerStoneAudioActionHeaders.ContainsKey(actionKey))
            {
                string actionHeaderName = "LayerStoneAudioAction" + actionKey;
                var actionHeader = CloneItem("SpeedLabel", actionHeaderName, content,
                    breaking ? "BRUCH" : "HIEB");
                StyleLayerAudioLabel(actionHeader, 21);
                var actionText = actionHeader.GetComponentInChildren<TextMeshProUGUI>();
                if (actionText) actionText.color = new Color(.86f, .7f, .42f);
                layerStoneAudioActionHeaders.Add(actionKey, actionHeaderName);
                layerStoneAudioItems.Add(actionHeaderName);
            }

            string rowId = layerStoneAudioControls.Count.ToString(CultureInfo.InvariantCulture);
            var control = new LayerStoneAudioControl
            {
                layerIndex = layerIndex,
                breaking = breaking,
                soundType = soundType,
                clipIndex = clipIndex,
                labelName = "LayerStoneAudioLabel" + rowId
            };
            StyleLayerAudioLabel(CloneItem("SpeedLabel", control.labelName, content,
                clip.name), 25);
            control.volume = CreateLayerStoneAudioInput("LayerStoneAudioVolume" + rowId,
                value => ApplyLayerStoneAudioValue(control, value, 0));
            control.pitch = CreateLayerStoneAudioInput("LayerStoneAudioPitch" + rowId,
                value => ApplyLayerStoneAudioValue(control, value, 1));
            control.pitchSpread = CreateLayerStoneAudioInput("LayerStoneAudioSpread" + rowId,
                value => ApplyLayerStoneAudioValue(control, value, 2));
            layerStoneAudioControls.Add(control);
            layerStoneAudioItems.Add(control.labelName);
            layerStoneAudioItems.Add(control.volume.name);
            layerStoneAudioItems.Add(control.pitch.name);
            layerStoneAudioItems.Add(control.pitchSpread.name);
        }
    }

    TMP_InputField CreateLayerStoneAudioInput(string name, System.Action<TMP_InputField> submit)
    {
        var input = CloneItem("DiggingSpeed", name, content).GetComponent<TMP_InputField>();
        input.onEndEdit = new TMP_InputField.SubmitEvent();
        input.onValueChanged = new TMP_InputField.OnChangeEvent();
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        input.textComponent.fontSize = 26;
        input.textComponent.alignment = TextAlignmentOptions.Center;
        if (input.targetGraphic is Image background)
            background.color = new Color(.12f, .16f, .22f, .95f);
        DisableInputChildRaycasts(input);
        input.onEndEdit.AddListener(_ => submit(input));
        return input;
    }

    static void StyleLayerAudioLabel(RectTransform rect, float fontSize)
    {
        var label = rect.GetComponentInChildren<TextMeshProUGUI>();
        if (!label) return;
        label.fontSize = fontSize;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    void ApplyLayerStoneAudioValue(LayerStoneAudioControl control, TMP_InputField input, int field)
    {
        if (!AudioManager.Instance || !float.TryParse(input.text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value))
        {
            items["Status"].GetComponent<TextMeshProUGUI>().text = "Audio: ungültiger Zahlenwert.";
            RefreshLayerStoneAudioSettings();
            return;
        }

        var tuning = AudioManager.Instance.GetLayerMiningClipTuning(control.layerIndex,
            control.breaking, control.soundType, control.clipIndex);
        float volume = tuning.volume;
        float pitch = tuning.pitch;
        float pitchSpread = tuning.pitchSpread;
        if (field == 0 && value >= 0f && value <= 100f) volume = value / 100f;
        else if (field == 1 && value >= 50f && value <= 200f) pitch = value / 100f;
        else if (field == 2 && value >= 0f && value <= 50f) pitchSpread = value / 100f;
        else
        {
            string range = field == 0 ? "0 bis 100" : field == 1 ? "50 bis 200" : "0 bis 50";
            items["Status"].GetComponent<TextMeshProUGUI>().text = $"Audio: bitte {range} eingeben.";
            RefreshLayerStoneAudioSettings();
            return;
        }

        AudioManager.Instance.SetLayerMiningClipTuning(control.layerIndex, control.breaking,
            control.soundType, control.clipIndex, volume, pitch, pitchSpread);
        items["Status"].GetComponent<TextMeshProUGUI>().text = "";
        RefreshLayerStoneAudioSettings();
    }

    void RefreshLayerStoneAudioSettings()
    {
        var audio = AudioManager.Instance;
        foreach (var control in layerStoneAudioControls)
        {
            if (!control.volume || !control.pitch || !control.pitchSpread) continue;
            control.volume.interactable = control.pitch.interactable = control.pitchSpread.interactable = audio;
            if (!audio) continue;
            var tuning = audio.GetLayerMiningClipTuning(control.layerIndex, control.breaking,
                control.soundType, control.clipIndex);
            control.volume.SetTextWithoutNotify((tuning.volume * 100f).ToString("0.##", CultureInfo.InvariantCulture));
            control.pitch.SetTextWithoutNotify((tuning.pitch * 100f).ToString("0.##", CultureInfo.InvariantCulture));
            control.pitchSpread.SetTextWithoutNotify((tuning.pitchSpread * 100f).ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    void CreateAudioSettingRow(string label, string labelName, string inputName,
        System.Action<TMP_InputField> submit, out TMP_InputField input)
    {
        CloneItem("SpeedLabel", labelName, content, label);
        var field = CloneItem("DiggingSpeed", inputName, content).GetComponent<TMP_InputField>();
        field.onEndEdit = new TMP_InputField.SubmitEvent();
        field.onValueChanged = new TMP_InputField.OnChangeEvent();
        field.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(field);
        field.onEndEdit.AddListener(_ => submit(field));
        input = field;
    }

    void ApplyAudioInput(int index, TMP_InputField input)
        => ApplyAudioInput(input, AudioSettings[index].setting);

    void ApplyConcreteAudioInput(int index, TMP_InputField input)
        => ApplyAudioInput(input, ConcreteAudioSettings[index].setting);

    void ApplyConcreteAudioOffset(int index)
    {
        var audio = AudioManager.Instance;
        var input = concreteAudioOffsetInputs[index];
        if (!audio || !float.TryParse(input.text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float seconds) || seconds < -10f || seconds > 10f)
        {
            items["Status"].GetComponent<TextMeshProUGUI>().text = "Zeitversatz: bitte einen Wert von -10 bis 10 Sekunden eingeben.";
            RefreshAudioSettings();
            return;
        }
        audio.SetTimeOffset(ConcreteAudioSettings[index].offsetSetting, seconds);
        items["Status"].GetComponent<TextMeshProUGUI>().text = "";
        RefreshAudioSettings();
#if UNITY_EDITOR
        QueueGpsDefault(audio, "dingLightOffsetSeconds");
#endif
    }

    void ApplyAudioInput(TMP_InputField input, AudioVolumeSetting setting)
    {
        var audio = AudioManager.Instance;
        if (!audio || !float.TryParse(input.text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float percent) || percent < 0f || percent > 100f)
        {
            items["Status"].GetComponent<TextMeshProUGUI>().text = "Soundlautstärke: bitte 0 bis 100 eingeben.";
            RefreshAudioSettings();
            return;
        }
        audio.SetVolume(setting, percent / 100f);
        items["Status"].GetComponent<TextMeshProUGUI>().text = "";
        RefreshAudioSettings();
#if UNITY_EDITOR
        QueueGpsDefault(audio, AudioVolumeProperty(setting));
#endif
    }

    static string AudioVolumeProperty(AudioVolumeSetting setting) => setting switch
    {
        AudioVolumeSetting.Ambience => "ambienceVolume",
        AudioVolumeSetting.Surface => "surfaceVolume",
        AudioVolumeSetting.Rain => "rainVolume",
        AudioVolumeSetting.Thunderstorm => "thunderstormVolume",
        AudioVolumeSetting.Underground => "undergroundVolume",
        AudioVolumeSetting.Cave => "caveVolume",
        AudioVolumeSetting.DigSounds => "digSoundVolume",
        AudioVolumeSetting.DingLight => "dingLightVolume",
        _ => null
    };

    void QueueGpsDefault(Component component, string propertyPath)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying || string.IsNullOrEmpty(propertyPath)) return;
        if (!GameplayDebugDefaults.QueueComponentValue(component, propertyPath, out string error))
        {
            string message = "GPS-Synchronisierung fehlgeschlagen: " + error;
            if (items.TryGetValue("Status", out var status) && status)
                status.GetComponent<TextMeshProUGUI>().text = message;
            Debug.LogWarning(message, component);
        }
#endif
    }

    void RefreshAudioSettings()
    {
        var audio = AudioManager.Instance;
        if (audioInputs == null) return;
        for (int i = 0; i < audioInputs.Length; i++)
        {
            if (!audioInputs[i]) continue;
            audioInputs[i].interactable = audio;
            if (audio) audioInputs[i].SetTextWithoutNotify((audio.GetVolume(AudioSettings[i].setting) * 100f)
                .ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (concreteAudioInputs == null || concreteAudioOffsetInputs == null) return;
        for (int i = 0; i < concreteAudioInputs.Length; i++)
        {
            if (!concreteAudioInputs[i] || i >= concreteAudioOffsetInputs.Length || !concreteAudioOffsetInputs[i]) continue;
            concreteAudioInputs[i].interactable = audio;
            concreteAudioOffsetInputs[i].interactable = audio;
            if (audio) concreteAudioInputs[i].SetTextWithoutNotify(
                (audio.GetVolume(ConcreteAudioSettings[i].setting) * 100f).ToString("0.##", CultureInfo.InvariantCulture));
            if (audio) concreteAudioOffsetInputs[i].SetTextWithoutNotify(
                audio.GetTimeOffset(ConcreteAudioSettings[i].offsetSetting).ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    public void RefreshSoundSettings()
    {
        RefreshAudioSettings();
        RefreshDetailSettings();
    }

    void CreateLayer1DetailSettings()
    {
        CloneItem("Section", "DetailSection", content, "LAYER 1 DETAILS");
        CloneItem("SpeedLabel", "DetailClipLabel", content, "Detailclip");
        detailClipDropdown = CreateStyledDropdown("DetailClipDropdown");
        detailClipDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        detailClipDropdown.onValueChanged.AddListener(_ => RefreshDetailSettings());
        CloneItem("SpeedLabel", "DetailVolumeLabel", content, "Lautstärke (%)");
        detailVolumeInput = CloneItem("DiggingSpeed", "DetailVolumeInput", content).GetComponent<TMP_InputField>();
        detailVolumeInput.onEndEdit = new TMP_InputField.SubmitEvent();
        detailVolumeInput.onValueChanged = new TMP_InputField.OnChangeEvent();
        detailVolumeInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(detailVolumeInput);
        detailVolumeInput.onEndEdit.AddListener(_ => ApplyDetailVolume());
        detailPreviewButton = CloneItem("Defaults", "DetailPreview", content, "Testen").GetComponent<Button>();
        detailPreviewButton.onClick = new Button.ButtonClickedEvent();
        detailPreviewButton.onClick.AddListener(PreviewDetailClip);
        RefreshDetailSettings();
    }

    void RefreshDetailSettings()
    {
        if (!detailClipDropdown || !detailVolumeInput || !detailPreviewButton) return;
        var ambience = UnityEngine.Object.FindFirstObjectByType<FirstLayerAmbience>();
        int count = ambience ? ambience.DetailClipCount : 0;
        if (ambience != detailAmbience || count != detailClipCount)
        {
            int selected = detailClipDropdown ? detailClipDropdown.value : 0;
            detailAmbience = ambience;
            detailClipCount = count;
            detailClipDropdown.ClearOptions();
            if (ambience)
            {
                var options = new List<TMP_Dropdown.OptionData>();
                for (int i = 0; i < count; i++)
                {
                    var clip = ambience.GetDetailClip(i);
                    options.Add(new TMP_Dropdown.OptionData(clip ? clip.name : "Fehlender Clip"));
                }
                detailClipDropdown.AddOptions(options);
                detailClipDropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, Mathf.Max(0, count - 1)));
            }
        }

        bool available = detailAmbience && detailClipCount > 0;
        detailClipDropdown.interactable = available;
        detailVolumeInput.interactable = available;
        detailPreviewButton.interactable = available;
        if (available) detailVolumeInput.SetTextWithoutNotify((detailAmbience
            .GetDetailVolumeMultiplier(detailClipDropdown.value) * 100f).ToString("0.##", CultureInfo.InvariantCulture));
    }

    bool ApplyDetailVolume()
    {
        if (!detailAmbience || !float.TryParse(detailVolumeInput.text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float percent) || percent < 0f || percent > 100f)
        {
            items["Status"].GetComponent<TextMeshProUGUI>().text = "Detail-Lautstärke: bitte 0 bis 100 eingeben.";
            RefreshDetailSettings();
            return false;
        }
        detailAmbience.SetDetailVolumeMultiplier(detailClipDropdown.value, percent / 100f);
        items["Status"].GetComponent<TextMeshProUGUI>().text = "";
        RefreshDetailSettings();
#if UNITY_EDITOR
        QueueGpsDefault(detailAmbience, "detailVolumeMultipliers");
#endif
        return true;
    }

    void PreviewDetailClip()
    {
        if (ApplyDetailVolume()) detailAmbience.PlayDetailPreview(detailClipDropdown.value);
    }

    bool CanGiveItem(ItemSO item, out int amount)
    {
        amount = 0;
        var inventory = InventoryManager.Instance;
        return inventory && item &&
            int.TryParse(giftAmount.text, out amount) && amount > 0 &&
            inventory.GetCount(item) <= int.MaxValue - amount;
    }

    void GiveItem(ItemSO item)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!CanGiveItem(item, out int amount)) return;
        InventoryManager.Instance.Add(item, amount);
        string itemName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
        Debug.Log($"[Debug] {amount} × {itemName} zum Inventar hinzugefügt.", this);
        RefreshGifting();
#endif
    }

    void RefreshGifting()
    {
        giftAmount.interactable = InventoryManager.Instance;
        foreach (var entry in giftCards)
            entry.Value.interactable = CanGiveItem(entry.Key, out _);
        bool valid = int.TryParse(giftAmount.text, out int current) && current > 0;
        for (int i = 0; i < giftPresetBackgrounds.Length; i++)
            giftPresetBackgrounds[i].color = valid && current == GiftPresets[i]
                ? new Color(.64f, .38f, .1f) : new Color(.12f, .16f, .22f);
    }

    void CreateIconEditor()
    {
        CloneItem("Section", "IconSection", content, "REZEPT-ICONS");
        iconItems.Add("IconSection");
        var workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (workbench && workbench.recipes != null)
            iconRecipes = workbench.recipes.Where(recipe => recipe && recipe.output && recipe.TryGetCosts(out _))
                .OrderBy(recipe => recipe.Category == CraftingRecipe.RecipeCategory.Building ? 0 :
                    recipe.Category == CraftingRecipe.RecipeCategory.Materials ? 1 : 2)
                .ThenBy(recipe => recipe.output.displayName, System.StringComparer.CurrentCultureIgnoreCase).ToArray();
        iconRecipeDropdown = CreateStyledDropdown("IconRecipeDropdown");
        iconItems.Add("IconRecipeDropdown");
        iconRecipeDropdown.ClearOptions();
        iconRecipeDropdown.AddOptions(iconRecipes.Select(recipe => new TMP_Dropdown.OptionData(
            IconCategoryName(recipe.Category) + " · " + recipe.output.displayName)).ToList());
        iconRecipeDropdown.interactable = iconRecipes.Length > 0;
        iconRecipeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        iconRecipeDropdown.onValueChanged.AddListener(_ => RefreshIconEditor());

        iconPreview = MakeRect("IconPreviewCard", content);
        items[iconPreview.name] = iconPreview;
        iconItems.Add(iconPreview.name);
        iconPreview.gameObject.AddComponent<Image>().color = new Color(.18f, .11f, .07f, 1f);
        var imageRect = MakeRect("Icon", iconPreview);
        imageRect.anchorMin = imageRect.anchorMax = new Vector2(0, 1);
        imageRect.pivot = new Vector2(0, 1);
        imageRect.anchoredPosition = new Vector2(20, -12);
        imageRect.sizeDelta = new Vector2(134, 106);
        iconPreviewImage = imageRect.gameObject.AddComponent<Image>();
        iconPreviewImage.preserveAspect = true;
        iconPreviewImage.raycastTarget = false;
        var caption = MakeRect("Name", iconPreview);
        caption.anchorMin = caption.anchorMax = new Vector2(0, 1);
        caption.pivot = new Vector2(0, 1);
        caption.anchoredPosition = new Vector2(8, -123);
        caption.sizeDelta = new Vector2(166, 31);
        iconPreviewName = caption.gameObject.AddComponent<TextMeshProUGUI>();
        iconPreviewName.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        iconPreviewName.fontSize = 20;
        iconPreviewName.alignment = TextAlignmentOptions.Center;
        iconPreviewName.raycastTarget = false;

        string[] labels = { "Skalierung X", "Skalierung Y", "X-Versatz", "Y-Versatz" };
        for (int i = 0; i < iconInputs.Length; i++)
        {
            string label = "IconLabel" + i;
            string input = "IconInput" + i;
            CloneItem("SpeedLabel", label, content, labels[i]);
            iconInputs[i] = CloneItem("DiggingSpeed", input, content).GetComponent<TMP_InputField>();
            iconInputs[i].onValueChanged = new TMP_InputField.OnChangeEvent();
            iconInputs[i].onEndEdit = new TMP_InputField.SubmitEvent();
            iconInputs[i].contentType = TMP_InputField.ContentType.DecimalNumber;
            DisableInputChildRaycasts(iconInputs[i]);
            iconInputs[i].onValueChanged.AddListener(_ => ApplyIconInputs(false));
            iconInputs[i].onEndEdit.AddListener(_ => CommitIconInputs());
            iconItems.Add(label);
            iconItems.Add(input);
        }
        CreateIconFlip("IconFlipX", "X spiegeln", 0);
        CreateIconFlip("IconFlipY", "Y spiegeln", 1);
        CloneItem("SpeedLabel", "HotbarVerticalOffsetLabel", content, "Vertikaler Versatz für Hotbar");
        hotbarVerticalOffsetInput = CloneItem("DiggingSpeed", "HotbarVerticalOffsetInput", content).GetComponent<TMP_InputField>();
        hotbarVerticalOffsetInput.onValueChanged = new TMP_InputField.OnChangeEvent();
        hotbarVerticalOffsetInput.onEndEdit = new TMP_InputField.SubmitEvent();
        hotbarVerticalOffsetInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(hotbarVerticalOffsetInput);
        hotbarVerticalOffsetInput.onValueChanged.AddListener(_ => ApplyHotbarVerticalOffset(false));
        hotbarVerticalOffsetInput.onEndEdit.AddListener(_ => ApplyHotbarVerticalOffset(true));
        iconItems.Add("HotbarVerticalOffsetLabel");
        iconItems.Add("HotbarVerticalOffsetInput");
        var reset = CloneItem("Defaults", "IconReset", content, "Zurücksetzen").GetComponent<Button>();
        reset.onClick = new Button.ButtonClickedEvent();
        reset.onClick.AddListener(ResetIconLayout);
        iconItems.Add("IconReset");
        iconStatus = CloneItem("Status", "IconStatus", content, "").GetComponent<TextMeshProUGUI>();
        iconItems.Add("IconStatus");
        RefreshIconEditor();
    }

    static string IconCategoryName(CraftingRecipe.RecipeCategory category) => category switch
    {
        CraftingRecipe.RecipeCategory.Building => "Bauen",
        CraftingRecipe.RecipeCategory.Materials => "Materialien",
        CraftingRecipe.RecipeCategory.Tools => "Werkzeuge",
        _ => "Sonstiges"
    };

    void CreateIconFlip(string name, string label, int index)
    {
        var row = MakeRect(name, content);
        items[name] = row;
        iconItems.Add(name);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var box = MakeRect("Box", row);
        box.anchorMin = box.anchorMax = new Vector2(0, .5f);
        box.anchoredPosition = new Vector2(17, 0);
        box.sizeDelta = new Vector2(30, 30);
        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.color = new Color(.3f, .37f, .45f);
        var check = MakeRect("Check", box);
        check.anchorMin = Vector2.zero;
        check.anchorMax = Vector2.one;
        check.offsetMin = new Vector2(6, 6);
        check.offsetMax = new Vector2(-6, -6);
        var checkImage = check.gameObject.AddComponent<Image>();
        checkImage.color = new Color(1f, .65f, .2f);
        var caption = MakeRect("Label", row);
        caption.anchorMin = Vector2.zero;
        caption.anchorMax = Vector2.one;
        caption.offsetMin = new Vector2(50, 0);
        caption.offsetMax = Vector2.zero;
        var text = caption.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        text.fontSize = 26;
        text.text = label;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        var toggle = row.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = checkImage;
        iconFlips[index] = toggle;
        toggle.onValueChanged.AddListener(_ => CommitIconInputs());
    }

    CraftingRecipe SelectedIconRecipe => iconRecipes.Length > 0
        ? iconRecipes[Mathf.Clamp(iconRecipeDropdown.value, 0, iconRecipes.Length - 1)] : null;

    void RefreshIconEditor()
    {
        var recipe = SelectedIconRecipe;
        editingIconRecipe = recipe;
        var settings = recipe ? recipe.CardIconLayout : new CraftingRecipe.RecipeIconLayout { scale = Vector2.one };
        float[] values = { settings.scale.x, settings.scale.y, settings.offset.x, settings.offset.y };
        for (int i = 0; i < iconInputs.Length; i++)
        {
            iconInputs[i].interactable = recipe;
            iconInputs[i].SetTextWithoutNotify(values[i].ToString("0.##", CultureInfo.InvariantCulture));
        }
        iconFlips[0].interactable = iconFlips[1].interactable = recipe;
        iconFlips[0].SetIsOnWithoutNotify(settings.flipX);
        iconFlips[1].SetIsOnWithoutNotify(settings.flipY);
        iconPreviewImage.sprite = recipe ? recipe.output.icon : null;
        iconPreviewName.text = recipe ? recipe.output.displayName : "";
        if (recipe) WorkbenchPanel.ApplyRecipeIconLayout(recipe, iconPreviewImage.rectTransform);
        hotbarVerticalOffsetInput.SetTextWithoutNotify(CompactHud.HotbarIconVerticalOffset.ToString("0.##", CultureInfo.InvariantCulture));
        iconStatus.text = "";
    }

    bool ApplyHotbarVerticalOffset(bool commit)
    {
        if (!float.TryParse(hotbarVerticalOffsetInput.text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
        {
            if (commit) iconStatus.text = "Ungültiger Wert";
            return false;
        }
        CompactHud.HotbarIconVerticalOffset = Mathf.Clamp(value, -48f, 48f);
        if (commit)
        {
            hotbarVerticalOffsetInput.SetTextWithoutNotify(
                CompactHud.HotbarIconVerticalOffset.ToString("0.##", CultureInfo.InvariantCulture));
            iconStatus.text = "";
        }
        return true;
    }

    bool ApplyIconInputs(bool save)
    {
        var recipe = editingIconRecipe;
        if (!recipe) return false;
        var values = new float[4];
        for (int i = 0; i < values.Length; i++)
            if (!float.TryParse(iconInputs[i].text.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) || float.IsNaN(values[i]) || float.IsInfinity(values[i]))
            {
                if (save) iconStatus.text = "Ungültiger Wert";
                return false;
            }
        var settings = new CraftingRecipe.RecipeIconLayout
        {
            scale = new Vector2(Mathf.Clamp(values[0], .1f, 4f), Mathf.Clamp(values[1], .1f, 4f)),
            offset = new Vector2(Mathf.Clamp(values[2], -200f, 200f), Mathf.Clamp(values[3], -200f, 200f)),
            flipX = iconFlips[0].isOn,
            flipY = iconFlips[1].isOn
        };
        recipe.SetCardIconLayout(settings);
        recipe.SaveCardIconLayout();
        WorkbenchPanel.ApplyRecipeIconLayout(recipe, iconPreviewImage.rectTransform);
        UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeIcons();
        UnityEngine.Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
        if (save)
        {
            RefreshIconEditor();
        }
        return true;
    }

    public bool CommitIconInputs() => ApplyIconInputs(true);

    void ResetIconLayout()
    {
        var recipe = SelectedIconRecipe;
        if (!recipe) return;
        recipe.ResetCardIconLayout();
        recipe.SaveCardIconLayout();
        UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeIcons();
        UnityEngine.Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
        RefreshIconEditor();
    }

    void CreateRecipeEditor()
    {
        CloneItem("Section", "RecipeEditorSection", content, "REZEPTBEARBEITUNG");
        recipeItems.Add("RecipeEditorSection");
        var workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (workbench && workbench.recipes != null)
            editableRecipes = workbench.recipes.Where(recipe => recipe && recipe.TryGetCosts(out _))
                .OrderBy(recipe => recipe.Category == CraftingRecipe.RecipeCategory.Building ? 0 :
                    recipe.Category == CraftingRecipe.RecipeCategory.Materials ? 1 :
                    recipe.Category == CraftingRecipe.RecipeCategory.Tools ? 2 : 3)
                .ThenBy(recipe => recipe.output.displayName, System.StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(recipe => recipe.name, System.StringComparer.CurrentCultureIgnoreCase).ToArray();
        recipeIngredientChoices = giftItems.Where(item => item).OrderBy(item => item.displayName,
            System.StringComparer.CurrentCultureIgnoreCase).ToArray();
        recipeDropdown = CreateStyledDropdown("RecipeEditorDropdown");
        recipeDropdown.ClearOptions();
        recipeDropdown.AddOptions(editableRecipes.Select(recipe => new TMP_Dropdown.OptionData(
            IconCategoryName(recipe.Category) + " · " + recipe.output.displayName)).ToList());
        recipeDropdown.interactable = editableRecipes.Length > 0;
        recipeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        recipeDropdown.onValueChanged.AddListener(_ => RefreshRecipeEditor());
        recipeItems.Add("RecipeEditorDropdown");
        CloneItem("SpeedLabel", "RecipeCategoryLabel", content, "Kategorie");
        recipeCategoryDropdown = CreateStyledDropdown("RecipeCategoryDropdown");
        recipeCategoryDropdown.ClearOptions();
        recipeCategoryDropdown.AddOptions(new List<string> { "Automatisch", "Werkzeuge", "Bauen", "Materialien" });
        recipeCategoryDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        recipeCategoryDropdown.onValueChanged.AddListener(_ => ApplyRecipeEditor(true));
        CloneItem("SpeedLabel", "RecipeOutputLabel", content, "Hergestellte Stückzahl");
        recipeOutputAmount = CloneItem("DiggingSpeed", "RecipeOutputAmount", content).GetComponent<TMP_InputField>();
        recipeOutputAmount.onValueChanged = new TMP_InputField.OnChangeEvent();
        recipeOutputAmount.onEndEdit = new TMP_InputField.SubmitEvent();
        recipeOutputAmount.contentType = TMP_InputField.ContentType.IntegerNumber;
        DisableInputChildRaycasts(recipeOutputAmount);
        recipeOutputAmount.onEndEdit.AddListener(_ => ApplyRecipeEditor(true));
        CloneItem("Section", "RecipeIngredientsSection", content, "ZUTATEN");
        recipeIngredientRows = MakeRect("RecipeIngredientRows", content);
        items[recipeIngredientRows.name] = recipeIngredientRows;
        recipeAddIngredient = CloneItem("Defaults", "RecipeAddIngredient", content, "Zutat hinzufügen").GetComponent<Button>();
        recipeAddIngredient.onClick = new Button.ButtonClickedEvent();
        recipeAddIngredient.onClick.AddListener(AddRecipeIngredient);
        recipeEditorStatus = CloneItem("Status", "RecipeEditorStatus", content, "").GetComponent<TextMeshProUGUI>();
        recipeItems.AddRange(new[] { "RecipeCategoryLabel", "RecipeCategoryDropdown", "RecipeOutputLabel",
            "RecipeOutputAmount", "RecipeIngredientsSection", "RecipeIngredientRows", "RecipeAddIngredient", "RecipeEditorStatus" });
        RefreshRecipeEditor();
    }

    CraftingRecipe SelectedEditableRecipe => editableRecipes.Length > 0
        ? editableRecipes[Mathf.Clamp(recipeDropdown.value, 0, editableRecipes.Length - 1)] : null;

    void RefreshRecipeEditor()
    {
        editingRecipe = SelectedEditableRecipe;
        bool available = editingRecipe;
        recipeCategoryDropdown.interactable = available;
        recipeOutputAmount.interactable = available;
        recipeAddIngredient.interactable = available && recipeIngredientChoices.Any(item => item != editingRecipe.output);
        if (available)
        {
            recipeCategoryDropdown.SetValueWithoutNotify((int)editingRecipe.category);
            recipeOutputAmount.SetTextWithoutNotify(editingRecipe.outputAmount.ToString(CultureInfo.InvariantCulture));
        }
        ClearRecipeIngredientRows();
        if (available && editingRecipe.ingredients != null)
            for (int i = 0; i < editingRecipe.ingredients.Length; i++) CreateRecipeIngredientRow(i, editingRecipe.ingredients[i]);
        recipeEditorStatus.text = "";
        Layout();
    }

    void ClearRecipeIngredientRows()
    {
        foreach (var row in recipeRows)
        {
            recipeItems.Remove(row.rect.name);
            items.Remove(row.rect.name);
            recipeItems.Remove(row.item.name);
            recipeItems.Remove(row.amountLabel.name);
            recipeItems.Remove(row.amount.name);
            recipeItems.Remove(row.remove.name);
            items.Remove(row.item.name);
            items.Remove(row.amountLabel.name);
            items.Remove(row.amount.name);
            items.Remove(row.remove.name);
            Destroy(row.rect.gameObject);
        }
        recipeRows.Clear();
    }

    void CreateRecipeIngredientRow(int index, CraftingIngredient ingredient)
    {
        var rowRect = MakeRect("RecipeIngredientRow_" + index, recipeIngredientRows);
        items[rowRect.name] = rowRect;
        recipeItems.Add(rowRect.name);
        var row = new RecipeIngredientEditorRow { rect = rowRect };
        row.item = CreateStyledDropdown("RecipeIngredientDropdown_" + index, rowRect);
        row.item.ClearOptions();
        var choices = recipeIngredientChoices.Where(item => item && item != editingRecipe.output).ToArray();
        row.item.AddOptions(new List<string> { "Auswählen" }.Concat(choices.Select(item => item.displayName)).ToList());
        row.item.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(choices, ingredient.item) + 1));
        row.item.onValueChanged = new TMP_Dropdown.DropdownEvent();
        row.item.onValueChanged.AddListener(_ => ApplyRecipeEditor(true));
        recipeItems.Add(row.item.name);

        row.amountLabel = CloneItem("SpeedLabel", "RecipeIngredientAmountLabel_" + index, rowRect, "Menge")
            .GetComponent<TextMeshProUGUI>();
        row.amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        row.amountLabel.fontSize = 22;
        row.amountLabel.enableWordWrapping = false;
        row.amountLabel.raycastTarget = false;
        recipeItems.Add(row.amountLabel.name);

        row.amount = CloneItem("DiggingSpeed", "RecipeIngredientAmount_" + index, rowRect).GetComponent<TMP_InputField>();
        row.amount.onValueChanged = new TMP_InputField.OnChangeEvent();
        row.amount.onEndEdit = new TMP_InputField.SubmitEvent();
        row.amount.contentType = TMP_InputField.ContentType.IntegerNumber;
        DisableInputChildRaycasts(row.amount);
        row.amount.targetGraphic.color = new Color(.2f, .25f, .32f);
        row.amount.SetTextWithoutNotify(ingredient.amount.ToString(CultureInfo.InvariantCulture));
        row.amount.onEndEdit.AddListener(_ => ApplyRecipeEditor(true));
        recipeItems.Add(row.amount.name);

        row.remove = CloneItem("Defaults", "RecipeIngredientRemove_" + index, rowRect, "−").GetComponent<Button>();
        row.remove.onClick = new Button.ButtonClickedEvent();
        row.remove.onClick.AddListener(() => RemoveRecipeIngredient(index));
        recipeItems.Add(row.remove.name);
        recipeRows.Add(row);
    }

    bool ApplyRecipeEditor(bool refresh)
    {
        var recipe = editingRecipe;
        if (!recipe) return false;
        if (!int.TryParse(recipeOutputAmount.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputAmount) || outputAmount <= 0)
        {
            recipeEditorStatus.text = "Stückzahl muss größer als 0 sein.";
            return false;
        }

        var choices = recipeIngredientChoices.Where(item => item && item != recipe.output).ToArray();
        var ingredients = new CraftingIngredient[recipeRows.Count];
        for (int i = 0; i < recipeRows.Count; i++)
        {
            var row = recipeRows[i];
            int choice = row.item.value - 1;
            if (choice < 0 || choice >= choices.Length ||
                !int.TryParse(row.amount.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                recipeEditorStatus.text = "Bitte für jede Zutat Item und Menge festlegen.";
                return false;
            }
            ingredients[i] = new CraftingIngredient(choices[choice], amount);
        }

        var category = (CraftingRecipe.RecipeCategory)recipeCategoryDropdown.value;
        if (!recipe.SetRecipeSettings(category, outputAmount, ingredients))
        {
            recipeEditorStatus.text = "Rezeptdaten sind ungültig.";
            return false;
        }
        recipe.SaveRecipeSettings();
        RefreshRecipeDropdownLabels();
        UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeSettings(recipe);
        recipeEditorStatus.text = "";
        if (refresh) RefreshRecipeEditor();
        return true;
    }

    public bool CommitRecipeInputs() => ApplyRecipeEditor(false);

    void RefreshRecipeDropdownLabels()
    {
        int selected = recipeDropdown.value;
        recipeDropdown.ClearOptions();
        recipeDropdown.AddOptions(editableRecipes.Select(recipe => new TMP_Dropdown.OptionData(
            IconCategoryName(recipe.Category) + " · " + recipe.output.displayName)).ToList());
        if (editableRecipes.Length > 0) recipeDropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, editableRecipes.Length - 1));
        recipeDropdown.RefreshShownValue();
    }

    void AddRecipeIngredient()
    {
        if (!editingRecipe) return;
        var first = recipeIngredientChoices.FirstOrDefault(item => item && item != editingRecipe.output);
        if (!first) return;
        var ingredients = editingRecipe.ingredients == null
            ? new List<CraftingIngredient>() : new List<CraftingIngredient>(editingRecipe.ingredients);
        ingredients.Add(new CraftingIngredient(first, 1));
        if (editingRecipe.SetRecipeSettings(editingRecipe.category, editingRecipe.outputAmount, ingredients.ToArray()))
        {
            editingRecipe.SaveRecipeSettings();
            RefreshRecipeEditor();
            UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeSettings(editingRecipe);
        }
    }

    void RemoveRecipeIngredient(int index)
    {
        if (!editingRecipe || editingRecipe.ingredients == null || editingRecipe.ingredients.Length <= 1 ||
            index < 0 || index >= editingRecipe.ingredients.Length) return;
        var ingredients = editingRecipe.ingredients.ToList();
        ingredients.RemoveAt(index);
        if (editingRecipe.SetRecipeSettings(editingRecipe.category, editingRecipe.outputAmount, ingredients.ToArray()))
        {
            editingRecipe.SaveRecipeSettings();
            RefreshRecipeEditor();
            UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeSettings(editingRecipe);
        }
    }
    void CreateTabs()
    {
        CreateTab("GameplayTab", "Gameplay", DebugTab.Gameplay);
        CreateTab("TestsTab", "Testeinstellungen", DebugTab.Tests);
        CreateTab("MiscTab", "Misc", DebugTab.Misc);
        CreateTab("StartingResourcesTab", "Spielstart", DebugTab.StartingResources);
        CreateTab("ItemsTab", "Items", DebugTab.Items);
        CreateTab("WorldTab", "Welt", DebugTab.World);
        CreateTab("AudioTab", "Audio", DebugTab.Audio);
        CreateTab("IconsTab", "Icons", DebugTab.Icons);
        CreateTab("RecipesTab", "Rezepte", DebugTab.Recipes);

        MoveToTab(startingResourceItems, "StartingResourcesSection", "StartingMoneyLabel", "StartingMoneyInput",
            "StartingResourceRows", "StartingResourceAdd", "StartingResourceStatus");

        MoveToTab(recipeItems, "RecipeEditorSection", "RecipeEditorDropdown", "RecipeCategoryLabel",
            "RecipeCategoryDropdown", "RecipeOutputLabel", "RecipeOutputAmount", "RecipeIngredientsSection",
            "RecipeIngredientRows", "RecipeAddIngredient", "RecipeEditorStatus");

        MoveToTab(giftingItems, "ItemsSection", "GiftAmountLabel", "GiftAmount",
            "GiftPreset0", "GiftPreset1", "GiftPreset2", "GiftPreset3");
        foreach (var section in giftSections)
        {
            MoveToTab(giftingItems, section.header);
            MoveToTab(giftingItems, section.cards.ToArray());
        }
        var audioNames = new List<string> { "AudioSection", "ConcreteAudioSection", "LayerStoneAudioBackdrop", "LayerStoneAudioSection",
            "LayerStoneAudioHeaderVolume", "LayerStoneAudioHeaderPitch", "LayerStoneAudioHeaderSpread",
            "DetailSection", "DetailClipLabel", "DetailClipDropdown",
            "DetailVolumeLabel", "DetailVolumeInput", "DetailPreview" };
        for (int i = 0; i < AudioSettings.Length; i++)
        {
            audioNames.Add("AudioLabel" + i);
            audioNames.Add("AudioInput" + i);
        }
        for (int i = 0; i < ConcreteAudioSettings.Length; i++)
        {
            audioNames.Add("ConcreteAudioLabel" + i);
            audioNames.Add("ConcreteAudioInput" + i);
            audioNames.Add("ConcreteAudioOffsetLabel" + i);
            audioNames.Add("ConcreteAudioOffsetInput" + i);
        }
        audioNames.AddRange(layerStoneAudioItems);
        MoveToTab(audioItems, audioNames.ToArray());
        var lightingNames = new List<string> { "LightingSection", "LightingInfo", "LightingEnabled", "LightingHint" };
        for (int i = 0; i < 6; i++)
        {
            lightingNames.Add("LightLabel" + i);
            lightingNames.Add("LightInput" + i);
        }
        MoveToTab(worldItems, lightingNames.ToArray());

        CloneItem("Section", "TestSection", content, "TESTMODUS");
        CloneItem("Section", "MiningTestSection", content, "ABBAU");
        CloneItem("Section", "PlayerTestSection", content, "SPIELER");
        CloneItem("Section", "TimeTestSection", content, "LICHT");
        CloneItem("Section", "MiscSection", content, "MISC");
        CloneItem("SpeedLabel", "TestLabel", content, "Abbau-Testfaktor (×)");
        testMultiplier = CloneItem("DiggingSpeed", "TestMultiplier", content).GetComponent<TMP_InputField>();
        testMultiplier.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(testMultiplier);
        testMultiplier.onEndEdit.AddListener(_ => ApplyTestInput());
        EnsureMiningHitOffsetControl();
        CloneItem("SpeedLabel", "MovementLabel", content, "Bewegungsfaktor (×)");
        movementMultiplier = CloneItem("DiggingSpeed", "MovementMultiplier", content).GetComponent<TMP_InputField>();
        movementMultiplier.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(movementMultiplier);
        movementMultiplier.onEndEdit.AddListener(_ => ApplyTestInput());
        testStatus = CloneItem("Status", "TestStatus", content, "").GetComponent<TextMeshProUGUI>();
        CreateModeToggle("TestActive", "Testmodus aktiv", GameplayTestMode.Active, null);
#if UNITY_EDITOR
        CreateModeToggle("KeepMap", "Map nach Play-Stopp im Editor behalten", GameplayTestMode.KeepMap, null);
        testItems.Remove("KeepMap");
        gameplayItems.Add("KeepMap");
#endif
        CreateModeToggle("GodMode", "God Mode", GameplayTestMode.God,
            "Schaden wird ignoriert.");
        CreateModeToggle("NoEnergy", "Kein Energieverbrauch", GameplayTestMode.NoEnergyConsume,
            "Verhindert Energieverbrauch im Stand, beim Bewegen und beim Abbauen.");
        CreateModeToggle("FlyMode", "Fly Mode", GameplayTestMode.Fly,
            "Gravitation aus. W/S: aufwärts/abwärts. A/D: seitwärts. Ohne Taste schweben. Kollisionen bleiben aktiv.");
        CreateModeToggle("NoClip", "No Clip", GameplayTestMode.NoClip, null);
        CreateModeToggle("GlobalLighting", "Global Lighting", GameplayTestMode.GlobalLighting, null);
        CloneItem("Section", "HealthTestSection", content, "GESUNDHEIT");
        CloneItem("SpeedLabel", "HealthValueLabel", content, "Aktuelle HP");
        healthInput = CloneItem("DiggingSpeed", "HealthValueInput", content).GetComponent<TMP_InputField>();
        healthInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(healthInput);
        var setHealth = CloneItem("Defaults", "SetHealth", content, "Setzen").GetComponent<Button>();
        setHealth.onClick = new Button.ButtonClickedEvent();
        setHealth.onClick.AddListener(ApplyHealthInput);
        foreach (int damage in new[] { 90, 40, 10, 1 })
        {
            string name = "DamageTest" + damage;
            var button = CloneItem("Defaults", name, content, damage + " Schaden").GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            int amount = damage;
            button.onClick.AddListener(() => StatsManager.Instance?.ApplyDamage(amount));
        }
        var testEndScreen = CloneItem("Defaults", "TestEndScreen", content, "Endscreen testen").GetComponent<Button>();
        testEndScreen.onClick = new Button.ButtonClickedEvent();
        testEndScreen.onClick.AddListener(ShowTestEndScreen);
        CreateDayNightSelector();
        testItems.AddRange(new[] {"TestSection", "MiningTestSection", "PlayerTestSection", "TimeTestSection",
            "TestLabel","TestMultiplier","MovementLabel","MovementMultiplier","TestStatus", "HealthTestSection",
            "HealthValueLabel", "HealthValueInput", "SetHealth",
            "DamageTest90", "DamageTest40", "DamageTest10", "DamageTest1", "TestEndScreen"});
        MoveToTab(miscItems, "MiscSection");
    }

    void CreateTab(string name, string label, DebugTab tab)
    {
        var rect = CloneItem("Defaults", name, window, label);
        var button = rect.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => SwitchTab(tab));
        tabNames[tab] = name;
    }

    void MoveToTab(List<string> destination, params string[] names)
    {
        foreach (string name in names)
        {
            if (!items.ContainsKey(name)) continue;
            gameplayItems.Remove(name);
            destination.Add(name);
        }
    }

    void CreateDayNightSelector()
    {
        var row = MakeRect("DayNightRow", content);
        items[row.name] = row;
        gameplayItems.Add(row.name);
        var caption = MakeRect("Label", row);
        caption.anchorMin = Vector2.zero; caption.anchorMax = Vector2.up;
        caption.offsetMin = Vector2.zero; caption.offsetMax = new Vector2(150, 0);
        var label = caption.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        label.fontSize = 26; label.text = "Tag / Nacht";
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        string[] names = { "Auto", "Tag", "Nacht" };
        for (int i = 0; i < names.Length; i++)
        {
            var rect = MakeRect(names[i], row);
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(0, .5f);
            dayNightButtons[i] = rect;
            var background = rect.gameObject.AddComponent<Image>();
            dayNightBackgrounds[i] = background;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            var textRect = MakeRect("Text", rect);
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = label.font; text.fontSize = 22;
            text.text = names[i]; text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var selected = (GameplayDayNightMode)i;
            button.onClick.AddListener(() => ApplyDayNightMode(selected));
        }
    }

    void CreateModeToggle(string name, string label, GameplayTestMode mode, string hint)
    {
        var row = MakeRect(name, content); items[name] = row; testItems.Add(name);
        var hit = row.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        var box = MakeRect("Box", row);
        box.anchorMin = box.anchorMax = new Vector2(0,.5f);
        box.anchoredPosition = new Vector2(17,0); box.sizeDelta = new Vector2(30,30);
        var boxImage = box.gameObject.AddComponent<Image>(); boxImage.color = new Color(.3f,.37f,.45f);
        var check = MakeRect("Check", box);
        check.anchorMin = Vector2.zero; check.anchorMax = Vector2.one;
        check.offsetMin = new Vector2(6,6); check.offsetMax = new Vector2(-6,-6);
        var checkImage = check.gameObject.AddComponent<Image>(); checkImage.color = new Color(1f,.65f,.2f);
        var caption = MakeRect("Label", row);
        caption.anchorMin = Vector2.zero; caption.anchorMax = Vector2.one;
        caption.offsetMin = new Vector2(50,0); caption.offsetMax = Vector2.zero;
        var text = caption.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        text.fontSize = 26; text.text = label; text.alignment = TextAlignmentOptions.MidlineLeft;
        var toggle = row.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage; toggle.graphic = checkImage;
        modeToggles[mode] = toggle;
        toggle.onValueChanged.AddListener(value => {
            if (mode == GameplayTestMode.KeepMap)
            {
                bool success = GameplayTestSettings.SetMode(mode, value, out string message);
                toggle.SetIsOnWithoutNotify(GameplayTestSettings.KeepMapInEditor);
                items["Status"].GetComponent<TextMeshProUGUI>().text = success ? "" : message;
                return;
            }
            if (!ApplyTestInput()) { toggle.SetIsOnWithoutNotify(GameplayTestSettings.GetConfiguredMode(mode)); return; }
            bool saved = GameplayTestSettings.SetMode(mode, value, out string error);
            RefreshTest();
            testStatus.text = saved ? "" : error;
        });
        if (hint != null) AttachTooltip(name, hint);
    }

    public void SwitchTab(bool tests)
        => SwitchTab(tests ? DebugTab.Tests : DebugTab.Gameplay);

    public void SwitchTab(string tab)
    {
        if (System.Enum.TryParse(tab, true, out DebugTab parsed)) SwitchTab(parsed);
    }

    void SwitchTab(DebugTab tab)
    {
        if (tab == currentTab) return;
        if (IsTestTab || IsMiscTab) { if (!ApplyTestInput()) return; }
        else if (IsIconTab) { if (!CommitIconInputs()) return; }
        else if (IsRecipeTab) { if (!CommitRecipeInputs()) return; }
        else if (IsStartingResourcesTab) { if (!CommitStartingResources()) return; }
        else if (!GetComponent<GameplayDebugPanel>().TryApplyAll()) return;
        SetTab(tab);
    }

    void SetTab(bool tests)
        => SetTab(tests ? DebugTab.Tests : DebugTab.Gameplay);

    void SetTab(DebugTab tab)
    {
        if (detailClipDropdown && detailClipDropdown.IsExpanded) detailClipDropdown.Hide();
        if (iconRecipeDropdown && iconRecipeDropdown.IsExpanded) iconRecipeDropdown.Hide();
        if (recipeDropdown && recipeDropdown.IsExpanded) recipeDropdown.Hide();
        foreach (var row in recipeRows) if (row.item && row.item.IsExpanded) row.item.Hide();
        foreach (var row in startingResourceRowsData) if (row.item && row.item.IsExpanded) row.item.Hide();
        HideTooltip(); currentTab = tab;
        SetActive(gameplayItems, tab == DebugTab.Gameplay);
        SetActive(testItems, tab == DebugTab.Tests);
        SetActive(miscItems, tab == DebugTab.Misc);
        SetActive(giftingItems, tab == DebugTab.Items);
        SetActive(worldItems, tab == DebugTab.World);
        SetActive(audioItems, tab == DebugTab.Audio);
        SetActive(iconItems, tab == DebugTab.Icons);
        SetActive(recipeItems, tab == DebugTab.Recipes);
        SetActive(startingResourceItems, tab == DebugTab.StartingResources);
#if !UNITY_EDITOR
        items["MakeDefaults"].gameObject.SetActive(false);
#endif
        foreach (var entry in tabNames)
            items[entry.Value].GetComponent<Image>().color = entry.Key == tab
                ? new Color(.64f,.38f,.1f) : new Color(.12f,.16f,.22f);
        RefreshTest(); Layout(); scroll.verticalNormalizedPosition = 1;
        if (tab == DebugTab.Audio)
        {
            CreateLayerStoneAudioControls();
            RefreshAudioSettings(); RefreshLayerStoneAudioSettings(); RefreshDetailSettings();
            Layout();
        }
        if (tab == DebugTab.Icons) RefreshIconEditor();
        if (tab == DebugTab.Recipes) RefreshRecipeEditor();
        if (tab == DebugTab.StartingResources) startingResourceStatus.text = "";
    }

    void SetActive(List<string> names, bool active)
    {
        foreach (string name in names)
            if (items.TryGetValue(name, out var rect)) rect.gameObject.SetActive(active);
    }

    void RefreshTest()
    {
        EnsureMiningHitOffsetControl();
        foreach (var entry in modeToggles) entry.Value.SetIsOnWithoutNotify(GameplayTestSettings.GetConfiguredMode(entry.Key));
        float factor = GameplayTestSettings.ConfiguredDiggingMultiplier;
        testMultiplier.SetTextWithoutNotify(factor.ToString("R", CultureInfo.InvariantCulture));
        movementMultiplier.SetTextWithoutNotify(GameplayTestSettings.ConfiguredMovementMultiplier.ToString("R", CultureInfo.InvariantCulture));
        if (!minerVisual) minerVisual = FindFirstObjectByType<MinerPlayerVisual>();
        if (GameplayTestSettings.HasMiningHitOffsetOverride && minerVisual)
            minerVisual.miningHitOffsetMs = GameplayTestSettings.ConfiguredMiningHitOffsetMs;
        float hitOffset = GameplayTestSettings.HasMiningHitOffsetOverride
            ? GameplayTestSettings.ConfiguredMiningHitOffsetMs
            : minerVisual ? minerVisual.miningHitOffsetMs : 0f;
        miningHitOffsetInput.SetTextWithoutNotify(hitOffset.ToString("R", CultureInfo.InvariantCulture));
        RefreshCurrentHealthInput();
        testStatus.text = GameplayTestSettings.Warning ?? "";
        RefreshDayNight();
    }

    void RefreshCurrentHealthInput()
    {
        if (!healthInput || healthInput.isFocused || !StatsManager.Instance) return;
        string value = StatsManager.Instance.Health.ToString("0.##", CultureInfo.InvariantCulture);
        if (healthInput.text != value) healthInput.SetTextWithoutNotify(value);
    }

    void ApplyHealthInput()
    {
        var stats = StatsManager.Instance;
        if (!stats) { testStatus.text = "Keine Gesundheitswerte verfügbar."; return; }
        if (!float.TryParse(healthInput.text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float health) || float.IsNaN(health) ||
            float.IsInfinity(health) || health < 0f || health > stats.MaxHealth)
        {
            testStatus.text = "HP: bitte 0 bis " + stats.MaxHealth.ToString("0.##", CultureInfo.InvariantCulture) + " eingeben.";
            return;
        }
        stats.SetHealthForDebug(health);
        testStatus.text = "";
    }

    void ShowTestEndScreen()
    {
        var endScreen = FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        if (!endScreen) return;
        GetComponent<GameplayDebugPanel>()?.Close();
        if (GameplayDebugPanel.IsOpen) return;
        endScreen.Show();
    }

    void EnsureMiningHitOffsetControl()
    {
        if (!items.ContainsKey("MiningHitOffsetLabel"))
            CloneItem("SpeedLabel", "MiningHitOffsetLabel", content, "Treffer-Versatz (ms)");
        if (!items.TryGetValue("MiningHitOffsetInput", out var inputRect))
            inputRect = CloneItem("DiggingSpeed", "MiningHitOffsetInput", content);
        miningHitOffsetInput = inputRect.GetComponent<TMP_InputField>();
        miningHitOffsetInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(miningHitOffsetInput);
        if (!miningHitOffsetListenerBound)
        { miningHitOffsetInput.onEndEdit.AddListener(_ => ApplyTestInput()); miningHitOffsetListenerBound = true; }
        if (!miscItems.Contains("MiningHitOffsetLabel")) miscItems.Add("MiningHitOffsetLabel");
        if (!miscItems.Contains("MiningHitOffsetInput")) miscItems.Add("MiningHitOffsetInput");
        inputRect.gameObject.SetActive(IsMiscTab);
        items["MiningHitOffsetLabel"].gameObject.SetActive(IsMiscTab);
    }

    void RefreshDayNight()
    {
        if (dayNightBackgrounds == null) return;
        var selected = GameplayTestSettings.ConfiguredDayNightMode;
        for (int i = 0; i < dayNightBackgrounds.Length; i++)
        {
            if (!dayNightBackgrounds[i]) continue;
            dayNightBackgrounds[i].color = (int)selected == i
                ? new Color(.55f, .35f, .12f) : new Color(.2f, .25f, .32f);
        }
    }

    void ApplyDayNightMode(GameplayDayNightMode mode)
    {
        if (!GameplayTestSettings.SetDayNightMode(mode, out string error))
        {
            testStatus.text = error;
            return;
        }
        var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
        if (!sky)
        {
            testStatus.text = "Tag-/Nachtmodus gespeichert; keine Himmelskomponente für GPS gefunden.";
            return;
        }

        sky.automaticCycle = mode == GameplayDayNightMode.Automatic;
        if (mode != GameplayDayNightMode.Automatic) sky.SetNight(mode == GameplayDayNightMode.Night);
        RefreshDayNight();
        testStatus.text = "";
#if UNITY_EDITOR
        QueueGpsDefault(sky, "automaticCycle");
        QueueGpsDefault(sky, "isNight");
#endif
    }

    static void DisableInputChildRaycasts(TMP_InputField input)
    {
        foreach (var graphic in input.GetComponentsInChildren<Graphic>(true))
            if (graphic != input.targetGraphic) graphic.raycastTarget = false;
    }

    public bool ApplyTestInput()
    {
        if (!float.TryParse(testMultiplier.text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float digging) ||
            !GameplayTestSettings.IsValid(digging))
        { testStatus.text = "Abbau-Testfaktor: bitte 0,1 bis 100 eingeben."; return false; }
        if (!float.TryParse(movementMultiplier.text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float movement) ||
            !GameplayTestSettings.IsValidMovementMultiplier(movement))
        { testStatus.text = "Bewegungsfaktor: bitte 0,1 bis 20 eingeben."; return false; }
        if (!float.TryParse(miningHitOffsetInput.text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float hitOffset) ||
            !GameplayTestSettings.IsValidMiningHitOffset(hitOffset))
        { testStatus.text = "Treffer-Versatz: bitte -500 bis 500 ms eingeben."; return false; }
        GameplayTestSettings.SetDiggingMultiplier(digging);
        GameplayTestSettings.SetMovementMultiplier(movement);
        GameplayTestSettings.SetMiningHitOffset(hitOffset);
        if (!minerVisual) minerVisual = FindFirstObjectByType<MinerPlayerVisual>();
        if (minerVisual) minerVisual.miningHitOffsetMs = hitOffset;
        if (GameplayTestSettings.HasUnsavedChanges && !GameplayTestSettings.Save(out string error))
        { testStatus.text = error; return false; }
        RefreshTest();
#if UNITY_EDITOR
        if (IsMiscTab && minerVisual) QueueGpsDefault(minerVisual, "miningHitOffsetMs");
#endif
        return true;
    }

    static RectTransform MakeRect(string name, Transform parent)
    {
        var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        result.SetParent(parent, false); return result;
    }

    RectTransform Handle(string name, Vector2 min, Vector2 max, Vector2 direction)
    {
        var rect = MakeRect(name, window); rect.anchorMin = min; rect.anchorMax = max;
        var image = rect.gameObject.AddComponent<Image>(); image.color = Color.clear;
        var handle = rect.gameObject.AddComponent<GameplayWindowHandle>();
        handle.owner = this; handle.direction = direction;
        return rect;
    }

    void CreateTooltip()
    {
        tooltip = MakeRect("ButtonTooltip", window);
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(.5f, .5f);
        tooltip.pivot = Vector2.zero;
        tooltip.sizeDelta = new Vector2(430, 76);
        var background = tooltip.gameObject.AddComponent<Image>();
        background.color = new Color(.22f, .28f, .36f, 1f);
        background.raycastTarget = false;
        var label = MakeRect("Text", tooltip);
        label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(14, 8); label.offsetMax = new Vector2(-14, -8);
        tooltipLabel = label.gameObject.AddComponent<TextMeshProUGUI>();
        tooltipLabel.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        tooltipLabel.fontSize = 21;
        tooltipLabel.color = new Color(.94f, .96f, 1f);
        tooltipLabel.alignment = TextAlignmentOptions.MidlineLeft;
        tooltipLabel.enableWordWrapping = true;
        tooltipLabel.raycastTarget = false;
        tooltip.gameObject.SetActive(false);
    }

    void AttachTooltip(string button, string message)
    {
        var trigger = items[button].gameObject.AddComponent<GameplayButtonTooltip>();
        trigger.owner = this;
        trigger.message = message;
    }

    void CreateLightingInfo()
    {
        var icon = MakeRect("LightingInfo", content);
        items[icon.name] = icon;
        var background = icon.gameObject.AddComponent<Image>();
        background.color = new Color(.26f, .34f, .43f);
        var label = MakeRect("i", icon);
        label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
        label.offsetMin = label.offsetMax = Vector2.zero;
        var text = label.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        text.text = "i"; text.fontSize = 21;
        text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        AttachTooltip("LightingInfo", "0,15 = 15 % Verlust bei Stärke 1.\nHöhere Stärke dunkelt schneller ab.\nGrundhelligkeit 0 = vollständiges Schwarz.");
    }

    public void ShowTooltip(string message, RectTransform button)
    {
        tooltipLabel.text = message;
        tooltip.sizeDelta = new Vector2(430, Mathf.Max(76, tooltipLabel.GetPreferredValues(message, 402, Mathf.Infinity).y + 16));
        tooltip.gameObject.SetActive(true);
        tooltip.SetAsLastSibling();
        MoveTooltip(button);
    }

    public void MoveTooltip(RectTransform button)
    {
        if (!tooltip.gameObject.activeSelf) return;
        var corners = new Vector3[4];
        button.GetWorldCorners(corners);
        var bottomLeft = window.InverseTransformPoint(corners[0]);
        var topRight = window.InverseTransformPoint(corners[2]);
        var size = tooltip.sizeDelta;
        var rect = window.rect;
        float y = topRight.y + 10;
        if (y + size.y > rect.yMax - 8) y = bottomLeft.y - size.y - 10;
        tooltip.anchoredPosition = new Vector2(
            Mathf.Clamp((bottomLeft.x + topRight.x - size.x) * .5f, rect.xMin + 8, rect.xMax - size.x - 8),
            Mathf.Clamp(y, rect.yMin + 8, rect.yMax - size.y - 8));
    }

    public void HideTooltip() { if (tooltip) tooltip.gameObject.SetActive(false); }

    public void Begin(PointerEventData e, Vector2 direction)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, e.position, e.pressEventCamera, out startPointer);
        startPosition = window.anchoredPosition; startSize = window.sizeDelta;
        resizeDirection = direction;
    }

    public void Drag(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, e.position, e.pressEventCamera, out var pointer);
        Vector2 delta = pointer - startPointer;
        if (resizeDirection == Vector2.zero) window.anchoredPosition = startPosition + delta;
        else
        {
            var size = startSize + Vector2.Scale(delta, resizeDirection);
            size.x = Mathf.Clamp(size.x, Mathf.Min(600, bounds.rect.width), bounds.rect.width);
            size.y = Mathf.Clamp(size.y, Mathf.Min(400, bounds.rect.height), bounds.rect.height);
            window.sizeDelta = size;
            window.anchoredPosition = startPosition + Vector2.Scale(size - startSize, resizeDirection) * 0.5f;
        }
        ClampWindow(); Layout();
    }

    void LateUpdate()
    {
        RefreshGifting();
        if (currentTab == DebugTab.Tests) RefreshCurrentHealthInput();
        if (lastSize != window.rect.size || lastBounds != bounds.rect.size) Layout();
    }

    void ClampWindow()
    {
        Vector2 available = bounds.rect.size;
        if (available.x <= 0 || available.y <= 0) return;
        window.sizeDelta = new Vector2(Mathf.Clamp(window.sizeDelta.x, Mathf.Min(600, available.x), available.x),
            Mathf.Clamp(window.sizeDelta.y, Mathf.Min(400, available.y), available.y));
        Vector2 limit = (available - window.sizeDelta) * .5f;
        window.anchoredPosition = new Vector2(Mathf.Clamp(window.anchoredPosition.x, -limit.x, limit.x),
            Mathf.Clamp(window.anchoredPosition.y, -limit.y, limit.y));
    }

    void Place(string name, float x, float y, float width, float height)
    {
        if (!items.TryGetValue(name, out var rect)) return;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        var label = rect.GetComponentInChildren<TextMeshProUGUI>();
        if (rect.GetComponent<Button>() && label)
        {
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8, 0); label.rectTransform.offsetMax = new Vector2(-8, 0);
        }
    }

    void Layout()
    {
        lastSize = window.rect.size; lastBounds = bounds.rect.size;
        float w = lastSize.x;
        Place("Title", 28, 20, 230, 58); Place("Close", w-78, 20, 56, 56);
        Place("Accent", 0, 0, w, 5);
        string[] tabs = { "GameplayTab", "TestsTab", "MiscTab", "StartingResourcesTab", "ItemsTab", "WorldTab", "AudioTab", "IconsTab", "RecipesTab" };
        for (int i = 0; i < tabs.Length; i++) Place(tabs[i], 20, 104 + i * 62, 244, 50);
        float inner = Mathf.Max(340, w - 370);
        float col = Mathf.Min(inner, 940);
        float x = 18;
        items["Hint"].gameObject.SetActive(false);
        items["SpeedHint"].gameObject.SetActive(false);
        items["LightingHint"].gameObject.SetActive(false);
        items["Save"].gameObject.SetActive(false);
        if (currentTab == DebugTab.StartingResources)
        {
            Place("StartingResourcesSection",x,12,col,36);
            Place("StartingMoneyLabel",x,60,col-150,48);
            Place("StartingMoneyInput",x+col-140,58,140,50);
            startingResourceRows.anchorMin = startingResourceRows.anchorMax = new Vector2(0, 1);
            startingResourceRows.pivot = new Vector2(0, 1);
            startingResourceRows.anchoredPosition = new Vector2(x, -126);
            startingResourceRows.sizeDelta = new Vector2(col, startingResourceRowsData.Count * 58);
            float itemWidth = Mathf.Max(100, col - 340);
            for (int i = 0; i < startingResourceRowsData.Count; i++)
            {
                var row = startingResourceRowsData[i];
                row.rect.anchorMin = row.rect.anchorMax = new Vector2(0, 1);
                row.rect.pivot = new Vector2(0, 1);
                row.rect.anchoredPosition = new Vector2(0, -i * 58);
                row.rect.sizeDelta = new Vector2(col, 50);
                Place(row.item.name, 0, 0, itemWidth, 48);
                Place(row.amountLabel.name, itemWidth + 8, 0, 112, 48);
                Place(row.amount.name, itemWidth + 128, 0, 150, 48);
                Place(row.remove.name, itemWidth + 288, 0, 46, 48);
            }
            float addY = 136 + startingResourceRowsData.Count * 58;
            Place("StartingResourceAdd",x,addY,col,48);
            Place("StartingResourceStatus",x,addY+58,col,44);
            content.sizeDelta = new Vector2(0, addY+120);
            return;
        }
        if (currentTab == DebugTab.Tests)
        {
            Place("TestSection",x,12,col,36);
            Place("TestActive",x,56,col,44);
            Place("MiningTestSection",x,122,col,36);
            Place("TestLabel",x,166,col-170,50); Place("TestMultiplier",x+col-150,166,150,50);
            Place("MiningHitOffsetLabel",x,218,col-170,50); Place("MiningHitOffsetInput",x+col-150,218,150,50);
            Place("PlayerTestSection",x,290,col,36);
            Place("MovementLabel",x,334,col-170,50); Place("MovementMultiplier",x+col-150,334,150,50);
            Place("GodMode",x,406,col,44); Place("NoEnergy",x,458,col,44);
            Place("FlyMode",x,510,col,44); Place("NoClip",x,562,col,44);
            Place("TimeTestSection",x,630,col,36);
            Place("GlobalLighting",x,674,col,44);
            Place("HealthTestSection",x,742,col,36);
            Place("HealthValueLabel",x,786,col-300,48);
            Place("HealthValueInput",x+col-280,786,140,48);
            Place("SetHealth",x+col-130,786,130,48);
            float damageButtonWidth = (col - 30f) / 4f;
            foreach (int damage in new[] { 90, 40, 10, 1 })
            {
                int index = System.Array.IndexOf(new[] { 90, 40, 10, 1 }, damage);
                Place("DamageTest" + damage, x + index * (damageButtonWidth + 10f), 846, damageButtonWidth, 48);
            }
            Place("TestEndScreen",x,914,col,48);
            Place("TestStatus",x,974,col,70); content.sizeDelta = new Vector2(0,1056);
            return;
        }

        if (currentTab == DebugTab.Misc)
        {
            Place("MiscSection",x,12,col,36);
            Place("MiningHitOffsetLabel",x,60,col-170,50);
            Place("MiningHitOffsetInput",x+col-150,60,150,50);
            content.sizeDelta = new Vector2(0,140);
            return;
        }

        if (currentTab == DebugTab.Items)
        {
            Place("ItemsSection",x,12,col,36);
            Place("GiftAmountLabel",x,64,95,48);
            Place("GiftAmount",x+100,62,120,50);
            float presetWidth = Mathf.Min(90, (col - 250 - 3 * 10) / 4f);
            for (int i = 0; i < GiftPresets.Length; i++)
                Place("GiftPreset" + i,x+244+i*(presetWidth+10),62,presetWidth,50);
            float y = 146;
            int columns = Mathf.Clamp(Mathf.FloorToInt((col + 12) / 232f), 2, 4);
            float cardWidth = (col - (columns-1)*12) / columns;
            foreach (var section in giftSections)
            {
                Place(section.header,x,y,col,36);
                y += 48;
                for (int i = 0; i < section.cards.Count; i++)
                {
                    var card = items[section.cards[i]];
                    card.anchorMin = card.anchorMax = new Vector2(0, 1);
                    card.pivot = new Vector2(0, 1);
                    card.anchoredPosition = new Vector2(x+(i%columns)*(cardWidth+12),-y-(i/columns)*90);
                    card.sizeDelta = new Vector2(cardWidth,78);
                }
                y += Mathf.CeilToInt(section.cards.Count/(float)columns)*90+24;
            }
            content.sizeDelta = new Vector2(0,y+16);
            return;
        }

        if (currentTab == DebugTab.Icons)
        {
            Place("IconSection", x, 12, col, 36);
            Place("IconRecipeDropdown", x, 60, col, 50);
            Place("IconPreviewCard", x + (col - 182) * .5f, 132, 182, 160);
            for (int i = 0; i < iconInputs.Length; i++)
            {
                float y = 316 + i * 58;
                Place("IconLabel" + i, x, y, col - 170, 48);
                Place("IconInput" + i, x + col - 150, y, 150, 48);
            }
            Place("IconFlipX", x, 558, col, 44);
            Place("IconFlipY", x, 610, col, 44);
            Place("HotbarVerticalOffsetLabel", x, 670, col - 170, 48);
            Place("HotbarVerticalOffsetInput", x + col - 150, 670, 150, 48);
            Place("IconReset", x, 738, 220, 52);
            Place("IconStatus", x, 806, col, 50);
            content.sizeDelta = new Vector2(0, 866);
            return;
        }

        if (currentTab == DebugTab.Recipes)
        {
            Place("RecipeEditorSection", x, 12, col, 36);
            Place("RecipeEditorDropdown", x, 60, col, 50);
            float sideWidth = Mathf.Min(260, col * .52f);
            Place("RecipeCategoryLabel", x, 126, col - sideWidth - 12, 48);
            Place("RecipeCategoryDropdown", x + col - sideWidth, 124, sideWidth, 50);
            Place("RecipeOutputLabel", x, 188, col - 150, 48);
            Place("RecipeOutputAmount", x + col - 140, 186, 140, 50);
            Place("RecipeIngredientsSection", x, 250, col, 36);
            recipeIngredientRows.anchorMin = recipeIngredientRows.anchorMax = new Vector2(0, 1);
            recipeIngredientRows.pivot = new Vector2(0, 1);
            recipeIngredientRows.anchoredPosition = new Vector2(x, -298);
            recipeIngredientRows.sizeDelta = new Vector2(col, recipeRows.Count * 58);
            float ingredientWidth = Mathf.Max(100, col - 340);
            for (int i = 0; i < recipeRows.Count; i++)
            {
                var row = recipeRows[i];
                row.rect.anchorMin = row.rect.anchorMax = new Vector2(0, 1);
                row.rect.pivot = new Vector2(0, 1);
                row.rect.anchoredPosition = new Vector2(0, -i * 58);
                row.rect.sizeDelta = new Vector2(col, 50);
                Place(row.item.name, 0, 0, ingredientWidth, 48);
                Place(row.amountLabel.name, ingredientWidth + 8, 0, 112, 48);
                Place(row.amount.name, ingredientWidth + 128, 0, 150, 48);
                Place(row.remove.name, ingredientWidth + 288, 0, 46, 48);
                row.remove.interactable = recipeRows.Count > 1;
            }
            float addY = 310 + recipeRows.Count * 58;
            Place("RecipeAddIngredient", x, addY, Mathf.Min(260, col), 50);
            Place("RecipeEditorStatus", x, addY + 64, col, 52);
            content.sizeDelta = new Vector2(0, addY + 142);
            return;
        }
        if (currentTab == DebugTab.Audio)
        {
            Place("AudioSection",x,12,col,36);
            for (int i = 0; i < AudioSettings.Length; i++)
            {
                float y = 60 + i * 58;
                Place("AudioLabel" + i,x,y,col-150,48);
                Place("AudioInput" + i,x+col-140,y,140,48);
            }
            float concreteSectionY = 60 + AudioSettings.Length * 58;
            Place("ConcreteAudioSection",x,concreteSectionY,col,36);
            for (int i = 0; i < ConcreteAudioSettings.Length; i++)
            {
                float y = concreteSectionY + 48 + i * 116;
                Place("ConcreteAudioLabel" + i,x,y,col-150,48);
                Place("ConcreteAudioInput" + i,x+col-140,y,140,48);
                Place("ConcreteAudioOffsetLabel" + i,x,y+58,col-150,48);
                Place("ConcreteAudioOffsetInput" + i,x+col-140,y+58,140,48);
            }
            float stoneSectionY = concreteSectionY + 48 + ConcreteAudioSettings.Length * 116 + 20;
            Place("LayerStoneAudioSection", x, stoneSectionY, col, 36);
            float labelWidth = col * .47f;
            float settingWidth = (col - labelWidth) / 3f;
            float stoneHeaderY = stoneSectionY + 40;
            Place("LayerStoneAudioHeaderVolume", x + labelWidth, stoneHeaderY, settingWidth, 34);
            Place("LayerStoneAudioHeaderPitch", x + labelWidth + settingWidth, stoneHeaderY, settingWidth, 34);
            Place("LayerStoneAudioHeaderSpread", x + labelWidth + settingWidth * 2, stoneHeaderY, settingWidth, 34);
            float stoneRowsY = stoneHeaderY + 36;
            int previousLayer = -1;
            int previousAction = -1;
            for (int i = 0; i < layerStoneAudioControls.Count; i++)
            {
                var control = layerStoneAudioControls[i];
                if (control.layerIndex != previousLayer)
                {
                    if (layerStoneAudioLayerHeaders.TryGetValue(control.layerIndex, out string headerName))
                        Place(headerName, x, stoneRowsY, col, 30);
                    stoneRowsY += 36;
                    previousLayer = control.layerIndex;
                    previousAction = -1;
                }
                int actionKey = control.layerIndex * 2 + (control.breaking ? 1 : 0);
                if (actionKey != previousAction)
                {
                    if (layerStoneAudioActionHeaders.TryGetValue(actionKey, out string actionName))
                        Place(actionName, x, stoneRowsY, labelWidth - 8, 24);
                    stoneRowsY += 26;
                    previousAction = actionKey;
                }
                Place(control.labelName, x, stoneRowsY, labelWidth - 8, 40);
                Place(control.volume.name, x + labelWidth, stoneRowsY, settingWidth - 8, 40);
                Place(control.pitch.name, x + labelWidth + settingWidth, stoneRowsY, settingWidth - 8, 40);
                Place(control.pitchSpread.name, x + labelWidth + settingWidth * 2, stoneRowsY, settingWidth - 8, 40);
                stoneRowsY += 44;
            }
            float detailY = stoneRowsY + 20;
            Place("LayerStoneAudioBackdrop", x - 8, stoneSectionY - 8, col + 16,
                detailY - stoneSectionY + 16);
            Place("DetailSection",x,detailY,col,36);
            Place("DetailClipLabel",x,detailY+48,col,40);
            Place("DetailClipDropdown",x,detailY+92,col,50);
            Place("DetailVolumeLabel",x,detailY+154,col-300,48);
            Place("DetailVolumeInput",x+col-290,detailY+154,140,48);
            Place("DetailPreview",x+col-140,detailY+154,140,48);
            content.sizeDelta = new Vector2(0,detailY+220);
            return;
        }

        if (currentTab == DebugTab.World)
        {
            Place("LightingSection",x,12,col-170,36);
            Place("LightingInfo",x+78,18,27,27);
            Place("LightingEnabled",x+col-150,12,150,42);
            for (int i=0;i<6;i++)
            {
                Place("LightLabel"+i,x,68+i*58,col-170,48);
                Place("LightInput"+i,x+col-150,68+i*58,150,48);
            }
            content.sizeDelta = new Vector2(0,440);
            return;
        }

        Place("Section",x,12,col,36);
        Place("SpeedLabel",x,58,col-150,64); Place("DiggingSpeed",x+col-140,58,140,50);
        Place("DayNightRow",x,136,col,50);
        float buttonWidth = (col - 165 - 16) / 3f;
        for (int i = 0; dayNightButtons != null && i < dayNightButtons.Length; i++)
        {
            if (!dayNightButtons[i]) continue;
            dayNightButtons[i].anchoredPosition = new Vector2(165 + i * (buttonWidth + 8), 0);
            dayNightButtons[i].sizeDelta = new Vector2(buttonWidth, 42);
        }
        float footer = 204;
#if UNITY_EDITOR
        Place("KeepMap",x,footer,col,64);
        footer += 80;
#endif
        Place("Defaults",x,footer,270,62);
        Place("MakeDefaults",x+290,footer,col-290,62);
        footer += 78;
        Place("Status",x,footer,col,72);
        content.sizeDelta = new Vector2(0,footer+86);
    }
}

public sealed class GameplayWindowHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public GameplayDebugWindow owner;
    public Vector2 direction;
    public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) owner.Begin(e,direction); }
    public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) owner.Drag(e); }
}

public sealed class GameplayButtonTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public GameplayDebugWindow owner;
    public string message;
    public void OnPointerEnter(PointerEventData e) => owner.ShowTooltip(message, (RectTransform)transform);
    public void OnPointerMove(PointerEventData e) => owner.MoveTooltip((RectTransform)transform);
    public void OnPointerExit(PointerEventData e) => owner.HideTooltip();
}
