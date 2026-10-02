using UnityEngine;

public sealed class RubbleAudioSettingsAsset : ScriptableObject
{
    [Min(0f)] public float minimumDelaySeconds = .1f;
    [Min(0f)] public float maximumDelaySeconds = .3f;
    [Range(0f, 100f)] public float nextClipStartPercent = 50f;
}
