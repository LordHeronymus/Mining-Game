using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class GameplayTestSettingsData
{
    public int version = 1;
    public float diggingMultiplier = 1f;
    public bool godMode, noEnergyConsume, flyMode;
    public bool testModeDisabled;
    public bool discardPlayedMap;
    public GameplayDayNightMode dayNightMode;
}

public enum GameplayTestMode { God, NoEnergyConsume, Fly, Active, KeepMap }
public enum GameplayDayNightMode { Automatic, Day, Night }

// Deliberately separate from GameplaySettingsData and the editor defaults writer.
public static class GameplayTestSettings
{
    static bool loaded;
    static float multiplier = 1f, saved = 1f;
    static bool godMode, noEnergyConsume, flyMode, testModeDisabled, discardPlayedMap;
    static GameplayDayNightMode dayNightMode;
    static string savedModes;
    static string Modes => $"{godMode},{noEnergyConsume},{flyMode},{testModeDisabled},{discardPlayedMap},{dayNightMode}";
    // Editor preview preference is independent of gameplay cheats and their master switch.
    public static bool KeepMapInEditor => GetConfiguredMode(GameplayTestMode.KeepMap);
    public static bool IsActive => GetMode(GameplayTestMode.Active);
    public static float ConfiguredDiggingMultiplier { get { EnsureLoaded(); return multiplier; } }
    public static bool GodMode => GetMode(GameplayTestMode.God);
    public static bool NoEnergyConsume => GetMode(GameplayTestMode.NoEnergyConsume);
    public static bool FlyMode => GetMode(GameplayTestMode.Fly);
    public static GameplayDayNightMode ConfiguredDayNightMode { get { EnsureLoaded(); return dayNightMode; } }
    public static GameplayDayNightMode EffectiveDayNightMode
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureLoaded();
            return testModeDisabled ? GameplayDayNightMode.Automatic : dayNightMode;
#else
            return GameplayDayNightMode.Automatic;
#endif
        }
    }
    public static bool GetMode(GameplayTestMode mode)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        EnsureLoaded();
        return !testModeDisabled && GetConfiguredMode(mode);
#else
        return false;
#endif
    }
    public static string Warning { get; private set; }
    public static bool GetConfiguredMode(GameplayTestMode mode)
    {
        EnsureLoaded();
        if (mode == GameplayTestMode.Active) return !testModeDisabled;
        if (mode == GameplayTestMode.KeepMap) return !discardPlayedMap;
        return mode == GameplayTestMode.God ? godMode : mode == GameplayTestMode.NoEnergyConsume ? noEnergyConsume : flyMode;
    }
    public static string FilePath => Path.Combine(Application.persistentDataPath, "gameplay-test-settings.json");
    public static float DiggingMultiplier
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureLoaded(); return testModeDisabled ? 1f : multiplier;
#else
            return 1f;
#endif
        }
    }
    public static bool HasUnsavedChanges { get { EnsureLoaded(); return multiplier != saved || Modes != savedModes; } }
    public static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= .1f && value <= 100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { loaded = false; multiplier = saved = 1f; Warning = null; godMode = noEnergyConsume = flyMode = testModeDisabled = discardPlayedMap = false; dayNightMode = GameplayDayNightMode.Automatic; savedModes = Modes; }

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonUtility.FromJson<GameplayTestSettingsData>(File.ReadAllText(FilePath));
                if (data == null || data.version != 1 || !IsValid(data.diggingMultiplier))
                    throw new FormatException("Ungültiger Test-Abbaufaktor.");
                multiplier = data.diggingMultiplier; testModeDisabled = data.testModeDisabled;
                discardPlayedMap = data.discardPlayedMap;
                godMode = data.godMode; noEnergyConsume = data.noEnergyConsume; flyMode = data.flyMode;
                dayNightMode = Enum.IsDefined(typeof(GameplayDayNightMode), data.dayNightMode)
                    ? data.dayNightMode : GameplayDayNightMode.Automatic;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is FormatException)
        { Warning = "Testfaktor 1× geladen: " + ex.Message; multiplier = 1f; }
#endif
        saved = multiplier;
        savedModes = Modes;
    }

    public static bool SetMode(GameplayTestMode mode, bool value, out string error)
    {
        EnsureLoaded();
        bool previous = GetConfiguredMode(mode);
        AssignMode(mode, value);
        if (Save(out error)) return true;
        AssignMode(mode, previous);
        return false;
    }

    public static bool SetDayNightMode(GameplayDayNightMode mode, out string error)
    {
        EnsureLoaded();
        if (!Enum.IsDefined(typeof(GameplayDayNightMode), mode))
        { error = "Ungültige Tageszeit."; return false; }
        GameplayDayNightMode previous = dayNightMode;
        dayNightMode = mode;
        if (Save(out error)) return true;
        dayNightMode = previous;
        return false;
    }

    static void AssignMode(GameplayTestMode mode, bool value)
    {
        if (mode == GameplayTestMode.Active) testModeDisabled = !value;
        else if (mode == GameplayTestMode.KeepMap) discardPlayedMap = !value;
        else if (mode == GameplayTestMode.God) godMode = value;
        else if (mode == GameplayTestMode.NoEnergyConsume) noEnergyConsume = value;
        else flyMode = value;
    }

    public static bool SetDiggingMultiplier(float value)
    {
        if (!IsValid(value)) return false;
        EnsureLoaded(); multiplier = value; return true;
    }

    public static bool Save(out string error)
    {
        EnsureLoaded(); error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(new GameplayTestSettingsData {
                discardPlayedMap = discardPlayedMap,
                testModeDisabled = testModeDisabled, diggingMultiplier = multiplier, godMode = godMode, noEnergyConsume = noEnergyConsume, flyMode = flyMode,
                dayNightMode = dayNightMode
            }, true));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
            saved = multiplier; savedModes = Modes; Warning = null; return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { error = "Testeinstellungen nicht gespeichert: " + ex.Message; return false; }
    }
}
