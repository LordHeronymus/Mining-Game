using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A canvas window: pointer coordinates are converted into canvas units, so dragging
// and resizing work independently of resolution and CanvasScaler settings.
public sealed class GameplayDebugWindow : MonoBehaviour
{
    RectTransform window, bounds, content, tooltip;
    TextMeshProUGUI tooltipLabel;
    ScrollRect scroll;
    readonly Dictionary<string, RectTransform> items = new Dictionary<string, RectTransform>();
    Vector2 lastSize, lastBounds, startPointer, startPosition, startSize;
    Vector2 resizeDirection;
    readonly List<string> gameplayItems = new List<string>();
    readonly List<string> testItems = new List<string>();
    TMP_InputField testMultiplier;
    TMP_Dropdown giftItem;
    TMP_InputField giftAmount;
    Button giftButton;
    ItemSO[] giftItems = System.Array.Empty<ItemSO>();
    static int lastGiftItem = -1;
    static string lastGiftAmount = "10";
    TextMeshProUGUI testStatus;
    readonly Dictionary<GameplayTestMode, Toggle> modeToggles = new Dictionary<GameplayTestMode, Toggle>();
    readonly RectTransform[] dayNightButtons = new RectTransform[3];
    readonly Image[] dayNightBackgrounds = new Image[3];
    static readonly (string label, AudioVolumeSetting setting)[] AudioSettings =
    {
        ("Gesamt-Ambience (%)", AudioVolumeSetting.Ambience),
        ("Oberfläche (%)", AudioVolumeSetting.Surface),
        ("Untergrund (%)", AudioVolumeSetting.Underground),
        ("Höhle (%)", AudioVolumeSetting.Cave),
        ("Abbausounds (%)", AudioVolumeSetting.DigSounds),
    };
    readonly TMP_InputField[] audioInputs = new TMP_InputField[AudioSettings.Length];
    FirstLayerAmbience detailAmbience;
    TMP_Dropdown detailClipDropdown;
    TMP_InputField detailVolumeInput;
    Button detailPreviewButton;
    int detailClipCount;
    public bool IsTestTab { get; private set; }

