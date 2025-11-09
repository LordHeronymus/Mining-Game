using System.Collections.Generic;
using UnityEngine;

public enum SoundType
{
    DigSoft = 2,
    DigMedium = 0,
    DigHard = 3,
    DigOre = 4,
    BreakRock = 1,
    BreakOre = 5,
}

[System.Serializable]
public class Sound
{
    public SoundType type;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 3f)] public float pitch = 1f;
}

public class AudioManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] int initialPoolSize = 8;
    [SerializeField] int maxPoolSize = 32;
    [SerializeField] Sound[] sounds;

    [HideInInspector] public static AudioManager Instance;

    List<AudioSource> pool = new();
    Dictionary<SoundType, Sound> soundLookup = new Dictionary<SoundType, Sound>();

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

    public void Play(SoundType type)
    {
        var sr = GetFreeSource();
        if (!sr) return;

        if (soundLookup.TryGetValue(type, out Sound s))
        {
            sr.clip = s.clip;
            sr.volume = s.volume;
            sr.pitch = s.pitch;
            sr.Play();
            return;
        }
        else Debug.LogWarning($"Sound '{type}' not found in AudioManager!");
    }

    void ExtendPool()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        pool.Add(s);
    }
}
