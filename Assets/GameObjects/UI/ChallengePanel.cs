using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The owner retains the pause and input lock while this panel is open.
public sealed class ChallengePanel : MonoBehaviour
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Color Muted = new Color32(193, 178, 152, 255);
    // Tab order must not follow the enum: Monthly was appended after Lifetime.
    static readonly ChallengePeriod[] Periods = { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly, ChallengePeriod.Lifetime };
    static readonly string[] TabNames = { "Täglich", "Wöchentlich", "Monatlich", "Langzeit" };
    static readonly string[] TierNames = { "Leicht", "Mittel", "Schwer", "XXL", "Projekt" };
    static readonly Color[] TierColors = { new Color32(112, 196, 96, 255), new Color32(86, 156, 232, 255), new Color32(170, 108, 230, 255), ProgressionArt.Gold, ProgressionArt.Gold };
    static readonly string[] MedalNames = { "", "Bronze", "Silber", "Gold", "Platin" };
    static readonly Color[] MedalColors = { Muted, new Color32(205, 127, 50, 255), new Color32(196, 204, 212, 255), ProgressionArt.Gold, new Color32(229, 228, 226, 255) };
    RectTransform layout, content, bonusBar;
    ScrollRect scroll;
    TextMeshProUGUI countdown, bonus;
    readonly System.Collections.Generic.List<Button> tabs = new();
    ChallengePeriod period;
    int countdownSecond = -1;
    long periodEnd;
    Action onBack;
    bool refresh, closing;
    float height;

    public static ChallengePanel Create(Transform owner, Action onBack)
    {
        var root = HomeUi.Panel("Challenges", owner);
        var panel = root.gameObject.AddComponent<ChallengePanel>();
        panel.onBack = onBack;
        panel.Build();
        return panel;
    }

    void Build()
    {
        layout = HomeUi.Rect("Challenges Layout", transform, Vector2.zero, new Vector2(1540, 940));
        HomeUi.Image("Board Backing", layout, Vector2.zero, new Vector2(1410, 830)).color = new Color32(23, 12, 7, 255);
        var board = HomeUi.Image("Board", layout, Vector2.zero, new Vector2(1500, 900), "Panel");
        board.pixelsPerUnitMultiplier = 1.5f; board.raycastTarget = true;
        Text("Title", layout, "Herausforderungen", new Vector2(-388, 356), new Vector2(520, 70), 48, TextAlignmentOptions.Left);
        countdown = Text("Countdown", layout, "", new Vector2(497, 350), new Vector2(316, 40), 23, TextAlignmentOptions.Right);
        var hourglass=HomeUi.Image("Hourglass",countdown.transform,new Vector2(-150,0),new Vector2(16,27));
        hourglass.sprite=ChallengeArt.Sprite(8);hourglass.preserveAspect=true;
        for(int i=0;i<Periods.Length;i++) {
            var selected=Periods[i];
            var tab=HomeUi.Button("Challenge Tab "+selected,layout,TabNames[i],new Vector2(-540+i*248,295),new Vector2(237,46),()=>SetPeriod(selected));
            UpgradeTileArt.StyleTab(tab);
            tab.GetComponent<HomeButtonFeedback>().selectionManaged=true;
            tabs.Add(tab);
        }
        bonusBar = HomeUi.Rect("Bonus Bar", layout, new Vector2(497, 295), new Vector2(316, 46));
        bonus = Text("Bonus", bonusBar, "", new Vector2(0, 9), new Vector2(316, 28), 21, TextAlignmentOptions.Center);
        bonus.color = ProgressionArt.Gold;
        HomeUi.Image("Header Rule", layout, new Vector2(0, 269), new Vector2(1310, 2)).color = ProgressionArt.Gold;

        var viewport = HomeUi.Rect("Challenges Viewport", layout, new Vector2(0, -36), new Vector2(1320, 588));
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 12);
        var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        content = HomeUi.Rect("Challenge Tiles", viewport, Vector2.zero, Vector2.zero);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 48;
        var track = HomeUi.Image("Scrollbar", layout, new Vector2(677, -36), new Vector2(7, 588));
        track.color = new Color(0, 0, 0, .4f); track.raycastTarget = true;
        var area = HomeUi.Rect("Sliding Area", track.transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(area);
        var handle = HomeUi.Image("Handle", area, Vector2.zero, Vector2.zero); HomeUi.Stretch(handle.rectTransform);
        handle.color = ProgressionArt.Gold; handle.raycastTarget = true;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop; bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var back = HomeUi.Button("Challenges Back", layout, "Zurück", new Vector2(-483, -370), new Vector2(340, 70), Close);
        Text("Track Hint", layout, "Mit dem Stern verfolgst du Herausforderungen im Spiel.",
            new Vector2(200, -370), new Vector2(900, 30), 20, TextAlignmentOptions.Right).color = Muted;
        MetaProgression.Changed += OnProgressChanged;
        SetPeriod(ChallengePeriod.Daily);
        scroll.verticalNormalizedPosition = 1;
        HomeUi.Fit(layout, new Vector2(1600, 980));
        ProgressionArt.Navigation(transform);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }

    void SetPeriod(ChallengePeriod value)
    {
        period=value;countdownSecond=-1;
        for(int i=0;i<tabs.Count;i++) tabs[i].GetComponent<HomeButtonFeedback>().primary=Periods[i]==period;
        // Read the end first: a rollover during Rebuild then shows up as a changed end and rebuilds once more.
        periodEnd=MetaProgression.ChallengePeriodEnd(period);Rebuild();
        scroll.StopMovement();scroll.verticalNormalizedPosition=1;
        UpdateCountdown();
    }

    void UpdateCountdown()
    {
        countdown.gameObject.SetActive(period!=ChallengePeriod.Lifetime);
        if(period==ChallengePeriod.Lifetime)return;
        var remaining=MetaProgression.ChallengeRemaining(period);
        // A rollover draws a new round with a later end; rebuild also while the game is paused.
        // Checked before the same-second shortcut: a jump of exactly one period leaves the remaining time unchanged.
        long end=MetaProgression.ChallengePeriodEnd(period);
        if(periodEnd!=end){periodEnd=end;refresh=true;}
        int seconds=(int)Math.Ceiling(remaining.TotalSeconds);
        if(countdownSecond==seconds)return;
        countdownSecond=seconds;
        countdown.text=period==ChallengePeriod.Daily
            ? $"Neu in {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"Neu in {remaining.Days} T {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        countdown.ForceMeshUpdate();
        ((RectTransform)countdown.transform.Find("Hourglass")).anchoredPosition=
            new Vector2(countdown.textBounds.min.x-17,0);
    }

    void Rebuild()
    {
        float position = scroll.verticalNormalizedPosition;
        for (int i = content.childCount - 1; i >= 0; i--) if (content.GetChild(i).gameObject.activeSelf) Discard(content.GetChild(i).gameObject);
        var challenges = MetaProgression.GetChallenges(period) ?? Array.Empty<MetaChallengeProgress>();
        var project = period == ChallengePeriod.Monthly ? MetaProgression.GetMonthlyProject() : null;
        // The project spans the first row; the regular tiles follow in the next one.
        int slot = 0;
        if (project != null) { BuildProject(At("Challenge " + project.id, new Vector2(0, -150), new Vector2(1312, 276)), project); slot = 3; }
        foreach (var challenge in challenges) {
            if (challenge == null || project != null && challenge.kind == 1) continue;
            BuildTile(At("Challenge " + challenge.id, new Vector2((slot % 3 - 1) * 446, -12 - slot / 3 * 292 - 138), new Vector2(420, 276)), challenge);
            slot++;
        }
        height = ((slot + 2) / 3) * 292 + 4;
        UpdateBonus();
        content.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, height));
        Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = position;
    }

    void UpdateBonus()
    {
        for (int i = bonusBar.childCount - 1; i >= 0; i--) {
            var child = bonusBar.GetChild(i);
            if (child != bonus.transform && child.gameObject.activeSelf) Discard(child.gameObject);
        }
        var state = MetaProgression.ChallengeBonus(period) ?? new MetaChallengeBonus();
        int done = Mathf.Max(0, state.done), required = Mathf.Max(0, state.required);
        string xp = Xp(state.xp);
        bonus.text = period == ChallengePeriod.Lifetime ? done + " / " + required + " Stufen erreicht"
            : state.awarded && state.xp > 0 ? "Bonus erhalten · " + xp
            : done + " / " + required + " erledigt" + (state.xp > 0 ? " → " + xp : "");
        bool pips = period != ChallengePeriod.Lifetime && required > 0 && required <= 16;
        bonus.rectTransform.anchoredPosition = new Vector2(0, pips ? 9 : 0);
        if (!pips) return;
        float step = Mathf.Min(44, 300f / required);
        for (int i = 0; i < required; i++) {
            var socket = HomeUi.Image("Bonus Pip " + i, bonusBar, new Vector2((i - (required - 1) * .5f) * step, -14), new Vector2(step - 3, 12));
            socket.sprite = UpgradeTileArt.RankSprite(false);
            if (i < done) HomeUi.Image("Fill", socket.transform, Vector2.zero, new Vector2(step - 3, 12)).sprite = UpgradeTileArt.RankSprite(true);
        }
    }

    RectTransform At(string name, Vector2 position, Vector2 size)
    {
        var rect = HomeUi.Rect(name, content, position, size);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); return rect;
    }

    static void BuildTile(RectTransform tile, MetaChallengeProgress challenge)
    {
        Frame(tile);
        string label = null; Color tone = Muted; bool tint = false;
        int stages = Mathf.Max(1, challenge.stages), reached = Mathf.Clamp(challenge.medal, 0, stages);
        if (challenge.kind == 2) {
            // Rim shows the medal already earned, the plaque the medal that the shown XP pays for.
            int next = Mathf.Min(reached + 1, stages);
            label = MedalNames[Mathf.Clamp(next, 1, 4)]; tone = MedalColors[Mathf.Clamp(next, 1, 4)]; tint = true;
            if (reached > 0) Rim(tile, MedalColors[Mathf.Min(reached, 4)]);
        } else if (challenge.kind == 3) label = "Run-Ziel";
        else if (challenge.tier >= 0 && challenge.tier < TierNames.Length) {
            label = TierNames[challenge.tier]; tone = TierColors[challenge.tier]; tint = true; Rim(tile, tone);
        }
        var icon = HomeUi.Image("Icon", tile, new Vector2(-20, 0), new Vector2(180, 140));
        icon.sprite = Icon(challenge); icon.preserveAspect = true;
        Plaque(tile, new Vector2(137, 58), challenge.xp, label, tone, tint);
        if (challenge.kind == 2)
            Text("Stage Count", tile, "Stufe " + reached + "/" + stages, new Vector2(137, 14), new Vector2(124, 28), 20, TextAlignmentOptions.Center)
                .color = MedalColors[Mathf.Min(reached, 4)];
        var name = Text("Name", tile, challenge.name, new Vector2(0, 106), new Vector2(366, 38), 28, TextAlignmentOptions.Center);
        name.fontSizeMin=24;
        Text("Progress", tile, Counter(challenge.current, challenge.target, Unit(challenge)), new Vector2(0, -80), new Vector2(370, 29), 21, TextAlignmentOptions.Center);
        Track(tile, new Vector2(0, -109), 376, challenge.target > 0 ? (float)challenge.current / challenge.target : 0);
        if (challenge.completed) Completed(tile, new Vector2(175, -104));
        else TrackStar(tile, new Vector2(-170, 64), challenge.track);
    }

    static void BuildProject(RectTransform tile, MetaChallengeProgress project)
    {
        Frame(tile); Rim(tile, TierColors[(int)ChallengeTier.Project]);
        if (!project.completed) TrackStar(tile, new Vector2(-616, 96), project.track);
        var icon = HomeUi.Image("Icon", tile, new Vector2(-540, 8), new Vector2(190, 146));
        icon.sprite = Icon(project); icon.preserveAspect = true;
        Text("Kicker", tile, "Monatsprojekt", new Vector2(-540, -98), new Vector2(200, 30), 22, TextAlignmentOptions.Center).color = ProgressionArt.Gold;
        Text("Name", tile, project.name, new Vector2(105, 106), new Vector2(1000, 38), 28, TextAlignmentOptions.Center).fontSizeMin = 24;
        string unit = Unit(project);
        var targets = project.stageTargets != null && project.stageTargets.Length > 0 ? project.stageTargets : new[] { project.target };
        var rewards = project.stageTargets != null && project.stageTargets.Length > 0 ? project.stageXp : new[] { project.xp };
        int count = Mathf.Min(targets.Length, 5), previous = 0;
        Text("Progress", tile, Counter(project.current, targets[count - 1], unit), new Vector2(105, -104), new Vector2(700, 29), 21, TextAlignmentOptions.Center);
        // Stages share x -410..620 with 26-unit gaps (3 stages: 326 wide at -247, 105, 457).
        float width = (1030f - 26 * (count - 1)) / count, inner = width - 26;
        for (int i = 0; i < count; i++) {
            int target = targets[i]; float x = -410 + width * .5f + i * (width + 26);
            var stage = HomeUi.Rect("Stage " + (i + 1), tile, new Vector2(x, 0), new Vector2(width, 200));
            Text("Stage Name", stage, "Etappe " + (i + 1), new Vector2(0, 66), new Vector2(inner, 30), 22, TextAlignmentOptions.Center);
            Text("Target", stage, Number(target) + (unit.Length > 0 ? " " + unit : ""), new Vector2(0, 36), new Vector2(inner, 28), 20, TextAlignmentOptions.Center).color = Muted;
            Plaque(stage, new Vector2(0, 2), rewards != null && i < rewards.Length ? rewards[i] : 0, null, TierColors[Mathf.Min(i, 2)], true);
            Track(stage, new Vector2(0, -52), inner, target > previous ? (float)(project.current - previous) / (target - previous) : project.current >= target ? 1 : 0);
            if (i < project.stagesDone || project.current >= target) Completed(stage, new Vector2(width * .5f - 28, -48));
            if (i < count - 1) Text("Arrow", tile, "→", new Vector2(x + width * .5f + 13, -52), new Vector2(30, 32), 24, TextAlignmentOptions.Center).color = ProgressionArt.Gold;
            previous = target;
        }
    }

    static void Frame(RectTransform tile)
    {
        var frame = HomeUi.Image("Frame", tile, Vector2.zero, tile.sizeDelta, "SelectionCardNormal");
        frame.pixelsPerUnitMultiplier = 5.5f;
        ChallengeArt.DarkWood(frame);
    }

    // Inset 16 puts the line on the inner wood lip of the card frame.
    static void Rim(RectTransform tile, Color color)
    {
        var rim = HomeUi.Rect("Tier Rim", tile, Vector2.zero, tile.sizeDelta - new Vector2(32, 32)).gameObject.AddComponent<ChallengeTierRim>();
        rim.color = color; rim.raycastTarget = false;
    }

    static void Plaque(Transform parent, Vector2 position, int xp, string label, Color tone, bool tint)
    {
        bool tier = !string.IsNullOrEmpty(label);
        var plaque = HomeUi.Image("XP Plaque", parent, position, new Vector2(124, tier ? 56 : 34), "SelectionCardNormal");
        plaque.pixelsPerUnitMultiplier = 14; ChallengeArt.DarkWood(plaque);
        if (tint) plaque.color = Color.Lerp(Color.white, tone, .6f);
        if (tier) {
            var text = Text("Tier", plaque.transform, label, new Vector2(0, 13), new Vector2(108, 22), 16, TextAlignmentOptions.Center);
            text.fontSizeMin = 12; text.color = tone;
        }
        Text("Reward", plaque.transform, Xp(xp), new Vector2(0, tier ? -9 : 0), new Vector2(110, 28), 21, TextAlignmentOptions.Center).color = ProgressionArt.Gold;
    }

    static void Track(Transform parent, Vector2 position, float width, float progress)
    {
        var track = HomeUi.Image("Progress Track", parent, position, new Vector2(width, 39));
        track.sprite=ChallengeArt.Sprite(6);track.type=Image.Type.Sliced;track.pixelsPerUnitMultiplier=2.5f;
        var interior=HomeUi.Rect("Interior",track.transform,Vector2.zero,new Vector2(width-68,16));
        var fill=HomeUi.Rect("Progress Fill",interior,Vector2.zero,Vector2.zero);
        fill.anchorMin=Vector2.zero;fill.anchorMax=new Vector2(Mathf.Clamp01(progress),1);
        fill.offsetMin=fill.offsetMax=Vector2.zero;
        fill.gameObject.AddComponent<ChallengeGoldFill>().raycastTarget=false;
    }

    // Star toggle: a marked challenge is followed in the HUD (ChallengeTrackerHud). The panel rebuilds on MetaProgression.Changed.
    static void TrackStar(Transform parent, Vector2 position, string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        bool tracked = MetaProgression.IsTracked(key);
        var hit = HomeUi.Image("Track Star", parent, position, new Vector2(52, 52));
        hit.color = Color.clear; hit.raycastTarget = true;
        var star = HomeUi.Rect("Star", hit.transform, Vector2.zero, new Vector2(38, 38)).gameObject.AddComponent<ChallengeStar>();
        star.Filled = tracked; star.raycastTarget = false;
        var button = hit.gameObject.AddComponent<Button>();
        button.targetGraphic = star; button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = colors.selectedColor = new Color(1, .93f, .78f);
        colors.pressedColor = new Color(.78f, .74f, .68f); colors.fadeDuration = .08f; button.colors = colors;
        button.onClick.AddListener(() => MetaProgression.SetTracked(key, !MetaProgression.IsTracked(key)));
    }

    static void Completed(Transform parent, Vector2 position)
    {
        var mark=HomeUi.Image("Completed",parent,position,new Vector2(51,51));
        mark.sprite=ChallengeArt.Sprite(7);mark.preserveAspect=true;
    }

    static Sprite Icon(MetaChallengeProgress challenge)
    {
        var sprite = ChallengeArt.Icon(challenge.icon);
        if (!sprite && !string.IsNullOrEmpty(challenge.metric)) sprite = ChallengeArt.Icon(ChallengeCatalog.Icon(null, challenge.metric, -1));
        if (!sprite) sprite = ChallengeArt.Sprite(0);
        return sprite ? sprite : ProgressionArt.Sprite(0);
    }

    static string Unit(MetaChallengeProgress challenge)
    {
        if (!string.IsNullOrEmpty(challenge.unit)) return challenge.unit;
        if (!string.IsNullOrEmpty(challenge.subject)) return challenge.subject;
        string metric = challenge.metric ?? "";
        string unit = metric.Length > 0 ? ChallengeCatalog.Unit(null, metric, -1) : null;
        if (!string.IsNullOrEmpty(unit)) return unit;
        if (metric.StartsWith("total-", StringComparison.Ordinal)) metric = metric.Substring(6);
        return metric switch {
            "blocks" => "Blöcke abgebaut", "resources" => "Erze", "depth" => "Blöcke Tiefe", "regions" => "Gebiete", "discoveries" => "Entdeckungen",
            "crafted" => "Gegenstände hergestellt", "crafts" => "Herstellungen", "exotics" => "Exotics hergestellt", "sales" => "$ Verkaufserlös",
            "artifacts" => "Artefakte geborgen", "variety" => "Erzarten", "rare" => "seltene Erzarten", "victory" => "Siege", _ => ""
        };
    }

    static string Counter(int current, int target, string unit) => Number(current) + " / " + Number(target) + (unit.Length > 0 ? " " + unit : "");
    static string Number(int value) => value.ToString("N0", German);
    static string Xp(int xp) => "+" + Number(xp) + " XP";

    static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float font, TextAlignmentOptions alignment)
    {
        var text = ProgressionArt.Text(name, parent, value, position, size, font);
        text.richText = false; text.alignment = alignment; return text;
    }
    // Hidden at once because tests count active children; edit-mode previews cannot use Destroy.
    static void Discard(GameObject target)
    {
        target.SetActive(false);
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }
    void OnProgressChanged() => refresh = true;
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(1600, 980));
    void Update()
    {
        UpdateCountdown();
        if (refresh) { refresh = false; Rebuild(); }
        float distance = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
        if (distance > 0) {
            float move = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + move * 400 * Time.unscaledDeltaTime / distance);
            if (Input.GetKeyDown(KeyCode.PageDown)) scroll.verticalNormalizedPosition -= scroll.viewport.rect.height / distance;
            if (Input.GetKeyDown(KeyCode.PageUp)) scroll.verticalNormalizedPosition += scroll.viewport.rect.height / distance;
        }
        if (Input.GetKeyDown(KeyCode.Escape) && RunPauseMenu.InputConsumedFrame != Time.frameCount) { RunPauseMenu.ConsumeInput(); Close(); }
    }
    public void Close()
    {
        if (closing) return; closing = true;
        // OnDestroy is not sent in edit mode, so unsubscribe here as well.
        MetaProgression.Changed -= OnProgressChanged;
        Discard(gameObject); onBack?.Invoke();
    }
    void OnDestroy() => MetaProgression.Changed -= OnProgressChanged;
}
