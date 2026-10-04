using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SaveSlotPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    bool saving;
    float previousScale;
    RectTransform layout, slotList;
    CanvasGroup content;
    TextMeshProUGUI status;
    GameObject confirmation, editing;
    readonly List<int> slots = new();
    readonly List<Button> buttons = new();
    readonly List<TextMeshProUGUI> descriptions = new();
    readonly List<TextMeshProUGUI> depths = new(), balances = new();
    readonly List<RectTransform> coins = new();
    readonly List<GameObject> statistics = new();
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly Vector2 LayoutReference = new(1600, 940);
    const int Columns = 3;
    const float TileWidth = 420, TileHeight = 210, ColumnStep = 446, RowStep = 240, TilePadding = 20;
    ScrollRect scroll;
    Button back, start, edit;
    int selectedSlot = -1;
    GameObject lastSelection;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsOpen = false;
    public static SaveSlotPanel Show(Transform parent, bool save)
    {
        if (!Application.isPlaying) return null;
        var existing = FindFirstObjectByType<SaveSlotPanel>(); if (existing) return existing;
        var root = HomeUi.Panel("Save Slots", parent); HomeUi.Stretch(root);
        var panel = root.gameObject.AddComponent<SaveSlotPanel>(); panel.saving = save; panel.Build(); return panel;
    }
    void Build()
    {
        IsOpen = true; previousScale = Time.timeScale; Time.timeScale = 0; GameplayInputBlocker.SetBlocked(this, true);
        var scrim = HomeUi.Image("Scrim", transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(scrim.rectTransform);
        scrim.color = new Color(0, 0, 0, .68f); scrim.raycastTarget = true;
        layout = HomeUi.Rect("Slot Layout", transform, Vector2.zero, new Vector2(1540, 890));
        content = layout.gameObject.AddComponent<CanvasGroup>();
        HomeUi.Image("Board Backing", layout, Vector2.zero, new Vector2(1410, 780)).color = new Color32(23, 12, 7, 255);
        var board = HomeUi.Image("Board", layout, Vector2.zero, new Vector2(1500, 850), "Panel");
        board.pixelsPerUnitMultiplier = 1.5f; board.raycastTarget = true;
        const float listMargin = 40, footerY = -330, footerHeight = 70;
        var title = HomeUi.Label("Title", layout, saving ? "Spiel speichern" : "Spielstände", new Vector2(0, 326), new Vector2(1310, 70), 48);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        HomeUi.Image("Header Rule", layout, new Vector2(0, 278), new Vector2(1310, 1)).color = new Color32(132, 89, 43, 180);
        title.ForceMeshUpdate();
        float listTop = title.rectTransform.anchoredPosition.y + title.textBounds.min.y - listMargin;
        float listBottom = footerY + footerHeight * .5f + listMargin;
        var viewport = HomeUi.Rect("Slot Viewport", layout, new Vector2(0, (listTop + listBottom) * .5f), new Vector2(1320, listTop - listBottom));
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 20);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        slotList = HomeUi.Rect("Slot List", viewport, Vector2.zero, Vector2.zero);
        slotList.anchorMin = new Vector2(0, 1); slotList.anchorMax = Vector2.one; slotList.pivot = new Vector2(.5f, 1);
        scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = slotList;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 38;
        var track = HomeUi.Image("Scrollbar", layout, new Vector2(677, (listTop + listBottom) * .5f), new Vector2(7, listTop - listBottom));
        track.color = new Color(0, 0, 0, .35f); track.raycastTarget = true;
        var area = HomeUi.Rect("Sliding Area", track.transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(area);
        var handle = HomeUi.Image("Handle", area, Vector2.zero, Vector2.zero);
        handle.color = new Color32(222, 174, 91, 255); handle.raycastTarget = true;
        HomeUi.Stretch(handle.rectTransform);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        for (int slot = 1; slot <= GameSaveSystem.MaxSlots; slot++) AddSlot(slot);
        status = HomeUi.Label("Status", layout, "", new Vector2(390, 326), new Vector2(520, 42), 20);
        status.alignment = TextAlignmentOptions.MidlineRight;
        back = HomeUi.Button("Back", layout, "Zurück", new Vector2(saving ? 0 : -446, footerY), new Vector2(340, footerHeight), Close); HomeUi.StyleSaveButton(back, 26);
        edit = HomeUi.Button("Edit Save", layout, "Bearbeiten", new Vector2(0, footerY), new Vector2(340, footerHeight), EditSelected); HomeUi.StyleSaveButton(edit, 26);
        start = HomeUi.Button("Start Save", layout, "Spiel starten", new Vector2(446, footerY), new Vector2(340, footerHeight), StartSelected, true); HomeUi.StyleSaveButton(start, 26);
        Refresh(); HomeUi.Fit(layout, LayoutReference); FocusSlot();
    }
    void AddSlot(int slot)
    {
        slots.Add(slot);
        var button = HomeUi.Button("Slot " + slot, slotList, "Spielstand " + slot, Vector2.zero, new Vector2(TileWidth, TileHeight), () => Choose(slot));
        var image = button.GetComponent<Image>(); image.sprite = HomeUi.Sprite("SelectionCardNormal"); image.pixelsPerUnitMultiplier = 5.5f; image.material = null;
        var feedback = button.GetComponent<HomeButtonFeedback>();
        feedback.normalPart = "SelectionCardNormal"; feedback.activePart = "SelectionCardHover"; feedback.animateScale = false;
        var name = button.GetComponentInChildren<TextMeshProUGUI>();
        name.rectTransform.anchoredPosition = new Vector2(0, 52); name.rectTransform.sizeDelta = new Vector2(356, 54);
        name.fontSize = 32; name.enableAutoSizing = false; name.alignment = TextAlignmentOptions.MidlineLeft;
        HomeUi.Image("Name Rule", button.transform, new Vector2(0, 22), new Vector2(356, 1)).color = new Color32(132, 89, 43, 180);
        var label = HomeUi.Label("Slot Details " + slot, button.transform, "", new Vector2(0, -4), new Vector2(356, 38), 23);
        label.alignment = TextAlignmentOptions.MidlineLeft; label.color = new Color32(215, 202, 178, 255);
        label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
        var stats = HomeUi.Rect("Stats", button.transform, new Vector2(0, -66), new Vector2(356, 38));
        var depthIcon = HomeUi.Rect("Depth Icon", stats, new Vector2(-166, 0), new Vector2(18, 22)).gameObject.AddComponent<SaveDepthGlyph>();
        depthIcon.color = HomeUi.Cream; depthIcon.raycastTarget = false;
        var depth = HomeUi.Label("Depth", stats, "", new Vector2(-89, 0), new Vector2(124, 38), 23); depth.alignment = TextAlignmentOptions.MidlineLeft;
        var coin = HomeUi.Image("Coin", stats, new Vector2(28, 0), new Vector2(26, 26));
        coin.sprite = Resources.LoadAll<Sprite>("GameOverCoin").FirstOrDefault(); coin.preserveAspect = true;
        var balance = HomeUi.Label("Balance", stats, "", new Vector2(109, 0), new Vector2(138, 38), 23); balance.alignment = TextAlignmentOptions.MidlineRight;
        foreach (var value in new[] { depth, balance })
        {
            value.textWrappingMode = TextWrappingModes.NoWrap; value.overflowMode = TextOverflowModes.Ellipsis;
            value.enableAutoSizing = true; value.fontSizeMin = 16; value.fontSizeMax = 23;
        }
        button.GetComponent<RectTransform>().anchorMin = button.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1);
        buttons.Add(button); descriptions.Add(label); depths.Add(depth); balances.Add(balance); coins.Add(coin.rectTransform); statistics.Add(stats.gameObject);
    }
    static void StyleButton(Button button)
    {
        HomeUi.StyleSaveButton(button);
    }
    static string Date(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy  HH:mm");
    void Refresh()
    {
        var summaries = slots.ToDictionary(slot => slot, slot => GameSaveSystem.GetSummary(slot));
        var ordered = saving ? slots.ToArray() : slots.Where(GameSaveSystem.HasSlotData).OrderByDescending(slot => summaries[slot]?.lastOpenedUtc ?? 0).ThenBy(slot => slot).ToArray();
        foreach (int slot in slots)
        {
            int i = slots.IndexOf(slot), order = Array.IndexOf(ordered, slot); bool visible = order >= 0;
            buttons[i].gameObject.SetActive(visible); if (!visible) continue;
            buttons[i].GetComponent<RectTransform>().anchoredPosition = new Vector2((order % Columns - 1) * ColumnStep, -TilePadding - TileHeight * .5f - order / Columns * RowStep);
            var summary = summaries[slot];
            buttons[i].GetComponentInChildren<TextMeshProUGUI>().text = string.IsNullOrWhiteSpace(summary?.name) ? "Spielstand " + slot : summary.name;
            descriptions[i].text = summary == null ? (GameSaveSystem.HasSlotData(slot) ? "Nicht lesbar" : "Leer") : Date(summary.lastOpenedUtc);
            statistics[i].SetActive(summary != null);
            depths[i].text = summary == null ? "" : summary.depth.ToString("N0", German) + " m";
            balances[i].text = summary == null ? "" : summary.money.ToString("N0", German) + " $";
            if (summary != null)
            {
                balances[i].ForceMeshUpdate();
                coins[i].anchoredPosition = new Vector2(178 - balances[i].textBounds.size.x - 20, 0);
            }
            buttons[i].interactable = !GameSaveSystem.IsBusy && (saving ? GameSaveSystem.CanSave : true);
        }
        if (!ordered.Contains(selectedSlot)) selectedSlot = saving || ordered.Length == 0 ? -1 : ordered[0];
        int rows = (ordered.Length + Columns - 1) / Columns;
        slotList.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, rows == 0 ? 0 : TilePadding * 2 + (rows - 1) * RowStep + TileHeight));
        RefreshSelection();
        for (int order = 0; order < ordered.Length; order++)
        {
            Button At(int index) => index >= 0 && index < ordered.Length ? buttons[slots.IndexOf(ordered[index])] : null;
            buttons[slots.IndexOf(ordered[order])].navigation = new Navigation {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = order % Columns > 0 ? At(order - 1) : null,
                selectOnRight = order % Columns < Columns - 1 ? At(order + 1) : null,
                selectOnUp = At(order - Columns),
                selectOnDown = At(order + Columns) ?? (saving ? back : order % Columns == 0 ? back : order % Columns == 1 ? edit : start)
            };
        }
    }
    void RefreshSelection()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var feedback = buttons[i].GetComponent<HomeButtonFeedback>();
            feedback.selectionManaged = true;
            feedback.primary = !saving && slots[i] == selectedSlot;
        }
        bool selected = !saving && selectedSlot > 0 && GameSaveSystem.HasSlotData(selectedSlot);
        edit.gameObject.SetActive(selected); start.gameObject.SetActive(selected);
        edit.interactable = selected && !GameSaveSystem.IsBusy;
        start.interactable = selected && !GameSaveSystem.IsBusy && GameSaveSystem.GetSummary(selectedSlot) != null;
    }
    void FocusSlot()
    {
        int index = slots.IndexOf(saving ? GameSaveSystem.ActiveSlot : selectedSlot);
        if (index < 0 || !buttons[index].gameObject.activeSelf || !buttons[index].interactable) index = buttons.FindIndex(b => b.gameObject.activeSelf && b.interactable);
        if (index < 0) { EventSystem.current?.SetSelectedGameObject(back.gameObject); return; }
        EnsureVisible(index);
        EventSystem.current?.SetSelectedGameObject(buttons[index].gameObject);
    }
    void EnsureVisible(int index)
    {
        float top = -buttons[index].GetComponent<RectTransform>().anchoredPosition.y - TileHeight * .5f;
        float overflow = Mathf.Max(0, slotList.sizeDelta.y - scroll.viewport.rect.height);
        float desired = Mathf.Clamp(slotList.anchoredPosition.y, top + TileHeight + TilePadding - scroll.viewport.rect.height, top - TilePadding);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = overflow > 0 ? 1 - Mathf.Clamp01(desired / overflow) : 1;
    }
    void Choose(int slot)
    {
        if (GameSaveSystem.IsBusy || RunNavigation.IsTransitioning) return;
        if (!saving) { selectedSlot = slot; RefreshSelection(); return; }
        if (GameSaveSystem.HasSlotData(slot)) Confirm("Spielstand " + slot + " überschreiben?", () => Save(slot)); else Save(slot);
    }
    void StartSelected()
    {
        if (!start.interactable) return;
        int slot = selectedSlot;
        if (FindFirstObjectByType<MapGenerator>()) Confirm("Spielstand laden?", () => Load(slot)); else Load(slot);
    }
    void Save(int slot)
    {
        status.text = "Speichern …"; foreach (var button in buttons) button.interactable = false;
        StartCoroutine(GameSaveSystem.Save(slot, this, (ok, message) => { status.text = message; Refresh(); }));
    }
    void Load(int slot) { if (!RunNavigation.LoadGame(slot, out string error)) status.text = error; }
    public void ShowLoadError(string error) { if (status) status.text = error; }
    void EditSelected()
    {
        if (selectedSlot < 1 || editing) return;
        int slot = selectedSlot; var summary = GameSaveSystem.GetSummary(slot);
        content.interactable = content.blocksRaycasts = false;
        var modal = HomeUi.Panel("Edit Save Panel", transform); HomeUi.Stretch(modal); editing = modal.gameObject;
        var shade = HomeUi.Image("Shade", modal, Vector2.zero, Vector2.zero); HomeUi.Stretch(shade.rectTransform); shade.color = new Color(0, 0, 0, .72f); shade.raycastTarget = true;
        var board = HomeUi.Rect("Edit Layout", modal, Vector2.zero, new Vector2(1140, 530));
        HomeUi.Image("Board Backing", board, Vector2.zero, new Vector2(1010, 410)).color = new Color32(23, 12, 7, 255);
        HomeUi.Image("Board", board, Vector2.zero, new Vector2(1080, 470), "Panel");
        HomeUi.Label("Title", board, "Spielstand bearbeiten", new Vector2(0, 157), new Vector2(880, 64), 42).alignment = TextAlignmentOptions.MidlineLeft;
        HomeUi.Image("Header Rule", board, new Vector2(0, 114), new Vector2(880, 2)).color = new Color32(222, 174, 91, 255);
        HomeUi.Label("Name Label", board, "Name", new Vector2(0, 78), new Vector2(880, 36), 28).alignment = TextAlignmentOptions.MidlineLeft;
        var background = HomeUi.Image("Save Name", board, new Vector2(0, 18), new Vector2(880, 74)); background.raycastTarget = true;
        var viewport = HomeUi.Rect("Text Viewport", background.transform, Vector2.zero, new Vector2(840, 66)); viewport.gameObject.AddComponent<RectMask2D>();
        var text = HomeUi.Label("Text", viewport, "", Vector2.zero, new Vector2(830, 64), 30); text.richText = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        var field = background.gameObject.AddComponent<TMP_InputField>(); field.enabled = false; field.textViewport = viewport; field.textComponent = text; field.targetGraphic = background; field.characterLimit = 40; field.lineType = TMP_InputField.LineType.SingleLine;
        field.customCaretColor = true; field.caretColor = new Color32(222, 174, 91, 255); field.caretWidth = 2; field.caretBlinkRate = .8f; field.onFocusSelectAll = false;
        field.enabled = true;
        HomeUi.StyleInputField(field);
        field.text = string.IsNullOrWhiteSpace(summary?.name) ? "Spielstand " + slot : summary.name;
        HomeUi.Label("Created Date", board, summary == null ? "" : "Erstellt am " + Date(summary.createdUtc), new Vector2(0, -48), new Vector2(880, 36), 24).alignment = TextAlignmentOptions.MidlineLeft;
        var message = HomeUi.Label("Edit Status", board, "", new Vector2(0, -86), new Vector2(880, 28), 22);
        HomeUi.Image("Footer Rule", board, new Vector2(0, -111), new Vector2(920, 1)).color = new Color32(132, 89, 43, 155);
        var cancel = HomeUi.Button("Cancel Edit", board, "Abbrechen", new Vector2(55, -148), new Vector2(240, 68), CloseEdit); StyleButton(cancel);
        var delete = HomeUi.Button("Delete Save", board, "Löschen", new Vector2(-315, -148), new Vector2(240, 68), () => Confirm("Spielstand löschen?", () => {
            if (!GameSaveSystem.DeleteSlot(slot, out string error)) { message.text = error; return; }
            CloseEdit(); Refresh(); FocusSlot();
        })); StyleButton(delete);
        var save = HomeUi.Button("Save Name", board, "Speichern", new Vector2(315, -148), new Vector2(240, 68), () => {
            if (!GameSaveSystem.RenameSlot(slot, field.text, out string error)) { message.text = error; return; }
            CloseEdit(); Refresh(); FocusSlot();
        }, true); StyleButton(save);
        save.interactable = summary != null;
        field.onValueChanged.AddListener(value => save.interactable = summary != null && !string.IsNullOrWhiteSpace(value));
        field.interactable = summary != null; HomeUi.Fit(board, new Vector2(1240, 630)); field.Select(); field.ActivateInputField();
    }
    void CloseEdit()
    {
        if (editing) Destroy(editing); editing = null;
        content.interactable = content.blocksRaycasts = true; FocusSlot();
    }
    void Confirm(string question, Action yes)
    {
        if (confirmation) Destroy(confirmation);
        content.interactable = content.blocksRaycasts = false;
        if (editing) { var gate = editing.GetComponent<CanvasGroup>(); if (!gate) gate = editing.AddComponent<CanvasGroup>(); gate.interactable = gate.blocksRaycasts = false; }
        var modal = HomeUi.Panel("Confirmation", transform); HomeUi.Stretch(modal); confirmation = modal.gameObject;
        var shade = HomeUi.Image("Shade", modal, Vector2.zero, Vector2.zero); HomeUi.Stretch(shade.rectTransform); shade.color = new Color(0, 0, 0, .72f); shade.raycastTarget = true;
        var panel = HomeUi.Rect("Confirm Layout", modal, Vector2.zero, new Vector2(850, 340));
        HomeUi.Image("Panel", panel, Vector2.zero, new Vector2(800, 300), "Panel");
        HomeUi.Label("Question", panel, question, new Vector2(0, 58), new Vector2(720, 75), 38);
        var cancel = HomeUi.Button("Cancel", panel, "Abbrechen", new Vector2(-174, -66), new Vector2(305, 68), DismissConfirmation); StyleButton(cancel);
        var accept = HomeUi.Button("Confirm", panel, "Bestätigen", new Vector2(174, -66), new Vector2(305, 68), () => { DismissConfirmation(); yes(); }, true); StyleButton(accept);
        HomeUi.Fit(panel, new Vector2(960, 440)); EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
    void DismissConfirmation()
    {
        if (confirmation) Destroy(confirmation); confirmation = null;
        content.interactable = content.blocksRaycasts = !editing;
        if (editing) { var gate = editing.GetComponent<CanvasGroup>(); gate.interactable = gate.blocksRaycasts = true; EventSystem.current?.SetSelectedGameObject(editing.GetComponentsInChildren<Button>().First().gameObject); }
        else FocusSlot();
    }
    void Update()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (selected && selected != lastSelection && !editing && !confirmation)
        {
            lastSelection = selected; int index = buttons.IndexOf(selected.GetComponent<Button>());
            if (index >= 0) EnsureVisible(index);
        }
        if (!Input.GetKeyDown(KeyCode.Escape) || GameSaveSystem.IsBusy) return;
        RunPauseMenu.ConsumeInput(); if (confirmation) DismissConfirmation(); else if (editing) CloseEdit(); else Close();
    }
    void Close() { if (!GameSaveSystem.IsBusy) Destroy(gameObject); }
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, LayoutReference);
    void OnDestroy()
    {
        IsOpen = false; GameplayInputBlocker.SetBlocked(this, false);
        if (!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale = previousScale;
        EventSystem.current?.SetSelectedGameObject(null);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class SaveDepthGlyph : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        for (int row = 0; row < 2; row++)
        {
            var tip = rect.center + new Vector2(0, (row == 0 ? .15f : -.22f) * rect.height);
            Stroke(mesh, tip + new Vector2(-rect.width * .4f, rect.height * .22f), tip);
            Stroke(mesh, tip, tip + new Vector2(rect.width * .4f, rect.height * .22f));
        }
    }
    void Stroke(VertexHelper mesh, Vector2 from, Vector2 to)
    {
        var normal = new Vector2(-(to - from).y, (to - from).x).normalized * 1.1f;
        int first = mesh.currentVertCount;
        mesh.AddVert(from - normal, color, Vector2.zero); mesh.AddVert(from + normal, color, Vector2.zero);
        mesh.AddVert(to + normal, color, Vector2.zero); mesh.AddVert(to - normal, color, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
    }
}
