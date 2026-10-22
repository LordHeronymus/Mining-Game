using System;
using UnityEngine;

// Tuning of the character voice lines (GPS section Audio/Stimme). Rules per event live in `events`.
public sealed class VoiceLineSettingsAsset : ScriptableObject
{
    public bool enabled = true;
    [Range(0f, 1f)] public float volume = 1f;
    [Min(0f)] public float globalCooldownSeconds = 20f;
    [Min(0f)] public float minimumGapSeconds = 1.5f;
    [Min(0f)] public float reactionGapSeconds = 4f;
    [Min(0)] public int interruptPriorityGap = 25;
    [Min(0f)] public float interruptFadeSeconds = .15f;
    [Min(0f)] public float pauseDropSeconds = 1.5f;        // a line paused (timeScale 0) longer than this is dropped
    [Range(0f, 1f)] public float energyLowFraction = .33f;
    [Range(0f, 1f)] public float energyCriticalFraction = .15f;
    [Range(0f, .5f)] public float energyRearmMargin = .05f;
    [Range(0f, 1f)] public float carryRearmMargin = .1f;
    [Range(0f, 1f)] public float refillMinimumFraction = .1f;  // recharge must add at least this share of max energy
    [Range(0f, 1f)] public float refillTargetFraction = .8f;   // and end at least at this share
    [Range(0f, 1f)] public float saleMinimumCarryShare = .3f;  // single-ore sale must remove this share of carry capacity (SellAll always counts)
    [Min(0f)] public float smallFallMinimumDamage = 5f;
    [Min(.02f)] public float watcherPollSeconds = .25f;
    [Min(1)] public int preloadClipsPerFrame = 8;
    public bool duckOtherAudio = true;
    [Range(0f, 1f)] public float duckVolume = .7f;
    [Min(0f)] public float duckFadeSeconds = .2f;
    public bool debugLog;
    public VoiceLineEventRule[] events = VoiceLineEventRule.Defaults();

    static VoiceLineEventRule[] fallback;
    VoiceLineEventRule[] indexed, lookup;
    VoiceLineConfig config;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => fallback = null;

    void OnEnable() => indexed = lookup = null;

    // First entry with that trigger, else the Defaults() entry. The cache follows the array reference because GPS
    // replaces the array via reflection (no OnValidate); an entry whose trigger was edited in place forces a rebuild.
    public VoiceLineEventRule Rule(VoiceLineEvent e)
    {
        int i = (int)e;
        if (lookup == null || !ReferenceEquals(indexed, events) ||
            i >= 0 && i < lookup.Length && (lookup[i] == null || lookup[i].trigger != e)) Index();
        return i >= 0 && i < lookup.Length ? lookup[i] : new VoiceLineEventRule { trigger = e };
    }

    void Index()
    {
        fallback ??= VoiceLineEventRule.Defaults();
        int size = 0;
        foreach (VoiceLineEvent value in Enum.GetValues(typeof(VoiceLineEvent))) size = Mathf.Max(size, (int)value + 1);
        lookup = new VoiceLineEventRule[size];
        indexed = events;
        if (events != null)
            foreach (var rule in events)
                if (rule != null && (int)rule.trigger >= 0 && (int)rule.trigger < size && lookup[(int)rule.trigger] == null)
                    lookup[(int)rule.trigger] = rule;
        for (int i = 0; i < size; i++)
        {
            if (lookup[i] != null) continue;
            foreach (var rule in fallback) if ((int)rule.trigger == i) { lookup[i] = rule; break; }
            lookup[i] ??= new VoiceLineEventRule { trigger = (VoiceLineEvent)i };
        }
    }

    // Cached snapshot object; values are refreshed on every call so GPS/F1 changes apply immediately.
    public VoiceLineConfig Config()
    {
        config ??= new VoiceLineConfig { rule = Rule };
        config.enabled = enabled;
        config.globalCooldownSeconds = Mathf.Max(0f, globalCooldownSeconds);
        config.minimumGapSeconds = Mathf.Max(0f, minimumGapSeconds);
        config.reactionGapSeconds = Mathf.Max(0f, reactionGapSeconds);
        config.interruptPriorityGap = Mathf.Max(0, interruptPriorityGap);
        return config;
    }
}
