using System.Collections.Generic;
using UnityEngine;

public enum AmbienceType { Surface, Rain, Thunderstorm, Underground, Cave, Birds, Frogs }
public enum AudioVolumeSetting { Ambience, Surface, Rain, Thunderstorm, Underground, Cave, DigSounds }

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
        }
    }

    [HideInInspector] public static AudioManager Instance;

    List<AudioSource> pool = new();
    readonly Dictionary<AudioSource, (float volume, AmbienceType type)> ambienceSources = new();
    Dictionary<SoundType, Sound> soundLookup = new Dictionary<SoundType, Sound>();
    AudioClip[] frogCroaks;
    AudioClip grassLanding;
    AudioSource craftingSoundSource;
    readonly System.Random ambienceRandom = new System.Random();

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

        foreach (var s in sounds) soundLookup[s.type] = s;
    }

    AudioSource GetFreeSource()
    {
        foreach (var sr in pool) if (!sr.isPlaying) return sr;
        if (pool.Count < maxPoolSize) { ExtendPool(); return pool[^1]; }

        AudioSource oldest = pool[0]; // terminiert älteste
        float t = oldest.time;
        foreach (var sr in pool) if (sr.time > t) { oldest = sr; t = sr.time; }
        oldest.Stop(); return oldest;
    }

    public void Play(SoundType type, bool dispersion = false)
    {
        if (type == SoundType.ItemInBag && craftingSoundSource && craftingSoundSource.isPlaying) return;
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
            if (type == SoundType.ItemInBag) craftingSoundSource = sr;
            return;
        }
        else Debug.LogWarning($"Sound '{type}' not found in AudioManager!");
    }

    public void PlayClip(AudioClip clip, float volume, float pan, float pitch = 1f, bool ambience = false, AmbienceType ambienceType = AmbienceType.Surface)
    {
        if (!clip || volume <= 0f) return;
        var source = GetFreeSource();
        if (!source) return;
        source.clip = clip;
        ambienceSources.Remove(source);
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
            SoundType.DirtHit;
    }
}
