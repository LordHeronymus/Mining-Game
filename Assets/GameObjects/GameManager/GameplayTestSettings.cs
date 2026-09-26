using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class GameplayTestSettingsData
{
    public int version = 4;
    public float diggingMultiplier = 1f;
    public float movementMultiplier = 1f;
    public bool hasMiningHitOffsetOverride;
    public float miningHitOffsetMs;
    public bool godMode, noEnergyConsume, flyMode, noClip;
    public bool globalLighting = true;
    public bool testModeDisabled;
    public bool discardPlayedMap;
    public GameplayDayNightMode dayNightMode;
}

public enum GameplayTestMode { God, NoEnergyConsume, Fly, NoClip, GlobalLighting, Active, KeepMap }
public enum GameplayDayNightMode { Automatic, Day, Night }

// Deliberately separate from GameplaySettingsData and the editor defaults writer.
public static class GameplayTestSettings
{
    static bool loaded;
    static float multiplier = 1f, saved = 1f, movementMultiplier = 1f, savedMovementMultiplier = 1f;
    static bool hasMiningHitOffsetOverride;
    static float miningHitOffsetMs;
    static bool savedHasMiningHitOffsetOverride;
    static float savedMiningHitOffsetMs;
    static bool godMode, noEnergyConsume, flyMode, noClip, testModeDisabled, discardPlayedMap;
    static bool globalLighting = true;
    static GameplayDayNightMode dayNightMode;
    static string savedModes;
    static string Modes => $"{godMode},{noEnergyConsume},{flyMode},{noClip},{globalLighting},{testModeDisabled},{discardPlayedMap},{dayNightMode}";
    // Editor preview preference is independent of gameplay cheats and their master switch.
    public static bool KeepMapInEditor => GetConfiguredMode(GameplayTestMode.KeepMap);
    public static bool IsActive => GetMode(GameplayTestMode.Active);
    public static float ConfiguredDiggingMultiplier { get { EnsureLoaded(); return multiplier; } }
    public static float ConfiguredMovementMultiplier { get { EnsureLoaded(); return movementMultiplier; } }
    public static bool HasMiningHitOffsetOverride { get { EnsureLoaded(); return hasMiningHitOffsetOverride; } }
    public static float ConfiguredMiningHitOffsetMs { get { EnsureLoaded(); return miningHitOffsetMs; } }
    public static bool GodMode => GetMode(GameplayTestMode.God);
    public static bool NoEnergyConsume => GetMode(GameplayTestMode.NoEnergyConsume);
    public static bool FlyMode => GetMode(GameplayTestMode.Fly);
    public static bool NoClipMode => GetMode(GameplayTestMode.NoClip);
    public static bool GlobalLighting => IsActive && GetConfiguredMode(GameplayTestMode.GlobalLighting);
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
        if (mode == GameplayTestMode.God) return godMode;
        if (mode == GameplayTestMode.NoEnergyConsume) return noEnergyConsume;
        if (mode == GameplayTestMode.Fly) return flyMode;
        if (mode == GameplayTestMode.NoClip) return noClip;
        return globalLighting;
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
    public static float MovementMultiplier
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureLoaded(); return testModeDisabled ? 1f : movementMultiplier;
#else
            return 1f;
#endif
        }
    }
    public static bool HasUnsavedChanges { get { EnsureLoaded(); return multiplier != saved || movementMultiplier != savedMovementMultiplier || hasMiningHitOffsetOverride != savedHasMiningHitOffsetOverride || miningHitOffsetMs != savedMiningHitOffsetMs || Modes != savedModes; } }
    public static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= .1f && value <= 100f;
    public static bool IsValidMovementMultiplier(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= .1f && value <= 20f;
    public static bool IsValidMiningHitOffset(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= -500f && value <= 500f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { loaded = false; multiplier = saved = movementMultiplier = savedMovementMultiplier = 1f; hasMiningHitOffsetOverride = savedHasMiningHitOffsetOverride = false; miningHitOffsetMs = savedMiningHitOffsetMs = 0f; Warning = null; godMode = noEnergyConsume = flyMode = noClip = testModeDisabled = discardPlayedMap = false; globalLighting = true; dayNightMode = GameplayDayNightMode.Automatic; savedModes = Modes; }

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
                if (data == null || data.version < 1 || data.version > 4 ||
                    !IsValid(data.diggingMultiplier) || (data.version >= 3 && !IsValidMovementMultiplier(data.movementMultiplier)) ||
                    (data.version >= 4 && data.hasMiningHitOffsetOverride && !IsValidMiningHitOffset(data.miningHitOffsetMs)))
                    throw new FormatException("Ungültiger Testfaktor.");
                multiplier = data.diggingMultiplier; testModeDisabled = data.testModeDisabled;
                movementMultiplier = data.version >= 3 ? data.movementMultiplier : 1f;
                hasMiningHitOffsetOverride = data.version >= 4 && data.hasMiningHitOffsetOverride;
                miningHitOffsetMs = hasMiningHitOffsetOverride ? data.miningHitOffsetMs : 0f;
                discardPlayedMap = data.discardPlayedMap;
                godMode = data.godMode; noEnergyConsume = data.noEnergyConsume; flyMode = data.flyMode;
                noClip = data.noClip; globalLighting = data.version < 2 || data.globalLighting;
                dayNightMode = Enum.IsDefined(typeof(GameplayDayNightMode), data.dayNightMode)
                    ? data.dayNightMode : GameplayDayNightMode.Automatic;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is FormatException)
        { Warning = "Testfaktor 1× geladen: " + ex.Message; multiplier = movementMultiplier = 1f; }
#endif
        saved = multiplier;
        savedMovementMultiplier = movementMultiplier;
        savedHasMiningHitOffsetOverride = hasMiningHitOffsetOverride;
        savedMiningHitOffsetMs = miningHitOffsetMs;
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
        else if (mode == GameplayTestMode.Fly) flyMode = value;
        else if (mode == GameplayTestMode.NoClip) noClip = value;
        else globalLighting = value;
    }

    public static bool SetDiggingMultiplier(float value)
    {
        if (!IsValid(value)) return false;
        EnsureLoaded(); multiplier = value; return true;
    }

    public static bool SetMovementMultiplier(float value)
    {
        if (!IsValidMovementMultiplier(value)) return false;
        EnsureLoaded(); movementMultiplier = value; return true;
    }

    public static bool SetMiningHitOffset(float value)
    {
        if (!IsValidMiningHitOffset(value)) return false;
        EnsureLoaded(); hasMiningHitOffsetOverride = true; miningHitOffsetMs = value; return true;
    }

    public static bool Save(out string error)
    {
        EnsureLoaded(); error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(new GameplayTestSettingsData {
                version = 4,
                discardPlayedMap = discardPlayedMap,
                testModeDisabled = testModeDisabled, diggingMultiplier = multiplier, movementMultiplier = movementMultiplier,
                hasMiningHitOffsetOverride = hasMiningHitOffsetOverride, miningHitOffsetMs = miningHitOffsetMs,
                godMode = godMode, noEnergyConsume = noEnergyConsume, flyMode = flyMode,
                noClip = noClip, globalLighting = globalLighting,
                dayNightMode = dayNightMode
            }, true));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
            saved = multiplier; savedMovementMultiplier = movementMultiplier; savedHasMiningHitOffsetOverride = hasMiningHitOffsetOverride; savedMiningHitOffsetMs = miningHitOffsetMs; savedModes = Modes; Warning = null; return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { error = "Testeinstellungen nicht gespeichert: " + ex.Message; return false; }
    }
}
