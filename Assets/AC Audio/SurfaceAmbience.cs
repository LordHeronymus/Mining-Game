using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceAmbience : MonoBehaviour
{
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = .5f;
    [SerializeField, Min(0)] int fadeStartDepth = 12;
    [SerializeField, Min(0)] int fadeEndDepth = 20;
    [SerializeField, Min(.01f)] float crossfadeSeconds = 3f;

    readonly AudioSource[] sources = new AudioSource[2];
    int current;
    bool running, transitioning;
    double nextStart, transitionStart;
    float gain;
    MapGenerator map;
    float Crossfade => clip ? Mathf.Min(crossfadeSeconds, clip.length * .25f) : .01f;
    float SegmentDuration(AudioSource source) => clip.length / Mathf.Max(.1f, Mathf.Abs(source.pitch));

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
        float target = clip ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            fadeStartDepth, Mathf.Max(fadeStartDepth + 1, fadeEndDepth), GetDepth())) : 0f;
        gain = target;
        if (gain <= 0f) { StopPlayback(); return; }
        double now = AudioSettings.dspTime;
        if (!running)
        {
            current = 0;
            sources[current].pitch = AudioManager.TunedPitch(clip, 1f, sources[current]);
            sources[current].Play();
            nextStart = now + SegmentDuration(sources[current]) - Crossfade;
            sources[1].pitch = AudioManager.TunedPitch(clip, 1f, sources[1]);
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
        float ambienceGain = (
            volume * gain * UltroniumAltarChamber.StandardLayerAmbienceGain *
            AudioManager.GetAmbienceVolume(AmbienceType.Surface));
        sources[current].volume = AudioManager.TunedAmbienceVolume(clip, ambienceGain * (1f - blend), sources[current]);
        sources[1 - current].volume = AudioManager.TunedAmbienceVolume(clip, ambienceGain * blend, sources[1 - current]);
        if (transitioning && blend >= 1f)
        {
            sources[current].Stop();
            current = 1 - current;
            transitioning = false;
            nextStart = transitionStart + SegmentDuration(sources[current]) - Crossfade;
            sources[1 - current].pitch = AudioManager.TunedPitch(clip, 1f, sources[1 - current]);
            sources[1 - current].PlayScheduled(nextStart);
        }
    }

    int GetDepth()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        return map && map.Terrain ? Mathf.Max(0, -map.Terrain.WorldToCell(transform.position).y) : 0;
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
