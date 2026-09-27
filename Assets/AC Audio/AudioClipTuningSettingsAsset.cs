using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AudioClipTuningEntry
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(.5f, 2f)] public float pitch = 1f;
    [Range(0f, .5f)] public float pitchSpread;
}

public sealed class AudioClipTuningSettingsAsset : ScriptableObject
{
    [SerializeField] List<AudioClipTuningEntry> clips = new();
    Dictionary<AudioClip, AudioClipTuningEntry> lookup;

    void OnEnable() => lookup = null;

    void EnsureLookup()
    {
        if (lookup != null) return;
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
        if (Mathf.Approximately(volume, 1f) && Mathf.Approximately(pitch, 1f) &&
            Mathf.Approximately(pitchSpread, 0f))
        {
            if (lookup.TryGetValue(clip, out var existing))
            {
                clips.Remove(existing);
                lookup.Remove(clip);
            }
            return;
        }
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
