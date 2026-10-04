using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public sealed class HomeAudioClipTuning
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(.5f, 2f)] public float pitch = 1f;
    [Range(0f, .5f)] public float volumeSpread;
    [Range(0f, .5f)] public float pitchSpread;
    public float SampleVolume() => volume * Random.Range(1f - volumeSpread, 1f + volumeSpread);
    public float SamplePitch() => pitch * Random.Range(1f - pitchSpread, 1f + pitchSpread);
}

public sealed class LoadingAudioSettingsAsset : ScriptableObject
{
    public List<HomeAudioClipTuning> homeClips = new List<HomeAudioClipTuning>();
    public HomeAudioClipTuning FindHomeClip(AudioClip clip) => homeClips.Find(entry => entry.clip == clip);
    [Range(0f, 1f)] public float yogaVolume = 1f;
    [Range(0f, 1f)] public float pickaxeVolume = 1f;
    [Min(0f)] public float worldFadeInSeconds = 2f;
    [Min(0f)] public float yogaFadeOutSeconds = 5f;
    [Range(0f, 1f)] public float distantPickaxeVolume = .5f;
    [Range(0f, 60f)] public float distantPickaxesPerMinute = 10f;
    [Range(0f, 50f)] public float distantPickaxePitchSpreadPercent = 10f;
    [Range(0f, 50f)] public float distantPickaxeVolumeSpreadPercent = 15f;
}
