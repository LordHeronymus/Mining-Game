using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ArtifactDiscoveryView : MonoBehaviour
{
    public const float DefaultDurationSeconds = 3f;
    public static ArtifactDiscoveryView Instance { get; private set; }
    public bool IsShowing { get; private set; }
    public float Elapsed { get; private set; }
    public float DurationSeconds { get; private set; } = DefaultDurationSeconds;
    public ArtifactTile CurrentArtifact { get; private set; }

    readonly Queue<ArtifactTile> pending = new();
    CanvasGroup group;
    RectTransform artwork;
    Image icon;
    TextMeshProUGUI title;
    ArtifactDiscoveryGraphic particles;
    Material auraMaterial, iconMaterial, backgroundMaterial, titleMaterial;
    RenderTexture backdrop;
    RawImage background;
    float previousTimeScale;
    float artifactYOffset;
    float iconRotationFrequency;
    float iconRotationAngle;
    double startedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Instance = null;

    public static void ShowArtifact(ArtifactTile artifact)
    {
        if (!artifact || !artifact.sprite || GameOverPanel.IsOpen) return;
        if (!Instance)
            new GameObject("Artifact Discovery", typeof(RectTransform)).AddComponent<ArtifactDiscoveryView>();
        Instance.pending.Enqueue(artifact);
        if (!Instance.IsShowing) Instance.Begin();
    }

    void Awake()
    {
        Instance = this;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        backgroundMaterial = new Material(Resources.Load<Shader>("ArtifactDiscovery/Backdrop"));
        background = MakeRect("Blurred game", transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
        background.rectTransform.anchorMin = Vector2.zero;
        background.rectTransform.anchorMax = Vector2.one;
        background.rectTransform.sizeDelta = Vector2.zero;
        background.material = backgroundMaterial;
        background.raycastTarget = true;

        artwork = MakeRect("Reveal", transform, Vector2.zero, new Vector2(1920, 1080));
        auraMaterial = new Material(Resources.Load<Shader>("ArtifactDiscovery/Aura"));
        var aura = MakeRect("Theme light and rays", artwork, new Vector2(0, 130), new Vector2(1360, 1080)).gameObject.AddComponent<RawImage>();
        aura.material = auraMaterial;
        aura.raycastTarget = false;

        particles = MakeRect("Drifting dust and crystals", artwork, Vector2.zero, new Vector2(1920, 1080)).gameObject.AddComponent<ArtifactDiscoveryGraphic>();
        particles.kind = ArtifactDiscoveryGraphic.Kind.Particles;
        particles.raycastTarget = false;

        iconMaterial = new Material(Resources.Load<Shader>("ArtifactDiscovery/Shimmer"));
        icon = MakeRect("Artifact", artwork, new Vector2(0, 90), new Vector2(720, 720)).gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.material = iconMaterial;
        icon.raycastTarget = false;

        title = MakeRect("Artifact name", artwork, new Vector2(0, -285), new Vector2(1540, 160)).gameObject.AddComponent<TextMeshProUGUI>();
        title.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        title.fontSize = 116;
        title.enableAutoSizing = true;
        title.fontSizeMin = 62;
        title.fontSizeMax = 116;
        title.alignment = TextAlignmentOptions.Center;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.enableVertexGradient = true;
        title.colorGradient = new VertexGradient(new Color32(255, 242, 194, 255), new Color32(255, 242, 194, 255),
            new Color32(217, 153, 55, 255), new Color32(217, 153, 55, 255));
        title.raycastTarget = false;
        titleMaterial = new Material(title.fontSharedMaterial);
        titleMaterial.EnableKeyword("UNDERLAY_ON");
        titleMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(.04f, .018f, .004f, .9f));
        titleMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .18f);
        titleMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.18f);
        titleMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .1f);
        title.fontSharedMaterial = titleMaterial;

        var frame = MakeRect("Fine gold frame", artwork, Vector2.zero, new Vector2(1920, 1080)).gameObject.AddComponent<ArtifactDiscoveryGraphic>();
        frame.kind = ArtifactDiscoveryGraphic.Kind.Frame;
        frame.raycastTarget = false;
        SetVisible(false);
    }

    void Begin()
    {
        if (pending.Count == 0) return;
        if (!IsShowing)
        {
            CaptureBackdrop();
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            GameplayInputBlocker.SetBlocked(this, true);
            ItemFeed.Instance?.SetAboveArtifactDiscovery(true);
        }
        IsShowing = true;
        var map = FindFirstObjectByType<MapGenerator>();
        particles.shardDistance = map ? Mathf.Clamp(map.artifactDiscoveryShardDistance, .25f, 2f) : 1f;
        DurationSeconds = map ? Mathf.Clamp(map.artifactDiscoveryDurationSeconds, .5f, 10f) : DefaultDurationSeconds;
        artifactYOffset = map ? Mathf.Clamp(map.artifactDiscoveryArtifactYOffset, -300f, 300f) : 0f;
        iconRotationFrequency = map ? Mathf.Clamp(map.artifactDiscoveryIconRotationFrequency, 0f, 2f) : .35f;
        iconRotationAngle = map ? Mathf.Clamp(map.artifactDiscoveryIconRotationAngle, 0f, 15f) : 3f;
        if (map)
            particles.SetShards(map.artifactDiscoveryShardCount,
                map.artifactDiscoveryShardSizeMin, map.artifactDiscoveryShardSizeMax,
                map.artifactDiscoveryShardRotationFrequency, map.artifactDiscoveryShardRotationAngle);
        CurrentArtifact = pending.Dequeue();
        artifactYOffset += CurrentArtifact.DiscoveryIconYOffset;
        icon.sprite = CurrentArtifact.sprite;
        title.text = CurrentArtifact.displayName;
        Color theme = CurrentArtifact.ThemeColor;
        auraMaterial.SetColor("_Theme", theme);
        iconMaterial.SetColor("_Theme", theme);
        // Image UVs refer to the shared sprite sheet. Keep the shine local to this artifact.
        Rect uv = CurrentArtifact.sprite.textureRect;
        Texture tex = CurrentArtifact.sprite.texture;
        iconMaterial.SetVector("_SpriteRect", new Vector4(uv.x / tex.width, uv.y / tex.height, uv.width / tex.width, uv.height / tex.height));
        particles.theme = theme;
        particles.secondaryTheme = CurrentArtifact.SecondaryThemeColor;
        SetVisible(true);
        SetPresentationTime(0f);
    }

    void CaptureBackdrop()
    {
        if (!Camera.main) return;
        int width = Mathf.Clamp(Screen.width / 3, 320, 960);
        int height = Mathf.Max(180, Mathf.RoundToInt(width * (Screen.height / (float)Mathf.Max(1, Screen.width))));
        if (backdrop && (backdrop.width != width || backdrop.height != height))
        {
            backdrop.Release();
            Destroy(backdrop);
            backdrop = null;
        }
        if (!backdrop)
        {
            backdrop = new RenderTexture(width, height, 24) { name = "Artifact discovery backdrop", filterMode = FilterMode.Bilinear };
            backdrop.Create();
        }
        var camera = Camera.main;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = backdrop;
            camera.Render();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
        }
        background.texture = backdrop;
    }

    void Update()
    {
        if (!IsShowing) return;
        if (GameOverPanel.IsOpen) { pending.Clear(); Finish(); return; }
        float nextTime = (float)(Time.realtimeSinceStartupAsDouble - startedAt);
        if (nextTime >= DurationSeconds)
        {
            if (pending.Count > 0) Begin();
            else Finish();
            return;
        }
        ApplyPresentationTime(nextTime);
    }

    // A deterministic animation clock also lets editor previews capture the real UI at exact moments.
    public void SetPresentationTime(float seconds)
    {
        startedAt = Time.realtimeSinceStartupAsDouble - seconds;
        ApplyPresentationTime(seconds);
    }

    void ApplyPresentationTime(float seconds)
    {
        Elapsed = seconds;
        float arrivalTime = Mathf.Min(.42f, DurationSeconds * .3f);
        float fadeInTime = Mathf.Min(.22f, DurationSeconds * .25f);
        float fadeOutTime = Mathf.Min(.4f, DurationSeconds * .25f);
        float arrival = 1f - Mathf.Pow(1f - Mathf.Clamp01(seconds / arrivalTime), 3f);
        float departure = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((DurationSeconds - seconds) / fadeOutTime));
        group.alpha = Mathf.Min(Mathf.Clamp01(seconds / fadeInTime), departure);
        artwork.localScale = Vector3.one * Mathf.Lerp(.92f, 1f, arrival);
        icon.rectTransform.anchoredPosition = new Vector2(0, 136 + artifactYOffset + Mathf.Sin(seconds * 1.8f) * 3f);
        icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Sin(seconds * Mathf.PI * 2f * iconRotationFrequency) * iconRotationAngle);
        auraMaterial.SetFloat("_RevealTime", seconds);
        iconMaterial.SetFloat("_RevealTime", seconds);
        particles.SetTime(seconds);
        particles.shardCenter = icon.rectTransform.anchoredPosition;
    }

    void SetVisible(bool visible)
    {
        group.alpha = 0;
        group.blocksRaycasts = visible;
        group.interactable = false;
        background.gameObject.SetActive(visible);
        artwork.gameObject.SetActive(visible);
    }

    void Finish()
    {
        if (IsShowing && !GameOverPanel.IsOpen) Time.timeScale = previousTimeScale;
        IsShowing = false;
        CurrentArtifact = null;
        GameplayInputBlocker.SetBlocked(this, false);
        ItemFeed.Instance?.SetAboveArtifactDiscovery(false);
        SetVisible(false);
    }

    void OnDisable()
    {
        pending.Clear();
        if (group) Finish();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (backdrop) { backdrop.Release(); Destroy(backdrop); }
        if (auraMaterial) Destroy(auraMaterial);
        if (iconMaterial) Destroy(iconMaterial);
        if (backgroundMaterial) Destroy(backgroundMaterial);
        if (titleMaterial) Destroy(titleMaterial);
    }

    static RectTransform MakeRect(string label, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
