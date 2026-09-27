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
    static readonly Color Cream = new Color32(255, 245, 229, 255);
    static readonly Color Dim = new Color32(78, 48, 34, 255);
    static readonly Color Orange = new Color32(193, 63, 10, 255);
    static readonly Color Gold = new Color32(245, 166, 60, 255);
    static readonly Color Line = new Color32(138, 91, 61, 255);
    static readonly KeyCode[] KeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    readonly List<Button> tabButtons = new();
    readonly List<TextMeshProUGUI> bindingTexts = new();
    CanvasGroup group;
    RectTransform layout;
    GameObject audioPage, keyPage, displayPage;
    TMP_FontAsset font;
    Material fontMaterial;
    Sprite rowSprite, selectedSprite, actionSprite;
    GameAction? capturing;
    int captureSlot, captureFrame;
    float previousTimeScale = 1f;
    bool open;
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
        var workbench = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (workbench)
        {
            font = workbench.font; fontMaterial = workbench.fontMaterial;
            rowSprite = workbench.rowSprite; selectedSprite = workbench.selectedRowSprite;
            actionSprite = workbench.actionSprite;
        }
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

    void Open()
    {
        open = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        transform.SetAsLastSibling();
        SetVisible(true);
        GameplayInputBlocker.SetBlocked(this, true);
        InfoPanel.Instance?.ShowPanel(false);
        SetTab(current);
    }

    void Close()
    {
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
        var scrim = Image("Scrim", transform, 0, 0, 0, 0, null, new Color(0, 0, 0, .25f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
        layout = Rect("Layout", transform, 0, 0, 1640, 960);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        layout.anchoredPosition = Vector2.zero;
        var background = Image("Background", layout, 0, 0, 1640, 960,
            Resources.Load<Sprite>("Settings/SettingsPanelCutout"), Color.white);
        background.raycastTarget = true;
        Label(layout, "Einstellungen", 560, 42, 520, 75, 50, TextAlignmentOptions.Center);
        Button("Schließen", layout, "×", 1485, 116, 58, 58, Close, true);
        string[] names = { "Audio", "Tastenbelegung", "Anzeige" };
        for (int i = 0; i < names.Length; i++)
        {
            var tab = (Tab)i;
            tabButtons.Add(Button("Tab " + names[i], layout, names[i], 190 + i * 420, 205, 400, 56,
                () => SetTab(tab)));
        }
        audioPage = Rect("Audio", layout, 0, 0, 1640, 960).gameObject;
        keyPage = Rect("Tastenbelegung", layout, 0, 0, 1640, 960).gameObject;
        displayPage = Rect("Anzeige", layout, 0, 0, 1640, 960).gameObject;
        BuildAudio(); BuildKeys(); BuildDisplay();
        status = Label(layout, "", 560, 795, 520, 48, 20, TextAlignmentOptions.Center);
        status.color = new Color32(255, 191, 98, 255);
        Button("Standard", layout, "Standard", 190, 795, 310, 56, RestoreDefaults);
        Button("Fortsetzen", layout, "Fortsetzen", 1140, 795, 310, 56, Close, true);
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
            var image = tabButtons[i].GetComponent<Image>();
            image.sprite = i == (int)tab ? actionSprite : rowSprite;
            image.color = Color.white;
        }
        if (status) status.text = "";
    }

    void BuildAudio()
    {
        AddSlider(audioPage.transform, "Gesamtlautstärke", 336, () => PlayerSettings.Master,
            value => PlayerSettings.Master = value, 0f, 1f, true);
        AddSlider(audioPage.transform, "Ambiente", 446, () => PlayerSettings.Ambience,
            value => PlayerSettings.Ambience = value, 0f, 1f, true);
        AddSlider(audioPage.transform, "Effekte", 556, () => PlayerSettings.Sfx,
            value => PlayerSettings.Sfx = value, 0f, 1f, true);
    }

    void BuildDisplay()
    {
        Label(displayPage.transform, "Vollbild", 245, 365, 570, 54, 28);
        var fullscreen = Button("Vollbild", displayPage.transform, PlayerSettings.Fullscreen ? "Ein" : "Aus",
            1040, 365, 330, 54, null);
        fullscreen.onClick.AddListener(() =>
        {
            PlayerSettings.Fullscreen = !PlayerSettings.Fullscreen;
            fullscreen.GetComponentInChildren<TextMeshProUGUI>().text = PlayerSettings.Fullscreen ? "Ein" : "Aus";
        });
        AddSlider(displayPage.transform, "UI-Größe", 490, () => PlayerSettings.UiScale,
            value => PlayerSettings.UiScale = value, .8f, 1.2f, false);
    }

    void BuildKeys()
    {
        Label(keyPage.transform, "Aktion", 180, 274, 440, 38, 26);
        Label(keyPage.transform, "Taste 1", 670, 274, 355, 38, 26, TextAlignmentOptions.Center);
        Label(keyPage.transform, "Taste 2", 1110, 274, 355, 38, 26, TextAlignmentOptions.Center);
        Image("HeaderLine", keyPage.transform, 180, 315, 1300, 2, null, Line);
        var viewport = Rect("KeyViewport", keyPage.transform, 180, 323, 1300, 450);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("KeyRows", viewport, 0, 0, 1300, GameBindings.Entries.Length * 50);
        int n = 0;
        foreach (var entry in GameBindings.Entries)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            if (entry.action == GameAction.DebugPanel) continue;
#endif
            int y = n++ * 50;
            Image("Row", content, 0, y, 1300, 48, null,
                n % 2 == 0 ? new Color32(44, 25, 17, 188) : new Color32(53, 30, 19, 188));
            Label(content, entry.label, 16, y + 4, 450, 42, 23);
            for (int slot = 0; slot < 2; slot++)
            {
                int capturedSlot = slot;
                var button = Button("Bind " + entry.action + " " + slot, content,
                    GameBindings.Display(GameBindings.Get(entry.action, slot)),
                    slot == 0 ? 490 : 930, y + 5, 355, 40,
                    () => BeginCapture(entry.action, capturedSlot));
                bindingTexts.Add(button.GetComponentInChildren<TextMeshProUGUI>());
            }
        }
        content.sizeDelta = new Vector2(1300, n * 50);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 38;
        var track = Image("Scrollbar", keyPage.transform, 1495, 323, 17, 450, rowSprite, Color.white);
        track.raycastTarget = true;
        var area = Rect("Sliding Area", track.transform, 3, 3, 11, 444);
        var handle = Image("Handle", area, 0, 0, 11, 444, selectedSprite, Color.white);
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
            "Ambiente" => PlayerSettings.Ambience,
            _ => PlayerSettings.Sfx
        };
        foreach (var slider in displayPage.GetComponentsInChildren<Slider>(true)) slider.value = PlayerSettings.UiScale;
        var fullscreen = displayPage.transform.Find("Vollbild");
        if (fullscreen) fullscreen.GetComponentInChildren<TextMeshProUGUI>().text = PlayerSettings.Fullscreen ? "Ein" : "Aus";
        status.text = "";
    }

    void AddSlider(Transform parent, string title, float y, Func<float> get, Action<float> set,
        float min, float max, bool percent)
    {
        Label(parent, title, 245, y, 570, 54, 28);
        var rail = Image(title, parent, 820, y + 15, 440, 24, rowSprite, Dim);
        var fill = Image("Fill", rail.transform, 4, 4, 428, 16, null, Orange);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(1, 1);
        fill.rectTransform.offsetMin = new Vector2(4, 4); fill.rectTransform.offsetMax = new Vector2(-4, -4);
        var handle = Image("Handle", rail.transform, 0, -7, 28, 38, selectedSprite, Gold);
        var slider = rail.gameObject.AddComponent<Slider>();
        slider.minValue = min; slider.maxValue = max;
        slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        var number = Label(parent, "", 1280, y, 150, 54, 26, TextAlignmentOptions.Right);
        slider.onValueChanged.AddListener(value =>
        {
            set(value);
            number.text = percent ? Mathf.RoundToInt(value * 100) + " %" : Mathf.RoundToInt(value * 100) + " %";
        });
        slider.value = get();
        number.text = Mathf.RoundToInt(get() * 100) + " %";
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
        return image;
    }

    TextMeshProUGUI Label(Transform parent, string caption, float x, float y, float w, float h,
        float size, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var label = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        if (fontMaterial) label.fontSharedMaterial = fontMaterial;
        label.text = caption; label.color = Cream; label.fontStyle = FontStyles.Bold;
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
        var image = Image(name, parent, x, y, w, h, primary ? actionSprite : rowSprite, Color.white);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (action != null) button.onClick.AddListener(() => action());
        Label(image.transform, caption, 8, 0, w - 16, h, h <= 48 ? 22 : 27, TextAlignmentOptions.Center);
        return button;
    }
}
