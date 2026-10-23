using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Presentation only: a wooden banner at the top centre whenever a challenge (or a stage of a milestone line or monthly
// project) is finished during a run. The clip charges up, hits lightly at FirstHitSeconds and hard at SoundPeakSeconds;
// the gold seal lands exactly on the hard hit. Never changes rewards, input or time scale.
// Completions are found by comparing challenge views after MetaProgression.Changed, so every source of progress counts.
// A pending level-up waits until the banner is done (GameplayLevelUpPresentation asks HoldsLevelUp).
public sealed class ChallengeCompletePresentation : MonoBehaviour
{
    public static ChallengeCompletePresentation Instance { get; private set; }
    public const float FirstHitSeconds = .61f, SoundPeakSeconds = .98f, Duration = 3.2f, SealFall = .22f;
    const float Width = 640, Height = 150, Top = 34, CheckInterval = .5f;
    const int MaxQueued = 3;
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Color Cream = new Color32(255, 245, 229, 255), Muted = new Color32(193, 178, 152, 255);
    static readonly Color Light = new Color(1f, .84f, .5f);
    static readonly string[] MedalNames = { "Bronze", "Silber", "Gold", "Platin" };

    public readonly struct Entry
    {
        public readonly string name, line, xp; public readonly Sprite icon;
        public Entry(string name, string line, string xp, Sprite icon) { this.name = name; this.line = line; this.xp = xp; this.icon = icon; }
    }

    public bool IsShowing { get; private set; }
    public int AudioPlayCount { get; private set; }
    public int Queued => queue.Count;
    public float Elapsed => elapsed;
    readonly List<Entry> queue = new();
    readonly Dictionary<string, int> scores = new();
    MetaProfile observedProfile;
    MetaRunState observedRun;
    bool dirty = true, soundPaused;
    float elapsed, nextCheck;
    CanvasGroup group;
    RectTransform banner, seal, charge, flash, ring, shine;
    Image icon, sealImage, chargeImage, flashImage, ringImage, shineImage;
    TextMeshProUGUI title, nameText, xpText;
    readonly List<RectTransform> sparks = new();
    AudioClip clip;
    AudioSource sound;
    static Sprite soft, ringSprite, band;

    public static bool HoldsLevelUp
    {
        get {
            var view = Instance;
            if (!view || !view.isActiveAndEnabled || !view.InRun) return false;
            view.Poll(true);
            return view.IsShowing || view.queue.Count > 0;
        }
    }

    // Test settings: shows the banner with the first open challenge of the day (or a stand-in) without touching progress.
    public static bool Preview()
    {
        var view = Instance;
        if (!view || !view.InRun) return false;
        view.Poll(true);
        MetaChallengeProgress sample = null;
        try { foreach (var value in MetaProgression.GetChallenges(ChallengePeriod.Daily)) if (value != null) { sample = value; break; } }
        catch (Exception e) { Debug.LogException(e); }
        view.queue.Add(sample != null ? Describe(sample) : new Entry("Testherausforderung", "Herausforderung erfüllt", "+100 XP", ChallengeArt.Sprite(1)));
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (Instance) return;
        var root = new GameObject("Challenge Complete", typeof(RectTransform));
        DontDestroyOnLoad(root); root.AddComponent<ChallengeCompletePresentation>();
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        clip = Resources.Load<AudioClip>("Progression/ChallengeComplete");
        if (clip) clip.LoadAudioData();
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 99;
        var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        group = gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        Build();
        sound = gameObject.AddComponent<AudioSource>(); sound.playOnAwake = false; sound.loop = false; sound.spatialBlend = 0;
    }

    void Build()
    {
        banner = HomeUi.Rect("Challenge Banner", transform, new Vector2(0, -Top - Height * .5f), new Vector2(Width, Height));
        banner.anchorMin = banner.anchorMax = new Vector2(.5f, 1);
        // Same dark wood with brass corners as the challenge tracker on the right.
        var frame = banner.gameObject.AddComponent<Image>();
        var vitals = Resources.Load<Sprite>("HUDv1/Vitals");
        frame.sprite = vitals ? vitals : HomeUi.Sprite("SelectionCardNormal");
        frame.type = Image.Type.Sliced; frame.raycastTarget = false;
        frame.pixelsPerUnitMultiplier = frame.sprite.rect.height / 70;
        frame.material = HudV1Art.DarkWood(frame.sprite);
        // A light band sweeps over the wood when the seal lands.
        var inner = HomeUi.Rect("Shine Mask", banner, Vector2.zero, new Vector2(Width - 24, Height - 24));
        inner.gameObject.AddComponent<RectMask2D>();
        shineImage = Picture("Shine", inner, Vector2.zero, new Vector2(90, 260), Band()); shine = shineImage.rectTransform;
        shine.localRotation = Quaternion.Euler(0, 0, -22);
        icon = Picture("Icon", banner, new Vector2(-242, 0), new Vector2(100, 100), null); icon.preserveAspect = true;
        title = Label("Title", new Vector2(6, 40), new Vector2(360, 30), 24, ProgressionArt.Gold);
        nameText = Label("Name", new Vector2(-4, 3), new Vector2(340, 50), 40, Cream); nameText.fontSizeMin = 24;
        xpText = Label("Reward", new Vector2(6, -36), new Vector2(360, 28), 22, Muted);
        var sealAt = new Vector2(Width * .5f - 72, 0);
        chargeImage = Picture("Charge Glow", banner, sealAt, new Vector2(150, 150), Soft()); charge = chargeImage.rectTransform;
        flashImage = Picture("Hit Glow", banner, sealAt, new Vector2(300, 300), Soft()); flash = flashImage.rectTransform;
        ringImage = Picture("Hit Ring", banner, sealAt, new Vector2(100, 100), Ring()); ring = ringImage.rectTransform;
        sealImage = Picture("Seal", banner, sealAt, new Vector2(124, 124), ChallengeArt.Sprite(7)); seal = sealImage.rectTransform;
        sealImage.preserveAspect = true; ChallengeSealCheck.Attach(sealImage);
        for (int i = 0; i < 18; i++) sparks.Add(Picture("Spark " + i, banner, sealAt, new Vector2(12, 12), Soft()).rectTransform);
        Hide();
    }

    Image Picture(string name, Transform parent, Vector2 position, Vector2 size, Sprite sprite)
    {
        var image = HomeUi.Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.raycastTarget = false; return image;
    }

    TextMeshProUGUI Label(string name, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var text = HomeUi.Rect(name, banner, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        text.fontSize = fontSize; text.color = color; text.alignment = TextAlignmentOptions.Left; text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = fontSize * .75f; text.fontSizeMax = fontSize;
        text.outlineColor = new Color32(20, 11, 8, 255); text.outlineWidth = .14f;
        return text;
    }

    void OnEnable() { MetaProgression.Changed += OnChanged; SceneManager.sceneLoaded += SceneLoaded; dirty = true; }
    void OnDisable() { MetaProgression.Changed -= OnChanged; SceneManager.sceneLoaded -= SceneLoaded; Clear(); }
    void OnChanged() => dirty = true;
    void SceneLoaded(Scene scene, LoadSceneMode mode) { Clear(); observedProfile = null; observedRun = null; scores.Clear(); }
    void OnDestroy() { if (Instance == this) Instance = null; }

    bool InRun => Application.isPlaying && SceneManager.GetActiveScene().name == RunNavigation.GameScene &&
        !LoadingProgress.Active && !RunNavigation.IsTransitioning && MetaProgression.CurrentRun != null && !MetaProgression.CurrentRun.ended;

    // Compares every challenge view with the last look; a higher score (done, or one more stage) queues a banner.
    // A new profile or run only records the current state, so loading a world never celebrates old results.
    void Poll(bool force)
    {
        if (!InRun) return;
        var profile = MetaProgression.Profile; var run = MetaProgression.CurrentRun;
        bool baseline = !ReferenceEquals(profile, observedProfile) || !ReferenceEquals(run, observedRun);
        if (!baseline && (!dirty || (!force && Time.unscaledTime < nextCheck))) return;
        dirty = false; nextCheck = Time.unscaledTime + CheckInterval;
        if (baseline) { scores.Clear(); queue.Clear(); observedProfile = profile; observedRun = run; }
        List<MetaChallengeProgress> views;
        try { views = Views(); }
        catch (Exception e) { Debug.LogException(e); return; }
        foreach (var view in views) {
            if (view?.track == null) continue;
            int score = Score(view);
            // Keys seen for the first time (new round, new run) are recorded silently.
            if (!baseline && scores.TryGetValue(view.track, out int old) && score > old && queue.Count < MaxQueued) queue.Add(Describe(view));
            scores[view.track] = score;
        }
    }

    static List<MetaChallengeProgress> Views()
    {
        var result = new List<MetaChallengeProgress>();
        foreach (var period in new[] { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly, ChallengePeriod.Lifetime })
            result.AddRange(MetaProgression.GetChallenges(period));
        var project = MetaProgression.GetMonthlyProject();
        if (project != null) result.Add(project);
        return result;
    }

    public static int Score(MetaChallengeProgress view)
    {
        if (view.kind == 2) return view.medal;
        if (view.kind == 1) return view.stagesDone + (view.completed ? 1 : 0);
        return view.completed ? 1 : 0;
    }

    public static Entry Describe(MetaChallengeProgress view)
    {
        string line = "Herausforderung erfüllt"; int xp = view.xp;
        if (view.kind == 2 && view.medal > 0) {
            int stage = view.medal; xp = StageXp(view, stage);
            line = view.stages == MedalNames.Length ? MedalNames[Mathf.Clamp(stage, 1, MedalNames.Length) - 1] + " erreicht" : "Stufe " + stage + " von " + view.stages + " erreicht";
        } else if (view.kind == 1 && view.stagesDone > 0) {
            int stage = view.stagesDone; xp = StageXp(view, stage);
            int stages = view.stageTargets?.Length ?? stage;
            if (stage < stages) line = "Stufe " + stage + " von " + stages + " erreicht";
        }
        return new Entry(view.name ?? "", line, xp > 0 ? "+" + xp.ToString("N0", German) + " XP" : "", ChallengeTrackerHud.Icon(view));
    }

    static int StageXp(MetaChallengeProgress view, int stage)
        => view.stageXp != null && stage >= 1 && stage <= view.stageXp.Length ? view.stageXp[stage - 1] : view.xp;

    void Update()
    {
        Poll(false);
        if (!InRun) { if (IsShowing || queue.Count > 0) Clear(); return; }
        bool obscured = GameplayInputBlocker.IsBlocked || Time.timeScale <= 0 || GameOverPanel.IsOpen || GameVictoryPanel.IsOpen;
        if (obscured) { group.alpha = 0; if (sound.isPlaying) { sound.Pause(); soundPaused = true; } return; }
        if (soundPaused) { sound.UnPause(); soundPaused = false; }
        if (sound.clip) sound.volume = AudioManager.TunedVolume(sound.clip, 1f, sound);
        if (!IsShowing) {
            // A level-up that is already on screen finishes first; one that is only pending waits for us.
            var levelUp = GameplayLevelUpPresentation.Instance;
            if (queue.Count == 0 || (levelUp && levelUp.IsShowing)) return;
            Begin();
        }
        elapsed += Time.unscaledDeltaTime;
        Animate(elapsed);
        if (elapsed >= Duration) { IsShowing = false; Hide(); }
    }

    void Begin()
    {
        var entry = queue[0]; queue.RemoveAt(0);
        title.text = entry.line; nameText.text = entry.name; xpText.text = entry.xp;
        icon.sprite = entry.icon; icon.enabled = entry.icon;
        elapsed = 0; IsShowing = true;
        sound.Stop(); soundPaused = false;
        if (clip) {
            sound.clip = clip; sound.pitch = AudioManager.TunedPitch(clip, 1, sound);
            sound.volume = AudioManager.TunedVolume(clip, 1, sound);
            sound.Play(); AudioPlayCount++;
        }
        Animate(0);
    }

    // t runs with the clip: slide in, charge glow, light shake on the first hit, seal lands on the peak, slide out.
    void Animate(float t)
    {
        float arrive = EaseOut(t / .45f), leave = Mathf.Clamp01((t - (Duration - .45f)) / .4f);
        float shake = 4 * Pulse(t, FirstHitSeconds, .12f) * Mathf.Sin(t * 90);
        banner.anchoredPosition = new Vector2(shake, -Top - Height * .5f + 190 * (1 - arrive) + 60 * leave * leave);
        group.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .15f)) * (1 - leave);

        float build = Mathf.Clamp01((t - .2f) / (SoundPeakSeconds - .2f));
        bool charging = t > .2f && t < SoundPeakSeconds;
        chargeImage.enabled = charging;
        if (charging) {
            float wobble = 1 + .06f * Mathf.Sin(t * 26) + .25f * Pulse(t, FirstHitSeconds, .15f);
            charge.localScale = Vector3.one * (.55f + .6f * build) * wobble;
            chargeImage.color = new Color(Light.r, Light.g, Light.b, .2f + .6f * build * build);
        }

        float fall = (t - (SoundPeakSeconds - SealFall)) / SealFall;
        sealImage.enabled = fall >= 0;
        if (fall >= 0 && t < SoundPeakSeconds) {
            seal.localScale = Vector3.one * (2.4f - 1.4f * fall * fall);
            sealImage.color = new Color(1, 1, 1, Mathf.Clamp01(fall * 1.6f));
        } else if (fall >= 0) {
            float settle = Mathf.Clamp01((t - SoundPeakSeconds) / .25f);
            seal.localScale = Vector3.one * (1 + .12f * Mathf.Sin(settle * Mathf.PI) * (1 - settle));
            sealImage.color = Color.white;
        }

        float after = t - SoundPeakSeconds;
        flashImage.enabled = after >= 0 && after < .45f;
        if (flashImage.enabled) flashImage.color = new Color(Light.r, Light.g, Light.b, .9f * (1 - after / .45f));
        float wave = after / .75f;
        ringImage.enabled = wave >= 0 && wave < 1;
        if (ringImage.enabled) {
            ring.localScale = Vector3.one * (.6f + 2.8f * EaseOut(wave));
            ringImage.color = new Color(1, .9f, .6f, .9f * (1 - wave));
        }
        for (int i = 0; i < sparks.Count; i++) {
            var spark = sparks[i]; bool on = wave >= 0 && wave < 1;
            spark.gameObject.SetActive(on);
            if (!on) continue;
            float angle = i * 2.39996f, reach = 120 + 80 * Mathf.Repeat(i * .618f, 1);
            var at = new Vector2(Width * .5f - 72, 0) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach * EaseOut(wave) + Vector2.down * 40 * wave * wave;
            spark.anchoredPosition = at; spark.localScale = Vector3.one * (1.2f - .6f * wave);
            spark.GetComponent<Image>().color = new Color(1, .9f, .55f, Mathf.Pow(1 - wave, 1.5f));
        }
        float sweep = after / .6f;
        shineImage.enabled = sweep >= 0 && sweep < 1;
        if (shineImage.enabled) {
            shine.anchoredPosition = new Vector2(-Width * .5f - 60 + (Width + 120) * sweep, 0);
            shineImage.color = new Color(1, .93f, .7f, .55f * (1 - sweep));
        }
    }

    static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
    static float Pulse(float t, float at, float length) => t < at ? 0 : Mathf.Max(0, 1 - (t - at) / length);

    void Hide()
    {
        if (group) group.alpha = 0;
        if (chargeImage) chargeImage.enabled = sealImage.enabled = flashImage.enabled = ringImage.enabled = shineImage.enabled = false;
        foreach (var spark in sparks) if (spark) spark.gameObject.SetActive(false);
    }

    void Clear()
    {
        IsShowing = false; soundPaused = false; elapsed = 0; queue.Clear();
        Hide(); if (sound) sound.Stop();
    }

    // Soft round light, a thin ring and a vertical light band, made once at runtime.
    static Sprite Soft() => soft ? soft : soft = Generated("Challenge Soft Light", 64, 64, (x, y) => { float d = Mathf.Clamp01(new Vector2(x, y).magnitude); return Mathf.Pow(1 - d, 2.2f); });
    static Sprite Ring() => ringSprite ? ringSprite : ringSprite = Generated("Challenge Ring", 128, 128, (x, y) => { float d = new Vector2(x, y).magnitude; return Mathf.Clamp01(1 - Mathf.Abs(d - .86f) / .1f); });
    static Sprite Band() => band ? band : band = Generated("Challenge Shine", 32, 8, (x, y) => Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(x)), 1.6f));
    static Sprite Generated(string name, int width, int height, Func<float, float, float> alpha)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color(1, 1, 1, alpha((x + .5f) / width * 2 - 1, (y + .5f) / height * 2 - 1));
        texture.SetPixels32(pixels); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100);
        sprite.name = name; sprite.hideFlags = HideFlags.DontSave; return sprite;
    }
}
