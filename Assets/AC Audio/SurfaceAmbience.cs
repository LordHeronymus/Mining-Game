using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceAmbience : MonoBehaviour
{
    public const string NightClipResource = "Audio/NightAmbience";

    [SerializeField] AudioClip clip;
    // Night bed without birds (crickets); falls back to the Resources clip so the scene needs no new reference.
    [SerializeField] AudioClip nightClip;
    [SerializeField, Range(0f, 1f)] float volume = .5f;
    [SerializeField, Min(0)] int fadeStartDepth = 12;
    [SerializeField, Min(0)] int fadeEndDepth = 20;
    [SerializeField, Min(.01f)] float crossfadeSeconds = 3f;
    [Min(0f)] public float nightFadeSeconds = 8f;

    Loop day, night;
    float nightMix;
    bool nightMixStarted;
    float nextSkySearch;
    SkyController sky;
    MapGenerator map;

    public float NightMix => nightMix;

    void Awake()
    {
        GpsSettings.ApplyComponent(this);
        if (!nightClip) nightClip = Resources.Load<AudioClip>(NightClipResource);
        day = new Loop(gameObject, clip, crossfadeSeconds);
        night = new Loop(gameObject, nightClip, crossfadeSeconds);
    }

    void OnEnable() => GamePause.Changed += PauseChanged;

    void PauseChanged(bool paused) { day.PauseChanged(paused); night.PauseChanged(paused); }

    void Update()
    {
        if (GamePause.IsPaused) return;
        float gain = clip ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            fadeStartDepth, Mathf.Max(fadeStartDepth + 1, fadeEndDepth), GetDepth())) : 0f;
        float target = night.clip ? NightTarget() : 0f;
        if (!nightMixStarted) { nightMix = target; nightMixStarted = true; }
        else nightMix = nightFadeSeconds > 0f ? Mathf.MoveTowards(nightMix, target, Time.deltaTime / nightFadeSeconds) : target;
        float ambienceGain = volume * gain * UltroniumAltarChamber.StandardLayerAmbienceGain *
            AudioManager.GetAmbienceVolume(AmbienceType.Surface);
        // Equal power: both beds are uncorrelated noise, so the sum keeps its loudness through the fade.
        day.Update(gain > 0f && nightMix < 1f, ambienceGain * Mathf.Cos(nightMix * Mathf.PI * .5f));
        night.Update(gain > 0f && nightMix > 0f, ambienceGain * Mathf.Sin(nightMix * Mathf.PI * .5f));
    }

    float NightTarget()
    {
        if (!sky && Time.unscaledTime >= nextSkySearch)
        {
            sky = FindFirstObjectByType<SkyController>();
            nextSkySearch = Time.unscaledTime + 1f;
        }
        return sky && sky.isActiveAndEnabled ? sky.NightBlend : 0f;
    }

    int GetDepth()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        return map && map.Terrain ? Mathf.Max(0, -map.Terrain.WorldToCell(transform.position).y) : 0;
    }

    void OnDisable()
    {
        GamePause.Changed -= PauseChanged;
        day?.Stop(); night?.Stop();
        nightMixStarted = false;
    }

    void OnDestroy() { day?.Destroy(); night?.Destroy(); }

    // Two sources alternate on the DSP clock and crossfade at the loop point.
    sealed class Loop
    {
        public readonly AudioClip clip;
        public readonly AudioSource[] sources = new AudioSource[2];
        public int current;
        public bool running, transitioning;
        double nextStart, transitionStart;
        readonly float crossfadeSeconds;
        float Crossfade => clip ? Mathf.Min(crossfadeSeconds, clip.length * .25f) : .01f;
        float SegmentDuration(AudioSource source) => clip.length / Mathf.Max(.1f, Mathf.Abs(source.pitch));

        public Loop(GameObject host, AudioClip clip, float crossfadeSeconds)
        {
            this.clip = clip;
            this.crossfadeSeconds = crossfadeSeconds;
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = host.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].loop = false;
                sources[i].spatialBlend = 0f;
                sources[i].volume = 0f;
                sources[i].clip = clip;
            }
        }

        // The DSP clock runs on while the listener is paused: hold the queued loop segment and move the schedule.
        public void PauseChanged(bool paused)
        {
            if (!running) return;
            if (paused)
            {
                if (transitioning) return;
                if (AudioSettings.dspTime >= nextStart) { transitioning = true; transitionStart = nextStart; }
                else sources[1 - current].Stop();
                return;
            }
            nextStart += GamePause.PausedDspSeconds; transitionStart += GamePause.PausedDspSeconds;
            if (!transitioning) sources[1 - current].PlayScheduled(nextStart);
        }

        public void Update(bool audible, float ambienceGain)
        {
            if (!clip || !audible) { Stop(); return; }
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

        public void Stop()
        {
            foreach (var source in sources)
            {
                if (!source) continue;
                source.Stop();
                source.volume = 0f;
            }
            running = transitioning = false;
        }

        public void Destroy()
        {
            foreach (var source in sources)
                if (source) Object.Destroy(source);
        }
    }
}
