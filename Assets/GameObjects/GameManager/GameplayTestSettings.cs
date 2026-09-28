using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class GameplayTestSettingsData
{
    public int version = 9;
    public float diggingMultiplier = 1f;
    public float movementMultiplier = 1f;
    public bool diggingMultiplierEnabled = true;
    public bool movementMultiplierEnabled = true;
    public bool hasMiningHitOffsetOverride;
    public float miningHitOffsetMs;
    public float smartCursorStrokeWidth = .5f;
    public bool godMode, noEnergyConsume, flyMode, noClip, noWeight, infiniteMoney;
    public bool globalLighting = true;
    public bool cameraFollow = true;
    public bool testModeDisabled;
    public bool discardPlayedMap;
    public GameplayDayNightMode dayNightMode;
}

public enum GameplayTestMode { God, NoEnergyConsume, Fly, NoClip, NoWeight, GlobalLighting, Active, KeepMap, InfiniteMoney, CameraFollow }
public enum GameplayDayNightMode { Automatic, Day, Night }

// Deliberately separate from GameplaySettingsData and the editor defaults writer.
public static class GameplayTestSettings
{
    const float DefaultSmartCursorStrokeWidth = .5f;
    const float MaximumSmartCursorStrokeWidth = 3f;
    static bool loaded;
    static float multiplier = 1f, saved = 1f, movementMultiplier = 1f, savedMovementMultiplier = 1f;
    static bool diggingMultiplierEnabled = true, savedDiggingMultiplierEnabled = true;
    static bool movementMultiplierEnabled = true, savedMovementMultiplierEnabled = true;
    static bool hasMiningHitOffsetOverride;
    static float miningHitOffsetMs;
    static float smartCursorStrokeWidth = DefaultSmartCursorStrokeWidth;
    static float savedSmartCursorStrokeWidth = DefaultSmartCursorStrokeWidth;
    static bool savedHasMiningHitOffsetOverride;
    static float savedMiningHitOffsetMs;
    static bool godMode, noEnergyConsume, flyMode, noClip, noWeight, infiniteMoney, testModeDisabled, discardPlayedMap;
    static bool globalLighting = true;
    static bool cameraFollow = true;
    static GameplayDayNightMode dayNightMode;
    static string savedModes;
    static string Modes => $"{godMode},{noEnergyConsume},{flyMode},{noClip},{noWeight},{infiniteMoney},{globalLighting},{testModeDisabled},{discardPlayedMap},{dayNightMode},{cameraFollow}";
    // Editor preview preference is independent of gameplay cheats and their master switch.
    public static bool KeepMapInEditor => GetConfiguredMode(GameplayTestMode.KeepMap);
    public static bool IsActive => GetMode(GameplayTestMode.Active);
    public static float ConfiguredDiggingMultiplier { get { EnsureLoaded(); return multiplier; } }
    public static float ConfiguredMovementMultiplier { get { EnsureLoaded(); return movementMultiplier; } }
    public static bool IsDiggingMultiplierEnabled { get { EnsureLoaded(); return diggingMultiplierEnabled; } }
    public static bool IsMovementMultiplierEnabled { get { EnsureLoaded(); return movementMultiplierEnabled; } }
    public static bool HasMiningHitOffsetOverride { get { EnsureLoaded(); return hasMiningHitOffsetOverride; } }
    public static float ConfiguredMiningHitOffsetMs { get { EnsureLoaded(); return miningHitOffsetMs; } }
    public static float ConfiguredSmartCursorStrokeWidth { get { EnsureLoaded(); return smartCursorStrokeWidth; } }
    public static bool GodMode => GetMode(GameplayTestMode.God);
    public static bool NoEnergyConsume => GetMode(GameplayTestMode.NoEnergyConsume);
    public static bool FlyMode => GetMode(GameplayTestMode.Fly);
    public static bool NoClipMode => GetMode(GameplayTestMode.NoClip);
    public static bool NoWeight => GetMode(GameplayTestMode.NoWeight);
    public static bool InfiniteMoney => GetMode(GameplayTestMode.InfiniteMoney);
    public static bool GlobalLighting => IsActive && GetConfiguredMode(GameplayTestMode.GlobalLighting);
    public static bool CameraFollowEnabled => GetConfiguredMode(GameplayTestMode.CameraFollow);
    public static GameplayDayNightMode ConfiguredDayNightMode { get { EnsureLoaded(); return dayNightMode; } }
    public static GameplayDayNightMode EffectiveDayNightMode
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureLoaded();
            return dayNightMode;
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
        if (mode == GameplayTestMode.NoWeight) return noWeight;
        if (mode == GameplayTestMode.InfiniteMoney) return infiniteMoney;
        if (mode == GameplayTestMode.CameraFollow) return cameraFollow;
        return globalLighting;
    }
    public static string FilePath => Path.Combine(Application.persistentDataPath, "gameplay-test-settings.json");
    public static float DiggingMultiplier
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureLoaded(); return testModeDisabled || !diggingMultiplierEnabled ? 1f : multiplier;
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
            EnsureLoaded(); return testModeDisabled || !movementMultiplierEnabled ? 1f : movementMultiplier;
