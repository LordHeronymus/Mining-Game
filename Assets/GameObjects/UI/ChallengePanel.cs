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
        total = Text("Completed Count", layout, "", new Vector2(492, 350), new Vector2(318, 40), 24, TextAlignmentOptions.Right);
        total.color = ProgressionArt.Gold;
        HomeUi.Image("Header Rule", layout, new Vector2(0, 307), new Vector2(1310, 1)).color = new Color32(132, 89, 43, 180);

        var viewport = HomeUi.Rect("Challenges Viewport", layout, new Vector2(0, -3), new Vector2(1320, 580));
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 12);
        var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        content = HomeUi.Rect("Challenge Tiles", viewport, Vector2.zero, Vector2.zero);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 48;
        var track = HomeUi.Image("Scrollbar", layout, new Vector2(677, -3), new Vector2(7, 580));
        track.color = new Color(0, 0, 0, .4f); track.raycastTarget = true;
        var area = HomeUi.Rect("Sliding Area", track.transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(area);
        var handle = HomeUi.Image("Handle", area, Vector2.zero, Vector2.zero); HomeUi.Stretch(handle.rectTransform);
        handle.color = ProgressionArt.Gold; handle.raycastTarget = true;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop; bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var back = HomeUi.Button("Challenges Back", layout, "Zurück", new Vector2(-483, -370), new Vector2(340, 70), Close);
        MetaProgression.Changed += OnProgressChanged;
        Rebuild();
        scroll.verticalNormalizedPosition = 1;
        HomeUi.Fit(layout, new Vector2(1600, 980));
        ProgressionArt.Navigation(transform);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }

    void Rebuild()
    {
        float position = scroll.verticalNormalizedPosition;
        foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        var challenges = MetaProgression.GetChallenges();
        int completed = 0; height = 12;
        foreach (var challenge in challenges) if (challenge.completed) completed++;
        total.text = completed + " / " + challenges.Length + " abgeschlossen";
        for (int category = 0; category < 2; category++)
        {
            int count = 0;
            foreach (var challenge in challenges) if (challenge.permanent == (category == 1)) count++;
            if (count == 0) continue;
            var heading = At("Section " + category, new Vector2(0, -height - 18), new Vector2(1310, 36));
            Text("Heading", heading, category == 0 ? "Run-Ziele" : "Meilensteine", new Vector2(-375, 0), new Vector2(550, 36), 27, TextAlignmentOptions.Left).color = ProgressionArt.Gold;
            height += 48;
            int index = 0;
            foreach (var challenge in challenges)
            {
                if (challenge.permanent != (category == 1)) continue;
                var tile = At("Challenge " + challenge.id, new Vector2((index % 3 - 1) * 446, -height - index / 3 * 240 - 110), new Vector2(420, 220));
                BuildTile(tile, challenge); index++;
            }
            height += ((count + 2) / 3) * 240 + 14;
        }
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
        var frame = HomeUi.Image("Frame", tile, Vector2.zero, tile.sizeDelta, challenge.completed ? "SelectionCardHover" : "SelectionCardNormal");
        frame.pixelsPerUnitMultiplier = 5.5f;
        string metric = MetaProgressionCatalog.Challenge(challenge.id)?.metric;
        var medallion = ProgressionArt.Image("Icon Setting", tile, new Vector2(-147, 61), new Vector2(71, 71), 2);
        medallion.color = new Color(.85f, .85f, .85f, 1);
        var icon = HomeUi.Image("Icon", tile, new Vector2(-147, 62), new Vector2(53, 53));
        icon.sprite = Icon(metric); icon.preserveAspect = true;
        Text("Reward", tile, "+" + challenge.xp.ToString("N0", German) + " XP", new Vector2(89, 62), new Vector2(179, 39), 29, TextAlignmentOptions.Right).color = ProgressionArt.Gold;
        var name = Text("Name", tile, challenge.name, new Vector2(0, 4), new Vector2(352, 63), 29, TextAlignmentOptions.Left);
        name.textWrappingMode = TextWrappingModes.Normal; name.fontSizeMin = 24;
        Text("Progress", tile, Counter(challenge, metric), new Vector2(challenge.completed ? -19 : 0, -49),
            new Vector2(challenge.completed ? 314 : 352, 30), 21, TextAlignmentOptions.Left).color = challenge.completed ? ProgressionArt.Gold : Muted;
        var track = HomeUi.Image("Progress Track", tile, new Vector2(0, -81), new Vector2(352, 10));
        track.color = new Color32(12, 9, 7, 255);
        var fill = HomeUi.Image("Progress Fill", track.transform, Vector2.zero, Vector2.zero);
        fill.color = ProgressionArt.Gold; fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(challenge.target > 0 ? Mathf.Clamp01((float)challenge.current / challenge.target) : 0, 1);
        fill.rectTransform.offsetMin = new Vector2(0, 2); fill.rectTransform.offsetMax = new Vector2(0, -2);
        if (challenge.completed) {
            var mark = HomeUi.Rect("Completed", tile, new Vector2(163, -49), new Vector2(24, 21)).gameObject.AddComponent<ProgressionSymbol>();
            mark.color = ProgressionArt.Gold; mark.raycastTarget = false;
        }
    }

    static Sprite Icon(string metric)
    {
        Item item = metric switch {
            "depth" => Item.IronLadder, "rare" => Item.Diamond, "victory" => Item.Ultronium,
            "exotics" => Item.PercussionHammer, "regions" or "discoveries" => Item.Torche,
            "crafts" => Item.IronPickaxe, "resources" => Item.Gold, _ => Item.CopperPickaxe
        };
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (catalog) foreach (var entry in catalog.items) if (entry && entry.item == item && entry.icon) return entry.icon;
        return ProgressionArt.Sprite(0);
    }

    static string Counter(MetaChallengeProgress challenge, string metric)
    {
        string unit = metric switch {
            "depth" => "Blöcke Tiefe", "regions" => "Gebiete", "variety" => "Erzarten", "resources" => "Rohstoffe",
            "crafts" => "Herstellungen", "exotics" => "Exotics hergestellt", "rare" => "seltene Erzarten",
            "discoveries" => "Entdeckungen", "victory" => "Siege", _ => ""
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
