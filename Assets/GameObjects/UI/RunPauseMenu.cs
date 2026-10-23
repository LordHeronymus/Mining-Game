using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1100)]
public sealed class RunPauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static int InputConsumedFrame { get; private set; } = -1;
    CanvasGroup group;
    RectTransform layout;
    TMPro.TextMeshProUGUI status;
    float previousScale;
    Button challengesButton;
    ChallengePanel challengesPanel;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { IsOpen = false; InputConsumedFrame = -1; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() { SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded; }
    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == RunNavigation.MenuScene || !GameObject.Find("ScreenCanvas") || FindFirstObjectByType<RunPauseMenu>()) return;
        var root = HomeUi.Rect("Pause Menu", GameObject.Find("ScreenCanvas").transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(root); root.gameObject.AddComponent<RunPauseMenu>();
    }
    public static void ConsumeInput() => InputConsumedFrame = Time.frameCount;
    void Awake()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        var scrim = HomeUi.Image("Scrim", transform, Vector2.zero, Vector2.zero); HomeUi.Stretch(scrim.rectTransform);
        scrim.color = new Color(0, 0, 0, .62f); scrim.raycastTarget = true;
        layout = HomeUi.Rect("Pause Layout", transform, Vector2.zero, new Vector2(700, 1000));
        // The run saves itself (RunAutosave and on leaving), so the menu has no manual save button;
        // loading another save is only offered in the main menu. The status line sits in the strip below "Hauptmenü".
        HomeUi.Image("Board", layout, new Vector2(0, 45), new Vector2(650, 699), "Panel");
        HomeUi.Label("Title", layout, "Tiefenhall", new Vector2(0, 307.5f), new Vector2(560, 85), 62);
        HomeUi.Button("Resume", layout, "Fortsetzen", new Vector2(0, 189.5f), new Vector2(500, 76), Close, true);
        HomeUi.Button("Progression", layout, "Fortschritt", new Vector2(0, 98.5f), new Vector2(500, 76), () => MetaProgressionPanel.Show(transform.parent, true));
        challengesButton = HomeUi.Button("Challenges", layout, "Herausforderungen", new Vector2(0, 7.5f), new Vector2(500, 76), OpenChallenges);
        HomeUi.Button("Settings", layout, "Einstellungen", new Vector2(0, -83.5f), new Vector2(500, 76), () =>
            FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include)?.Open());
        HomeUi.Button("Home", layout, "Hauptmenü", new Vector2(0, -174.5f), new Vector2(500, 76), SaveAndHome);
        status = HomeUi.Label("Status", layout, "", new Vector2(0, -230), new Vector2(440, 40), 24); // centred in the strip between "Hauptmenü" and the frame
        status.enableAutoSizing = true; status.fontSizeMin = 14; status.fontSizeMax = 24; // longer error messages shrink to fit the strip
        Visible(false); HomeUi.Fit(layout, new Vector2(780, 1070));
    }
    void Update()
    {
        if (!LoadingProgress.Active && Time.timeScale > 0 && !MainMenuController.IsVisible && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen)
            GameSaveSystem.PlayedSeconds += Time.deltaTime;
        if (!Input.GetKeyDown(KeyCode.Escape) || InputConsumedFrame == Time.frameCount || LoadingProgress.Active || RunNavigation.IsTransitioning) return;
        var settings = FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        if (challengesPanel || SaveSlotPanel.IsOpen || MetaProgressionPanel.IsOpen || (settings && settings.IsOpen)) return;
        if (IsOpen && !GameSaveSystem.IsBusy) { ConsumeInput(); Close(); }
        else if (!GameplayInputBlocker.IsBlocked && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen) { ConsumeInput(); Open(); }
    }
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true; previousScale = Time.timeScale; Time.timeScale = 0;
        GameplayInputBlocker.SetBlocked(this, true); GamePause.Set(this, true); transform.SetAsLastSibling(); Visible(true);
        status.text = "";
    }
    public void Close()
    {
        if (GameSaveSystem.IsBusy || challengesPanel) return;
        IsOpen = false; GameplayInputBlocker.SetBlocked(this, false); GamePause.Set(this, false); Visible(false);
        if (!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale = previousScale;
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }
    void OpenChallenges()
    {
        if (!IsOpen || challengesPanel) return;
        layout.gameObject.SetActive(false);
        challengesPanel = ChallengePanel.Create(transform, () => {
            challengesPanel = null;
            layout.gameObject.SetActive(true);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(challengesButton.gameObject);
        });
    }
    // Saves the running game into its own slot, then returns home. A run that cannot be captured
    // (e.g. no energy left) keeps its last save; only a run without any slot still offers the slot picker.
    void SaveAndHome()
    {
        if (GameSaveSystem.IsBusy || RunNavigation.IsTransitioning) return;
        if (GameSaveSystem.ActiveSlot < 1) { SaveSlotPanel.Show(transform.parent, true); return; }
        if (!GameSaveSystem.CanSave) { RunNavigation.MainMenu(); return; }
        status.text = "Speichern …";
        StartCoroutine(GameSaveSystem.Save(GameSaveSystem.ActiveSlot, this,
            (ok, message) => { status.text = message; if (ok) RunNavigation.MainMenu(); }));
    }
    void Visible(bool show) { group.alpha = show ? 1 : 0; group.interactable = group.blocksRaycasts = show; }
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(780, 1070));
    void OnDestroy() { IsOpen = false; GameplayInputBlocker.SetBlocked(this, false); GamePause.Set(this, false); }
}
