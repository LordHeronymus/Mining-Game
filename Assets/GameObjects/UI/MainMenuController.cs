using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    public static bool IsVisible { get; private set; }
    RectTransform layout;
    Button continueButton, newGameButton;
    TMPro.TextMeshProUGUI status;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsVisible = false;
    System.Collections.IEnumerator Start()
    {
        yield return null;
        yield return LoadingScreen.Prepare();
    }
    void Awake()
    {
        GameSaveSystem.MigrateLegacyAutomaticSave();
        IsVisible = true; Time.timeScale = 1; HomeUi.EnsureEventSystem();
        LoadingAudio.StartHome();
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var canvas = GameObject.Find("ScreenCanvas");
        if (!canvas)
        {
            canvas = new GameObject("ScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        }
        var background = HomeUi.Rect("Cave Landscape", canvas.transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(background); background.gameObject.AddComponent<HomeCaveVisual>();
        layout = HomeUi.Rect("Home Layout", canvas.transform, Vector2.zero, new Vector2(1920, 1080));
        var board = HomeUi.Image("Menu Board", layout, new Vector2(-565, -50), new Vector2(500, 760), "Panel");
        board.pixelsPerUnitMultiplier = 1.5f;
        HomeUi.Image("Title Plaque", layout, new Vector2(-565, 380), new Vector2(508, 205), "Title");
        HomeUi.Label("Game Title", layout, "Tiefenhall", new Vector2(-565, 410), new Vector2(430, 95), 76);
        continueButton = HomeUi.Button("Continue", layout, "Fortsetzen", new Vector2(-565, 208), new Vector2(420, 102), Continue, true);
        newGameButton = HomeUi.Button("New Game", layout, "Neues Spiel", new Vector2(-565, 88), new Vector2(420, 102), () => NewGamePanel.Show(canvas.transform));
        HomeUi.Button("Save Games", layout, "Spielstände", new Vector2(-565, -32), new Vector2(420, 102), () => SaveSlotPanel.Show(canvas.transform, false));
        HomeUi.Button("Settings", layout, "Einstellungen", new Vector2(-565, -152), new Vector2(420, 102), () =>
            FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include)?.Open());
        HomeUi.Button("Quit", layout, "Beenden", new Vector2(-565, -272), new Vector2(420, 102), RunNavigation.Quit);
        status = HomeUi.Label("Status", layout, "", new Vector2(-565, -432), new Vector2(620, 55), 25);
        GameSaveSystem.SlotsChanged += Refresh; Refresh(); Fit();
    }
    void Continue()
    {
        int slot = GameSaveSystem.MostRecentSlot();
        if (slot >= 0 && !RunNavigation.LoadGame(slot, out string error)) status.text = error;
    }
    public void ShowStatus(string message) { if (status) status.text = message; }
    void Refresh()
    {
        if (!continueButton) return;
        continueButton.interactable = GameSaveSystem.MostRecentSlot() >= 0;
        newGameButton.interactable = GameSaveSystem.NextFreeSlot() > 0 && !GameSaveSystem.IsBusy;
        newGameButton.GetComponent<HomeButtonFeedback>().primary = !continueButton.interactable;
    }
    void OnRectTransformDimensionsChange() => Fit();
    void Fit() => HomeUi.Fit(layout, new Vector2(1920, 1080));
    void OnDestroy() { IsVisible = false; GameSaveSystem.SlotsChanged -= Refresh; }
}
