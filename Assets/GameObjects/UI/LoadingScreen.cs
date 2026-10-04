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
    Texture2DArray terrainMasks;
    TextMeshProUGUI percentage, step, title;
    float displayed, readyAt = -1f, animationTime;
    int previousFrameRate;
    bool frameRateChanged;
    UnityEngine.ThreadPriority previousLoadingPriority;
    bool loadingPriorityChanged;
    static CanvasGroup curtain;
    static LoadingTransitionInput curtainInput;
    bool presented = true, revealing, finishing;
    public static bool ContentVisible => LoadingProgress.Active && instance && instance.presented;
    public static bool TransitionActive => curtain && curtain.gameObject.activeSelf;
    public static string TransitionPhase { get; private set; } = "Idle";
    static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("Loading.BuildScreen");
    static readonly Unity.Profiling.ProfilerMarker ShowMarker = new("Loading.ShowScreen");

    public static IEnumerator FadeToBlack()
    {
        EnsureCurtain();
        TransitionPhase = "HomeFadeOut";
        GameplayInputBlocker.SetBlocked(curtainInput, true); Time.timeScale = 0;
        yield return FadeCurtain(1f, .25f);
        TransitionPhase = "BlackPreparation";
        yield return new WaitForEndOfFrame();
        yield return null;
    }

    static void EnsureCurtain()
    {
        if (!curtain)
        {
            var root = new GameObject("LoadingTransition", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32761;
            curtain = root.AddComponent<CanvasGroup>(); curtain.alpha = 0;
            curtainInput = root.AddComponent<LoadingTransitionInput>();
            var image = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root.transform, false); image.color = Color.black;
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        }
    }

    public static IEnumerator FadeFromBlack()
    {
        yield return FadeCurtain(0f, .35f);
        if (curtain)
        {
            GameplayInputBlocker.SetBlocked(curtainInput, false);
            curtain.gameObject.SetActive(false);
        }
        TransitionPhase = LoadingProgress.Active ? "Loading" : "Idle";
    }

    static IEnumerator FadeCurtain(float target, float duration)
    {
        if (!curtain) yield break;
        curtain.gameObject.SetActive(true);
        float from = curtain.alpha, elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            curtain.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, elapsed / duration));
            if (elapsed >= duration) break;
            yield return null;
        }
        curtain.alpha = target;
    }

    public static IEnumerator Prepare()
    {
        if (instance) yield break;
        var requests = new[] {
            Resources.LoadAsync<Sprite>("Loading/CrystalIllustration"),
            Resources.LoadAsync<Sprite>("Loading/Pickaxe"),
            Resources.LoadAsync<Texture2D>("Loading/ImpactAtlas"),
            Resources.LoadAsync<Texture2D>("Loading/ProgressAtlas"),
            Resources.LoadAsync<TMP_FontAsset>("ArtifactDiscovery/TitleFont"),
            Resources.LoadAsync<AudioClip>("Audio/LoadingPickaxeHit"),
            Resources.LoadAsync<Texture2DArray>("TerrainEdgeMasks")
        };
        foreach (var request in requests) yield return request;
        EnsureBuilt();
        instance.terrainMasks = requests[6].asset as Texture2DArray;
    }

    public static IEnumerator PrepareTransitionAudio()
    {
        EnsureBuilt();
        // AudioSource's first playback creates native sound data. Keep that
        // work behind the fully black transition, before the animated screen.
        instance.presented = false;
        instance.gameObject.SetActive(true); instance.group.alpha = 0;
        LoadingAudio.WarmLoadingTrack();
        instance.visual.WarmImpactSound();
        yield return null;
        instance.gameObject.SetActive(false);
    }

    static void EnsureBuilt()
    {
        using var measurement = BuildMarker.Auto();
        if (!instance)
        {
            var root = new GameObject("LoadingScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            instance = root.AddComponent<LoadingScreen>();
            instance.Build();
            root.SetActive(false);
        }
    }

    public static void Show()
    {
        using var measurement = ShowMarker.Auto();
        EnsureBuilt();
        LoadingAudio.Begin();
        instance.gameObject.SetActive(true); instance.group.alpha = 1f;
        if (!instance.frameRateChanged) instance.previousFrameRate = Application.targetFrameRate;
        instance.frameRateChanged = true;
        // Present faster than the minimum 60 Hz so scheduler jitter has some
        // room inside the 16.67 ms frame limit. Work still uses the 60 Hz budget.
        Application.targetFrameRate = 90;
        LoadingWorkBudget.Begin();
        instance.displayed = 0f; instance.readyAt = -1f;
        instance.animationTime = 0f;
        instance.presented = true; instance.revealing = false; instance.finishing = false;
        instance.fill.fillAmount = 0f; instance.percentage.text = "0 %";
        LoadingProgress.Begin(GameSaveSystem.HasPendingLoad);
        instance.title.text = LoadingProgress.Restoring ? "Spielstand laden" : "Mine vorbereiten";
        instance.step.text = LoadingProgress.CurrentLabel;
        GameplayInputBlocker.SetBlocked(instance, true); Time.timeScale = 0f;
    }

    public static void LoadScene(int index) { Show(); instance.presented = false; instance.StartCoroutine(instance.Load(index.ToString(), true)); }
    public static void LoadScene(string name) { Show(); instance.presented = false; instance.StartCoroutine(instance.Load(name, false)); }
    IEnumerator Load(string scene, bool byIndex)
    {
        if (!curtain || !curtain.gameObject.activeSelf || curtain.alpha < 1f) yield return FadeToBlack();
        yield return new WaitForEndOfFrame();
        yield return null;
        previousLoadingPriority = Application.backgroundLoadingPriority;
        loadingPriorityChanged = true;
        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;
        TransitionPhase = "BlackSceneLoad";
        var operation = byIndex ? SceneManager.LoadSceneAsync(int.Parse(scene)) : SceneManager.LoadSceneAsync(scene);
        while (!operation.isDone)
        {
            if (LoadingProgress.Stage == 0) LoadingProgress.Report(Mathf.Clamp01(operation.progress / .9f));
            yield return null;
        }
        RestoreLoadingPriority();
        yield return new WaitForEndOfFrame();
        yield return null;
        // Start methods, native UI setup and asset integration can extend beyond
        // isDone. Render these settling frames behind the opaque curtain.
        for (int frame = 0; frame < 3; frame++) yield return null;
        revealing = true; TransitionPhase = "LoadingFadeIn";
        yield return FadeFromBlack();
        presented = true;
        if (!FindFirstObjectByType<MapGenerator>()) { LoadingProgress.SetStage(7); LoadingProgress.Complete(); }
    }

    void Update()
    {
        if (!LoadingProgress.Active) return;
        LoadingWorkBudget.ObserveFrame(Time.unscaledDeltaTime);
        if (presented)
        {
            float next = Mathf.Lerp(displayed, LoadingProgress.Target, 1f - Mathf.Exp(-5f * Mathf.Min(Time.unscaledDeltaTime, 1f / 30f)));
            displayed = Mathf.Max(displayed, Mathf.Min(next, displayed + .01f));
        }
        if (LoadingProgress.Ready && 1f - displayed < .0015f) displayed = 1f;
        fill.fillAmount = displayed;
        percentage.text = (displayed >= 1f ? 100 : Mathf.Min(99, Mathf.FloorToInt(displayed * 100f))) + " %";
        step.text = LoadingProgress.CurrentLabel;
        // Keep a separate animation clock. A slow native scene activation must not
        // skip an entire swing (and its contact effects) on the following frame.
        if (presented || revealing) animationTime += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        visual.Animate(animationTime);
        if (displayed < 1f) return;
        LoadingAudio.Complete();
        if (readyAt < 0f) readyAt = Time.unscaledTime;
        if (finishing || Time.unscaledTime - readyAt < .25f) return;
        finishing = true;
        StartCoroutine(FinishTransition());
    }

    IEnumerator FinishTransition()
    {
        EnsureCurtain();
        GameplayInputBlocker.SetBlocked(curtainInput, true);
        TransitionPhase = "LoadingFadeOut";
        yield return FadeCurtain(1f, .25f);
        group.alpha = 0f;
        presented = false;
        TransitionPhase = "BlackCompletion";
        double revealAt = Time.realtimeSinceStartupAsDouble + .5;
        while (Time.realtimeSinceStartupAsDouble < revealAt) yield return null;
        TransitionPhase = "GameplayFadeIn";
        yield return FadeFromBlack();
        RestoreFrameRate();
        TransitionPhase = "Idle";
        GameplayInputBlocker.SetBlocked(this, false); Time.timeScale = 1f; LoadingProgress.Dismiss(); gameObject.SetActive(false);
    }

    void OnDisable() { RestoreFrameRate(); RestoreLoadingPriority(); }
    void RestoreLoadingPriority()
    {
        if (!loadingPriorityChanged) return;
        Application.backgroundLoadingPriority = previousLoadingPriority;
        loadingPriorityChanged = false;
    }
    void RestoreFrameRate()
    {
        if (!frameRateChanged) return;
        Application.targetFrameRate = previousFrameRate;
        frameRateChanged = false;
    }

    void Build()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        var backdrop = MakeImage("Backdrop", transform, Vector2.zero, new Vector2(1920, 1080), null, new Color32(8, 12, 17, 255));
        var rect = backdrop.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        visual = gameObject.AddComponent<LoadingCrystalVisual>();
        fill = visual.Build();
        title = Text("Mine vorbereiten", new Vector2(0, -210), new Vector2(1000, 76), 55);
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

public sealed class LoadingTransitionInput : MonoBehaviour
{
    void OnDisable() => GameplayInputBlocker.SetBlocked(this, false);
}
