using UnityEngine;

[DisallowMultipleComponent]
public sealed class SecondLayerAmbience : MonoBehaviour
{
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = .5f;
    [SerializeField] AudioClip windClip;
    [SerializeField, Range(0f, 1f)] float windVolume = .3f;
    [SerializeField, Min(0f)] float windMinDuration = 20f;
    [SerializeField, Min(0f)] float windMaxDuration = 50f;
    [SerializeField, Min(0f)] float windMinPause = 30f;
    [SerializeField, Min(0f)] float windMaxPause = 180f;
    [SerializeField, Min(.01f)] float windFadeSeconds = 1.5f;
    [SerializeField] AudioClip tribalSongClip;
    [SerializeField, Range(0f, 1f)] float tribalSongVolume = .35f;
    [SerializeField, Min(.01f)] float tribalSongLayer2MeanMinutes = 20f;
    [SerializeField, Min(.01f)] float tribalSongLayer3MeanMinutes = 60f;
    [SerializeField] AudioClip[] ghostWhisperClips;
    [SerializeField, Range(0f, 1f)] float ghostWhisperVolume = .3f;
    [SerializeField, Min(.01f)] float ghostWhisperMeanMinutes = 10f;
    [SerializeField, Range(0f, 1f)] float ghostWhisperDarknessThreshold = .2f;
    [SerializeField, Min(1)] int transitionDepth = 20;
    [SerializeField, Min(.01f)] float crossfadeSeconds = 5f;

    readonly AudioSource[] sources = new AudioSource[2];
    AudioSource windSource;
    AudioSource tribalSongSource;
    AudioSource ghostWhisperSource;
    int current;
    bool running, transitioning;
    double nextStart, transitionStart;
    MapGenerator map;
    readonly System.Random windRandom = new System.Random();
    bool windActive, windScheduled;
    double nextWindStateChange;
    float currentWindGain;
    int tribalSongScheduledLayer = -1;
    bool tribalSongPlaying;
    double nextTribalSongTime, tribalSongStartTime;
    MapLighting mapLighting;
    bool ghostWhisperScheduled, ghostWhisperPlaying;
    double nextGhostWhisperTime;

    float Crossfade => clip ? Mathf.Min(crossfadeSeconds, clip.length * .25f) : .01f;

