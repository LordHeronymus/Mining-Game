using System;
using System.Collections.Generic;
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
        layout = HomeUi.Rect("Slot Layout", transform, Vector2.zero, new Vector2(960, 840));
        content = layout.gameObject.AddComponent<CanvasGroup>();
        HomeUi.Image("Board", layout, Vector2.zero, new Vector2(900, 810), "Panel");
        HomeUi.Label("Title", layout, saving ? "Spiel speichern" : "Spielstände", new Vector2(0, 323), new Vector2(720, 75), 49);
        var viewport = HomeUi.Rect("Slot Viewport", layout, new Vector2(0, 10), new Vector2(780, 480));
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 48);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        slotList = HomeUi.Rect("Slot List", viewport, Vector2.zero, Vector2.zero);
        slotList.anchorMin = new Vector2(0, 1); slotList.anchorMax = Vector2.one; slotList.pivot = new Vector2(.5f, 1);
        scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = slotList;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        for (int slot = 1; slot <= GameSaveSystem.MaxSlots; slot++) AddSlot(slot);
        status = HomeUi.Label("Status", layout, "", new Vector2(0, 268), new Vector2(760, 28), 20);
        back = HomeUi.Button("Back", layout, "Zurück", new Vector2(saving ? 0 : -260, -280), new Vector2(saving ? 340 : 240, 68), Close); StyleButton(back);
        edit = HomeUi.Button("Edit Save", layout, "Bearbeiten", new Vector2(0, -280), new Vector2(240, 68), EditSelected); StyleButton(edit);
        start = HomeUi.Button("Start Save", layout, "Spiel starten", new Vector2(260, -280), new Vector2(240, 68), StartSelected, true); StyleButton(start);
        Refresh(); HomeUi.Fit(layout, new Vector2(1100, 930)); FocusSlot();
    }
    void AddSlot(int slot)
    {
        slots.Add(slot);
        var button = HomeUi.Button("Slot " + slot, slotList, "Spielstand " + slot, new Vector2(0, -60), new Vector2(750, 68), () => Choose(slot)); StyleButton(button);
        var label = HomeUi.Label("Slot Details " + slot, slotList, "", new Vector2(0, -109), new Vector2(750, 39), 21);
        button.GetComponent<RectTransform>().anchorMin = button.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, 1);
        buttons.Add(button); descriptions.Add(label);
    }
    static void StyleButton(Button button)
    {
        var image = button.GetComponent<Image>(); image.sprite = HomeUi.Sprite("SaveButton"); image.pixelsPerUnitMultiplier = image.sprite.rect.height / 68f;
        var feedback = button.GetComponent<HomeButtonFeedback>(); feedback.normalPart = "SaveButton"; feedback.activePart = "SaveActive"; feedback.animateScale = false;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(); label.rectTransform.sizeDelta = new Vector2(button.GetComponent<RectTransform>().sizeDelta.x - 100, 44);
        label.richText = false; label.fontSize = 23; label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 23;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
    }
    static string Date(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy  HH:mm");
    void Refresh()
    {
        var summaries = slots.ToDictionary(slot => slot, slot => GameSaveSystem.GetSummary(slot));
        var ordered = saving ? slots.ToArray() : slots.Where(GameSaveSystem.HasSlotData).OrderByDescending(slot => summaries[slot]?.lastOpenedUtc ?? 0).ThenBy(slot => slot).ToArray();
        foreach (int slot in slots)
        {
            int i = slots.IndexOf(slot), row = Array.IndexOf(ordered, slot); bool visible = row >= 0;
            buttons[i].gameObject.SetActive(visible); descriptions[i].gameObject.SetActive(visible); if (!visible) continue;
            buttons[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -60 - row * 129);
            descriptions[i].rectTransform.anchoredPosition = new Vector2(0, -109 - row * 129);
            var summary = summaries[slot];
            buttons[i].GetComponentInChildren<TextMeshProUGUI>().text = string.IsNullOrWhiteSpace(summary?.name) ? "Spielstand " + slot : summary.name;
            descriptions[i].text = summary == null ? (GameSaveSystem.HasSlotData(slot) ? "Nicht lesbar" : "Leer") : Date(summary.lastOpenedUtc) + "   ·   " + summary.depth + " m   ·   " + summary.money + " $";
            buttons[i].interactable = !GameSaveSystem.IsBusy && (saving ? GameSaveSystem.CanSave : true);
        }
        if (!ordered.Contains(selectedSlot)) selectedSlot = saving || ordered.Length == 0 ? -1 : ordered[0];
        slotList.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, ordered.Length * 129 + 24));
        RefreshSelection();
    }
    void RefreshSelection()
    {
        for (int i = 0; i < slots.Count; i++) buttons[i].GetComponent<HomeButtonFeedback>().primary = !saving && slots[i] == selectedSlot;
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
        float top = -buttons[index].GetComponent<RectTransform>().anchoredPosition.y - 60;
        float overflow = slotList.sizeDelta.y - scroll.viewport.rect.height;
        scroll.verticalNormalizedPosition = overflow > 0 ? 1 - Mathf.Clamp01((top - 129) / overflow) : 1;
        EventSystem.current?.SetSelectedGameObject(buttons[index].gameObject);
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
        var board = HomeUi.Rect("Edit Layout", modal, Vector2.zero, new Vector2(900, 640));
        HomeUi.Image("Board", board, Vector2.zero, new Vector2(860, 600), "Panel");
        HomeUi.Label("Title", board, "Spielstand bearbeiten", new Vector2(0, 217), new Vector2(740, 70), 42);
        HomeUi.Label("Name Label", board, "Name", new Vector2(0, 116), new Vector2(620, 40), 28);
        var background = HomeUi.Image("Save Name", board, new Vector2(0, 53), new Vector2(650, 72)); background.color = new Color(.08f, .035f, .015f, .85f); background.raycastTarget = true;
        var viewport = HomeUi.Rect("Text Viewport", background.transform, Vector2.zero, new Vector2(610, 58)); viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 48);
        var text = HomeUi.Label("Text", viewport, "", Vector2.zero, new Vector2(600, 54), 30); text.richText = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        var field = background.gameObject.AddComponent<TMP_InputField>(); field.enabled = false; field.textViewport = viewport; field.textComponent = text; field.targetGraphic = background; field.characterLimit = 40; field.lineType = TMP_InputField.LineType.SingleLine;
        field.customCaretColor = true; field.caretColor = new Color32(222, 174, 91, 255); field.caretWidth = 2; field.caretBlinkRate = .8f; field.onFocusSelectAll = false;
        field.enabled = true;
        LichtfadenCaret.Apply(field);
        field.text = string.IsNullOrWhiteSpace(summary?.name) ? "Spielstand " + slot : summary.name;
        HomeUi.Label("Created Date", board, summary == null ? "" : "Erstellt am " + Date(summary.createdUtc), new Vector2(0, -25), new Vector2(700, 42), 24);
        var message = HomeUi.Label("Edit Status", board, "", new Vector2(0, -87), new Vector2(710, 38), 22);
        var cancel = HomeUi.Button("Cancel Edit", board, "Abbrechen", new Vector2(-260, -198), new Vector2(240, 68), CloseEdit); StyleButton(cancel);
        var delete = HomeUi.Button("Delete Save", board, "Löschen", new Vector2(0, -198), new Vector2(240, 68), () => Confirm("Spielstand löschen?", () => {
            if (!GameSaveSystem.DeleteSlot(slot, out string error)) { message.text = error; return; }
            CloseEdit(); Refresh(); FocusSlot();
        })); StyleButton(delete);
        var save = HomeUi.Button("Save Name", board, "Speichern", new Vector2(260, -198), new Vector2(240, 68), () => {
            if (!GameSaveSystem.RenameSlot(slot, field.text, out string error)) { message.text = error; return; }
            CloseEdit(); Refresh(); FocusSlot();
        }, true); StyleButton(save);
        save.interactable = summary != null;
        field.onValueChanged.AddListener(value => save.interactable = summary != null && !string.IsNullOrWhiteSpace(value));
        field.interactable = summary != null; HomeUi.Fit(board, new Vector2(1020, 760)); field.Select(); field.ActivateInputField();
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
            if (index >= 0)
            {
                float rowTop = -buttons[index].GetComponent<RectTransform>().anchoredPosition.y - 60;
                float overflow = slotList.sizeDelta.y - scroll.viewport.rect.height;
                float desired = Mathf.Clamp(slotList.anchoredPosition.y, rowTop + 129 - scroll.viewport.rect.height, rowTop);
                if (overflow > 0) scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(desired / overflow);
            }
        }
        if (!Input.GetKeyDown(KeyCode.Escape) || GameSaveSystem.IsBusy) return;
        RunPauseMenu.ConsumeInput(); if (confirmation) DismissConfirmation(); else if (editing) CloseEdit(); else Close();
    }
    void Close() { if (!GameSaveSystem.IsBusy) Destroy(gameObject); }
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(1100, 930));
    void OnDestroy()
    {
        IsOpen = false; GameplayInputBlocker.SetBlocked(this, false);
        if (!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale = previousScale;
        EventSystem.current?.SetSelectedGameObject(null);
    }
}
