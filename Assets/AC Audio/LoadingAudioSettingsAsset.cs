using UnityEngine;

public sealed class LoadingAudioSettingsAsset : ScriptableObject
{
    [Range(0f, 1f)] public float yogaVolume = 1f;
    [Range(0f, 1f)] public float pickaxeVolume = 1f;
    [Min(0f)] public float worldFadeInSeconds = 2f;
    [Min(0f)] public float yogaFadeOutSeconds = 5f;
}
