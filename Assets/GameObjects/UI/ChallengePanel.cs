using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Owned by the progression overview, which retains the pause and input lock.
public sealed class ChallengePanel : MonoBehaviour
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Color Muted = new Color32(193, 178, 152, 255);
    RectTransform layout, content;
    ScrollRect scroll;
    TextMeshProUGUI total;
    TextMeshProUGUI countdown;
    readonly System.Collections.Generic.List<Button> tabs = new();
    ChallengePeriod period;
    int countdownSecond = -1;
    long periodEnd;
    Action onBack;
    bool refresh, closing;
    float height;

    internal static ChallengePanel Create(Transform owner, Action onBack)
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
        Text("Title", layout, "Herausforderungen", new Vector2(-180, 356), new Vector2(935, 70), 48, TextAlignmentOptions.Left);
        total = Text("Completed Count", layout, "", new Vector2(510, 350), new Vector2(290, 40), 24, TextAlignmentOptions.Right);
        total.color = ProgressionArt.Gold;
        countdown = Text("Countdown", layout, "", new Vector2(185, 350), new Vector2(315, 40), 23, TextAlignmentOptions.Right);
        var hourglass=HomeUi.Image("Hourglass",countdown.transform,new Vector2(-150,0),new Vector2(16,27));
        hourglass.sprite=ChallengeArt.Sprite(8);hourglass.preserveAspect=true;
        for(int i=0;i<3;i++) {
            var selected=(ChallengePeriod)i;
            var tab=HomeUi.Button("Challenge Tab "+selected,layout,new[]{"Täglich","Wöchentlich","Langzeit"}[i],
                new Vector2(-540+i*248,295),new Vector2(237,46),()=>SetPeriod(selected));
            UpgradeTileArt.StyleTab(tab);
            tab.GetComponent<HomeButtonFeedback>().selectionManaged=true;
            tabs.Add(tab);
        }
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
        for(int i=0;i<tabs.Count;i++) tabs[i].GetComponent<HomeButtonFeedback>().primary=i==(int)period;
        Rebuild();scroll.StopMovement();scroll.verticalNormalizedPosition=1;
        UpdateCountdown();
    }

    void UpdateCountdown()
    {
        countdown.gameObject.SetActive(period!=ChallengePeriod.Lifetime);
        if(period==ChallengePeriod.Lifetime)return;
        var remaining=MetaProgression.ChallengeRemaining(period);
        int seconds=(int)Math.Ceiling(remaining.TotalSeconds);
        if(countdownSecond==seconds)return;
        countdownSecond=seconds;
        countdown.text=period==ChallengePeriod.Weekly
            ? $"Neu in {remaining.Days} T {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"Neu in {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        countdown.ForceMeshUpdate();
        ((RectTransform)countdown.transform.Find("Hourglass")).anchoredPosition=
            new Vector2(countdown.textBounds.min.x-17,0);
        // A rollover increases the remaining time; rebuild also while the game is paused.
        long end=MetaProgression.Profile.challengePeriods.Find(p=>p.period==(int)period)?.endUtc??0;
        if(periodEnd!=end){periodEnd=end;refresh=true;}
    }

    void Rebuild()
    {
        float position = scroll.verticalNormalizedPosition;
        foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        var challenges = MetaProgression.GetChallenges(period);
        int completed = 0; height = 12;
        foreach (var challenge in challenges) if (challenge.completed) completed++;
        total.text = completed + " / " + challenges.Length + " abgeschlossen";
        for(int index=0;index<challenges.Length;index++) {
            var tile=At("Challenge "+challenges[index].id,new Vector2((index%3-1)*446,-12-index/3*292-138),new Vector2(420,276));
            BuildTile(tile,challenges[index]);
        }
        height=((challenges.Length+2)/3)*292+4;
        content.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, height));
        Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = position;
    }

    RectTransform At(string name, Vector2 position, Vector2 size)
    {
        var rect = HomeUi.Rect(name, content, position, size);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); return rect;
    }

    static void BuildTile(RectTransform tile, MetaChallengeProgress challenge)
    {
        var frame = HomeUi.Image("Frame", tile, Vector2.zero, tile.sizeDelta, "SelectionCardNormal");
        frame.pixelsPerUnitMultiplier = 5.5f;
        ChallengeArt.DarkWood(frame);
        string metric = challenge.metric ?? MetaProgressionCatalog.Challenge(challenge.id)?.metric;
        var icon = HomeUi.Image("Icon", tile, new Vector2(-12, 1), new Vector2(190, 146));
        icon.sprite = Icon(metric); icon.preserveAspect = true;
        var plaque=HomeUi.Image("XP Plaque",tile,new Vector2(137,61),new Vector2(112,34),"SelectionCardNormal");
        plaque.pixelsPerUnitMultiplier=14;ChallengeArt.DarkWood(plaque);
        Text("Reward", plaque.transform, "+" + challenge.xp.ToString("N0", German) + " XP", Vector2.zero, new Vector2(98,28), 21, TextAlignmentOptions.Center).color = ProgressionArt.Gold;
        var name = Text("Name", tile, challenge.name, new Vector2(0, 106), new Vector2(366, 38), 28, TextAlignmentOptions.Center);
        name.fontSizeMin=24;
        Text("Progress", tile, Counter(challenge, metric), new Vector2(0, -80), new Vector2(370, 29), 21, TextAlignmentOptions.Center);
        var track = HomeUi.Image("Progress Track", tile, new Vector2(0, -109), new Vector2(376, 39));
        track.sprite=ChallengeArt.Sprite(6);track.type=Image.Type.Sliced;track.pixelsPerUnitMultiplier=2.5f;
        var interior=HomeUi.Rect("Interior",track.transform,Vector2.zero,new Vector2(308,16));
        var fillRect=HomeUi.Rect("Progress Fill",interior,Vector2.zero,Vector2.zero);
        fillRect.anchorMin=Vector2.zero;
        fillRect.anchorMax=new Vector2(challenge.target>0?Mathf.Clamp01((float)challenge.current/challenge.target):0,1);
        fillRect.offsetMin=fillRect.offsetMax=Vector2.zero;
        fillRect.gameObject.AddComponent<ChallengeGoldFill>().raycastTarget=false;
        if (challenge.completed) {
            var mark=HomeUi.Image("Completed",tile,new Vector2(175,-104),new Vector2(51,51));
            mark.sprite=ChallengeArt.Sprite(7);mark.preserveAspect=true;
        }
    }

    static Sprite Icon(string metric)
    {
        if (metric.StartsWith("total-", StringComparison.Ordinal)) metric=metric.Substring(6);
        int part=metric switch {"resources"=>0,"depth"=>1,"crafted"=>2,"copper"=>3,"sales"=>4,"torches"=>5,_=>-1};
        if(part>=0)return ChallengeArt.Sprite(part);
        Item item = metric switch {
            "depth" => Item.IronLadder, "rare" => Item.Diamond, "victory" => Item.Ultronium,
            "exotics" => Item.PercussionHammer, "regions" or "discoveries" => Item.Torche,
            "crafts" => Item.IronPickaxe, "resources" => Item.Gold, "coal"=>Item.Coal, "iron"=>Item.Iron,
            "silver"=>Item.Silver,"gold"=>Item.Gold,"ladders"=>Item.IronLadder, _ => Item.CopperPickaxe
        };
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (catalog) foreach (var entry in catalog.items) if (entry && entry.item == item && entry.icon) return entry.icon;
        return ProgressionArt.Sprite(0);
    }

    static string Counter(MetaChallengeProgress challenge, string metric)
    {
        if (metric.StartsWith("total-", StringComparison.Ordinal)) metric=metric.Substring(6);
        string unit = metric switch {
            "depth" => "Blöcke Tiefe", "regions" => "Gebiete", "variety" => "Erzarten", "resources" => "Erze",
            "crafted"=>"Gegenstände hergestellt","copper"=>"Kupfer","sales"=>"$ verkauft","torches"=>"Fackeln hergestellt",
            "crafts" => "Herstellungen", "exotics" => "Exotics hergestellt", "rare" => "seltene Erzarten",
            "discoveries" => "Entdeckungen", "victory" => "Siege",
            "coal"=>"Kohle","iron"=>"Eisen","silver"=>"Silber","gold"=>"Gold","ladders"=>"Leitern hergestellt", _ => ""
        };
        return challenge.current.ToString("N0", German) + " / " + challenge.target.ToString("N0", German) + " " + unit;
    }

    static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float font, TextAlignmentOptions alignment)
    {
        var text = ProgressionArt.Text(name, parent, value, position, size, font);
        text.richText = false; text.alignment = alignment; return text;
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
        gameObject.SetActive(false); Destroy(gameObject); onBack?.Invoke();
    }
    void OnDestroy() => MetaProgression.Changed -= OnProgressChanged;
}
