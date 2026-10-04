using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Profile progress at home; the current run's fixed loadout while paused.</summary>
public sealed class MetaProgressionPanel : MonoBehaviour
{
    sealed class UpgradeRow
    {
        public MetaUpgradeDefinition definition;
        public TextMeshProUGUI effect, rank, cost;
        public Button buy;
        public RectTransform tile;
        public Image[] segments;
    }

    static readonly Color Gold = new Color32(222, 174, 91, 255);
    static readonly Color Muted = new Color32(190, 175, 147, 255);
    static readonly Color Line = new Color32(132, 89, 43, 155);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    public static bool IsOpen { get; private set; }
    public bool IsReadOnly => readOnly;
    RectTransform layout, list;
    ScrollRect scroll;
    TextMeshProUGUI powerText, comfortText, status, runXpText;
    readonly List<UpgradeRow> upgrades = new();
    Button refund, upgradesBack;
    bool readOnly, pendingRefresh, closing;
    float previousScale;
    GameObject previousSelection, lastSelection;
    RectTransform overview;
    ProgressionTimeline timeline;
    TextMeshProUGUI overviewPoints;
    bool showingOverview;
    bool comfortCategory;
    readonly List<Button> categoryTabs = new();
    ChallengePanel challengesPanel;
    BlueprintCollectionPanel blueprintsPanel;
    public static bool OpenUpgradesOnHome { get; set; }
    public bool ShowingOverview => showingOverview;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsOpen = false; OpenUpgradesOnHome = false; }

    public static string RunSummary()
    {
        string summary = "+" + (MetaProgression.CurrentRun?.earnedXp ?? 0).ToString("N0", German) + " XP  ·  Level " + MetaProgression.Level;
        if (MetaProgression.Level < MetaProgressionCatalog.MaxLevel)
            summary += "  ·  " + MetaProgression.XpIntoCurrentLevel.ToString("N0", German) + " / " + MetaProgression.XpForNextLevel.ToString("N0", German) + " XP";
        return summary;
    }

    public static MetaProgressionPanel Show(Transform parent, bool readOnly = false)
    {
        if (!Application.isPlaying || RunNavigation.IsTransitioning || LoadingProgress.Active || !parent) return null;
        var existing = FindFirstObjectByType<MetaProgressionPanel>();
        if (existing) return existing;
        var root = HomeUi.Panel("Meta Progression", parent);
        var panel = root.gameObject.AddComponent<MetaProgressionPanel>();
        panel.readOnly = readOnly || !MainMenuController.IsVisible;
        panel.Build();
        return panel;
    }

    void Build()
    {
        IsOpen = true;
        previousScale = Time.timeScale;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        Time.timeScale = 0;
        GameplayInputBlocker.SetBlocked(this, true);
        transform.SetAsLastSibling();
        var shade = HomeUi.Image("Scrim", transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(shade.rectTransform);
        shade.color = new Color(0, 0, 0, .72f); shade.raycastTarget = true;
        layout = HomeUi.Rect("Upgrades Layout", transform, Vector2.zero, new Vector2(1540, 940));
        HomeUi.Image("Board Backing", layout, Vector2.zero, new Vector2(1410, 830)).color = new Color32(23, 12, 7, 255);
        var board = HomeUi.Image("Board", layout, Vector2.zero, new Vector2(1500, 900), "Panel");
        board.pixelsPerUnitMultiplier = 1.5f; board.raycastTarget = true;
        Label("Title", layout, "Upgrades", -448, 378, 400, 70, 50, TextAlignmentOptions.Left);
        powerText = Label("Power Points", layout, "", 343, 374, 230, 38, 26, TextAlignmentOptions.Left);
        comfortText = Label("Comfort Points", layout, "", 598, 374, 200, 38, 26, TextAlignmentOptions.Left);
        UpgradeTileArt.PointIcon(layout, new Vector2(209,374), false);
        UpgradeTileArt.PointIcon(layout, new Vector2(482,374), true);
        HomeUi.Image("Currency Divider", layout, new Vector2(461,374), new Vector2(1,49)).color=Gold;
        HomeUi.Image("Header Rule", layout, new Vector2(0, 338), new Vector2(1310, 1)).color = Line;
        for (int i = 0; i < 2; i++) {
            bool comfort = i == 1;
            var tab = HomeUi.Button(comfort ? "Comfort Tab" : "Power Tab", layout, comfort ? "Komfort" : "Powerups",
                new Vector2(-540 + i * 248, 307), new Vector2(237, 46), () => SetCategory(comfort));
            UpgradeTileArt.StyleTab(tab);
            tab.GetComponent<HomeButtonFeedback>().selectionManaged = true;
            categoryTabs.Add(tab);
        }
        BuildScroll();
        HomeUi.Image("Tab Rule", layout, new Vector2(0,284), new Vector2(1310,2)).color=Gold;
        status = Label("Status", layout, "", 0, -357, 1070, 22, 18, TextAlignmentOptions.Center);
        status.color = Gold;
        upgradesBack = HomeUi.Button("Back", layout, "Zurück", new Vector2(-507, -391), new Vector2(306, 52), ShowOverview);
        refund = HomeUi.Button("Refund Upgrades", layout, "Punkte zurücksetzen", new Vector2(507, -391), new Vector2(306, 52), Refund);
        HomeUi.StyleSaveButton(upgradesBack,26); HomeUi.StyleSaveButton(refund,24);
        if (readOnly) runXpText = Label("Run Experience", layout, "", 317, -370, 432, 48, 28, TextAlignmentOptions.Right);
        MetaProgression.Changed += OnProgressChanged;
        BuildUpgrades();
        SetCategory(false);
        refund.gameObject.SetActive(!readOnly);
        RefreshValues();
        BuildOverview();
        ShowOverview();
        HomeUi.Fit(layout, new Vector2(1600, 980));
    }

    void BuildOverview()
    {
        overview = HomeUi.Rect("Progression Overview", transform, Vector2.zero, new Vector2(1720, 880));
        HomeUi.Image("Board Backing", overview, new Vector2(0,30), new Vector2(1640,735)).color=new Color32(23,12,7,255);
        var board = HomeUi.Image("Board", overview, new Vector2(0, 30), new Vector2(1700, 790), "Panel");
        board.pixelsPerUnitMultiplier = 1.4f; board.raycastTarget = true;
        timeline = ProgressionTimeline.Create(overview, new Vector2(0, 90), MetaProgression.TotalXp);
        string[] names = { "Upgrades", "Herausforderungen", "Baupläne" };
        for (int i=0; i<3; i++) {
            int entry=i;
            HomeUi.Button("Open " + names[i], overview, names[i], new Vector2((i-1)*510,-269), new Vector2(470,82), ()=> {
                if (entry==1) OpenChallenges(); else if (entry==2) OpenBlueprints(); else OpenUpgrades();
            });
        }
        overviewPoints = ProgressionArt.Text("Available Points", overview, "", new Vector2(-510,-201), new Vector2(470,34), 24);
        overviewPoints.color = ProgressionArt.Gold;
        HomeUi.Button("Overview Back", overview, "Zurück", new Vector2(-620,-423), new Vector2(330,70), Close);
        FitOverview();
    }

    void FitOverview() => HomeUi.Fit(overview, new Vector2(1800,980));
    void ShowOverview()
    {
        showingOverview=true; layout.gameObject.SetActive(false); overview.gameObject.SetActive(true);
        timeline.SetExperience(MetaProgression.TotalXp, true); RefreshOverview();
        ProgressionArt.Navigation(transform);
        EventSystem.current?.SetSelectedGameObject(overview.Find("Open Upgrades").gameObject);
    }
    void RefreshOverview()
    {
        if (!timeline) return;
        timeline.SetExperience(MetaProgression.TotalXp);
        overviewPoints.text = readOnly ? "Aktiver Run" : MetaProgression.AvailablePowerPoints + " Powerup-Punkte" +
            (MetaProgression.Level>=200 ? "  ·  " + MetaProgression.AvailableComfortPoints + " Komfortpunkte" : "");
    }
    public void OpenUpgrades()
    {
        if (!showingOverview || closing) return;
        showingOverview=false; overview.gameObject.SetActive(false); layout.gameObject.SetActive(true);
        RefreshValues(); scroll.verticalNormalizedPosition=1;
        EventSystem.current?.SetSelectedGameObject(upgradesBack.gameObject);
    }

    void OpenBlueprints()
    {
        if (!showingOverview || blueprintsPanel || closing) return;
        showingOverview=false; overview.gameObject.SetActive(false);
        blueprintsPanel=BlueprintCollectionPanel.Create(transform, readOnly, () => {
            blueprintsPanel=null;
            if (!closing) {
                ShowOverview();
                EventSystem.current?.SetSelectedGameObject(overview.Find("Open Baupläne").gameObject);
            }
        });
    }

    void OpenChallenges()
    {
        if (!showingOverview || challengesPanel) return;
        showingOverview=false;
        overview.gameObject.SetActive(false);
        challengesPanel=ChallengePanel.Create(transform, () => {
            challengesPanel=null;
            if (!closing) {
                ShowOverview();
                EventSystem.current?.SetSelectedGameObject(overview.Find("Open Herausforderungen").gameObject);
            }
        });
    }

    void BuildScroll()
    {
        var viewport = HomeUi.Rect("Progression Viewport", layout, new Vector2(0, -40), new Vector2(1330, 630));
        viewport.gameObject.AddComponent<RectMask2D>().softness = Vector2Int.zero;
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        list = HomeUi.Rect("Progression List", viewport, Vector2.zero, Vector2.zero);
        list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one; list.pivot = new Vector2(.5f, 1);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = list;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 38;
        var track = HomeUi.Image("Scrollbar", layout, new Vector2(677, -40), new Vector2(6, 630));
        track.color = new Color(0, 0, 0, .35f); track.raycastTarget = true;
        var area = HomeUi.Rect("Sliding Area", track.transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(area);
        var handle = HomeUi.Image("Handle", area, Vector2.zero, Vector2.zero); HomeUi.Stretch(handle.rectTransform);
        handle.color = Gold; handle.raycastTarget = true;
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle; bar.direction = Scrollbar.Direction.BottomToTop;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    static TextMeshProUGUI Label(string name, Transform parent, string value, float x, float y, float width, float height,
        float size, TextAlignmentOptions alignment)
    {
        var text = HomeUi.Label(name, parent, value, new Vector2(x, y), new Vector2(width, height), size);
        text.alignment = alignment; text.richText = false; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true; text.fontSizeMin = 18; text.fontSizeMax = size;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    void BuildUpgrades()
    {
        foreach (string upgradeId in new[] { "money", "movement", "jump", "health", "energy", "mining", "reach", "capacity", "torches", "ladders" })
        {
            var definition = MetaProgressionCatalog.Upgrade(upgradeId);
            var card = HomeUi.Button("Upgrade " + definition.id, list, "", Vector2.zero, new Vector2(448, 322), () => { });
            var tile = (RectTransform)card.transform;
            tile.anchorMin = tile.anchorMax = new Vector2(.5f, 1);
            var frame = card.GetComponent<Image>();
            frame.sprite = HomeUi.Sprite("SelectionCardNormal"); frame.pixelsPerUnitMultiplier = 5.5f;
            var feedback = card.GetComponent<HomeButtonFeedback>();
            feedback.normalPart = "SelectionCardNormal"; feedback.activePart = "SelectionCardHover";
            UpgradeTileArt.DarkenCard(card);
            Destroy(card.GetComponentInChildren<TextMeshProUGUI>().gameObject);
            var icon = HomeUi.Image("Icon", tile, new Vector2(0, 84), new Vector2(158, 124));
            icon.sprite = UpgradeTileArt.Icon(definition.id); icon.preserveAspect = true;
            Label("Name", tile, definition.name, 0, 17, 370, 34, 29, TextAlignmentOptions.Center);
            var record = new UpgradeRow { definition = definition, tile = tile };
            record.rank = Label("Rank", tile, "", 0, -16, 330, 28, 24, TextAlignmentOptions.Center);
            record.segments = new Image[definition.maxRank];
            float step = 300f / definition.maxRank;
            for (int i = 0; i < definition.maxRank; i++) {
                var socket = HomeUi.Image("Rank Socket " + i, tile, new Vector2((i - (definition.maxRank - 1) * .5f) * step, -43), new Vector2(step - 2, 16));
                socket.sprite=UpgradeTileArt.RankSprite(false);
                var fill = HomeUi.Image("Rank Fill", socket.transform, Vector2.zero, new Vector2(step - 2, 16));
                fill.sprite=UpgradeTileArt.RankSprite(true);
                record.segments[i] = fill;
            }
            record.effect = Label("Effect", tile, "", 0, -73, 370, 30, 24, TextAlignmentOptions.Center);
            record.effect.richText = true;
            if (!readOnly) {
                string id = definition.id;
                record.buy = HomeUi.Button("Buy " + id, tile, "", new Vector2(0, -117), new Vector2(328, 52), () => Spend(id));
                HomeUi.StyleSaveButton(record.buy, 27);
                record.buy.GetComponent<HomeButtonFeedback>().selectionManaged = true;
                record.cost = record.buy.GetComponentInChildren<TextMeshProUGUI>();
            }
            else Label("Active", tile, "Aktiv", 0, -108, 300, 28, 22, TextAlignmentOptions.Center).color = Muted;
            upgrades.Add(record);
        }
    }

    void SetCategory(bool comfort)
    {
        comfortCategory = comfort;
        int index = 0;
        foreach (var row in upgrades) {
            bool visible = row.definition.comfort == comfort;
            row.tile.gameObject.SetActive(visible);
            if (visible) {
                row.tile.anchoredPosition = new Vector2((index % 3 - 1) * 450, -153 - index / 3 * 318);
                index++;
            }
        }
        list.sizeDelta = new Vector2(0, Mathf.Max(630, Mathf.CeilToInt(index / 3f) * 318 - 6));
        scroll.verticalNormalizedPosition = 1;
        for (int i = 0; i < categoryTabs.Count; i++) categoryTabs[i].GetComponent<HomeButtonFeedback>().primary = (i == 1) == comfortCategory;
        RestrictNavigation();
    }

    void RefreshValues()
    {
        powerText.text = MetaProgression.AvailablePowerPoints + " Powerup-Punkte";
        comfortText.text = MetaProgression.AvailableComfortPoints + " Komfortpunkte";
        status.text = MetaProgression.LastError ?? "";
        RefreshOverview();
        if (runXpText) runXpText.text = "Run: +" + (MetaProgression.CurrentRun?.earnedXp ?? 0).ToString("N0", German) + " XP";
        bool hasRanks = false;
        foreach (var row in upgrades)
        {
            int rank = readOnly ? MetaProgression.GetRunRank(row.definition.id) : MetaProgression.GetRank(row.definition.id);
            bool max = rank >= row.definition.maxRank;
            row.rank.text = "Rang " + rank + "/" + row.definition.maxRank;
            for (int i = 0; i < row.segments.Length; i++) row.segments[i].color = i < rank ? Color.white : Color.clear;
            row.effect.text = row.definition.ValueLabel(rank);
            if (!readOnly && !max) row.effect.text += "  <color=#FFD45F>→  " + row.definition.ValueLabel(rank + 1) + "</color>";
            if (rank > 0) hasRanks = true;
            if (!row.buy) continue;
            int cost = row.definition.Cost(rank);
            row.cost.text = max ? "Maximum" : "+ " + cost + (cost == 1 ? " Punkt" : " Punkte");
            int balance = row.definition.comfort ? MetaProgression.AvailableComfortPoints : MetaProgression.AvailablePowerPoints;
            row.buy.interactable = !max && balance >= cost && MetaProgression.CanEdit && !GameSaveSystem.IsBusy && !RunNavigation.IsTransitioning;
        }
        refund.interactable = hasRanks && MetaProgression.CanEdit && !GameSaveSystem.IsBusy && !RunNavigation.IsTransitioning;
        RestrictNavigation();
    }

    void RestrictNavigation()
    {
        // Automatic navigation can otherwise reach a main-menu or pause button behind the scrim.
        var available = new List<Button>();
        foreach (var button in GetComponentsInChildren<Button>()) if (button.IsInteractable()) available.Add(button);
        for (int i = 0; i < available.Count; i++)
        {
            var previous = available[(i + available.Count - 1) % available.Count];
            var next = available[(i + 1) % available.Count];
            available[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
        }
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        var focused = selected ? selected.GetComponent<Button>() : null;
        if (focused && focused.transform.IsChildOf(transform) && !focused.IsInteractable())
            EventSystem.current.SetSelectedGameObject(upgradesBack.gameObject);
    }

    void Spend(string id)
    {
        if (readOnly || !MainMenuController.IsVisible || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) return;
        bool success = MetaProgression.Spend(id);
        status.text = success ? "" : MetaProgression.LastError;
        RefreshValues();
    }

    void Refund()
    {
        if (readOnly || !MainMenuController.IsVisible || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) return;
        bool success = MetaProgression.RefundAll();
        status.text = success ? "" : MetaProgression.LastError;
        RefreshValues();
    }

    void OnProgressChanged() => pendingRefresh = true;

    void Update()
    {
        if (challengesPanel || blueprintsPanel) return;
        if (pendingRefresh)
        {
            pendingRefresh = false;
            RefreshValues();
        }
        KeepSelectedVisible();
        if (!Input.GetKeyDown(KeyCode.Escape) || RunPauseMenu.InputConsumedFrame == Time.frameCount) return;
        RunPauseMenu.ConsumeInput(); if (showingOverview) Close(); else ShowOverview();
    }

    void KeepSelectedVisible()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == lastSelection) return;
        lastSelection = selected;
        if (!selected || !selected.transform.IsChildOf(list)) return;
        var row = selected.transform;
        while (row.parent && row.parent != list) row = row.parent;
        if (!(row is RectTransform rect)) return;
        float top = -rect.anchoredPosition.y - rect.sizeDelta.y * .5f;
        float overflow = list.rect.height - scroll.viewport.rect.height;
        if (overflow <= 0) return;
        float desired = Mathf.Clamp(list.anchoredPosition.y, top + rect.sizeDelta.y - scroll.viewport.rect.height, top);
        scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(desired / overflow);
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        GetComponent<CanvasGroup>().interactable = false;
        Destroy(gameObject);
    }

    void OnRectTransformDimensionsChange() { HomeUi.Fit(layout, new Vector2(1600, 980)); FitOverview(); }
    void OnDestroy()
    {
        IsOpen = false;
        MetaProgression.Changed -= OnProgressChanged;
        GameplayInputBlocker.SetBlocked(this, false);
        if (!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale = previousScale;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
    }
}