#else
            return 1f;
#endif
        }
    }
    public static bool HasUnsavedChanges { get { EnsureLoaded(); return multiplier != saved || movementMultiplier != savedMovementMultiplier || diggingMultiplierEnabled != savedDiggingMultiplierEnabled || movementMultiplierEnabled != savedMovementMultiplierEnabled || hasMiningHitOffsetOverride != savedHasMiningHitOffsetOverride || miningHitOffsetMs != savedMiningHitOffsetMs || smartCursorStrokeWidth != savedSmartCursorStrokeWidth || Modes != savedModes; } }
    public static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= .1f && value <= 100f;
    public static bool IsValidMovementMultiplier(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= .1f && value <= 20f;
    public static bool IsValidMiningHitOffset(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= -500f && value <= 500f;
    public static bool IsValidSmartCursorStrokeWidth(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= MaximumSmartCursorStrokeWidth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { loaded = false; multiplier = saved = movementMultiplier = savedMovementMultiplier = 1f; diggingMultiplierEnabled = savedDiggingMultiplierEnabled = movementMultiplierEnabled = savedMovementMultiplierEnabled = true; hasMiningHitOffsetOverride = savedHasMiningHitOffsetOverride = false; miningHitOffsetMs = savedMiningHitOffsetMs = 0f; smartCursorStrokeWidth = savedSmartCursorStrokeWidth = DefaultSmartCursorStrokeWidth; Warning = null; godMode = noEnergyConsume = flyMode = noClip = noWeight = infiniteMoney = testModeDisabled = discardPlayedMap = false; globalLighting = cameraFollow = true; dayNightMode = GameplayDayNightMode.Automatic; savedModes = Modes; }

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
                if (data == null || data.version < 1 || data.version > 9 ||
                    !IsValid(data.diggingMultiplier) || (data.version >= 3 && !IsValidMovementMultiplier(data.movementMultiplier)) ||
                    (data.version >= 4 && data.hasMiningHitOffsetOverride && !IsValidMiningHitOffset(data.miningHitOffsetMs)) ||
                    (data.version >= 9 && !IsValidSmartCursorStrokeWidth(data.smartCursorStrokeWidth)))
                    throw new FormatException("Ungültiger Testfaktor.");
                multiplier = data.diggingMultiplier; testModeDisabled = data.testModeDisabled;
                movementMultiplier = data.version >= 3 ? data.movementMultiplier : 1f;
                diggingMultiplierEnabled = data.version >= 5 ? data.diggingMultiplierEnabled : true;
                movementMultiplierEnabled = data.version >= 5 ? data.movementMultiplierEnabled : true;
                hasMiningHitOffsetOverride = data.version >= 4 && data.hasMiningHitOffsetOverride;
                miningHitOffsetMs = hasMiningHitOffsetOverride ? data.miningHitOffsetMs : 0f;
                smartCursorStrokeWidth = data.version >= 9 ? data.smartCursorStrokeWidth : DefaultSmartCursorStrokeWidth;
                discardPlayedMap = data.discardPlayedMap;
                godMode = data.godMode; noEnergyConsume = data.noEnergyConsume; flyMode = data.flyMode;
                noClip = data.noClip; globalLighting = data.version < 2 || data.globalLighting;
                noWeight = data.version >= 6 && data.noWeight;
                infiniteMoney = data.version >= 7 && data.infiniteMoney;
                cameraFollow = data.version < 8 || data.cameraFollow;
                dayNightMode = Enum.IsDefined(typeof(GameplayDayNightMode), data.dayNightMode)
                    ? data.dayNightMode : GameplayDayNightMode.Automatic;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is FormatException)
        { Warning = "Testfaktor 1× geladen: " + ex.Message; multiplier = movementMultiplier = 1f; diggingMultiplierEnabled = movementMultiplierEnabled = true; }
#endif
        saved = multiplier;
        savedMovementMultiplier = movementMultiplier;
        savedDiggingMultiplierEnabled = diggingMultiplierEnabled;
        savedMovementMultiplierEnabled = movementMultiplierEnabled;
        savedHasMiningHitOffsetOverride = hasMiningHitOffsetOverride;
        savedMiningHitOffsetMs = miningHitOffsetMs;
        savedSmartCursorStrokeWidth = smartCursorStrokeWidth;
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
        else if (mode == GameplayTestMode.NoWeight) noWeight = value;
        else if (mode == GameplayTestMode.InfiniteMoney) infiniteMoney = value;
        else if (mode == GameplayTestMode.CameraFollow) cameraFollow = value;
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

    public static bool SetDiggingMultiplierEnabled(bool value, out string error)
        => SetMultiplierEnabled(true, value, out error);

    public static bool SetMovementMultiplierEnabled(bool value, out string error)
        => SetMultiplierEnabled(false, value, out error);

    static bool SetMultiplierEnabled(bool digging, bool value, out string error)
    {
        EnsureLoaded();
        bool previous = digging ? diggingMultiplierEnabled : movementMultiplierEnabled;
        if (digging) diggingMultiplierEnabled = value;
        else movementMultiplierEnabled = value;
        if (Save(out error)) return true;
        if (digging) diggingMultiplierEnabled = previous;
        else movementMultiplierEnabled = previous;
        return false;
    }

    public static bool SetMiningHitOffset(float value)
    {
        if (!IsValidMiningHitOffset(value)) return false;
        EnsureLoaded(); hasMiningHitOffsetOverride = true; miningHitOffsetMs = value; return true;
    }

    public static bool SetSmartCursorStrokeWidth(float value)
    {
        if (!IsValidSmartCursorStrokeWidth(value)) return false;
        EnsureLoaded(); smartCursorStrokeWidth = value; return true;
    }

    public static bool Save(out string error)
    {
        EnsureLoaded(); error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(new GameplayTestSettingsData {
                version = 9,
                discardPlayedMap = discardPlayedMap,
                testModeDisabled = testModeDisabled, diggingMultiplier = multiplier, movementMultiplier = movementMultiplier,
                diggingMultiplierEnabled = diggingMultiplierEnabled, movementMultiplierEnabled = movementMultiplierEnabled,
                hasMiningHitOffsetOverride = hasMiningHitOffsetOverride, miningHitOffsetMs = miningHitOffsetMs,
                smartCursorStrokeWidth = smartCursorStrokeWidth,
                godMode = godMode, noEnergyConsume = noEnergyConsume, flyMode = flyMode,
                noClip = noClip, noWeight = noWeight, infiniteMoney = infiniteMoney, globalLighting = globalLighting,
                cameraFollow = cameraFollow,
                dayNightMode = dayNightMode
            }, true));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
            saved = multiplier; savedMovementMultiplier = movementMultiplier;
            savedDiggingMultiplierEnabled = diggingMultiplierEnabled; savedMovementMultiplierEnabled = movementMultiplierEnabled;
            savedHasMiningHitOffsetOverride = hasMiningHitOffsetOverride; savedMiningHitOffsetMs = miningHitOffsetMs;
            savedSmartCursorStrokeWidth = smartCursorStrokeWidth; savedModes = Modes; Warning = null; return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { error = "Testeinstellungen nicht gespeichert: " + ex.Message; return false; }
    }
}
