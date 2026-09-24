using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public class GameplayDebugPanel : MonoBehaviour
{
    [SerializeField] CanvasGroup panel;
    [SerializeField] TMP_InputField diggingSpeedInput;
    [SerializeField] TextMeshProUGUI statusText;
    [SerializeField] Button saveButton;
    [SerializeField] Button defaultsButton;
    [SerializeField] Button makeDefaultsButton;
    [SerializeField] Button closeButton;

    [SerializeField] TMP_InputField[] lightInputs;
    [SerializeField] Button lightingButton;
    [SerializeField] TextMeshProUGUI lightingButtonText;
    static readonly string[] LightNames = { "Tageslicht", "Grundhelligkeit", "Verlust nach unten", "Verlust seitlich / oben", "Blockverlust", "Exponentielle Stärke" };

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => IsOpen = false;

    void Awake()
    {
        SetVisible(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        gameObject.AddComponent<GameplayDebugWindow>();
        DisableInputChildRaycasts(diggingSpeedInput);
        diggingSpeedInput.onEndEdit.AddListener(ApplyInput);
        foreach (var input in lightInputs)
        {
            DisableInputChildRaycasts(input);
            input.onEndEdit.AddListener(ApplyInput);
        }
        lightingButton.onClick.AddListener(ToggleLighting);
        saveButton.gameObject.SetActive(false);
        defaultsButton.onClick.AddListener(RestoreDefaults);
        closeButton.onClick.AddListener(Close);
        GameplaySettings.Changed += Refresh;
#if UNITY_EDITOR
        if (makeDefaultsButton) makeDefaultsButton.onClick.AddListener(MakeDefaults);
#else
        if (makeDefaultsButton) makeDefaultsButton.gameObject.SetActive(false);
#endif
#else
        gameObject.SetActive(false);
#endif
    }

    static void DisableInputChildRaycasts(TMP_InputField input)
    {
        if (!input) return;
        foreach (var graphic in input.GetComponentsInChildren<Graphic>(true))
            if (graphic != input.targetGraphic) graphic.raycastTarget = false;
    }

    void Start()
    {
        Refresh();
    }

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F1)) Toggle();
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
#endif
    }

    public void Toggle()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsOpen) Close();
        else
        {
            Refresh();
            GetComponent<GameplayDebugWindow>()?.RefreshSoundSettings();
            transform.SetAsLastSibling();
            SetVisible(true);
        }
#endif
    }

    public void Close()
    {
        var window = GetComponent<GameplayDebugWindow>();
        if (window && window.IsTestTab) { if (!window.ApplyTestInput()) return; }
        else if (window && window.IsIconTab) { if (!window.CommitIconInputs()) return; }
        else if (window && window.IsRecipeTab) { if (!window.CommitRecipeInputs()) return; }
        else if (window && window.IsStartingResourcesTab) { if (!window.CommitStartingResources()) return; }
        else if (!TryApplyAll()) return;
        var events = EventSystem.current;
        if (events && events.currentSelectedGameObject &&
            events.currentSelectedGameObject.transform.IsChildOf(transform))
            events.SetSelectedGameObject(null);
        SetVisible(false);
    }

    void SetVisible(bool visible)
    {
        IsOpen = visible;
        GameplayInputBlocker.SetBlocked(this, visible);
        if (!visible) GetComponent<GameplayDebugWindow>()?.HideTooltip();
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = visible;
        panel.blocksRaycasts = visible;
    }

    static bool Parse(string text, out float value) =>
        float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        !float.IsNaN(value) && !float.IsInfinity(value);

    public bool TryApplyAll()
    {
        if (!Parse(diggingSpeedInput.text, out float speed) || !GameplaySettingsStore.IsValidSpeed(speed))
        {
            statusText.text = "Abbaugeschwindigkeit: bitte 0,01 bis 100 eingeben.";
            return false;
        }
        var light = GameplaySettings.Lighting;
        if (GameplaySettings.LightingAvailable)
        {
            var values = new float[6];
            for (int i = 0; i < values.Length; i++)
            {
                float min = i == 5 ? 0.1f : i == 2 || i == 3 ? 0.0001f : 0f;
                float max = i == 5 ? 5f : 1f;
                if (!Parse(lightInputs[i].text, out values[i]) || values[i] < min || values[i] > max)
                {
                    statusText.text = $"{LightNames[i]}: bitte {min} bis {max} eingeben.";
                    return false;
                }
            }
            light.daylight = values[0]; light.ambient = values[1];
            light.downLoss = values[2]; light.sideLoss = values[3];
            light.blockLoss = values[4]; light.strength = values[5];
        }
        // Capture and validate every field before change events refresh the UI.
        GameplaySettings.SetBaseDiggingSpeed(speed);
        if (GameplaySettings.LightingAvailable) GameplaySettings.SetLighting(light);
        return SaveDebugSettings();
    }

    void ApplyInput(string input) => TryApplyAll();

    void ToggleLighting()
    {
        if (!TryApplyAll()) return;
        var light = GameplaySettings.Lighting;
        light.enabled = !light.enabled;
        GameplaySettings.SetLighting(light);
        SaveDebugSettings();
    }

    bool SaveDebugSettings()
    {
        if (GameplaySettings.HasUnsavedChanges && !GameplaySettings.Save(out string error))
        {
            statusText.text = error;
            return false;
        }
        statusText.text = GameplaySettings.LoadWarning ?? "";
        return true;
    }
    void RestoreDefaults()
    {
        GameplaySettings.RestoreDefaults();
        SaveDebugSettings();
    }

#if UNITY_EDITOR
    void MakeDefaults()
    {
        if (!TryApplyAll()) return;
        GameplayDebugDefaults.QueueCurrent(out string message);
        statusText.text = message;
    }
#endif

    void Refresh()
    {
        diggingSpeedInput.SetTextWithoutNotify(GameplaySettings.BaseDiggingSpeed.ToString("R", CultureInfo.InvariantCulture));
        var light = GameplaySettings.Lighting;
        var values = new[] { light.daylight, light.ambient, light.downLoss, light.sideLoss, light.blockLoss, light.strength };
        for (int i = 0; i < lightInputs.Length; i++)
        {
            lightInputs[i].SetTextWithoutNotify(values[i].ToString("R", CultureInfo.InvariantCulture));
            lightInputs[i].interactable = GameplaySettings.LightingAvailable;
        }
        lightingButton.interactable = GameplaySettings.LightingAvailable;
        lightingButtonText.text = !GameplaySettings.LightingAvailable ? "Keine Map" : light.enabled ? "Licht: An" : "Licht: Aus";
        statusText.text = GameplaySettings.LoadWarning ?? "";
    }

    void OnDisable()
    {
        IsOpen = false;
        GameplayInputBlocker.SetBlocked(this, false);
    }

    void OnDestroy()
    {
        GameplaySettings.Changed -= Refresh;
        IsOpen = false;
        GameplayInputBlocker.SetBlocked(this, false);
    }
}
