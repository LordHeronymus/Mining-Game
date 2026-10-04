using System;
using System.IO;
using System.Linq;
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
    public static event Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Changed = null;
    public static float BaseDiggingSpeed => (float)(GpsSettings.Document.records
        .Find(record => record.type == typeof(PlayerBaseStats).AssemblyQualifiedName)?.fields.Find(field => field.name == "miningSpeed")?.number ?? 1.5);
    public static bool HasUnsavedChanges => GpsSettings.HasUnsavedChanges;
    public static string FilePath => GpsSettings.FilePath;
    public static string LoadWarning => GpsSettings.Warning;
    public static bool LightingAvailable => GpsSettings.Document.records.Exists(record => record.type == typeof(MapLighting).AssemblyQualifiedName);
    static GpsRecord LightRecord => GpsSettings.Document.records.Find(record => record.type == typeof(MapLighting).AssemblyQualifiedName);
    public static LightingSettingsData Lighting
    {
        get
        {
            var record = LightRecord;
            float Number(string field, float fallback) => (float)(record?.fields.Find(node => node.name == field)?.number ?? fallback);
            return new LightingSettingsData { enabled = record?.fields.Find(node => node.name == "lightingEnabled")?.flag ?? true,
                daylight = Number("daylightStrength", 1), ambient = Number("ambientBrightness", 0), downLoss = Number("downwardLoss", .003f),
                sideLoss = Number("sidewaysLoss", .08f), blockLoss = Number("blockLoss", .28f), strength = Number("exponentialStrength", 1) };
        }
    }
    public static void Initialize(float defaultSpeed) => GpsSettings.EnsureLoaded();
    public static void RegisterLighting(LightingSettingsData defaults) => GpsSettings.EnsureLoaded();
    public static bool SetBaseDiggingSpeed(float value)
    {
        if (!GameplaySettingsStore.IsValidSpeed(value)) return false;
        var record = GpsSettings.Document.records.Find(record => record.type == typeof(PlayerBaseStats).AssemblyQualifiedName);
        var node = record?.fields.Find(field => field.name == "miningSpeed")?.Copy();
        if (node == null) return false; node.number = value;
        bool changed = GpsSettings.SetValue(record.key, node, out _); if (changed) NotifyChanged(); return changed;
    }
    public static bool SetLighting(LightingSettingsData value)
    {
        if (value == null || !value.IsValid || LightRecord == null) return false;
        var record = LightRecord;
        var numbers = new System.Collections.Generic.Dictionary<string, float> { ["daylightStrength"] = value.daylight,
            ["ambientBrightness"] = value.ambient, ["downwardLoss"] = value.downLoss, ["sidewaysLoss"] = value.sideLoss,
            ["blockLoss"] = value.blockLoss, ["exponentialStrength"] = value.strength };
        foreach (var field in record.fields.ToArray())
        {
            var node = field.Copy();
            if (field.name == "lightingEnabled") node.flag = value.enabled;
            else if (numbers.TryGetValue(field.name, out float number)) node.number = number;
            else continue;
            if (!GpsSettings.SetValue(record.key, node, out _)) return false;
        }
        NotifyChanged(); return true;
    }
    public static void RestoreDefaults() => GpsSettings.Reload();
    public static bool Save(out string error) => GpsSettings.Save(out error);
}
