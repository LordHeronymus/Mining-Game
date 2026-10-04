using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AudioClipTuningEntry
{
    public AudioClip clip;
    public string category;
    public string displayName;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(.5f, 2f)] public float pitch = 1f;
    [Range(0f, .5f)] public float pitchSpread;
    [Range(0f, .5f)] public float volumeSpread;
}

public sealed class AudioClipTuningSettingsAsset : ScriptableObject
{
    [SerializeField] List<AudioClipTuningEntry> clips = new();
    public int catalogVersion;
    Dictionary<AudioClip, AudioClipTuningEntry> lookup;
    List<AudioClipTuningEntry> indexedClips;

    void OnEnable() => lookup = null;

    void EnsureLookup()
    {
        if (lookup != null && ReferenceEquals(indexedClips, clips)) return;
        indexedClips = clips;
        lookup = new Dictionary<AudioClip, AudioClipTuningEntry>();
        foreach (var entry in clips)
            if (entry != null && entry.clip) lookup[entry.clip] = entry;
    }

    public bool TryGet(AudioClip clip, out AudioClipTuningEntry entry)
    {
        EnsureLookup();
        entry = null;
        return clip && lookup.TryGetValue(clip, out entry);
    }

    public void Set(AudioClip clip, float volume, float pitch, float pitchSpread)
    {
        if (!clip) return;
        EnsureLookup();
        volume = Mathf.Clamp01(volume);
        pitch = Mathf.Clamp(pitch, .5f, 2f);
        pitchSpread = Mathf.Clamp(pitchSpread, 0f, .5f);
        if (!lookup.TryGetValue(clip, out var entry))
        {
            entry = new AudioClipTuningEntry { clip = clip };
            clips.Add(entry);
            lookup.Add(clip, entry);
        }
        entry.volume = volume;
        entry.pitch = pitch;
        entry.pitchSpread = pitchSpread;
    }
}
