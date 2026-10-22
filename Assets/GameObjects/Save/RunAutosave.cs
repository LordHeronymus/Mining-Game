using UnityEngine;
using UnityEngine.SceneManagement;

// Saves the running game into its own slot every few minutes of play (GPS: Spieler → Autosave) while the game
// keeps running, and once more when play mode stops or the application quits.
[DefaultExecutionOrder(-1050)]
public sealed class RunAutosave : MonoBehaviour
{
    const float NoticeSeconds = 2.5f;
    public static RunAutosave Instance { get; private set; }
    public float SecondsSinceSave { get; private set; }
    TMPro.TextMeshProUGUI notice;
    float noticeUntil;
    static bool exitSaved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance = null; exitSaved = false; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        Application.quitting -= SaveOnExit; Application.quitting += SaveOnExit;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= PlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += PlayModeChanged;
#endif
    }
#if UNITY_EDITOR
    static void PlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode) SaveOnExit();
    }
#endif
    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var canvas = GameObject.Find("ScreenCanvas");
        if (scene.name == RunNavigation.MenuScene || !canvas || Instance) return;
        new GameObject("Run Autosave").AddComponent<RunAutosave>().BuildNotice(canvas.transform);
    }
    static void SaveOnExit()
    {
        if (exitSaved || !Instance) return;
        exitSaved = true;
        if (GameSaveSystem.SaveActiveRunNow(Instance)) Debug.Log("Tiefenhall: Spielstand beim Beenden gespeichert (Slot " + GameSaveSystem.ActiveSlot + ").");
    }
    public static float IntervalSeconds => Mathf.Clamp(GpsSettings.Preferences.autosaveIntervalMinutes, 0f, 60f) * 60f;

    void Awake() { Instance = this; GameSaveSystem.SlotsChanged += Saved; }
    // Leaving the run stops its autosave coroutine; drop the unfinished capture with it.
    void OnDisable() => GameSaveSystem.StopAutosave();
    void OnDestroy() { if (Instance == this) Instance = null; GameSaveSystem.SlotsChanged -= Saved; }
    // Any successful save (also leaving to the main menu) restarts the interval.
    void Saved() => SecondsSinceSave = 0;
    void BuildNotice(Transform canvas)
    {
        notice = HomeUi.Label("Autosave Notice", canvas, "Automatisch gespeichert", Vector2.zero, new Vector2(420, 40), 24);
        var rect = notice.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-28, 24);
        notice.alignment = TMPro.TextAlignmentOptions.BottomRight;
        notice.raycastTarget = false; notice.alpha = 0;
    }
    void Update()
    {
        if (notice) notice.alpha = Mathf.Clamp01((noticeUntil - Time.unscaledTime) / .5f);
        // Count only real play time: no pause, menus, loading or finished runs.
        if (Time.timeScale <= 0 || LoadingProgress.Active || RunNavigation.IsTransitioning || MainMenuController.IsVisible ||
            GameOverPanel.IsOpen || GameVictoryPanel.IsOpen) return;
        SecondsSinceSave += Time.unscaledDeltaTime;
        float interval = IntervalSeconds;
        // Wait while a panel holds the gameplay input, so a save never interrupts shop or dialog interaction.
        if (interval <= 0 || SecondsSinceSave < interval || GameplayInputBlocker.IsBlocked || !GameSaveSystem.CanAutoSave ||
            GameSaveSystem.IsAutoSaving) return;
        SecondsSinceSave = 0;
        StartCoroutine(GameSaveSystem.Autosave(this, (ok, _) => { if (ok) noticeUntil = Time.unscaledTime + NoticeSeconds; }));
    }
}
