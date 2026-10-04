using System;
using System.Collections.Generic;
using UnityEngine;

// The profile is the only persisted gameplay configuration. Scene fields are its consumers.
public sealed class GpsProfile : ScriptableObject
{
    [HideInInspector] public string documentJson;
    [HideInInspector] public List<GpsAssetReference> assets = new();
    public UnityEngine.Object Resolve(string key) => assets.Find(entry => entry.key == key)?.asset;
    public string Key(UnityEngine.Object asset) => asset ? assets.Find(entry => entry.asset == asset)?.key : null;
}

[Serializable] public sealed class GpsAssetReference { public string key; public UnityEngine.Object asset; }
[Serializable] public sealed class GpsDocument
{
    public int version = 1;
    public bool legacyMigrated;
    public List<GpsRecord> records = new();
    public GpsPreferences preferences = new();
    public GameplayTestSettingsData tests = new();
}
[Serializable] public sealed class GpsPreferences
{
    public StartingResourcesData startingResources = new();
    public float panelBackdropAlpha = .55f, panelElementAlpha = 1f;
    public float hotbarVerticalOffset, hotbarHoldDuration = .5f, masterVolume = 1f;
}
[Serializable] public sealed class GpsRecord
{
    public string key, type, assetKey, name;
    public List<GpsValue> fields = new();
}
public enum GpsValueKind { Number, Integer, Boolean, Text, Enum, Reference, Vector, Color, Curve, Object, Array }
[Serializable] public sealed class GpsValue
{
    public string name, type, text;
    public GpsValueKind kind;
    public double number;
    public bool flag;
    public Vector4 vector;
    public Color color;
    public GpsCurve curve;
    public List<GpsValue> children = new();
    public GpsValue Copy() => JsonUtility.FromJson<GpsValue>(JsonUtility.ToJson(this));
}
[Serializable] public sealed class GpsCurve
{
    [Serializable] public struct Key
    {
        public float time, value, inTangent, outTangent, inWeight, outWeight;
        public int weightedMode;
    }
    public int preWrap, postWrap;
    public List<Key> keys = new();
    public static GpsCurve From(AnimationCurve curve)
    {
        var result = new GpsCurve();
        if (curve == null) return result;
        result.preWrap = (int)curve.preWrapMode; result.postWrap = (int)curve.postWrapMode;
        foreach (var key in curve.keys) result.keys.Add(new Key { time = key.time, value = key.value,
            inTangent = key.inTangent, outTangent = key.outTangent, inWeight = key.inWeight,
            outWeight = key.outWeight, weightedMode = (int)key.weightedMode });
        return result;
    }
    public AnimationCurve ToCurve()
    {
        var result = new AnimationCurve();
        foreach (var key in keys) result.AddKey(new Keyframe(key.time, key.value, key.inTangent,
            key.outTangent, key.inWeight, key.outWeight) { weightedMode = (WeightedMode)key.weightedMode });
        result.preWrapMode = (WrapMode)preWrap; result.postWrapMode = (WrapMode)postWrap;
        return result;
    }
}
