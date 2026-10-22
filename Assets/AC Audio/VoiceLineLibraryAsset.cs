using System;
using System.Collections.Generic;
using UnityEngine;

// Voice clips per event: sets → sentences (lines) → takes (V1/V2…). Generated from Design/Sprachausgabe/Voice-Manifest.json.
// Line/take indices always refer to the filtered lists: takes without clip and lines without any clip are skipped.
public sealed class VoiceLineLibraryAsset : ScriptableObject
{
    public List<VoiceLineSet> sets = new();
    List<VoiceLineSet> indexed;
    int indexedCount;
    readonly Dictionary<int, VoiceLineSet> lookup = new();

    void OnEnable() => indexed = null;

    public VoiceLineSet Set(VoiceLineEvent e)
    {
        if (!ReferenceEquals(indexed, sets) || sets != null && indexedCount != sets.Count)
        {
            indexed = sets; indexedCount = sets?.Count ?? 0; lookup.Clear();
            if (sets != null)
                foreach (var set in sets)
                    if (set != null && !lookup.ContainsKey((int)set.trigger)) lookup.Add((int)set.trigger, set);
        }
        return lookup.TryGetValue((int)e, out var found) ? found : null;
    }

    public int LineCount(VoiceLineEvent e)
    {
        var set = Set(e);
        if (set?.lines == null) return 0;
        int count = 0;
        foreach (var line in set.lines) if (HasClip(line)) count++;
        return count;
    }

    public int TakeCount(VoiceLineEvent e, int line)
    {
        var entry = Line(e, line);
        if (entry == null) return 0;
        int count = 0;
        foreach (var take in entry.takes) if (take != null && take.clip) count++;
        return count;
    }

    public VoiceLineTake Take(VoiceLineEvent e, int line, int take)
    {
        var entry = Line(e, line);
        if (entry == null || take < 0) return null;
        foreach (var candidate in entry.takes)
            if (candidate != null && candidate.clip && take-- == 0) return candidate;
        return null;
    }

    public IEnumerable<AudioClip> AllClips()
    {
        if (sets == null) yield break;
        foreach (var set in sets)
            if (set?.lines != null)
                foreach (var line in set.lines)
                    if (line?.takes != null)
                        foreach (var take in line.takes)
                            if (take != null && take.clip) yield return take.clip;
    }

    VoiceLine Line(VoiceLineEvent e, int index)
    {
        var set = Set(e);
        if (set?.lines == null || index < 0) return null;
        foreach (var line in set.lines)
            if (HasClip(line) && index-- == 0) return line;
        return null;
    }

    static bool HasClip(VoiceLine line)
    {
        if (line?.takes == null) return false;
        foreach (var take in line.takes) if (take != null && take.clip) return true;
        return false;
    }
}

[Serializable] public sealed class VoiceLineSet { public VoiceLineEvent trigger; public List<VoiceLine> lines = new(); }
[Serializable] public sealed class VoiceLine { public string id; public List<VoiceLineTake> takes = new(); }
[Serializable] public sealed class VoiceLineTake { public AudioClip clip; [Range(0f, 1f)] public float gain = 1f; }
