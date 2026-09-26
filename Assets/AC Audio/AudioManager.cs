using System.Collections.Generic;
using UnityEngine;

public enum AmbienceType { Surface, Rain, Thunderstorm, Underground, Cave, Birds, Frogs }
public enum AudioVolumeSetting { Ambience, Surface, Rain, Thunderstorm, Underground, Cave, DigSounds, DingLight }
public enum AudioTimeOffsetSetting { DingLight }

public enum SoundType
{
    DigSoft = 2,
    DigMedium = 0,
    DigHard = 3,
    DigOre = 4,
    BreakRock = 1,
    BreakOre = 5,
    SellItem = 6,
    Recharge = 9,
    DoorOpen = 10,
    UI_Alert = 7,
    UI_Click = 8,
    DigDirt = 11,
    DigTransitionStone = 12,
    DigStone = 13,
    DirtHit = 14,
    WoodChop = 15,
    LadderPlace = 16,
    ItemInBag = 17,
    DryGrass = 18,
    TreeFall = 19,
    DigDeepStone = 21,
    LadderRemove = 20,
    StoneBreak = 22,
    ClayBreak = 23,
    Hurt = 24,
    BoneBreaking1 = 25,
    Death = 26,
}

[System.Serializable]
public class Sound
{
    public SoundType type;
    public AudioClip clip;
    public AudioClip[] variants;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 3f)] public float pitch = 1f;
}

[System.Serializable]
public sealed class LayerMiningClipTuning
{
    [Range(0f, 1f)] public float volume = 1f;
    [Range(.5f, 2f)] public float pitch = 1f;
    [Range(0f, .5f)] public float pitchSpread = .05f;
}

