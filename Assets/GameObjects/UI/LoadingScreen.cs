using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class LoadingScreen : MonoBehaviour
{
    static LoadingScreen instance;
    CanvasGroup group;
    Image fill;
    LoadingCrystalVisual visual;
    TextMeshProUGUI percentage, step;
    float displayed, readyAt = -1f, animationTime;

    public static void Show()
    {
        LoadingAudio.Begin();
        if (!instance)
        {
            var root = new GameObject("LoadingScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            instance = root.AddComponent<LoadingScreen>();
            instance.Build();
        }
        instance.gameObject.SetActive(true); instance.group.alpha = 1f;
        instance.displayed = 0f; instance.readyAt = -1f;
        instance.animationTime = 0f;
        instance.fill.fillAmount = 0f; instance.percentage.text = "0 %";
        instance.step.text = LoadingProgress.Labels[0];
        LoadingProgress.Begin();
        GameplayInputBlocker.SetBlocked(instance, true); Time.timeScale = 0f;
    }

    public static void LoadScene(int index) { Show(); instance.StartCoroutine(instance.Load(index.ToString(), true)); }
    public static void LoadScene(string name) { Show(); instance.StartCoroutine(instance.Load(name, false)); }
    IEnumerator Load(string scene, bool byIndex)
    {
        yield return null;
        var operation = byIndex ? SceneManager.LoadSceneAsync(int.Parse(scene)) : SceneManager.LoadSceneAsync(scene);
        while (!operation.isDone)
        {
            if (LoadingProgress.Stage == 0) LoadingProgress.Report(Mathf.Clamp01(operation.progress / .9f));
            yield return null;
        }
        yield return null;
        if (!FindFirstObjectByType<MapGenerator>()) { LoadingProgress.SetStage(7); LoadingProgress.Complete(); }
    }

    void Update()
    {
        if (!LoadingProgress.Active) return;
        displayed = Mathf.Max(displayed, Mathf.Lerp(displayed, LoadingProgress.Target, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime)));
        if (LoadingProgress.Ready && 1f - displayed < .0015f) displayed = 1f;
        fill.fillAmount = displayed;
        percentage.text = (displayed >= 1f ? 100 : Mathf.Min(99, Mathf.FloorToInt(displayed * 100f))) + " %";
        step.text = LoadingProgress.Labels[LoadingProgress.Stage];
        // Keep a separate animation clock. A slow native scene activation must not
        // skip an entire swing (and its contact effects) on the following frame.
        animationTime += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        visual.Animate(animationTime);
        if (displayed < 1f) return;
        LoadingAudio.Complete();
        if (readyAt < 0f) readyAt = Time.unscaledTime;
        float fade = Mathf.Clamp01((Time.unscaledTime - readyAt - .25f) / .4f);
        group.alpha = 1f - fade;
        if (fade < 1f) return;
        GameplayInputBlocker.SetBlocked(this, false); Time.timeScale = 1f; LoadingProgress.Dismiss(); gameObject.SetActive(false);
    }

    void Build()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        var backdrop = MakeImage("Backdrop", transform, Vector2.zero, new Vector2(1920, 1080), null, new Color32(8, 12, 17, 255));
        var rect = backdrop.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        visual = gameObject.AddComponent<LoadingCrystalVisual>();
        fill = visual.Build();
        Text("Mine vorbereiten", new Vector2(0, -210), new Vector2(1000, 76), 55);
        step = Text("Spielszene laden", new Vector2(0, -268), new Vector2(1000, 44), 27);
        percentage = Text("0 %", new Vector2(0, -412), new Vector2(240, 54), 38);
    }

    Image MakeImage(string name, Transform parent, Vector2 position, Vector2 size, Sprite sprite, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false); image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition = position;
        image.sprite = sprite; image.color = color; image.raycastTarget = name == "Backdrop"; return image;
    }
    TextMeshProUGUI Text(string value, Vector2 position, Vector2 size, float fontSize)
    {
        var text = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(transform, false); text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = position;
        text.text = value; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold; text.color = new Color32(255, 242, 216, 255);
        var font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (font) { text.font = font; text.fontStyle = FontStyles.Normal; }
        text.outlineWidth = .12f; text.outlineColor = new Color32(9, 14, 18, 255);
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text;
    }
}
