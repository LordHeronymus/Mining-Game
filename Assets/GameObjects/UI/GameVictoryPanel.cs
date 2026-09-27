using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class GameVictoryPanel : MonoBehaviour
{
    static GameVictoryPanel current;
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Color ValueColor = new Color32(255, 207, 103, 255);
    static readonly Color ButtonTextColor = new Color32(255, 245, 224, 255);

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        current = null;
        IsOpen = false;
    }

    CanvasGroup group;
    RectTransform panel;
    TextMeshProUGUI depthValue;
    TextMeshProUGUI pointsValue;
    TextMeshProUGUI moneyValue;
    RectTransform moneyCoin;
    Button retryButton;
    TMP_FontAsset font;
    Material fontMaterial;

    public static void Show()
    {
        if (current || GameOverPanel.IsOpen) return;
        var gameOver = Object.FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        var canvas = gameOver ? gameOver.GetComponentInParent<Canvas>() : null;
        if (!canvas) canvas = Object.FindFirstObjectByType<Canvas>();
        if (!canvas) return;

        var root = new GameObject("Victory Panel", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        var victory = root.AddComponent<GameVictoryPanel>();
        if (gameOver)
        {
            victory.font = gameOver.font;
            victory.fontMaterial = gameOver.fontMaterial;
        }
        victory.Build();
        victory.Open();
    }

    void Awake()
    {
        current = this;
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;
    }

    void Update()
    {
        if (IsOpen) RefreshValues();
    }

    void OnDestroy()
    {
        GameplayInputBlocker.SetBlocked(this, false);
        if (current != this) return;
        current = null;
        IsOpen = false;
        Time.timeScale = 1f;
    }

    void Open()
    {
        RefreshValues();
        IsOpen = true;
        transform.SetAsLastSibling();
        GameplayInputBlocker.SetBlocked(this, true);
        AudioManager.Instance?.SetLowHealthHeartbeat(false, true);
        Time.timeScale = 0f;
        group.blocksRaycasts = group.interactable = true;
        StartCoroutine(FadeIn());
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
    }

    void RefreshValues()
    {
        var stats = StatsManager.Instance;
        var hud = Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include);
        depthValue.text = (hud ? hud.DepthMeters : 0).ToString("N0", German) + " m";
        pointsValue.text = (stats ? stats.Points : 0).ToString("N0", German);
        string money = ShopMoneyFormatter.Format(stats ? stats.Money : 0);
        if (moneyValue.text == money) return;
        moneyValue.text = money;
        LayoutMoney();
    }

    void LayoutMoney()
    {
        float textWidth = Mathf.Min(160f, Mathf.Ceil(moneyValue.GetPreferredValues(moneyValue.text, Mathf.Infinity, 62f).x) + 4f);
        const float coinWidth = 44f;
        const float gap = 9f;
        float left = -(coinWidth + gap + textWidth) * .5f;
        moneyCoin.anchoredPosition = new Vector2(left + coinWidth * .5f, 0f);
        moneyValue.rectTransform.sizeDelta = new Vector2(textWidth, 62f);
        moneyValue.rectTransform.anchoredPosition = new Vector2(left + coinWidth + gap + textWidth * .5f, 0f);
    }

    IEnumerator FadeIn()
    {
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
    }

    void Restart()
    {
        CloseForTransition();
        retryButton.interactable = false;
        StartCoroutine(RestartRun());
    }

    IEnumerator RestartRun()
    {
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;
        var stats = StatsManager.Instance ? StatsManager.Instance : Object.FindFirstObjectByType<StatsManager>();
        if (stats) Destroy(stats.gameObject);
        yield return null;
        SceneManager.LoadScene(sceneIndex);
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
        var root = (RectTransform)transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        var shade = CreateRect("Background Shade", root, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        shade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, .58f);

        panel = CreateRect("Game Won Panel", root, Vector2.zero, new Vector2(1120f, 635f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        var image = panel.gameObject.AddComponent<Image>();
        image.sprite = Resources.Load<Sprite>("GameWonPanel");
        image.preserveAspect = true;
        image.raycastTarget = false;

        depthValue = AddText("Depth Value", panel, new Vector2(-265f, -101f), new Vector2(230f, 62f), 43f, ValueColor);
        pointsValue = AddText("Points Value", panel, new Vector2(0f, -101f), new Vector2(230f, 62f), 43f, ValueColor);
        var moneyRow = CreateRect("Money Row", panel, new Vector2(265f, -101f), new Vector2(240f, 62f));
        moneyRow.anchorMin = moneyRow.anchorMax = moneyRow.pivot = new Vector2(.5f, .5f);
        moneyCoin = CreateRect("Money Coin", moneyRow, Vector2.zero, new Vector2(44f, 44f));
        moneyCoin.anchorMin = moneyCoin.anchorMax = moneyCoin.pivot = new Vector2(.5f, .5f);
        var coinSprites = Resources.LoadAll<Sprite>("GameOverCoin");
        var coinImage = moneyCoin.gameObject.AddComponent<Image>();
        coinImage.sprite = coinSprites.Length > 0 ? coinSprites[0] : null;
        coinImage.preserveAspect = true;
        coinImage.raycastTarget = false;
        moneyValue = AddText("Money Value", moneyRow, Vector2.zero, new Vector2(150f, 62f), 43f, ValueColor);
        moneyValue.enableAutoSizing = true;
        moneyValue.fontSizeMin = 30f;
        moneyValue.fontSizeMax = 43f;

        retryButton = AddButton("Retry", panel, new Vector2(-207f, -211f), new Vector2(384f, 78f), "Nochmal spielen", Restart);
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
        var text = AddText("Label", rect, new Vector2(0f, 6f), size, 34f, ButtonTextColor);
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
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
