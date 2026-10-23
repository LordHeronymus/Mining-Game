using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Starred challenges on the right screen edge, under the counters: at most MetaProgression.TrackedShown unfinished ones
// with name, counter and progress bar. A finished one drops out and the next mark moves up.
// Child of CompactHud, so it shares the HUD's fade (CanvasGroup) and scales like the counters. Never takes clicks.
public sealed class ChallengeTrackerHud : MonoBehaviour
{
    const float Width = 384, RowHeight = 58, Padding = 14, PaddingRight = 26, Top = 134, Margin = 18, Header = 40;
    const float RefreshDelay = .5f, IdleRefresh = 5;
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Color Cream = new Color32(255, 245, 229, 255), Muted = new Color32(193, 178, 152, 255);
    static readonly Color[] TierColors = { new Color32(112, 196, 96, 255), new Color32(86, 156, 232, 255), new Color32(170, 108, 230, 255), ProgressionArt.Gold, ProgressionArt.Gold };
    static readonly Color[] MedalColors = { Muted, new Color32(205, 127, 50, 255), new Color32(196, 204, 212, 255), ProgressionArt.Gold, new Color32(229, 228, 226, 255) };

    sealed class Row
    {
        public RectTransform rect, fill;
        public Image icon;
        public ChallengeGem gem;
        public TextMeshProUGUI name, counter;
    }

    RectTransform panel;
    readonly List<Row> rows = new();
    bool dirty = true;
    float nextRefresh, idleRefresh;
    Vector2 fittedSize;
    float fittedHudScale = -1;
    public int ShownCount { get; private set; }