    void Awake()
    {
        window = (RectTransform)transform.Find("Card");
        bounds = (RectTransform)transform;
        var backdrop = GetComponent<Image>();
        if (backdrop) { backdrop.color = Color.clear; backdrop.raycastTarget = false; }
        foreach (RectTransform child in window) items[child.name] = child;

        var viewport = MakeRect("WindowViewport", window);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(18, 22); viewport.offsetMax = new Vector2(-18, -152);
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
        track.offsetMin = new Vector2(-15,26); track.offsetMax = new Vector2(-7,-154);
        var trackImage = track.gameObject.AddComponent<Image>(); trackImage.color = new Color(.12f,.16f,.21f);
        var thumb = MakeRect("Thumb", track);
        thumb.anchorMin = Vector2.zero; thumb.anchorMax = Vector2.one; thumb.sizeDelta = Vector2.zero;
        var thumbImage = thumb.gameObject.AddComponent<Image>(); thumbImage.color = new Color(.45f,.52f,.6f);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = thumb; bar.targetGraphic = thumbImage; bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var titleHandle = Handle("TitleDrag", new Vector2(0, 1), Vector2.one, Vector2.zero);
        titleHandle.offsetMin = new Vector2(12, -88); titleHandle.offsetMax = new Vector2(-90, -8);
        items["Title"].GetComponent<TextMeshProUGUI>().raycastTarget = false;
        items["Close"].SetAsLastSibling();
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                Vector2 min = new Vector2(x < 0 ? 0 : x > 0 ? 1 : 0, y < 0 ? 0 : y > 0 ? 1 : 0);
                Vector2 max = new Vector2(x == 0 ? 1 : min.x, y == 0 ? 1 : min.y);
                var handle = Handle("Resize " + x + " " + y, min, max, new Vector2(x, y));
                handle.sizeDelta = new Vector2(x == 0 ? -36 : 16, y == 0 ? -36 : 16);
                handle.anchoredPosition = Vector2.zero;
            }
        for (int i = 0; i < 3; i++)
        {
            var line = MakeRect("ResizeGrip", window);
            line.anchorMin = line.anchorMax = Vector2.right;
            line.anchoredPosition = new Vector2(-12-i*5, 12+i*5);
            line.sizeDelta = new Vector2(17-i*5, 2); line.localRotation = Quaternion.Euler(0,0,45);
            var image = line.gameObject.AddComponent<Image>(); image.color = new Color(.7f,.75f,.8f); image.raycastTarget = false;
        }
        CreateTooltip();
        AttachTooltip("MakeDefaults", "Übernimmt beide Sektionen nach dem Play-Stopp dauerhaft in die Gameplay Settings.");
        CreateLightingInfo();
        CreateItemGifting();
        CreateAudioSettings();
        CreateLayer1DetailSettings();
        foreach (var entry in items) if (entry.Value.parent == content) gameplayItems.Add(entry.Key);
        CreateTabs();
        window.sizeDelta += new Vector2(0,56);
        SetTab(false);
        ClampWindow(); Layout();
    }

    RectTransform CloneItem(string source, string name, Transform parent, string label = null)
    {
        var rect = Instantiate(items[source], parent);
        rect.name = name; items[name] = rect;
        if (label != null) rect.GetComponentInChildren<TextMeshProUGUI>().text = label;
        var trigger = rect.GetComponent<GameplayButtonTooltip>();
        if (trigger) Destroy(trigger);
        return rect;
    }

    void CreateItemGifting()
    {
        CloneItem("Section", "ItemsSection", content, "ITEMS");
        var dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObject.name = "GiftItem";
        dropdownObject.transform.SetParent(content, false);
        items["GiftItem"] = (RectTransform)dropdownObject.transform;
        giftItem = dropdownObject.GetComponent<TMP_Dropdown>();
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
        giftItem.template.GetComponent<Image>().color = new Color(.2f, .25f, .32f);
        var optionToggle = giftItem.itemText.GetComponentInParent<Toggle>(true);
        optionToggle.targetGraphic.color = new Color(.3f, .37f, .45f);
        ((RectTransform)optionToggle.transform).sizeDelta = new Vector2(0, 36);
        giftItem.ClearOptions();
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (catalog) giftItems = System.Array.FindAll(catalog.items, item => item);
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (var item in giftItems)
            options.Add(new TMP_Dropdown.OptionData(string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName));
        giftItem.AddOptions(options);
        int selected = System.Array.FindIndex(giftItems, item => (int)item.item == lastGiftItem);
        giftItem.SetValueWithoutNotify(Mathf.Max(0, selected));
        giftItem.onValueChanged.AddListener(i => lastGiftItem = (int)giftItems[i].item);
        giftAmount = CloneItem("DiggingSpeed", "GiftAmount", content).GetComponent<TMP_InputField>();
        giftAmount.onEndEdit = new TMP_InputField.SubmitEvent();
        giftAmount.onValueChanged = new TMP_InputField.OnChangeEvent();
        giftAmount.contentType = TMP_InputField.ContentType.IntegerNumber;
        giftAmount.characterLimit = 10;
        DisableInputChildRaycasts(giftAmount);
        giftAmount.SetTextWithoutNotify(lastGiftAmount);
        giftAmount.onValueChanged.AddListener(value => lastGiftAmount = value);
        giftButton = CloneItem("Defaults", "GiftAdd", content, "Hinzufügen").GetComponent<Button>();
        giftButton.onClick = new Button.ButtonClickedEvent();
        giftButton.onClick.AddListener(GiveItem);
        RefreshGifting();
    }

    void CreateAudioSettings()
    {
        CloneItem("Section", "AudioSection", content, "SOUND");
        for (int i = 0; i < AudioSettings.Length; i++)
        {
            CloneItem("SpeedLabel", "AudioLabel" + i, content, AudioSettings[i].label);
            var input = CloneItem("DiggingSpeed", "AudioInput" + i, content).GetComponent<TMP_InputField>();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            DisableInputChildRaycasts(input);
            int index = i;
            input.onEndEdit.AddListener(_ => ApplyAudioInput(index));
            audioInputs[i] = input;
        }
        RefreshAudioSettings();
    }

    void ApplyAudioInput(int index)
    {
        var audio = AudioManager.Instance;
        if (!audio || !float.TryParse(audioInputs[index].text.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float percent) || percent < 0f || percent > 100f)
        {
            items["Status"].GetComponent<TextMeshProUGUI>().text = "Soundlautstärke: bitte 0 bis 100 eingeben.";
            RefreshAudioSettings();
            return;
        }
        audio.SetVolume(AudioSettings[index].setting, percent / 100f);
        items["Status"].GetComponent<TextMeshProUGUI>().text = "";
        RefreshAudioSettings();
    }

    void RefreshAudioSettings()
    {
        var audio = AudioManager.Instance;
        for (int i = 0; i < audioInputs.Length; i++)
        {
            audioInputs[i].interactable = audio;
            if (audio) audioInputs[i].SetTextWithoutNotify((audio.GetVolume(AudioSettings[i].setting) * 100f)
                .ToString("0.##", CultureInfo.InvariantCulture));
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
        detailClipDropdown = CloneItem("GiftItem", "DetailClipDropdown", content).GetComponent<TMP_Dropdown>();
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
        return true;
    }

    void PreviewDetailClip()
    {
        if (ApplyDetailVolume()) detailAmbience.PlayDetailPreview(detailClipDropdown.value);
    }

    bool CanGiveItem(out int amount)
    {
        amount = 0;
        var inventory = InventoryManager.Instance;
        return inventory && giftItems.Length > 0 && giftItem.value < giftItems.Length &&
            int.TryParse(giftAmount.text, out amount) && amount > 0 &&
            inventory.GetCount(giftItems[giftItem.value]) <= int.MaxValue - amount;
    }

    void GiveItem()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!CanGiveItem(out int amount)) return;
        var item = giftItems[giftItem.value];
        lastGiftItem = (int)item.item;
        InventoryManager.Instance.Add(item, amount);
        string itemName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
        Debug.Log($"[Debug] {amount} × {itemName} zum Inventar hinzugefügt.", this);
        RefreshGifting();