    void Awake()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = gameObject.AddComponent<AudioSource>();
            sources[i].playOnAwake = false;
            sources[i].loop = false;
            sources[i].spatialBlend = 0f;
            sources[i].volume = 0f;
            sources[i].clip = clip;
        }
        windSource = gameObject.AddComponent<AudioSource>();
        windSource.playOnAwake = false;
        windSource.loop = false;
        windSource.spatialBlend = 0f;
        windSource.volume = 0f;
        windSource.clip = windClip;
        tribalSongSource = gameObject.AddComponent<AudioSource>();
        tribalSongSource.playOnAwake = false;
        tribalSongSource.loop = false;
        tribalSongSource.spatialBlend = 0f;
        tribalSongSource.volume = 0f;
        tribalSongSource.clip = tribalSongClip;
        ghostWhisperSource = gameObject.AddComponent<AudioSource>();
        ghostWhisperSource.playOnAwake = false;
        ghostWhisperSource.loop = false;
        ghostWhisperSource.spatialBlend = 0f;
        ghostWhisperSource.volume = 0f;
    }

    void Update()
    {
        float gain = clip ? GetLayerGain() : 0f;
        UpdateWind(gain);
        UpdateTribalSong();
        UpdateGhostWhispers();
        if (gain <= 0f) { StopPlayback(); return; }

        double now = AudioSettings.dspTime;
        if (!running)
        {
            current = 0;
            sources[current].Play();
            nextStart = now + clip.length - Crossfade;
            sources[1].PlayScheduled(nextStart);
            running = true;
        }

        if (!transitioning && now >= nextStart)
        {
            transitioning = true;
            transitionStart = nextStart;
        }
        float blend = transitioning ? Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((float)(now - transitionStart) / Mathf.Max(.01f, Crossfade))) : 0f;
        float ambienceGain = volume * gain * AudioManager.GetAmbienceVolume(AmbienceType.Cave);
        sources[current].volume = ambienceGain * (1f - blend);
        sources[1 - current].volume = ambienceGain * blend;
        if (transitioning && blend >= 1f)
        {
            sources[current].Stop();
            current = 1 - current;
            transitioning = false;
            nextStart = transitionStart + clip.length - Crossfade;
            sources[1 - current].PlayScheduled(nextStart);
        }
    }

    float GetLayerGain()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain || map.layers == null || map.layers.Length < 2) return 0f;

        int depth = Mathf.Max(0, -map.Terrain.WorldToCell(transform.position).y);
        int firstCave = FirstCaveLayer;
        if (map.layers.Length <= firstCave) return 0f;
        int start = map.layers[firstCave].startDepth;
        int end = map.layers.Length > firstCave + 1 ? map.layers[firstCave + 1].startDepth : int.MaxValue;
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            Mathf.Max(0, start - transitionDepth), start, depth));
        float fadeOut = end == int.MaxValue ? 1f : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(
            end - transitionDepth, end, depth));
        return fadeIn * fadeOut;
    }

    int FirstCaveLayer => map && map.layers != null && map.layers.Length > 0 &&
        map.layers[0] != null && map.layers[0].stone && map.layers[0].stone.id == BlockType.Dirt ? 2 : 1;

    int GetCurrentLayerIndex()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain || map.layers == null || map.layers.Length == 0) return -1;

        int depth = Mathf.Max(0, -map.Terrain.WorldToCell(transform.position).y);
        int layer = 0;
        for (int i = 1; i < map.layers.Length; i++)
        {
            if (depth < map.layers[i].startDepth) break;
            layer = i;
        }
        return layer;
    }

    void UpdateTribalSong()
    {
        int layer = GetCurrentLayerIndex();
        int firstCave = FirstCaveLayer;
        if (!tribalSongClip || layer < firstCave || layer >= firstCave + 2)
        {
            tribalSongScheduledLayer = -1;
            tribalSongPlaying = false;
            if (tribalSongSource) { tribalSongSource.Stop(); tribalSongSource.volume = 0f; }
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        if (tribalSongPlaying && tribalSongSource.isPlaying)
        {
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(now - tribalSongStartTime) / 1.5f));
            tribalSongSource.volume = tribalSongVolume * fadeIn * AudioManager.GetAmbienceVolume(AmbienceType.Cave);
            return;
        }
        if (tribalSongPlaying)
        {
            tribalSongPlaying = false;
            tribalSongScheduledLayer = -1;
        }
        if (tribalSongScheduledLayer != layer)
        {
            tribalSongScheduledLayer = layer;
            nextTribalSongTime = now + NextTribalSongDelay(layer);
        }
        if (now < nextTribalSongTime) return;

        tribalSongSource.time = 0f;
        tribalSongSource.volume = 0f;
        tribalSongSource.Play();
        tribalSongPlaying = true;
        tribalSongStartTime = now;
    }

    double NextTribalSongDelay(int layer)
    {
        float meanMinutes = layer == FirstCaveLayer ? tribalSongLayer2MeanMinutes : tribalSongLayer3MeanMinutes;
        return -System.Math.Log(1d - windRandom.NextDouble()) * meanMinutes * 60d;
    }

    void UpdateGhostWhispers()
    {
        if (!CanPlayGhostWhispers())
        {
            ghostWhisperScheduled = false;
            ghostWhisperPlaying = false;
            if (ghostWhisperSource) { ghostWhisperSource.Stop(); ghostWhisperSource.volume = 0f; }
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        if (ghostWhisperPlaying && ghostWhisperSource.isPlaying)
        {
            ghostWhisperSource.volume = ghostWhisperVolume * AudioManager.GetAmbienceVolume(AmbienceType.Cave);
            return;
        }
        if (ghostWhisperPlaying)
        {
            ghostWhisperPlaying = false;
            ghostWhisperScheduled = false;
        }
        if (!ghostWhisperScheduled)
        {
            ghostWhisperScheduled = true;
            nextGhostWhisperTime = now + NextGhostWhisperDelay();
        }
        if (now < nextGhostWhisperTime) return;

        var clip = ghostWhisperClips[windRandom.Next(ghostWhisperClips.Length)];
        if (!clip) return;
        ghostWhisperSource.clip = clip;
        ghostWhisperSource.volume = ghostWhisperVolume * AudioManager.GetAmbienceVolume(AmbienceType.Cave);
        ghostWhisperSource.Play();
        ghostWhisperPlaying = true;
    }

    bool CanPlayGhostWhispers()
    {
        if (ghostWhisperClips == null || ghostWhisperClips.Length == 0) return false;
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain) return false;

        Vector3Int cell = map.Terrain.WorldToCell(transform.position);
        if (-cell.y <= 100) return false;
        if (!mapLighting) mapLighting = map.GetComponent<MapLighting>();
        return mapLighting && mapLighting.lightingEnabled &&
            mapLighting.GetBrightness(cell) <= ghostWhisperDarknessThreshold;
    }

    double NextGhostWhisperDelay()
    {
        return -System.Math.Log(1d - windRandom.NextDouble()) * ghostWhisperMeanMinutes * 60d;
    }

    void UpdateWind(float layerGain)
    {
        if (!windClip || layerGain <= 0f)
        {
            windScheduled = false;
            windActive = false;
            currentWindGain = 0f;
            if (windSource) { windSource.Pause(); windSource.volume = 0f; }
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        if (!windScheduled)
        {
            windScheduled = true;
            windActive = false;
            nextWindStateChange = now + RandomRange(windMinPause, windMaxPause);
        }
        else if (now >= nextWindStateChange)
        {
            windActive = !windActive;
            nextWindStateChange = now + RandomRange(
                windActive ? windMinDuration : windMinPause,
                windActive ? windMaxDuration : windMaxPause);
        }

        float target = windActive ? 1f : 0f;
        currentWindGain = Mathf.MoveTowards(currentWindGain, target,
            Time.unscaledDeltaTime / Mathf.Max(.01f, windFadeSeconds));
        if (windActive && !windSource.isPlaying)
        {
            if (windSource.time >= windClip.length - .02f) windSource.time = 0f;
            windSource.UnPause();
            if (!windSource.isPlaying) windSource.Play();
        }
        else if (!windActive && currentWindGain <= 0f && windSource.isPlaying)
            windSource.Pause();

        windSource.volume = windVolume * currentWindGain * layerGain *
            AudioManager.GetAmbienceVolume(AmbienceType.Cave);
    }

    float RandomRange(float min, float max)
    {
        return Mathf.Lerp(Mathf.Min(min, max), Mathf.Max(min, max), (float)windRandom.NextDouble());
    }

    void StopPlayback(bool stopTribalSong = false, bool stopGhostWhispers = false)
    {
        foreach (var source in sources)
        {
            if (!source) continue;
            source.Stop();
            source.volume = 0f;
        }
        running = transitioning = false;
        if (windSource) { windSource.Pause(); windSource.volume = 0f; }
        if (stopTribalSong && tribalSongSource) { tribalSongSource.Stop(); tribalSongSource.volume = 0f; }
        if (stopGhostWhispers && ghostWhisperSource) { ghostWhisperSource.Stop(); ghostWhisperSource.volume = 0f; }
    }

    void OnDisable() { StopPlayback(true, true); }

    void OnDestroy()
    {
        foreach (var source in sources)
            if (source) Destroy(source);
        if (windSource) Destroy(windSource);
        if (tribalSongSource) Destroy(tribalSongSource);
        if (ghostWhisperSource) Destroy(ghostWhisperSource);
    }
}
