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

// Compatibility API; all values and persistence belong to the shared GPS profile.
public static class GameplayTestSettings
{
    static GameplayTestSettingsData Data => GpsSettings.Tests;
    public static string FilePath => GpsSettings.FilePath;
    public static string Warning => GpsSettings.Warning;
    public static bool HasUnsavedChanges => GpsSettings.HasUnsavedChanges;
    public static float ConfiguredDiggingMultiplier => Data.diggingMultiplier;
    public static float ConfiguredMovementMultiplier => Data.movementMultiplier;
    public static bool IsDiggingMultiplierEnabled => Data.diggingMultiplierEnabled;
    public static bool IsMovementMultiplierEnabled => Data.movementMultiplierEnabled;
    public static bool HasMiningHitOffsetOverride => false;
    public static float ConfiguredMiningHitOffsetMs => (float)(GpsSettings.Document.records.Find(record => record.type == typeof(MinerPlayerVisual).AssemblyQualifiedName)?.fields.Find(field => field.name == "miningHitOffsetMs")?.number ?? 0);
    public static float ConfiguredSmartCursorStrokeWidth => Data.smartCursorStrokeWidth;
    public static bool KeepMapInEditor => GetConfiguredMode(GameplayTestMode.KeepMap);
    public static bool IsActive => GetMode(GameplayTestMode.Active);
    public static bool GodMode => GetMode(GameplayTestMode.God);
    public static bool NoEnergyConsume => GetMode(GameplayTestMode.NoEnergyConsume);
    public static bool FlyMode => GetMode(GameplayTestMode.Fly);
    public static bool NoClipMode => GetMode(GameplayTestMode.NoClip);
    public static bool NoWeight => GetMode(GameplayTestMode.NoWeight);
    public static bool InfiniteMoney => GetMode(GameplayTestMode.InfiniteMoney);
    public static bool GlobalLighting => GetMode(GameplayTestMode.GlobalLighting);
    public static bool CameraFollowEnabled
    {
        get {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Data.cameraFollow;
#else
            return true;
#endif
        }
    }
    public static GameplayDayNightMode ConfiguredDayNightMode => Data.dayNightMode;
    public static GameplayDayNightMode EffectiveDayNightMode => GameplayDayNightMode.Automatic;
    public static bool GetConfiguredMode(GameplayTestMode mode) => mode switch
    {
        GameplayTestMode.Active => !Data.testModeDisabled,
        GameplayTestMode.KeepMap => !Data.discardPlayedMap,
        GameplayTestMode.God => Data.godMode,
        GameplayTestMode.NoEnergyConsume => Data.noEnergyConsume,
        GameplayTestMode.Fly => Data.flyMode,
        GameplayTestMode.NoClip => Data.noClip,
        GameplayTestMode.NoWeight => Data.noWeight,
        GameplayTestMode.InfiniteMoney => Data.infiniteMoney,
        GameplayTestMode.CameraFollow => Data.cameraFollow,
        _ => Data.globalLighting
    };
    public static bool GetMode(GameplayTestMode mode)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return !Data.testModeDisabled && GetConfiguredMode(mode);
#else
        return false;
#endif
    }
    public static float DiggingMultiplier => IsActive && Data.diggingMultiplierEnabled ? Data.diggingMultiplier : 1f;
    public static float MovementMultiplier => IsActive && Data.movementMultiplierEnabled ? Data.movementMultiplier : 1f;
    public static bool IsValid(float value) => GpsCodec.Finite(value) && value >= .1f && value <= 100f;
    public static bool IsValidMovementMultiplier(float value) => GpsCodec.Finite(value) && value >= .1f && value <= 20f;
    public static bool IsValidMiningHitOffset(float value) => GpsCodec.Finite(value) && value >= -500f && value <= 500f;
    public static bool IsValidSmartCursorStrokeWidth(float value) => GpsCodec.Finite(value) && value >= 0f && value <= 3f;
    static bool Set(string field, object value)
    {
        var node = GpsCodec.Read(field, GpsCodec.MemberType(typeof(GameplayTestSettingsData), field), value, GpsSettings.Profile.Key);
        return GpsSettings.SetValue("tests", node, out _);
    }
    public static bool SetMode(GameplayTestMode mode, bool value, out string error)
    {
        string field = mode switch { GameplayTestMode.Active => "testModeDisabled", GameplayTestMode.KeepMap => "discardPlayedMap",
            GameplayTestMode.God => "godMode", GameplayTestMode.NoEnergyConsume => "noEnergyConsume", GameplayTestMode.Fly => "flyMode",
            GameplayTestMode.NoClip => "noClip", GameplayTestMode.NoWeight => "noWeight", GameplayTestMode.InfiniteMoney => "infiniteMoney",
            GameplayTestMode.CameraFollow => "cameraFollow", _ => "globalLighting" };
        if (mode == GameplayTestMode.Active || mode == GameplayTestMode.KeepMap) value = !value;
        var node = GpsSettings.GetValue("tests", field); node.flag = value;
        return GpsSettings.SetValue("tests", node, out error);
    }
    public static bool SetDayNightMode(GameplayDayNightMode mode, out string error)
    {
        error = null;
        if (!Enum.IsDefined(typeof(GameplayDayNightMode), mode)) { error = "Ungültige Tageszeit."; return false; }
        Data.dayNightMode = GameplayDayNightMode.Automatic;
        var record = GpsSettings.Document.records.Find(record => record.type == typeof(SkyController).AssemblyQualifiedName);
        if (record == null) { error = "Himmel fehlt."; return false; }
        var automatic = record.fields.Find(field => field.name == "automaticCycle").Copy(); automatic.flag = mode == GameplayDayNightMode.Automatic;
        if (!GpsSettings.SetValue(record.key, automatic, out error)) return false;
        if (mode != GameplayDayNightMode.Automatic) { var night = record.fields.Find(field => field.name == "isNight").Copy(); night.flag = mode == GameplayDayNightMode.Night; return GpsSettings.SetValue(record.key, night, out error); }
        return true;
    }
    public static bool SetDiggingMultiplier(float value) => IsValid(value) && Set("diggingMultiplier", value);
    public static bool SetMovementMultiplier(float value) => IsValidMovementMultiplier(value) && Set("movementMultiplier", value);
    public static bool SetDiggingMultiplierEnabled(bool value, out string error) { error = null; return Set("diggingMultiplierEnabled", value); }
    public static bool SetMovementMultiplierEnabled(bool value, out string error) { error = null; return Set("movementMultiplierEnabled", value); }
    public static bool SetSmartCursorStrokeWidth(float value) => IsValidSmartCursorStrokeWidth(value) && Set("smartCursorStrokeWidth", value);
    public static bool SetMiningHitOffset(float value)
    {
        if (!IsValidMiningHitOffset(value)) return false;
        var record = GpsSettings.Document.records.Find(record => record.type == typeof(MinerPlayerVisual).AssemblyQualifiedName);
        var node = record?.fields.Find(field => field.name == "miningHitOffsetMs")?.Copy(); if (node == null) return false;
        node.number = value; return GpsSettings.SetValue(record.key, node, out _);
    }
    public static bool Save(out string error) => GpsSettings.Save(out error);
}
