using UnityEngine;

[DisallowMultipleComponent]
public sealed class FirstLayerAmbience : MonoBehaviour
{
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = .5f;
    [SerializeField] AudioClip[] detailClips;
    [SerializeField] float[] detailVolumeMultipliers;
    [SerializeField, Min(0f)] float detailsPerMinute = 3f;
    [SerializeField, Min(0)] int fadeInStartDepth = 12;
    [SerializeField, Min(0)] int fadeInEndDepth = 20;
    [SerializeField, Min(1)] int fadeOutDepth = 20;
    [SerializeField] float lowerBoundaryY = -150f;
    [SerializeField, Min(.01f)] float crossfadeSeconds = 5f;

    readonly AudioSource[] sources = new AudioSource[2];
    int current;
    bool running, transitioning;
    double nextStart, transitionStart;
    float gain;
    MapGenerator map;
    readonly System.Random detailRandom = new System.Random();
    double nextDetailTime;
    bool detailsActive;
    int lastDetailIndex = -1;
    int consecutiveDetailCount;

    float Crossfade => clip ? Mathf.Min(crossfadeSeconds, clip.length * .25f) : .01f;

    public int DetailClipCount => detailClips?.Length ?? 0;

    public AudioClip GetDetailClip(int index) =>
        index >= 0 && index < DetailClipCount ? detailClips[index] : null;

    public float GetDetailVolumeMultiplier(int index) =>
        index >= 0 && index < detailVolumeMultipliers?.Length ? detailVolumeMultipliers[index] : 1f;

    public void SetDetailVolumeMultiplier(int index, float value)
    {
        if (index < 0 || index >= DetailClipCount) return;
        if (detailVolumeMultipliers == null) detailVolumeMultipliers = new float[DetailClipCount];
        if (detailVolumeMultipliers.Length < DetailClipCount)
        {
            int previousLength = detailVolumeMultipliers.Length;
            System.Array.Resize(ref detailVolumeMultipliers, DetailClipCount);
            for (int i = previousLength; i < detailVolumeMultipliers.Length; i++) detailVolumeMultipliers[i] = 1f;
        }
        detailVolumeMultipliers[index] = Mathf.Clamp01(value);
    }

    public void PlayDetailPreview(int index)
    {
        var detail = GetDetailClip(index);
        if (detail) AudioManager.Instance?.PlayClip(detail, volume * GetDetailVolumeMultiplier(index), 0f,
            ambience: true, ambienceType: AmbienceType.Underground);
    }

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
    }

    void Update()
    {
        float target = clip ? GetLayerGain() : 0f;
        bool inLayer = target > 0f;
        gain = target;
        UpdateDetails(inLayer);
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
        float ambienceGain = volume * gain * AudioManager.GetAmbienceVolume(AmbienceType.Underground);
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

    float GetLowerBoundaryY()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        int boundary = NextCaveLayer;
        if (!map || map.layers == null || map.layers.Length <= boundary || !map.Terrain) return lowerBoundaryY;
        return -map.layers[boundary].startDepth * map.Terrain.layoutGrid.cellSize.y;
    }

    int NextCaveLayer => map && map.layers != null && map.layers.Length > 0 &&
        map.layers[0] != null && map.layers[0].stone && map.layers[0].stone.id == BlockType.Dirt ? 2 : 1;

    float GetLayerGain()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain)
            return transform.position.y > lowerBoundaryY ? 1f : 0f;

        int depth = Mathf.Max(0, -map.Terrain.WorldToCell(transform.position).y);
        int boundary = NextCaveLayer;
        int layerTwoDepth = map.layers != null && map.layers.Length > boundary ?
            map.layers[boundary].startDepth : int.MaxValue;
        if (depth >= layerTwoDepth) return 0f;
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            fadeInStartDepth, Mathf.Max(fadeInStartDepth + 1, fadeInEndDepth), depth));
        float fadeOut = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(
            layerTwoDepth - fadeOutDepth, layerTwoDepth, depth));
        return fadeIn * fadeOut;
    }

    void UpdateDetails(bool inLayer)
    {
        if (!inLayer || detailsPerMinute <= 0f || detailClips == null || detailClips.Length == 0)
        {
            detailsActive = false;
            return;
        }

        double now = Time.unscaledTimeAsDouble;
        if (!detailsActive)
        {
            detailsActive = true;
            nextDetailTime = now + NextDetailDelay();
            return;
        }
        if (now < nextDetailTime) return;

        int index = GetNextDetailIndex();
        var detail = detailClips[index];
        float detailVolume = index < detailVolumeMultipliers?.Length
            ? Mathf.Clamp01(detailVolumeMultipliers[index]) : 1f;
        if (detail) AudioManager.Instance?.PlayClip(detail, volume * detailVolume, 0f, ambience: true,
            ambienceType: AmbienceType.Underground);
        nextDetailTime = now + NextDetailDelay();
    }

    double NextDetailDelay()
    {
        double random = 1d - detailRandom.NextDouble();
        return Mathf.Max(3f, (float)(-System.Math.Log(random) * 60d / detailsPerMinute));
    }

    int GetNextDetailIndex()
    {
        if (detailClips.Length < 2) return 0;

        int index;
        do index = detailRandom.Next(detailClips.Length);
        while (index == lastDetailIndex && consecutiveDetailCount >= 2);

        if (index == lastDetailIndex) consecutiveDetailCount++;
        else
        {
            lastDetailIndex = index;
            consecutiveDetailCount = 1;
        }
        return index;
    }

    void StopPlayback()
    {
        foreach (var source in sources)
        {
            if (!source) continue;
            source.Stop();
            source.volume = 0f;
        }
        running = transitioning = false;
    }

    void OnDisable() { StopPlayback(); gain = 0f; }

    void OnDestroy()
    {
        foreach (var source in sources)
            if (source) Destroy(source);
    }
}
