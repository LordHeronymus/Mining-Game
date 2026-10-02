using UnityEngine;

public sealed class LoadingAudio : MonoBehaviour
{
    static LoadingAudio instance;
    static LoadingAudioSettingsAsset settings;
    public static LoadingAudioSettingsAsset Settings
    {
        get
        {
            if (!settings) settings = Resources.Load<LoadingAudioSettingsAsset>("Audio/LoadingAudioSettings");
            return settings;
        }
    }
    AudioSource yoga;
    float completedAt = -1f;
    bool loading;

    public static float WorldAmbienceGain => !instance ? 1f : instance.loading ? 0f :
        instance.completedAt < 0f ? 1f : Mathf.SmoothStep(0f, 1f,
            FadeFraction(Time.unscaledTime - instance.completedAt,
                Settings ? Settings.worldFadeInSeconds : 2f));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { instance = null; settings = null; }

    static float FadeFraction(float elapsed, float duration) => duration <= 0f ? 1f :
        Mathf.Clamp01(elapsed / duration);

    public static void Begin()
    {
        if (!instance)
        {
            var root = new GameObject("LoadingAudio");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<LoadingAudio>();
            instance.yoga = root.AddComponent<AudioSource>();
            instance.yoga.playOnAwake = false;
            instance.yoga.loop = true;
            instance.yoga.spatialBlend = 0f;
            instance.yoga.ignoreListenerPause = true;
            instance.yoga.clip = Resources.Load<AudioClip>("Audio/LoadingYogaAmbience");
        }
        instance.loading = true;
        instance.completedAt = -1f;
        instance.ApplyVolume();
        if (instance.yoga.clip && !instance.yoga.isPlaying) instance.yoga.Play();
    }

    public static void Complete()
    {
        if (!instance || !instance.loading) return;
        instance.loading = false;
        instance.completedAt = Time.unscaledTime;
    }

    void Update() => ApplyVolume();

    void ApplyVolume()
    {
        if (!yoga) return;
        float gain = loading ? 1f : completedAt < 0f ? 0f :
            1f - Mathf.SmoothStep(0f, 1f, FadeFraction(Time.unscaledTime - completedAt,
                Settings ? Settings.yogaFadeOutSeconds : 5f));
        yoga.volume = AudioManager.TunedAmbienceVolume(yoga.clip,
            AudioManager.AmbienceVolume * gain * (Settings ? Settings.yogaVolume : 1f));
        if (gain <= 0f && yoga.isPlaying) yoga.Stop();
    }
}
