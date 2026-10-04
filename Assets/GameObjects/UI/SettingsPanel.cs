using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public sealed class SettingsPanel : MonoBehaviour
{
    enum Tab { Audio, Keys, Display }
    static readonly Color Cream = HomeUi.Cream;
    static readonly Color Gold = new Color32(222, 174, 91, 255);
    static readonly Color Line = new Color32(148, 104, 49, 255);
    const float BoardWidth = 1480, BoardHeight = 840;
    const float PageWidth = 972, PageHeight = 568, RowHeight = 56;
    static readonly KeyCode[] KeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    readonly List<Button> tabButtons = new();
    readonly List<TextMeshProUGUI> bindingTexts = new();
    CanvasGroup group;
    RectTransform layout;
    GameObject audioPage, keyPage, displayPage;
    TMP_FontAsset font;
    GameAction? capturing;
    int captureSlot, captureFrame;
    float previousTimeScale = 1f;
    bool open;
    public bool IsOpen => open;
    Tab current = Tab.Keys;
    TextMeshProUGUI status;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneLoad()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Create();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include)) return;
        var canvas = GameObject.Find("ScreenCanvas");
        if (!canvas || !canvas.GetComponent<Canvas>()) return;
        var root = new GameObject("SettingsPanel", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        root.AddComponent<SettingsPanel>();
    }

    void Awake()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        group = GetComponent<CanvasGroup>();
        if (!group) group = gameObject.AddComponent<CanvasGroup>();
        Build();
        SetVisible(false);
    }

    void OnDisable()
    {
        if (open) Close();
    }

    void OnRectTransformDimensionsChange() => Fit();
    void Fit()
    {
        if (!layout) return;
        Vector2 size = ((RectTransform)transform).rect.size;
        float scale = Mathf.Min(size.x / 1640f, size.y / 960f);
        layout.localScale = Vector3.one * scale;
    }

    void Update()
    {
        if (capturing.HasValue) { CaptureKey(); return; }
        if (!open && (RunPauseMenu.InputConsumedFrame == Time.frameCount || RunPauseMenu.IsOpen || MainMenuController.IsVisible)) return;
        if (!GameBindings.Down(GameAction.Settings)) return;
        if (open) { Close(); return; }
        if (GameOverPanel.IsOpen || GameVictoryPanel.IsOpen) return;
        var inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var workbench = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var shop = FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        if ((inventory && inventory.IsOpen) || (workbench && workbench.IsOpen) ||
            (shop && shop.IsOpen) || GameplayDebugPanel.IsOpen || GameplayInputBlocker.IsBlocked) return;
        Open();
    }

    public void Open()
    {
        if (!Application.isPlaying || open) return;
        open = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        transform.SetAsLastSibling();
        SetVisible(true);
        GameplayInputBlocker.SetBlocked(this, true);
        InfoPanel.Instance?.ShowPanel(false);
        SetTab(current);
        RefreshBindingTexts();
    }

    void Close()
    {
        foreach (var pending in GetComponentsInChildren<SettingsSliderCommit>(true)) pending.Commit();
        capturing = null;
        open = false;
        SetVisible(false);
        GameplayInputBlocker.SetBlocked(this, false);
        Time.timeScale = previousTimeScale;
        InfoPanel.Instance?.ShowPanel(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    void SetVisible(bool value)
    {
        if (!group) return;
        group.alpha = value ? 1f : 0f;
        group.interactable = group.blocksRaycasts = value;
    }

    void Build()
    {
        var scrim = Image("Scrim", transform, 0, 0, 0, 0, null, new Color(0, 0, 0, .68f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
        layout = Rect("Layout", transform, 0, 0, BoardWidth, BoardHeight);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        layout.anchoredPosition = Vector2.zero;
        Image("Board Backing", layout, 30, 30, BoardWidth - 60, BoardHeight - 60, null, new Color32(23, 12, 7, 255));
        var background = Image("Background", layout, 0, 0, BoardWidth, BoardHeight,
            HomeUi.Sprite("Panel"), Color.white);
        background.raycastTarget = true;
        Label(layout, "Einstellungen", 80, 38, 1320, 76, 49, TextAlignmentOptions.Center);
        Image("Navigation Divider", layout, 376, 136, 2, 562, null, Line);
        string[] names = { "Audio", "Tastenbelegung", "Anzeige" };
        const float navigationTop = 114, navigationBottom = 700, tabSpacing = 100, tabHeight = 68;
        float firstTabY = (navigationTop + navigationBottom - tabHeight - (names.Length - 1) * tabSpacing) * .5f;
        for (int i = 0; i < names.Length; i++)
        {
            var tab = (Tab)i;
            tabButtons.Add(Button("Tab " + names[i], layout, names[i], 64, firstTabY + i * tabSpacing, 280, tabHeight,
                () => SetTab(tab)));
        }
        audioPage = Rect("Audio", layout, 424, 136, PageWidth, PageHeight).gameObject;
        keyPage = Rect("Tastenbelegung", layout, 424, 136, PageWidth, PageHeight).gameObject;
        displayPage = Rect("Anzeige", layout, 424, 136, PageWidth, PageHeight).gameObject;
        BuildAudio(); BuildKeys(); BuildDisplay();
        const float footerButtonWidth = 280, footerButtonGap = 668 - 360 - footerButtonWidth;
        float footerButtonsStart = keyPage.GetComponent<RectTransform>().anchoredPosition.x + 360 + (250 - footerButtonWidth) * .5f;
        status = Label(layout, "", 424, 706, footerButtonsStart - 424 - 24, 54, 20, TextAlignmentOptions.Left);
        status.color = Gold;
        Button("Standard", layout, "Standard", footerButtonsStart, 700,
            footerButtonWidth, 68, RestoreDefaults);
        Button("Fortsetzen", layout, "Fortsetzen", footerButtonsStart + footerButtonWidth + footerButtonGap, 700,
            footerButtonWidth, 68, Close, true);
        Fit(); SetTab(Tab.Keys);
    }

    void SetTab(Tab tab)
    {
        current = tab;
        if (!audioPage) return;
        audioPage.SetActive(tab == Tab.Audio);
        keyPage.SetActive(tab == Tab.Keys);
        displayPage.SetActive(tab == Tab.Display);
        for (int i = 0; i < tabButtons.Count; i++)
        {
            tabButtons[i].GetComponent<HomeButtonFeedback>().primary = i == (int)tab;
            tabButtons[i].GetComponent<HomeButtonFeedback>().selectionManaged = true;
        }
        if (status) status.text = "";
    }

    void BuildAudio()
    {
        const float rowSpacing = 100, rowHeight = 68;
        float navigationCenterY = (114 + 700) * .5f;
        float firstRowY = navigationCenterY + audioPage.GetComponent<RectTransform>().anchoredPosition.y - 1.5f * rowSpacing - rowHeight * .5f;
        AddSlider(audioPage.transform, "Gesamtlautstärke", firstRowY, () => PlayerSettings.Master,
            GpsSettings.PreviewMasterVolume, 0f, 1f, true, SaveMasterVolume);
        AddSlider(audioPage.transform, "Musik", firstRowY + rowSpacing, () => PlayerSettings.Music,
            GpsSettings.PreviewMusicVolume, 0f, 1f, true, SaveMusicVolume);
        AddSlider(audioPage.transform, "Ambiente", firstRowY + 2 * rowSpacing, () => PlayerSettings.Ambience,
            value => PlayerSettings.Ambience = value, 0f, 1f, true);
        AddSlider(audioPage.transform, "Effekte", firstRowY + 3 * rowSpacing, () => PlayerSettings.Sfx,
            value => PlayerSettings.Sfx = value, 0f, 1f, true);
    }

    void BuildDisplay()
    {
        const float rowSpacing = 148, rowHeight = 68;
        float firstRowY = (PageHeight - rowSpacing - rowHeight) * .5f;
        Label(displayPage.transform, "Vollbild", 16, firstRowY, 340, rowHeight, 28);
        var fullscreen = Button("Vollbild", displayPage.transform, PlayerSettings.Fullscreen ? "Ein" : "Aus",
            668, firstRowY, 250, rowHeight, null);
        fullscreen.onClick.AddListener(() =>
        {
            PlayerSettings.Fullscreen = !PlayerSettings.Fullscreen;
            fullscreen.GetComponentInChildren<TextMeshProUGUI>().text = PlayerSettings.Fullscreen ? "Ein" : "Aus";
        });
        AddSlider(displayPage.transform, "UI-Größe", firstRowY + rowSpacing, () => PlayerSettings.UiScale,
            value => PlayerSettings.UiScale = value, .8f, 1.2f, false);
    }

    void BuildKeys()
    {
        Label(keyPage.transform, "Aktion", 16, 0, 328, 42, 26);
        Label(keyPage.transform, "Taste 1", 360, 0, 250, 42, 26, TextAlignmentOptions.Center);
        Label(keyPage.transform, "Taste 2", 668, 0, 250, 42, 26, TextAlignmentOptions.Center);
        Image("HeaderLine", keyPage.transform, 0, 46, 936, 1, null, Line);
        var viewport = Rect("KeyViewport", keyPage.transform, 0, 54, 936, 504);
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 12);
        var content = Rect("KeyRows", viewport, 0, 0, 936, GameBindings.Entries.Length * RowHeight);
        int n = 0;
        foreach (var entry in GameBindings.Entries)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            if (entry.action == GameAction.DebugPanel) continue;
#endif
            float y = n++ * RowHeight;
            Image("Row", content, 0, y, 936, RowHeight, null, new Color(0, 0, 0, .12f));
            Image("Row Divider", content, 0, y + RowHeight - 1, 936, 1, null, new Color( .58f, .41f, .19f, .22f));
            Label(content, entry.label, 16, y + 6, 328, 44, 23);
            for (int slot = 0; slot < 2; slot++)
            {
                int capturedSlot = slot;
                var button = Button("Bind " + entry.action + " " + slot, content,
                    GameBindings.Display(GameBindings.Get(entry.action, slot)),
                    slot == 0 ? 360 : 668, y + 6, 250, 44,
                    () => BeginCapture(entry.action, capturedSlot));
                bindingTexts.Add(button.GetComponentInChildren<TextMeshProUGUI>());
            }
        }
        content.sizeDelta = new Vector2(936, n * RowHeight);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 38;
        var track = Image("Scrollbar", keyPage.transform, 952, 54, 6, 504, null, new Color(0, 0, 0, .35f));
        track.raycastTarget = true;
        var area = Rect("Sliding Area", track.transform, 0, 0, 6, 504);
        var handle = Image("Handle", area, 0, 0, 6, 504, null, Gold);
        handle.raycastTarget = true;
        Stretch(handle.rectTransform);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    void BeginCapture(GameAction action, int slot)
    {
        capturing = action; captureSlot = slot; captureFrame = Time.frameCount;
        RefreshBindingTexts();
    }

    void CaptureKey()
    {
        if (Time.frameCount <= captureFrame + 1) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { capturing = null; RefreshBindingTexts(); return; }
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            GameBindings.Set(capturing.Value, captureSlot, KeyCode.None);
            capturing = null; RefreshBindingTexts(); return;
        }
        foreach (var key in KeyCodes)
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Backspace || key == KeyCode.Delete) continue;
            if (!Input.GetKeyDown(key)) continue;
            var action = capturing.Value;
            GameBindings.Set(action, captureSlot, key);
            capturing = null;
            RefreshBindingTexts();
            foreach (var entry in GameBindings.Entries)
                if (entry.action != action && (GameBindings.Get(entry.action, 0) == key || GameBindings.Get(entry.action, 1) == key))
                { status.text = "Auch belegt: " + entry.label; break; }
            return;
        }
    }

    void RefreshBindingTexts()
    {
        int index = 0;
        foreach (var entry in GameBindings.Entries)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            if (entry.action == GameAction.DebugPanel) continue;
#endif
            for (int slot = 0; slot < 2; slot++)
            {
                var text = bindingTexts[index++];
                text.text = capturing == entry.action && captureSlot == slot ? "…" :
                    GameBindings.Display(GameBindings.Get(entry.action, slot));
                text.color = capturing == entry.action && captureSlot == slot ? Gold : Cream;
            }
        }
    }

    void RestoreDefaults()
    {
        PlayerSettings.RestoreDefaults();
        RefreshBindingTexts();
        foreach (var slider in audioPage.GetComponentsInChildren<Slider>(true)) slider.value = slider.name switch
        {
            "Gesamtlautstärke" => PlayerSettings.Master,
            "Musik" => PlayerSettings.Music,
            "Ambiente" => PlayerSettings.Ambience,
            _ => PlayerSettings.Sfx
        };
        foreach (var slider in displayPage.GetComponentsInChildren<Slider>(true)) slider.value = PlayerSettings.UiScale;
        var fullscreen = displayPage.transform.Find("Vollbild");
        if (fullscreen) fullscreen.GetComponentInChildren<TextMeshProUGUI>().text = PlayerSettings.Fullscreen ? "Ein" : "Aus";
        status.text = "";
    }

    void AddSlider(Transform parent, string title, float y, Func<float> get, Action<float> set,
        float min, float max, bool percent, Func<bool> commit = null)
    {
        Label(parent, title, 16, y, 340, 68, 28);
        var root = Rect(title, parent, 360, y + 12, 450, 44);
        var hitArea = Image("Hit Area", root, 0, 0, 450, 44, null, Color.clear);
        hitArea.raycastTarget = true;
        var rail = Image("Rail", root, 0, 13, 450, 18, HomeUi.Sprite("SaveButton"), Color.white);
        rail.pixelsPerUnitMultiplier = rail.sprite.rect.height / 18f;
        var fillArea = Rect("Fill Area", root, 11, 20, 428, 4);
        var fill = Image("Fill", fillArea, 0, 0, 428, 4, null, Gold);
        Stretch(fill.rectTransform);
        var handleArea = Rect("Handle Area", root, 11, 0, 428, 44);
        var handle = Image("Handle", handleArea, 0, 0, 22, 32, HomeUi.Sprite("SaveHandle"), Color.white);
        handle.rectTransform.pivot = new Vector2(.5f, .5f);
        // Slider stretches the handle vertically across its movement area.
        handle.rectTransform.sizeDelta = new Vector2(22, -12);
        handle.raycastTarget = rail.raycastTarget = true;
        var slider = root.gameObject.AddComponent<Slider>();
        slider.minValue = min; slider.maxValue = max;
        slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        SettingsSliderCommit pending = null;
        if (commit != null)
        {
            pending = root.gameObject.AddComponent<SettingsSliderCommit>();
            pending.Save = commit;
        }
        var number = Label(parent, "", 834, y, 84, 68, 26, TextAlignmentOptions.Right);
        slider.onValueChanged.AddListener(value =>
        {
            set(value);
            if (pending) pending.MarkChanged();
            number.text = percent ? Mathf.RoundToInt(value * 100) + " %" : Mathf.RoundToInt(value * 100) + " %";
        });
        slider.SetValueWithoutNotify(get());
        number.text = Mathf.RoundToInt(get() * 100) + " %";
    }
    bool SaveMusicVolume()
    {
        bool saved = GpsSettings.SavePreference("musicVolume", out string error);
        if (!saved && status) status.text = error;
        return saved;
    }

    bool SaveMasterVolume()
    {
        bool saved = GpsSettings.SavePreference("masterVolume", out string error);
        if (!saved && status) status.text = error;
        return saved;
    }

    RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    Image Image(string name, Transform parent, float x, float y, float w, float h, Sprite sprite, Color color)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.type = sprite && sprite.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        image.color = color; image.raycastTarget = false;
        HomeUi.StylePanelWood(image);
        return image;
    }

    TextMeshProUGUI Label(Transform parent, string caption, float x, float y, float w, float h,
        float size, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var label = HomeUi.Label("Label", parent, caption, Vector2.zero, new Vector2(w, h), size);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
        label.rectTransform.anchoredPosition = new Vector2(x, -y);
        label.font = font; label.color = Cream; label.fontStyle = FontStyles.Normal;
        label.fontSize = size; label.enableAutoSizing = true;
        label.fontSizeMin = size * .65f; label.fontSizeMax = size;
        label.alignment = alignment; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    Button Button(string name, Transform parent, string caption, float x, float y, float w, float h,
        Action action, bool primary = false)
    {
        var button = HomeUi.Button(name, parent, caption, Vector2.zero, new Vector2(w, h), action, primary);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        HomeUi.StyleSaveButton(button, h <= 48 ? 22 : 23);
        return button;
    }
}

public sealed class SettingsSliderCommit : MonoBehaviour, IPointerUpHandler, IEndDragHandler, IDeselectHandler
{
    public Func<bool> Save;
    bool pending;
    public void MarkChanged() => pending = true;
    public void Commit()
    {
        if (pending && Save != null && Save()) pending = false;
    }
    public void OnPointerUp(PointerEventData data) => Commit();
    public void OnEndDrag(PointerEventData data) => Commit();
    public void OnDeselect(BaseEventData data) => Commit();
    void OnDisable() => Commit();
    void OnApplicationQuit() => Commit();
}