#endif
    }

    void RefreshGifting()
    {
        bool available = InventoryManager.Instance && giftItems.Length > 0;
        giftItem.interactable = available;
        giftAmount.interactable = available;
        giftButton.interactable = CanGiveItem(out _);
        if ((!GameplayDebugPanel.IsOpen || IsTestTab) && giftItem.IsExpanded) giftItem.Hide();
    }

    void CreateTabs()
    {
        var gameplay = CloneItem("Defaults", "GameplayTab", window, "Gameplay");
        var tests = CloneItem("Defaults", "TestsTab", window, "Testeinstellungen");
        gameplay.GetComponent<Button>().onClick.AddListener(() => SwitchTab(false));
        tests.GetComponent<Button>().onClick.AddListener(() => SwitchTab(true));
        CloneItem("Section", "TestSection", content, "TESTEINSTELLUNGEN");
        CloneItem("SpeedLabel", "TestLabel", content, "Abbau-Testfaktor (×)");
        testMultiplier = CloneItem("DiggingSpeed", "TestMultiplier", content).GetComponent<TMP_InputField>();
        testMultiplier.contentType = TMP_InputField.ContentType.DecimalNumber;
        DisableInputChildRaycasts(testMultiplier);
        testMultiplier.onEndEdit.AddListener(_ => ApplyTestInput());
        testStatus = CloneItem("Status", "TestStatus", content, "").GetComponent<TextMeshProUGUI>();
        CreateModeToggle("TestActive", "Testmodus aktiv", GameplayTestMode.Active, null);
#if UNITY_EDITOR
        CreateModeToggle("KeepMap", "Map nach Play-Stopp im Editor behalten", GameplayTestMode.KeepMap, null);
        testItems.Remove("KeepMap");
        gameplayItems.Add("KeepMap");
#endif
        CreateModeToggle("GodMode", "God Mode", GameplayTestMode.God,
            "Unverwundbarkeit für spätere Schadensmechaniken. Aktuell besitzt das Spiel noch kein Schadenssystem.");
        CreateModeToggle("NoEnergy", "Kein Energieverbrauch", GameplayTestMode.NoEnergyConsume,
            "Verhindert Energieverbrauch im Stand, beim Bewegen und beim Abbauen.");
        CreateModeToggle("FlyMode", "Fly Mode", GameplayTestMode.Fly,
            "Gravitation aus. W/S: aufwärts/abwärts. A/D: seitwärts. Ohne Taste schweben. Kollisionen bleiben aktiv.");
        CreateDayNightSelector();
        testItems.AddRange(new[] {"TestSection","TestLabel","TestMultiplier","TestStatus"});
    }

    void CreateDayNightSelector()
    {
        var row = MakeRect("DayNightRow", content);
        items[row.name] = row;
        testItems.Add(row.name);
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
            button.onClick.AddListener(() => {
                bool saved = GameplayTestSettings.SetDayNightMode(selected, out string error);
                RefreshDayNight();
                testStatus.text = saved ? "" : error;
            });
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
    {
        if (tests == IsTestTab) return;
        if (IsTestTab) { if (!ApplyTestInput()) return; }
        else if (!GetComponent<GameplayDebugPanel>().TryApplyAll()) return;
        SetTab(tests);
    }

    void SetTab(bool tests)
    {
        if (giftItem && giftItem.IsExpanded) giftItem.Hide();
        HideTooltip(); IsTestTab = tests;
        foreach (var name in gameplayItems) items[name].gameObject.SetActive(!tests);
        foreach (var name in testItems) items[name].gameObject.SetActive(tests);
#if !UNITY_EDITOR
        items["MakeDefaults"].gameObject.SetActive(false);
#endif
        items["GameplayTab"].GetComponent<Image>().color = tests ? new Color(.2f,.25f,.32f) : new Color(.55f,.35f,.12f);
        items["TestsTab"].GetComponent<Image>().color = tests ? new Color(.55f,.35f,.12f) : new Color(.2f,.25f,.32f);
        RefreshTest(); Layout(); scroll.verticalNormalizedPosition = 1;
        if (!tests) { RefreshAudioSettings(); RefreshDetailSettings(); }
    }

    void RefreshTest()
    {
        foreach (var entry in modeToggles) entry.Value.SetIsOnWithoutNotify(GameplayTestSettings.GetConfiguredMode(entry.Key));
        float factor = GameplayTestSettings.ConfiguredDiggingMultiplier;
        testMultiplier.SetTextWithoutNotify(factor.ToString("R", CultureInfo.InvariantCulture));
        testStatus.text = GameplayTestSettings.Warning ?? "";
        RefreshDayNight();
    }

    void RefreshDayNight()
    {
        var selected = GameplayTestSettings.ConfiguredDayNightMode;
        for (int i = 0; i < dayNightBackgrounds.Length; i++)
            dayNightBackgrounds[i].color = (int)selected == i
                ? new Color(.55f, .35f, .12f) : new Color(.2f, .25f, .32f);
    }

    static void DisableInputChildRaycasts(TMP_InputField input)
    {
        foreach (var graphic in input.GetComponentsInChildren<Graphic>(true))
            if (graphic != input.targetGraphic) graphic.raycastTarget = false;
    }

    public bool ApplyTestInput()
    {
        if (!float.TryParse(testMultiplier.text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
            !GameplayTestSettings.SetDiggingMultiplier(value))
        { testStatus.text = "Bitte einen Testfaktor von 0,1 bis 100 eingeben."; return false; }
        if (GameplayTestSettings.HasUnsavedChanges && !GameplayTestSettings.Save(out string error))
        { testStatus.text = error; return false; }
        RefreshTest(); return true;
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
        if (lastSize != window.sizeDelta || lastBounds != bounds.rect.size) { ClampWindow(); Layout(); }
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
        lastSize = window.sizeDelta; lastBounds = bounds.rect.size;
        float w = lastSize.x;
        Place("Title", 28, 20, w-130, 58); Place("Close", w-78, 20, 56, 56);
        Place("Accent", 0, 0, w, 5);
        Place("GameplayTab",28,88,220,44); Place("TestsTab",260,88,280,44);
        float inner = w-72;
        bool columns = inner >= 950;
        float col = columns ? (inner-32)/2 : inner;
        float x = 18;
        items["Hint"].gameObject.SetActive(false);
        items["SpeedHint"].gameObject.SetActive(false);
        items["LightingHint"].gameObject.SetActive(false);
        items["Save"].gameObject.SetActive(false);
        if (IsTestTab)
        {
            Place("TestSection",x,12,inner,36);
            Place("TestActive",x,60,inner,44);
            Place("TestLabel",x,122,inner-170,50); Place("TestMultiplier",x+inner-150,122,150,50);
            Place("GodMode",x,202,inner,44); Place("NoEnergy",x,256,inner,44); Place("FlyMode",x,310,inner,44);
            Place("DayNightRow",x,370,inner,50);
            float buttonWidth = (inner - 165 - 16) / 3f;
            for (int i = 0; i < dayNightButtons.Length; i++)
            {
                dayNightButtons[i].anchoredPosition = new Vector2(165 + i * (buttonWidth + 8), 0);
                dayNightButtons[i].sizeDelta = new Vector2(buttonWidth, 42);
            }
            Place("TestStatus",x,444,inner,80); content.sizeDelta = new Vector2(0,540);
            return;
        }
        Place("Section",x,12,col,36);
        Place("SpeedLabel",x,58,col-150,64); Place("DiggingSpeed",x+col-140,58,140,50);
        items["SpeedHint"].gameObject.SetActive(false);
        Place("ItemsSection",x,140,col,36);
        Place("GiftItem",x,190,col-270,50);
        Place("GiftAmount",x+col-260,190,90,50);
        Place("GiftAdd",x+col-160,190,160,50);
        float soundY = 280;
        Place("AudioSection",x,soundY,col,36);
        for (int i = 0; i < AudioSettings.Length; i++)
        {
            float y = soundY + 48 + i * 58;
            Place("AudioLabel" + i,x,y,col-150,48);
            Place("AudioInput" + i,x+col-140,y,140,48);
        }
        float detailY = soundY + 48 + AudioSettings.Length * 58 + 20;
        Place("DetailSection",x,detailY,col,36);
        Place("DetailClipLabel",x,detailY+48,col,40);
        Place("DetailClipDropdown",x,detailY+92,col,50);
        Place("DetailVolumeLabel",x,detailY+154,col-300,48);
        Place("DetailVolumeInput",x+col-290,detailY+154,140,48);
        Place("DetailPreview",x+col-140,detailY+154,140,48);
        float detailEnd = detailY + 212;
        float lx = columns ? x+col+32 : x, ly = columns ? 12 : detailEnd + 20;
        Place("LightingSection",lx,ly,col-170,36);
        Place("LightingInfo",lx+78,ly+6,27,27);
        Place("LightingEnabled",lx+col-150,ly,150,42);
        for (int i=0;i<6;i++)
        {
            Place("LightLabel"+i,lx,ly+56+i*58,col-170,48);
            Place("LightInput"+i,lx+col-150,ly+56+i*58,150,48);
        }
        items["LightingHint"].gameObject.SetActive(false);
        float footer = Mathf.Max(ly+420, detailEnd + 20);
#if UNITY_EDITOR
        Place("KeepMap",x,footer,inner,64);
        footer += 80;
#endif
        if (columns)
        {
            Place("Defaults",x,footer,270,62);
            Place("MakeDefaults",x+290,footer,inner-290,62);
            footer += 78;
        }
        else
        {
            Place("Defaults",x,footer,inner,62);
            Place("MakeDefaults",x,footer+76,inner,62); footer += 154;
        }
        Place("Status",x,footer,inner,72);
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