public class AudioManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] Sound[] sounds;
    [SerializeField] int initialPoolSize = 8;
    [SerializeField] int maxPoolSize = 32;
    [SerializeField] float dispersionAmount = 0.05f;
    [SerializeField, Range(0f, 1f)] float ambienceVolume = 1f;
    [SerializeField, Range(0f, 1f)] float surfaceVolume = 1f;
    [SerializeField, Range(0f, 1f)] float rainVolume = 1f;
    [SerializeField, Range(0f, 1f)] float thunderstormVolume = 1f;
    [SerializeField, Range(0f, 1f)] float undergroundVolume = 1f;
    [SerializeField, Range(0f, 1f)] float caveVolume = 1f;
    [SerializeField, Range(0f, 1f)] float digSoundVolume = 1f;
    [SerializeField, Range(0f, 1f)] float dingLightVolume = 1f;
    [SerializeField, Range(0f, 1f)] float lowHealthHeartbeatVolume = 0.65f;
    [SerializeField, Range(-10f, 10f)] float dingLightOffsetSeconds;
    [SerializeField] LayerMiningAudioSettingsAsset layerMiningSettings;

    [SerializeField, Range(-1f, 1f)] float grassLandingOffset = -.08f;

    public static float GetAmbienceVolume(AmbienceType type)
    {
        if (!Instance) return 1f;
        float category = type switch
        {
            AmbienceType.Surface => Instance.surfaceVolume,
            AmbienceType.Rain => Instance.rainVolume,
            AmbienceType.Thunderstorm => Instance.thunderstormVolume,
            AmbienceType.Underground => Instance.undergroundVolume,
            AmbienceType.Cave => Instance.caveVolume,
            _ => 1f
        };
        return AmbienceVolume * Mathf.Clamp01(category);
    }

    public static float AmbienceVolume => Instance ? Mathf.Clamp01(Instance.ambienceVolume) : 1f;

    public float GetVolume(AudioVolumeSetting setting) => setting switch
    {
        AudioVolumeSetting.Ambience => ambienceVolume,
        AudioVolumeSetting.Surface => surfaceVolume,
        AudioVolumeSetting.Rain => rainVolume,
        AudioVolumeSetting.Thunderstorm => thunderstormVolume,
        AudioVolumeSetting.Underground => undergroundVolume,
        AudioVolumeSetting.Cave => caveVolume,
        AudioVolumeSetting.DigSounds => digSoundVolume,
        AudioVolumeSetting.DingLight => dingLightVolume,
        _ => 1f
    };

    public void SetVolume(AudioVolumeSetting setting, float value)
    {
        value = Mathf.Clamp01(value);
        switch (setting)
        {
            case AudioVolumeSetting.Ambience: ambienceVolume = value; break;
            case AudioVolumeSetting.Surface: surfaceVolume = value; break;
            case AudioVolumeSetting.Rain: rainVolume = value; break;
            case AudioVolumeSetting.Thunderstorm: thunderstormVolume = value; break;
            case AudioVolumeSetting.Underground: undergroundVolume = value; break;
            case AudioVolumeSetting.Cave: caveVolume = value; break;
            case AudioVolumeSetting.DigSounds: digSoundVolume = value; break;
            case AudioVolumeSetting.DingLight: dingLightVolume = value; break;
        }
    }

    public float GetTimeOffset(AudioTimeOffsetSetting setting) => setting switch
    {
        AudioTimeOffsetSetting.DingLight => dingLightOffsetSeconds,
        _ => 0f
    };

    public void SetTimeOffset(AudioTimeOffsetSetting setting, float seconds)
    {
        seconds = Mathf.Clamp(seconds, -10f, 10f);
        switch (setting)
        {
            case AudioTimeOffsetSetting.DingLight: dingLightOffsetSeconds = seconds; break;
        }
    }

    [HideInInspector] public static AudioManager Instance;

    List<AudioSource> pool = new();
    readonly List<(AudioSource source, AudioClip clip)> activeCraftingSounds = new();
    readonly Dictionary<AudioSource, (float volume, AmbienceType type)> ambienceSources = new();
    readonly Dictionary<string, LayerMiningClipTuning> layerMiningTuning = new();
    Dictionary<SoundType, Sound> soundLookup = new Dictionary<SoundType, Sound>();
    AudioClip[] frogCroaks;
    AudioClip grassLanding;
    AudioSource lowHealthHeartbeatSource;
    Coroutine lowHealthHeartbeatFade;
    bool lowHealthHeartbeatRequested;
    float nextLowHealthHeartbeatTime;
    const float LowHealthHeartbeatPitchSpread = .02f;
    readonly System.Random ambienceRandom = new System.Random();

    LayerMiningAudioSettingsAsset LayerMiningSettings
    {
        get
        {
            if (!layerMiningSettings)
                layerMiningSettings = Resources.Load<LayerMiningAudioSettingsAsset>("Audio/LayerMiningAudioSettings");
            return layerMiningSettings;
        }
    }

    public AudioClip GetRandomFrogCroak()
    {
        if (frogCroaks == null) frogCroaks = Resources.LoadAll<AudioClip>("FrogCroaks");
        return frogCroaks.Length > 0 ? frogCroaks[ambienceRandom.Next(frogCroaks.Length)] : null;
    }

    public void UpdateGrassLanding(ref bool triggered, float secondsToLanding, Vector3 position)
    {
        if (triggered || secondsToLanding > Mathf.Max(0f, -grassLandingOffset)) return;
        triggered = true;
        float delay = Mathf.Max(0f, secondsToLanding + grassLandingOffset);
        if (delay > 0f) StartCoroutine(DelayedGrassLanding(position, delay));
        else PlayGrassLanding(position);
    }

    System.Collections.IEnumerator DelayedGrassLanding(Vector3 position, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayGrassLanding(position);
    }

    public void PlayGrassLanding(Vector3 position)
    {
        if (!grassLanding) grassLanding = Resources.Load<AudioClip>("Audio/WalkOnGrass");
        var camera = Camera.main;
        if (!grassLanding || !camera) return;
        float distance = Vector2.Distance(position, camera.transform.position);
        float volume = .35f * Mathf.Clamp01(1f - distance / 20f);
        float pan = Mathf.Clamp((camera.WorldToViewportPoint(position).x - .5f) * 1.4f, -.7f, .7f);
        PlayClip(grassLanding, volume, pan, ambience: true, ambienceType: AmbienceType.Surface);
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        for (int i = 0; i < initialPoolSize; i++) ExtendPool();

        var heartbeatClip = Resources.Load<AudioClip>("Audio/SingleHeartBeat");
        if (heartbeatClip)
        {
            lowHealthHeartbeatSource = gameObject.AddComponent<AudioSource>();
            lowHealthHeartbeatSource.playOnAwake = false;
            lowHealthHeartbeatSource.loop = false;
            lowHealthHeartbeatSource.spatialBlend = 0f;
            lowHealthHeartbeatSource.volume = Mathf.Clamp01(lowHealthHeartbeatVolume);
            lowHealthHeartbeatSource.clip = heartbeatClip;
        }

        foreach (var s in sounds) soundLookup[s.type] = s;
        if (!soundLookup.ContainsKey(SoundType.BoneBreaking1))
        {
            var boneBreakingClip = Resources.Load<AudioClip>("Audio/BoneBreaking_01");
            if (boneBreakingClip)
                soundLookup[SoundType.BoneBreaking1] = new Sound
                {
                    type = SoundType.BoneBreaking1,
                    clip = boneBreakingClip,
                    volume = 1f,
                    pitch = 1f
                };
        }
        if (!soundLookup.ContainsKey(SoundType.Death))
        {
            var deathClip = Resources.Load<AudioClip>("Audio/Hurt2");
            if (deathClip)
                soundLookup[SoundType.Death] = new Sound
                {
                    type = SoundType.Death,
                    clip = deathClip,
                    volume = 1f,
                    pitch = 1f
                };
        }
    }

    public bool TryGetLowHealthHeartbeatPlayback(out float positionSeconds, out float durationSeconds)
    {
        positionSeconds = durationSeconds = 0f;
        if (!lowHealthHeartbeatRequested || !lowHealthHeartbeatSource ||
            !lowHealthHeartbeatSource.isPlaying || !lowHealthHeartbeatSource.clip) return false;
        var clip = lowHealthHeartbeatSource.clip;
        positionSeconds = lowHealthHeartbeatSource.timeSamples / (float)clip.frequency;
        durationSeconds = clip.length;
        return true;
    }

    public void SetLowHealthHeartbeat(bool playing)
    {
        if (!lowHealthHeartbeatSource) return;
        if (playing)
        {
            if (!lowHealthHeartbeatRequested) nextLowHealthHeartbeatTime = Time.unscaledTime;
            lowHealthHeartbeatRequested = true;
            if (lowHealthHeartbeatFade != null)
            {
                StopCoroutine(lowHealthHeartbeatFade);
                lowHealthHeartbeatFade = null;
            }
            lowHealthHeartbeatSource.volume = Mathf.Clamp01(lowHealthHeartbeatVolume);
            if (Time.unscaledTime >= nextLowHealthHeartbeatTime)
            {
                lowHealthHeartbeatSource.Stop();
                lowHealthHeartbeatSource.pitch = Random.Range(
                    1f - LowHealthHeartbeatPitchSpread, 1f + LowHealthHeartbeatPitchSpread);
                lowHealthHeartbeatSource.Play();
                float interval = StatsManager.Instance
                    ? Mathf.Clamp(StatsManager.Instance.heartbeatFlashIntervalSeconds, .1f, 5f)
                    : 1.2f;
                nextLowHealthHeartbeatTime = Time.unscaledTime + interval;
            }
        }
        else if (lowHealthHeartbeatRequested)
        {
            lowHealthHeartbeatRequested = false;
            if (lowHealthHeartbeatFade != null) StopCoroutine(lowHealthHeartbeatFade);
            lowHealthHeartbeatFade = StartCoroutine(FadeOutLowHealthHeartbeat());
        }
    }

    System.Collections.IEnumerator FadeOutLowHealthHeartbeat()
    {
        float startVolume = lowHealthHeartbeatSource.volume;
        const float fadeDuration = 1f;
        float elapsed = 0f;
        while (elapsed < fadeDuration && lowHealthHeartbeatSource && !lowHealthHeartbeatRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            lowHealthHeartbeatSource.volume = startVolume * (1f - Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        if (lowHealthHeartbeatSource && !lowHealthHeartbeatRequested)
        {
            lowHealthHeartbeatSource.Stop();
            lowHealthHeartbeatSource.volume = Mathf.Clamp01(lowHealthHeartbeatVolume);
        }
        lowHealthHeartbeatFade = null;
    }

    AudioSource GetFreeSource()
    {
        foreach (var sr in pool) if (!sr.isPlaying) return sr;
        if (pool.Count < maxPoolSize) { ExtendPool(); return pool[^1]; }

        AudioSource oldest = null;
        float t = float.MinValue;
        foreach (var sr in pool)
        {
            bool crafting = false;
            foreach (var active in activeCraftingSounds)
                if (active.source == sr && sr.isPlaying && sr.clip == active.clip) { crafting = true; break; }
            if (!crafting && sr.time > t) { oldest = sr; t = sr.time; }
        }
        if (!oldest) oldest = pool[0];
        oldest.Stop(); return oldest;
    }

    bool CanPlayCraftingSound()
    {
        for (int i = activeCraftingSounds.Count - 1; i >= 0; i--)
        {
            var active = activeCraftingSounds[i];
            if (!active.source || !active.source.isPlaying || active.source.clip != active.clip)
                activeCraftingSounds.RemoveAt(i);
        }

        if (activeCraftingSounds.Count >= 2) return false;
        if (activeCraftingSounds.Count == 0) return true;

        var current = activeCraftingSounds[activeCraftingSounds.Count - 1].source;
        return current.clip && current.clip.length > 0f && current.time >= current.clip.length * .6f;
    }

    public bool HasSound(SoundType type)
    {
        if (!soundLookup.TryGetValue(type, out var sound)) return false;
        if (sound.variants != null && sound.variants.Length > 0)
        {
            foreach (var variant in sound.variants) if (variant) return true;
            return false;
        }
        return sound.clip;
    }

    public int GetSoundClipCount(SoundType type)
    {
        if (!soundLookup.TryGetValue(type, out var sound)) return 0;
        return sound.variants != null && sound.variants.Length > 0 ? sound.variants.Length : 1;
    }

    public AudioClip GetSoundClip(SoundType type, int clipIndex)
    {
        if (!soundLookup.TryGetValue(type, out var sound)) return null;
        if (sound.variants != null && sound.variants.Length > 0)
            return clipIndex >= 0 && clipIndex < sound.variants.Length ? sound.variants[clipIndex] : null;
        return clipIndex == 0 ? sound.clip : null;
    }

    public LayerMiningClipTuning GetLayerMiningClipTuning(int layerIndex, bool breaking,
        SoundType type, int clipIndex)
    {
        var settings = LayerMiningSettings;
        if (settings) return settings.GetOrCreate(layerIndex, breaking, type, clipIndex);

        string key = $"{layerIndex}:{(breaking ? 1 : 0)}:{(int)type}:{clipIndex}";
        if (!layerMiningTuning.TryGetValue(key, out var tuning))
        {
            tuning = new LayerMiningClipTuning();
            layerMiningTuning.Add(key, tuning);
        }
        return tuning;
    }

    public void SetLayerMiningClipTuning(int layerIndex, bool breaking, SoundType type, int clipIndex,
        float volume, float pitch, float pitchSpread)
    {
        var tuning = GetLayerMiningClipTuning(layerIndex, breaking, type, clipIndex);
        tuning.volume = Mathf.Clamp01(volume);
        tuning.pitch = Mathf.Clamp(pitch, .5f, 2f);
        tuning.pitchSpread = Mathf.Clamp(pitchSpread, 0f, .5f);

#if UNITY_EDITOR
        var settings = LayerMiningSettings;
        if (settings)
        {
            UnityEditor.EditorUtility.SetDirty(settings);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(settings);
        }
#endif
    }

    public void PlayLayerMiningSound(int layerIndex, SoundType type, bool breaking)
    {
        if (!soundLookup.TryGetValue(type, out var sound)) return;
        int clipCount = GetSoundClipCount(type);
        if (clipCount <= 0) return;

        int clipIndex = clipCount == 1 ? 0 : Random.Range(0, clipCount);
        AudioClip clip = GetSoundClip(type, clipIndex);
        if (!clip) return;

        var tuning = GetLayerMiningClipTuning(layerIndex, breaking, type, clipIndex);
        var source = GetFreeSource();
        if (!source) return;
        ambienceSources.Remove(source);
        float pitch = Mathf.Max(.01f, sound.pitch) * Mathf.Clamp(tuning.pitch, .5f, 2f);
        float spread = Mathf.Clamp01(tuning.pitchSpread);
        source.pitch = Mathf.Clamp(pitch * Random.Range(1f - spread, 1f + spread), .1f, 3f);
        source.clip = clip;
        source.volume = sound.volume * Mathf.Clamp01(tuning.volume) *
            (IsDigSound(type) ? digSoundVolume : 1f);
        source.panStereo = 0f;
        source.Play();
    }

    public AudioSource TryPlayCraftingSound(bool dispersion = false)
    {
        if (!CanPlayCraftingSound() || !soundLookup.TryGetValue(SoundType.ItemInBag, out var sound)) return null;
        var clip = sound.variants != null && sound.variants.Length > 0
            ? sound.variants[ambienceRandom.Next(sound.variants.Length)] : sound.clip;
        if (!clip) return null;
        var source = GetFreeSource();
        if (!source) return null;
        ambienceSources.Remove(source);
        source.pitch = dispersion
            ? sound.pitch + Random.Range(-dispersionAmount, dispersionAmount)
            : sound.pitch;
        source.clip = clip;
        source.volume = sound.volume;
        source.panStereo = 0f;
        source.Play();
        activeCraftingSounds.Add((source, clip));
        return source;
    }

    public void Play(SoundType type, bool dispersion = false)
    {
        if (type == SoundType.ItemInBag)
        {
            TryPlayCraftingSound(dispersion);
            return;
        }
        if (soundLookup.TryGetValue(type, out Sound s))
        {
            var clip = s.variants != null && s.variants.Length > 0
                ? s.variants[ambienceRandom.Next(s.variants.Length)] : s.clip;
            if (!clip) return;
            var sr = GetFreeSource();
            if (!sr) return;
            ambienceSources.Remove(sr);
            if (dispersion)
                sr.pitch = s.pitch + Random.Range(-dispersionAmount, dispersionAmount);
            else sr.pitch = s.pitch;

            sr.clip = clip;
            sr.volume = s.volume * (IsDigSound(type) ? digSoundVolume : 1f);
            sr.panStereo = 0f;
            sr.Play();
            return;
        }
        else Debug.LogWarning($"Sound '{type}' not found in AudioManager!");
    }

    public void PlayClipWithOffset(AudioClip clip, float volume, float pan, float timeOffsetSeconds)
    {
        if (!clip || volume <= 0f) return;
        if (timeOffsetSeconds > 0f)
            StartCoroutine(PlayClipAfterDelay(clip, volume, pan, timeOffsetSeconds));
        else
            PlayClip(clip, volume, pan, startTimeSeconds: -timeOffsetSeconds);
    }

    System.Collections.IEnumerator PlayClipAfterDelay(AudioClip clip, float volume, float pan, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        PlayClip(clip, volume, pan);
    }

    public void PlayClip(AudioClip clip, float volume, float pan, float pitch = 1f, bool ambience = false,
        AmbienceType ambienceType = AmbienceType.Surface, float startTimeSeconds = 0f)
    {
        if (!clip || volume <= 0f) return;
        var source = GetFreeSource();
        if (!source) return;
        source.clip = clip;
        ambienceSources.Remove(source);
        if (startTimeSeconds > 0f)
        {
            int startSample = Mathf.RoundToInt(startTimeSeconds * clip.frequency);
            if (startSample >= clip.samples) return;
            source.timeSamples = Mathf.Clamp(startSample, 0, clip.samples - 1);
        }
        if (ambience) ambienceSources[source] = (Mathf.Clamp01(volume), ambienceType);
        source.volume = Mathf.Clamp01(volume) * (ambience ? GetAmbienceVolume(ambienceType) : 1f);
        source.panStereo = Mathf.Clamp(pan, -1f, 1f);
        source.pitch = Mathf.Clamp(pitch, .5f, 2f);
        source.Play();
    }

    void LateUpdate()
    {
        foreach (var entry in ambienceSources)
            if (entry.Key && entry.Key.isPlaying)
                entry.Key.volume = entry.Value.volume * GetAmbienceVolume(entry.Value.type);
    }

    void ExtendPool()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        pool.Add(s);
    }

    static bool IsDigSound(SoundType type)
    {
        return type is SoundType.DigSoft or SoundType.DigMedium or SoundType.DigHard or
            SoundType.DigOre or SoundType.BreakRock or SoundType.BreakOre or
            SoundType.DigDirt or SoundType.DigTransitionStone or SoundType.DigStone or
            SoundType.DirtHit or SoundType.DigDeepStone or SoundType.StoneBreak or SoundType.ClayBreak;
    }
}
