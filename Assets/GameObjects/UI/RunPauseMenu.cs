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
    Button saveButton;
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
        layout = HomeUi.Rect("Pause Layout", transform, Vector2.zero, new Vector2(700, 900));
        HomeUi.Image("Board", layout, Vector2.zero, new Vector2(650, 850), "Panel");
        HomeUi.Label("Title", layout, "Tiefenhall", new Vector2(0, 338), new Vector2(560, 85), 62);
        HomeUi.Button("Resume", layout, "Fortsetzen", new Vector2(0, 220), new Vector2(500, 76), Close, true);
        saveButton = HomeUi.Button("Save", layout, "Speichern", new Vector2(0, 129), new Vector2(500, 76), () => SaveSlotPanel.Show(transform.parent, true));
        HomeUi.Button("Load", layout, "Laden", new Vector2(0, 38), new Vector2(500, 76), () => SaveSlotPanel.Show(transform.parent, false));
        HomeUi.Button("Progression", layout, "Fortschritt", new Vector2(0, -53), new Vector2(500, 76), () => MetaProgressionPanel.Show(transform.parent, true));
        HomeUi.Button("Settings", layout, "Einstellungen", new Vector2(0, -144), new Vector2(500, 76), () =>
            FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include)?.Open());
        HomeUi.Button("Home", layout, "Speichern & Hauptmenü", new Vector2(0, -235), new Vector2(500, 76), SaveAndHome);
        status = HomeUi.Label("Status", layout, "", new Vector2(0, -350), new Vector2(580, 60), 24);
        Visible(false); HomeUi.Fit(layout, new Vector2(780, 970));
    }
    void Update()
    {
        if (!LoadingProgress.Active && Time.timeScale > 0 && !MainMenuController.IsVisible && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen)
            GameSaveSystem.PlayedSeconds += Time.deltaTime;
        if (!Input.GetKeyDown(KeyCode.Escape) || InputConsumedFrame == Time.frameCount || LoadingProgress.Active || RunNavigation.IsTransitioning) return;
        var settings = FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        if (SaveSlotPanel.IsOpen || MetaProgressionPanel.IsOpen || (settings && settings.IsOpen)) return;
        if (IsOpen && !GameSaveSystem.IsBusy) { ConsumeInput(); Close(); }
        else if (!GameplayInputBlocker.IsBlocked && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen) { ConsumeInput(); Open(); }
    }
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true; previousScale = Time.timeScale; Time.timeScale = 0;
        GameplayInputBlocker.SetBlocked(this, true); transform.SetAsLastSibling(); Visible(true);
        saveButton.interactable = GameSaveSystem.CanSave;
    }
    public void Close()
    {
        if (GameSaveSystem.IsBusy) return;
        IsOpen = false; GameplayInputBlocker.SetBlocked(this, false); Visible(false);
        if (!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale = previousScale;
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }
    void SaveAndHome()
    {
        if (GameSaveSystem.IsBusy) return;
        status.text = "Speichern …";
        if (GameSaveSystem.ActiveSlot < 1) { SaveSlotPanel.Show(transform.parent, true); return; }
        StartCoroutine(GameSaveSystem.Save(GameSaveSystem.ActiveSlot, this,
            (ok, message) => { status.text = message; if (ok) RunNavigation.MainMenu(); }));
    }
    void Visible(bool show) { group.alpha = show ? 1 : 0; group.interactable = group.blocksRaycasts = show; }
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(780, 970));
    void OnDestroy() { IsOpen = false; GameplayInputBlocker.SetBlocked(this, false); }
}