    public static ChallengeTrackerHud Attach(Component hud)
    {
        if (!hud) return null;
        var existing = hud.GetComponentInChildren<ChallengeTrackerHud>(true);
        if (existing) return existing;
        var root = new GameObject("Challenge Tracker", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(hud.transform, false); root.gameObject.layer = hud.gameObject.layer;
        HomeUi.Stretch(root);
        var tracker = root.gameObject.AddComponent<ChallengeTrackerHud>();
        tracker.Build();
        return tracker;
    }

    void Build()
    {
        panel = Place("Tracker Panel", transform, 0, 0, Width, Header);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
        Frame(panel.gameObject, 70);
        // Title centred between two fine gold rules, each ending in a small gold gem.
        Label("Title", panel, "Herausforderungen", Width * .5f - 100, 9, 200, 26, 20, TextAlignmentOptions.Center).color = ProgressionArt.Gold;
        float length = Width * .5f - 134;
        foreach (float x in new[] { 30f, Width * .5f + 104 }) {
            var rule = Place("Title Rule", panel, x, 22, length, 1.5f).gameObject.AddComponent<Image>();
            rule.color = new Color(ProgressionArt.Gold.r, ProgressionArt.Gold.g, ProgressionArt.Gold.b, .65f); rule.raycastTarget = false;
            var gem = Place("Title Gem", panel, x < Width * .5f ? x - 7 : x + length - 5, 17, 12, 12).gameObject.AddComponent<ChallengeGem>();
            gem.color = ProgressionArt.Gold; gem.raycastTarget = false;
        }
        for (int i = 0; i < MetaProgression.TrackedShown; i++) rows.Add(BuildRow(i));
        panel.gameObject.SetActive(false);
    }

    // HUD wood frame; cornerUnits sets how large the brass corners are drawn (the counter frame uses 85 at 106 high).
    static void Frame(GameObject target, float cornerUnits)
    {
        var sprite = Resources.Load<Sprite>("HUDv1/Vitals");
        var image = target.AddComponent<Image>();
        image.sprite = sprite ? sprite : HomeUi.Sprite("SelectionCardNormal");
        image.type = Image.Type.Sliced; image.raycastTarget = false;
        image.pixelsPerUnitMultiplier = image.sprite.rect.height / cornerUnits;
        image.material = HudV1Art.DarkWood(image.sprite);
    }

    Row BuildRow(int index)
    {
        float width = Width - Padding - PaddingRight;
        var row = new Row { rect = Place("Tracked " + index, panel, Padding, Header + index * RowHeight, width, RowHeight) };
        if (index > 0) {
            var rule = Place("Rule", row.rect, 46, 0, width - 46, 1).gameObject.AddComponent<Image>();
            rule.color = new Color(ProgressionArt.Gold.r, ProgressionArt.Gold.g, ProgressionArt.Gold.b, .22f); rule.raycastTarget = false;
        }
        row.icon = Place("Icon", row.rect, 2, 7, 42, 42).gameObject.AddComponent<Image>();
        row.icon.preserveAspect = true; row.icon.raycastTarget = false;
        row.gem = Place("Tier Gem", row.rect, 31, 35, 16, 16).gameObject.AddComponent<ChallengeGem>();
        row.gem.raycastTarget = false;
        float textLeft = 54, textWidth = width - textLeft;
        row.name = Label("Name", row.rect, "", textLeft, 6, textWidth - 96, 26, 21, TextAlignmentOptions.Left);
        row.counter = Label("Counter", row.rect, "", textLeft + textWidth - 94, 8, 94, 24, 18, TextAlignmentOptions.Right);
        row.counter.color = Muted;
        // Brass hairline around a dark groove, so an empty bar still reads as a bar on the dark wood.
        var track = Place("Progress Track", row.rect, textLeft, 38, textWidth, 10).gameObject.AddComponent<Image>();
        track.color = new Color(ProgressionArt.Gold.r, ProgressionArt.Gold.g, ProgressionArt.Gold.b, .45f); track.raycastTarget = false;
        var groove = Place("Groove", track.transform, 1, 1, textWidth - 2, 8).gameObject.AddComponent<Image>();
        groove.color = new Color(.05f, .03f, .02f, .92f); groove.raycastTarget = false;
        var inner = Place("Interior", track.transform, 1.5f, 1.5f, textWidth - 3, 7);
        row.fill = HomeUi.Rect("Progress Fill", inner, Vector2.zero, Vector2.zero);
        row.fill.anchorMin = Vector2.zero; row.fill.anchorMax = new Vector2(0, 1); row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;
        row.fill.gameObject.AddComponent<ChallengeGoldFill>().raycastTarget = false;
        return row;
    }

    void OnEnable() { MetaProgression.Changed += OnChanged; dirty = true; }
    void OnDisable() => MetaProgression.Changed -= OnChanged;
    void OnChanged() => dirty = true;

    void LateUpdate()
    {
        Fit();
        float now = Time.unscaledTime;
        // A rollover ends a round without a Changed event; look again every few seconds.
        if (now >= idleRefresh) dirty = true;
        if (!dirty || now < nextRefresh) return;
        dirty = false; nextRefresh = now + RefreshDelay; idleRefresh = now + IdleRefresh;
        Refresh();
    }

    public void Refresh()
    {
        Fit();
        List<MetaChallengeProgress> shown;
        try { shown = MetaProgression.TrackedForHud(); }
        catch (System.Exception e) { Debug.LogException(e); shown = new List<MetaChallengeProgress>(); }
        ShownCount = shown.Count;
        panel.gameObject.SetActive(shown.Count > 0);
        if (shown.Count == 0) return;
        for (int i = 0; i < rows.Count; i++) {
            var row = rows[i]; bool active = i < shown.Count;
            row.rect.gameObject.SetActive(active);
            if (active) Show(row, shown[i]);
        }
        panel.sizeDelta = new Vector2(Width, Header + shown.Count * RowHeight + Padding - 4);
    }

    static void Show(Row row, MetaChallengeProgress challenge)
    {
        row.icon.sprite = Icon(challenge);
        row.gem.color = Tone(challenge);
        row.name.text = challenge.name;
        row.counter.text = Number(challenge.current) + " / " + Number(challenge.target);
        float progress = challenge.target > 0 ? Mathf.Clamp01((float)challenge.current / challenge.target) : 0;
        row.fill.anchorMax = new Vector2(progress, 1);
    }

    static Color Tone(MetaChallengeProgress challenge)
    {
        if (challenge.kind == 2) return MedalColors[Mathf.Clamp(challenge.medal + 1, 1, 4)];
        if (challenge.kind == 3) return Muted;
        return challenge.tier >= 0 && challenge.tier < TierColors.Length ? TierColors[challenge.tier] : Muted;
    }

    internal static Sprite Icon(MetaChallengeProgress challenge)
    {
        var sprite = ChallengeArt.Icon(challenge.icon);
        if (!sprite && !string.IsNullOrEmpty(challenge.metric)) sprite = ChallengeArt.Icon(ChallengeCatalog.Icon(null, challenge.metric, -1));
        if (!sprite) sprite = ChallengeArt.Sprite(0);
        return sprite ? sprite : ProgressionArt.Sprite(0);
    }

    // Same rule as CompactHud.Fit: 1920×1080 reference, times the HUD size setting.
    void Fit()
    {
        var size = ((RectTransform)transform).rect.size;
        float hudScale = PlayerSettings.HudScale;
        if (size == fittedSize && Mathf.Approximately(hudScale, fittedHudScale)) return;
        fittedSize = size; fittedHudScale = hudScale;
        float scale = Mathf.Min(size.x / 1920f, size.y / 1080f) * (hudScale / PlayerSettings.DefaultHudScale);
        panel.localScale = Vector3.one * scale;
        panel.anchoredPosition = new Vector2(-Margin * scale, -Top * scale);
    }

    static string Number(int value) => value.ToString("N0", German);

    static RectTransform Place(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    static TextMeshProUGUI Label(string name, Transform parent, string value, float x, float y, float w, float h, float size, TextAlignmentOptions alignment)
    {
        var text = Place(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        text.text = value; text.fontSize = size; text.color = Cream; text.alignment = alignment; text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = size * .75f; text.fontSizeMax = size;
        text.outlineColor = new Color32(20, 11, 8, 255); text.outlineWidth = .12f;
        return text;
    }
}
