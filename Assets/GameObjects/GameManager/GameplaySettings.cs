using System;
using System.IO;
using UnityEngine;

[Serializable]
public class GameplaySettingsData
{
    public int version = 1;
    public float baseDiggingSpeed = 1.5f;
    public bool hasLightingOverride;
    public LightingSettingsData lighting = new LightingSettingsData();
}

[Serializable]
public class LightingSettingsData
{
    public bool enabled = true;
    public float daylight = 1f, ambient = 0f, downLoss = 0.003f,
        sideLoss = 0.08f, blockLoss = 0.28f, strength = 1f;

    public LightingSettingsData Copy() => (LightingSettingsData)MemberwiseClone();
    public bool IsValid => Range(daylight, 0, 1) && Range(ambient, 0, 1) &&
        Range(downLoss, 0.0001f, 1) && Range(sideLoss, 0.0001f, 1) &&
        Range(blockLoss, 0, 1) && Range(strength, 0.1f, 5);
    static bool Range(float value, float min, float max) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
}

// Extend this data model and its validation for future gameplay settings.
public static class GameplaySettingsStore
{
    public const float MinDiggingSpeed = 0.01f;
    public const float MaxDiggingSpeed = 100f;

    public static bool IsValidSpeed(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) &&
        value >= MinDiggingSpeed && value <= MaxDiggingSpeed;

    public static GameplaySettingsData Load(string path, float defaultSpeed, out string error)
    {
        var defaults = new GameplaySettingsData { baseDiggingSpeed = defaultSpeed };
        error = null;
        try
        {
            if (!File.Exists(path)) return defaults;
            string json = File.ReadAllText(path).Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                throw new FormatException("Die Datei enthält kein JSON-Objekt.");
            var loaded = new GameplaySettingsData { baseDiggingSpeed = defaultSpeed };
            JsonUtility.FromJsonOverwrite(json, loaded);
            if (loaded.version != 1 || !IsValidSpeed(loaded.baseDiggingSpeed) ||
                (loaded.hasLightingOverride && (loaded.lighting == null || !loaded.lighting.IsValid)))
                throw new FormatException("Unbekannte Version oder ungültige Geschwindigkeit.");
            return loaded;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is FormatException)
        {
            error = "Standardwerte geladen: " + ex.Message;
            return defaults;
        }
    }

    public static bool Save(string path, GameplaySettingsData data, out string error)
    {
        error = null;
        if (data == null || data.version != 1 || !IsValidSpeed(data.baseDiggingSpeed) ||
            (data.hasLightingOverride && (data.lighting == null || !data.lighting.IsValid)))
        {
            error = "Ungültige Gameplay-Einstellungen.";
            return false;
        }
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
            // Replace only after the complete JSON has been written.
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        {
            error = "Speichern fehlgeschlagen: " + ex.Message;
            return false;
        }
    }
}

public static class GameplaySettings
{
    static GameplaySettingsData current = new GameplaySettingsData();
    static float defaultDiggingSpeed = 1.5f;
    static float savedDiggingSpeed = 1.5f;
    static bool initialized;
    static LightingSettingsData defaultLighting;
    static bool savedLightingOverride;
    static string savedLightingJson;

    public static event Action Changed;
    public static float BaseDiggingSpeed => current.baseDiggingSpeed;
    public static bool HasUnsavedChanges => current.baseDiggingSpeed != savedDiggingSpeed ||
        current.hasLightingOverride != savedLightingOverride ||
        (current.hasLightingOverride && JsonUtility.ToJson(current.lighting) != savedLightingJson);
    public static LightingSettingsData Lighting => (current.lighting ?? new LightingSettingsData()).Copy();
    public static bool LightingAvailable => defaultLighting != null;
    public static string LoadWarning { get; private set; }
    public static string FilePath => Path.Combine(Application.persistentDataPath, "gameplay-settings.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        initialized = false;
        current = new GameplaySettingsData();
        defaultDiggingSpeed = savedDiggingSpeed = 1.5f;
        Changed = null;
        LoadWarning = null;
        defaultLighting = null;
        savedLightingOverride = false;
        savedLightingJson = null;
    }

    public static void Initialize(float defaultSpeed)
    {
        if (initialized) return;
        defaultDiggingSpeed = GameplaySettingsStore.IsValidSpeed(defaultSpeed) ? defaultSpeed : 1.5f;
        current = new GameplaySettingsData { baseDiggingSpeed = defaultDiggingSpeed };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        current = GameplaySettingsStore.Load(FilePath, defaultDiggingSpeed, out string warning);
        LoadWarning = warning;
        if (warning != null) Debug.LogWarning(warning);
#endif
        savedDiggingSpeed = current.baseDiggingSpeed;
        savedLightingOverride = current.hasLightingOverride;
        savedLightingJson = JsonUtility.ToJson(current.lighting);
        initialized = true;
        Changed?.Invoke();
    }

    public static bool SetBaseDiggingSpeed(float value)
    {
        if (!GameplaySettingsStore.IsValidSpeed(value)) return false;
        current.baseDiggingSpeed = value;
        Changed?.Invoke();
        return true;
    }

    public static void RegisterLighting(LightingSettingsData defaults)
    {
        defaultLighting = defaults.Copy();
        if (!current.hasLightingOverride) current.lighting = defaults.Copy();
        Changed?.Invoke();
    }

    public static bool SetLighting(LightingSettingsData value)
    {
        if (value == null || !value.IsValid || !LightingAvailable) return false;
        if (JsonUtility.ToJson(value) == JsonUtility.ToJson(current.lighting)) return true;
        current.lighting = value.Copy();
        current.hasLightingOverride = true;
        Changed?.Invoke();
        return true;
    }

    public static void RestoreDefaults()
    {
        current.baseDiggingSpeed = defaultDiggingSpeed;
        current.hasLightingOverride = false;
        if (defaultLighting != null) current.lighting = defaultLighting.Copy();
        Changed?.Invoke();
    }

    public static bool Save(out string error)
    {
        if (!GameplaySettingsStore.Save(FilePath, current, out error)) return false;
        savedDiggingSpeed = current.baseDiggingSpeed;
        savedLightingOverride = current.hasLightingOverride;
        savedLightingJson = JsonUtility.ToJson(current.lighting);
        LoadWarning = null;
        Changed?.Invoke();
        return true;
    }
}
