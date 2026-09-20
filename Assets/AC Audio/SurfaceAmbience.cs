using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceAmbience : MonoBehaviour
{
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = .5f;
    [SerializeField] float fadeOutY = -40f;
    [SerializeField, Min(.01f)] float fadeSeconds = 2f;
    [SerializeField, Min(.01f)] float crossfadeSeconds = 3f;

    readonly AudioSource[] sources = new AudioSource[2];
    int current;
    bool running, transitioning;
    double nextStart, transitionStart;
    float gain;
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
    }

    void Update()
    {
        float target = clip && transform.position.y > fadeOutY ? 1f : 0f;
        gain = Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime / Mathf.Max(.01f, fadeSeconds));
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
        float ambienceGain = volume * gain * AudioManager.GetAmbienceVolume(AmbienceType.Surface);
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
