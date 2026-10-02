using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SaveSlotPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    bool saving;
    float previousScale;
    RectTransform layout;
    CanvasGroup content;
    TextMeshProUGUI status;
    GameObject confirmation;
    readonly Button[] buttons = new Button[4];
    readonly TextMeshProUGUI[] descriptions = new TextMeshProUGUI[4];
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsOpen = false;
    public static SaveSlotPanel Show(Transform parent, bool save)
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<SaveSlotPanel>(); if (existing) return existing;
        var root = HomeUi.Rect("Save Slots", parent, Vector2.zero, Vector2.zero); HomeUi.Stretch(root);
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
        for (int i = 0; i < buttons.Length; i++)
        {
            int slot = i; float y = 211 - i * 129;
            buttons[i] = HomeUi.Button("Slot " + i, layout, i == 0 ? "Automatische Sicherung" : "Spielstand " + i,
                new Vector2(0, y), new Vector2(720, 68), () => Choose(slot), i == 0);
            descriptions[i] = HomeUi.Label("Slot Details " + i, layout, "", new Vector2(0, y - 49), new Vector2(750, 39), 23);
        }
        status = HomeUi.Label("Status", layout, "", new Vector2(0, -278), new Vector2(760, 45), 24);
        HomeUi.Button("Back", layout, "Zurück", new Vector2(0, -342), new Vector2(340, 64), Close);
        Refresh(); HomeUi.Fit(layout, new Vector2(1100, 930));
        EventSystem.current?.SetSelectedGameObject(buttons[saving ? 1 : Mathf.Max(0, GameSaveSystem.MostRecentSlot())].gameObject);
    }
    void Refresh()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            var summary = GameSaveSystem.GetSummary(i);
            descriptions[i].text = summary == null ? "Leer" : new DateTime(summary.savedUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy  HH:mm") +
                "   ·   " + summary.depth + " m   ·   " + summary.money + " $";
            buttons[i].interactable = !GameSaveSystem.IsBusy && (saving ? i > 0 && GameSaveSystem.CanSave : summary != null);
        }
    }
    void Choose(int slot)
    {
        if (GameSaveSystem.IsBusy || RunNavigation.IsTransitioning) return;
        if (saving)
        {
            if (GameSaveSystem.GetSummary(slot) != null) Confirm("Spielstand " + slot + " überschreiben?", () => Save(slot));
            else Save(slot);
        }
        else if (FindFirstObjectByType<MapGenerator>()) Confirm("Spielstand laden?", () => Load(slot));
        else Load(slot);
    }
    void Save(int slot)
    {
        status.text = "Speichern …"; foreach (var button in buttons) button.interactable = false;
        StartCoroutine(GameSaveSystem.Save(slot, this, (ok, message) => { status.text = message; Refresh(); }));
    }
    void Load(int slot)
    {
        if (!RunNavigation.LoadGame(slot, out string error)) status.text = error;
    }
    void Confirm(string question, Action yes)
    {
        if (confirmation) Destroy(confirmation);
        content.interactable = content.blocksRaycasts = false;
        var modal = HomeUi.Rect("Confirmation", transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(modal); confirmation = modal.gameObject;
        var shade = HomeUi.Image("Shade", modal, Vector2.zero, Vector2.zero); HomeUi.Stretch(shade.rectTransform);
        shade.color = new Color(0, 0, 0, .72f); shade.raycastTarget = true;
        var panel = HomeUi.Rect("Confirm Layout", modal, Vector2.zero, new Vector2(850, 340));
        HomeUi.Image("Panel", panel, Vector2.zero, new Vector2(800, 300), "Panel");
        HomeUi.Label("Question", panel, question, new Vector2(0, 58), new Vector2(720, 75), 38);
        var cancel = HomeUi.Button("Cancel", panel, "Abbrechen", new Vector2(-174, -66), new Vector2(305, 76), DismissConfirmation);
        HomeUi.Button("Confirm", panel, "Bestätigen", new Vector2(174, -66), new Vector2(305, 76), () => { DismissConfirmation(); yes(); }, true);
        HomeUi.Fit(panel, new Vector2(960, 440)); EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) || GameSaveSystem.IsBusy) return;
        RunPauseMenu.ConsumeInput();
        if (confirmation) DismissConfirmation(); else Close();
    }
    void DismissConfirmation()
    {
        if (confirmation) Destroy(confirmation);
        confirmation = null;
        content.interactable = content.blocksRaycasts = true;
        EventSystem.current?.SetSelectedGameObject(buttons[saving ? 1 : Mathf.Max(0, GameSaveSystem.MostRecentSlot())].gameObject);
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
