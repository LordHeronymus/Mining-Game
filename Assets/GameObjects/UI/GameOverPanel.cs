using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public sealed class GameOverPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public Sprite panelSprite;
    public TMP_FontAsset font;
    public Material fontMaterial;

    CanvasGroup group;
    RectTransform panel;
    TextMeshProUGUI depthValue;
    TextMeshProUGUI pointsValue;
    TextMeshProUGUI moneyValue;
    Button retryButton;
    StatsManager observedStats;
    Coroutine fade;

    static readonly Color ValueColor = new Color32(255, 199, 70, 255);
    static readonly Color ButtonTextColor = new Color32(255, 245, 224, 255);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    void Awake()
    {
        Build();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;
    }

    void OnEnable() => ObserveStats();

    void OnDisable()
    {
        if (observedStats) observedStats.OnHealthChanged -= OnHealthChanged;
        observedStats = null;
        if (IsOpen)
        {
            IsOpen = false;
            GameplayInputBlocker.SetBlocked(this, false);
            Time.timeScale = 1f;
        }
    }

    void Update()
    {
        if (observedStats != StatsManager.Instance) ObserveStats();
        if (IsOpen) RefreshValues();
    }

    void ObserveStats()
    {
        if (observedStats) observedStats.OnHealthChanged -= OnHealthChanged;
        observedStats = StatsManager.Instance;
        if (observedStats) observedStats.OnHealthChanged += OnHealthChanged;
    }

    void OnHealthChanged(float health, float maximum)
    {
        if (health <= 0f && !IsOpen) Show();
    }

    public void Show()
    {
        RefreshValues();

        IsOpen = true;
        transform.SetAsLastSibling();
        GameplayInputBlocker.SetBlocked(this, true);
        AudioManager.Instance?.SetLowHealthHeartbeat(false);
        Time.timeScale = 0f;

        group.blocksRaycasts = group.interactable = true;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadeIn());
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
    }

    void RefreshValues()
    {
        var stats = StatsManager.Instance;
        var hud = FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include);
        depthValue.text = (hud ? hud.DepthMeters : 0).ToString("N0", German) + " m";
        pointsValue.text = (stats ? stats.Points : 0).ToString("N0", German);
        moneyValue.text = (stats ? stats.Money : 0).ToString("N0", German);
    }

    IEnumerator FadeIn()
    {
        group.alpha = 0f;
        panel.localScale = Vector3.one * .94f;
        for (float t = 0f; t < .22f; t += Time.unscaledDeltaTime)
        {
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / .22f), 3f);
            group.alpha = p;
            panel.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, p);
            yield return null;
        }
        group.alpha = 1f;
        panel.localScale = Vector3.one;
        fade = null;
    }

    void Restart()
    {
        CloseForTransition();
        StatsManager.Instance?.ResetRun();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void ReturnToMainMenu()
    {
        CloseForTransition();
        const string mainMenuScene = "MainMenu";
        if (Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            SceneManager.LoadScene(mainMenuScene);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void CloseForTransition()
    {
        IsOpen = false;
        GameplayInputBlocker.SetBlocked(this, false);
        Time.timeScale = 1f;
        group.blocksRaycasts = group.interactable = false;
    }

    void Build()
    {
        var root = GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        group = GetComponent<CanvasGroup>();

        var shade = CreateRect("Background Shade", root, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        var shadeImage = shade.gameObject.AddComponent<Image>();
        shadeImage.color = new Color(0f, 0f, 0f, .58f);

        panel = CreateRect("Game Over Panel", root, Vector2.zero, new Vector2(1120f, 635f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = panelSprite;
        panelImage.preserveAspect = true;
        panelImage.raycastTarget = false;

        depthValue = AddText("Depth Value", panel, new Vector2(-270f, -101f), new Vector2(230f, 62f), 43f, ValueColor);
        pointsValue = AddText("Points Value", panel, new Vector2(0f, -101f), new Vector2(230f, 62f), 43f, ValueColor);
        moneyValue = AddText("Money Value", panel, new Vector2(298f, -101f), new Vector2(170f, 62f), 43f, ValueColor);

        retryButton = AddButton("Retry", panel, new Vector2(-207f, -211f), new Vector2(384f, 78f), "Nochmal versuchen", Restart);
        AddButton("Main Menu", panel, new Vector2(207f, -211f), new Vector2(360f, 78f), "Zum Hauptmenü", ReturnToMainMenu);
    }

    Button AddButton(string objectName, Transform parent, Vector2 position, Vector2 size, string label, UnityEngine.Events.UnityAction action)
    {
        var rect = CreateRect(objectName, parent, position, size);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.clear,
            highlightedColor = new Color(1f, .82f, .32f, .13f),
            pressedColor = new Color(1f, .72f, .22f, .25f),
            selectedColor = new Color(1f, .82f, .32f, .10f),
            disabledColor = Color.clear,
            colorMultiplier = 1f,
            fadeDuration = .08f
        };
        button.onClick.AddListener(action);

        var text = AddText("Label", rect, Vector2.zero, size, 34f, ButtonTextColor);
        text.text = label;
        text.raycastTarget = false;
        return button;
    }

    TextMeshProUGUI AddText(string objectName, Transform parent, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var rect = CreateRect(objectName, parent, position, size);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font ? font : TMP_Settings.defaultFontAsset;
        if (fontMaterial) text.fontSharedMaterial = fontMaterial;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.outlineColor = new Color32(45, 18, 8, 255);
        text.outlineWidth = .18f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    static RectTransform CreateRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
