using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    public static bool IsVisible { get; private set; }
    RectTransform layout;
    Button continueButton, newGameButton;
    TMPro.TextMeshProUGUI status, progressionLabel;
    static readonly Vector2 MenuButtonSize = new Vector2(420, 102);
    const float MenuButtonFontSize = 28;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsVisible = false;
    System.Collections.IEnumerator Start()
    {
        yield return null;
        yield return LoadingScreen.Prepare();
        while (RunNavigation.IsTransitioning) yield return null;
        if (MetaProgressionPanel.OpenUpgradesOnHome)
        {
            MetaProgressionPanel.OpenUpgradesOnHome = false;
            var canvas = GameObject.Find("ScreenCanvas");
            if (canvas) MetaProgressionPanel.Show(canvas.transform)?.OpenUpgrades();
        }
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
        var board = HomeUi.Image("Menu Board", layout, new Vector2(-565, 50), new Vector2(500, 900), "Panel");
        board.pixelsPerUnitMultiplier = 1.5f;
        var title = HomeUi.Image("Game Title", layout, new Vector2(-565, 383), new Vector2(430, 145));
        title.sprite = Resources.Load<Sprite>("Homescreen/TiefenhallLogo");
        title.preserveAspect = true;
        BuildHeaderDivider();
        continueButton = MenuButton("Continue", "Fortsetzen", 235, Continue);
        newGameButton = MenuButton("New Game", "Neues Spiel", 125, () => NewGamePanel.Show(canvas.transform));
        MenuButton("Save Games", "Spielstände", 15, () => SaveSlotPanel.Show(canvas.transform, false));
        var progression = MenuButton("Progression", "Fortschritt", -95,
            () => MetaProgressionPanel.Show(canvas.transform));
        progressionLabel = progression.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        MenuButton("Settings", "Einstellungen", -205, () =>
            FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include)?.Open());
        MenuButton("Quit", "Beenden", -315, RunNavigation.Quit);
        status = HomeUi.Label("Status", layout, "", new Vector2(-565, -430), new Vector2(620, 40), 22);
        GameSaveSystem.SlotsChanged += Refresh;
        MetaProgression.Changed += RefreshProgression; RefreshProgression();
        Refresh(); Fit();
    }
    Button MenuButton(string name, string caption, float y, System.Action action)
    {
        var button = HomeUi.Button(name, layout, caption, new Vector2(-565, y), MenuButtonSize, action);
        var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        // A single fixed size also fits the longest progression caption at level 1000.
        label.enableAutoSizing = false;
        label.fontSize = label.fontSizeMin = label.fontSizeMax = MenuButtonFontSize;
        return button;
    }
    void BuildHeaderDivider()
    {
        var divider = HomeUi.Rect("Header Divider", layout, new Vector2(-565, 305), new Vector2(436, 22));
        HomeUi.Image("Rail Shadow", divider, new Vector2(0, -2), new Vector2(436, 5)).color = new Color32(29, 12, 3, 220);
        HomeUi.Image("Gold Rail", divider, Vector2.zero, new Vector2(436, 3)).color = new Color32(183, 103, 28, 255);
        HomeUi.Image("Rail Highlight", divider, new Vector2(0, 1), new Vector2(436, 1)).color = new Color32(255, 218, 125, 255);
        var shadow = HomeUi.Image("Diamond Shadow", divider, new Vector2(0, -2), new Vector2(18, 18));
        shadow.color = new Color32(29, 12, 3, 255); shadow.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var border = HomeUi.Image("Diamond Rim", divider, Vector2.zero, new Vector2(16, 16));
        border.color = new Color32(255, 212, 109, 255); border.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var inset = HomeUi.Image("Diamond Inset", divider, Vector2.zero, new Vector2(11, 11));
        inset.color = new Color32(107, 48, 10, 255); inset.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var center = HomeUi.Image("Diamond Gold", divider, Vector2.zero, new Vector2(7, 7));
        center.color = new Color32(232, 160, 52, 255); center.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
    }
    void Continue()
    {
        int slot = GameSaveSystem.MostRecentSlot();
        if (slot >= 0 && !RunNavigation.LoadGame(slot, out string error)) status.text = error;
    }
    public void ShowStatus(string message) { if (status) status.text = message; }
    void RefreshProgression()
    {
        if (progressionLabel) progressionLabel.text = "Fortschritt · Level " + MetaProgression.Level;
    }
    void Refresh()
    {
        if (!continueButton) return;
        continueButton.interactable = GameSaveSystem.MostRecentSlot() >= 0;
        newGameButton.interactable = GameSaveSystem.NextFreeSlot() > 0 && !GameSaveSystem.IsBusy;
        newGameButton.GetComponent<HomeButtonFeedback>().primary = false;
    }
    void OnRectTransformDimensionsChange() => Fit();
    void Fit() => HomeUi.Fit(layout, new Vector2(1920, 1080));
    void OnDestroy() { IsVisible = false; GameSaveSystem.SlotsChanged -= Refresh; MetaProgression.Changed -= RefreshProgression; }
}
