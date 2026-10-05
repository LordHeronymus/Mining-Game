using UnityEngine;
using System.Collections.Generic;

public sealed partial class LoadingAudio : MonoBehaviour
{
    const float HomeFadeOutSeconds = 2f;
    const float YogaStartDelaySeconds = HomeFadeOutSeconds * .5f;
    const float YogaFadeInSeconds = 1.25f;
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
    [SerializeField, HideInInspector] AudioSource yoga;
    AudioSource loadingPickaxe;
    [SerializeField, HideInInspector] AudioSource[] homeSources = new AudioSource[2];
    int homeCurrent;
    bool homeRunning;
    float homeStartedAt, homeFadeOutAt = -1f, homeFadeOutGain, yogaFadeInAt = -1f;
    double homeNextStart;
    float HomeLoopFade => homeSources[homeCurrent] && homeSources[homeCurrent].clip
        ? Mathf.Min(3f, homeSources[homeCurrent].clip.length / homeSources[homeCurrent].pitch * .25f) : .01f;
    float HomeGain => !homeRunning ? 0f : homeFadeOutAt >= 0f
        ? homeFadeOutGain * HomeFadeGain(Time.unscaledTime - homeFadeOutAt)
        : Mathf.SmoothStep(0f, 1f, FadeFraction(Time.unscaledTime - homeStartedAt, 1f));
    float completedAt = -1f;
    bool loading;
    bool loadingTrackPrepared;
    AudioClip[] distantClips;
    AudioClip[] waterdropClips;
    [SerializeField, HideInInspector] List<AudioSource> distantSources = new List<AudioSource>();
    readonly Dictionary<AudioSource, float> distantVolumeGains = new Dictionary<AudioSource, float>();
    readonly Dictionary<AudioSource, float> homeVolumeGains = new Dictionary<AudioSource, float>();
    AudioSource homePreview;
    float nextDistantAt = float.PositiveInfinity;
    int lastDistantIndex = -1, distantRepeatCount;

    public static float WorldAmbienceGain => !instance ? 1f : instance.loading ? 0f :
        instance.completedAt < 0f ? 1f : Mathf.SmoothStep(0f, 1f,
            FadeFraction(Time.unscaledTime - instance.completedAt,
                Settings ? Settings.worldFadeInSeconds : 2f));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { instance = null; settings = null; }

    static float FadeFraction(float elapsed, float duration) => duration <= 0f ? 1f :
        Mathf.Clamp01(elapsed / duration);

    static float HomeFadeGain(float elapsed)
    {
        // A quieter tail with zero slope at both ends; halfway is 25% gain.
        float gain = 1f - Mathf.SmoothStep(0f, 1f, FadeFraction(elapsed, HomeFadeOutSeconds));
        return gain * gain;
    }

    static void EnsureInstance()
    {
        if (!instance) instance = FindFirstObjectByType<LoadingAudio>();
        if (!instance)
        {
            var root = new GameObject("LoadingAudio");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<LoadingAudio>();
        }
        if (!instance.yoga)
        {
            instance.yoga = instance.gameObject.AddComponent<AudioSource>();
            instance.yoga.playOnAwake = false;
            instance.yoga.loop = true;
            instance.yoga.spatialBlend = 0f;
            instance.yoga.ignoreListenerPause = true;
            instance.yoga.clip = Resources.Load<AudioClip>("Audio/LoadingYogaAmbience");
        }
    }

    public static void StartHome()
    {
        if (GameAudioLifecycle.IsStopping) return;
        EnsureInstance();
        instance.loading = false;
        instance.completedAt = -1f;
        instance.yogaFadeInAt = -1f;
        instance.yoga.Stop();
        if (instance.loadingPickaxe) instance.loadingPickaxe.Stop();
        instance.StopDistantDetails();
        instance.StopMeadow(); instance.meadowGain = 0;
        var clip = Resources.Load<AudioClip>("Audio/HomescreenAmbience");
        if (!clip) return;
        for (int i = 0; i < 2; i++)
        {
            if (!instance.homeSources[i])
            {
                instance.homeSources[i] = instance.gameObject.AddComponent<AudioSource>();
                instance.homeSources[i].playOnAwake = false;
                instance.homeSources[i].spatialBlend = 0f;
                instance.homeSources[i].ignoreListenerPause = true;
            }
            var source = instance.homeSources[i];
            source.Stop();
            source.clip = clip;
            instance.PrepareHomeLoop(source);
            source.volume = 0f;
        }
        instance.homeCurrent = 0;
        instance.homeStartedAt = Time.unscaledTime;
        instance.homeFadeOutAt = -1f;
        instance.homeRunning = true;
        instance.nextDistantAt = Time.unscaledTime + instance.NextDistantInterval();
        instance.homeSources[0].Play();
        instance.homeNextStart = AudioSettings.dspTime + clip.length / instance.homeSources[0].pitch - instance.HomeLoopFade;
        instance.homeSources[1].PlayScheduled(instance.homeNextStart);
    }

    public static void Begin()
    {
        if (GameAudioLifecycle.IsStopping) return;
        EnsureInstance();
        instance.nextDistantAt = float.PositiveInfinity;
        if (!instance.loading && instance.homeRunning)
        {
            instance.homeFadeOutGain = instance.HomeGain;
            instance.homeFadeOutAt = Time.unscaledTime;
            instance.yogaFadeInAt = Time.unscaledTime + YogaStartDelaySeconds;
        }
        else if (!instance.loading) instance.yogaFadeInAt = -1f;
        instance.loading = true;
        instance.completedAt = -1f;
        instance.ApplyVolume();
    }

    public static void WarmLoadingTrack()
    {
        if (GameAudioLifecycle.IsStopping) return;
        EnsureInstance();
        if (!instance.yoga.clip || instance.yoga.isPlaying || instance.loadingTrackPrepared) return;
        instance.yoga.volume = 0f;
        instance.yoga.pitch = AudioManager.TunedPitch(instance.yoga.clip, 1f, instance.yoga);
        instance.yoga.Play();
        instance.yoga.Pause();
        instance.loadingTrackPrepared = true;
    }

    public static void Complete()
    {
        if (!instance || !instance.loading) return;
        instance.loading = false;
        instance.completedAt = Time.unscaledTime;
    }

    public static void PlayLoadingPickaxe(AudioClip clip, bool warm = false)
    {
        if (!clip || GameAudioLifecycle.IsStopping) return;
        EnsureInstance();
        if (!instance.loadingPickaxe)
        {
            instance.loadingPickaxe = instance.gameObject.AddComponent<AudioSource>();
            instance.loadingPickaxe.playOnAwake = false;
            instance.loadingPickaxe.spatialBlend = 0f;
            instance.loadingPickaxe.ignoreListenerPause = true;
        }
        var source = instance.loadingPickaxe;
        source.volume = instance.LoadingFadeOutGain();
        source.pitch = warm ? 1f : AudioManager.TunedPitch(clip, 1f, source);
        source.PlayOneShot(clip, warm ? 0f : AudioManager.TunedVolume(clip,
            Settings ? Settings.pickaxeVolume : 1f, source));
    }

    void Update()
    {
        if (GameAudioLifecycle.IsStopping) return;
        ApplyVolume();
        UpdateHome();
        UpdateMeadow();
        UpdateDistantDetails();
    }

    void UpdateHome()
    {
        if (!homeRunning) return;
        if (!homeSources[0] || !homeSources[1])
        {
            homeRunning = false;
            if (MainMenuController.IsVisible) StartHome();
            return;
        }
        if (homeFadeOutAt >= 0f && Time.unscaledTime - homeFadeOutAt >= HomeFadeOutSeconds)
        {
            foreach (var source in homeSources) { source.Stop(); source.volume = 0f; }
            homeRunning = false;
            StopDistantDetails();
            return;
        }
        double now = AudioSettings.dspTime;
        float blend = Mathf.Clamp01((float)(now - homeNextStart) / HomeLoopFade);
        float volume = HomeGain;
        homeSources[homeCurrent].volume = AudioManager.TunedMusicVolume(homeSources[homeCurrent].clip,
            volume * HomeSourceVolume(homeSources[homeCurrent]) * Mathf.Cos(blend * Mathf.PI * .5f), homeSources[homeCurrent]);
        homeSources[1 - homeCurrent].volume = AudioManager.TunedMusicVolume(homeSources[1 - homeCurrent].clip,
            volume * HomeSourceVolume(homeSources[1 - homeCurrent]) * Mathf.Sin(blend * Mathf.PI * .5f), homeSources[1 - homeCurrent]);
        if (blend < 1f) return;
        homeSources[homeCurrent].Stop();
        homeCurrent = 1 - homeCurrent;
        homeNextStart += homeSources[homeCurrent].clip.length / homeSources[homeCurrent].pitch - HomeLoopFade;
        var next = homeSources[1 - homeCurrent];
        PrepareHomeLoop(next);
        next.volume = 0f;
        next.PlayScheduled(homeNextStart);
    }

    float LoadingFadeOutGain() => loading ? 1f : completedAt < 0f ? 0f :
            1f - Mathf.SmoothStep(0f, 1f, FadeFraction(Time.unscaledTime - completedAt,
                Settings ? Settings.yogaFadeOutSeconds : 5f));

    float LoadingTrackGain()
    {
        float gain = LoadingFadeOutGain();
        if (yogaFadeInAt >= 0f)
            gain *= Mathf.SmoothStep(0f, 1f,
                FadeFraction(Time.unscaledTime - yogaFadeInAt, YogaFadeInSeconds));
        return gain;
    }

    void ApplyVolume()
    {
        if (loadingPickaxe)
        {
            float pickaxeGain = LoadingFadeOutGain();
            loadingPickaxe.volume = pickaxeGain;
            if (pickaxeGain <= 0f && loadingPickaxe.isPlaying) loadingPickaxe.Stop();
        }
        if (!yoga) return;
        float gain = LoadingTrackGain();
        if(gain>0f && yoga.clip && !yoga.isPlaying && !loadingTrackPrepared) yoga.pitch=AudioManager.TunedPitch(yoga.clip,1f,yoga);
        yoga.volume = AudioManager.TunedMusicVolume(yoga.clip,
            gain * (Settings ? Settings.yogaVolume : 1f), yoga);
        if (gain <= 0f && yoga.isPlaying) { yoga.Stop(); loadingTrackPrepared = false; }
        else if (gain > 0f && yoga.clip && !yoga.isPlaying)
        {
            if (loadingTrackPrepared) { yoga.UnPause(); loadingTrackPrepared = false; }
            else yoga.Play();
        }
    }

    float NextDistantInterval()
    {
        float rate = Settings ? Settings.distantPickaxesPerMinute : 10f;
        if (rate <= 0f) return float.PositiveInfinity;
        float mean = 60f / rate;
        float minimum = Mathf.Min(1.5f, mean * .25f);
        return minimum - Mathf.Log(Mathf.Max(.00001f, 1f - Random.value)) * (mean - minimum);
    }

    void UpdateDistantDetails()
    {
        if (homeLayer <= 1) { StopDistantDetails(); return; }
        foreach (var source in distantSources)
            if (source && source.isPlaying)
            {
                float gain = distantVolumeGains.TryGetValue(source, out float savedGain) ? savedGain : 1f;
                source.volume = AudioManager.TunedAmbienceVolume(source.clip,
                    AudioManager.AmbienceVolume * HomeGain * gain, source);
            }
        if (!homeRunning || homeFadeOutAt >= 0f || loading) return;
        float rate = Settings ? Settings.distantPickaxesPerMinute : 10f;
        if (rate <= 0f) { nextDistantAt = float.PositiveInfinity; return; }
        if (float.IsPositiveInfinity(nextDistantAt)) nextDistantAt = Time.unscaledTime + NextDistantInterval();
        if (Time.unscaledTime < nextDistantAt) return;
        nextDistantAt = Time.unscaledTime + NextDistantInterval();
        if (distantClips == null)
        {
            distantClips = new AudioClip[8];
            for (int i = 0; i < distantClips.Length; i++)
                distantClips[i] = Resources.Load<AudioClip>($"Audio/HomeDistantPickaxe{i + 1:00}");
        }
        int index = Random.Range(0, distantClips.Length);
        if (index == lastDistantIndex && distantRepeatCount >= 2)
            index = (index + Random.Range(1, distantClips.Length)) % distantClips.Length;
        distantRepeatCount = index == lastDistantIndex ? distantRepeatCount + 1 : 1;
        lastDistantIndex = index;
        var clip = distantClips[index];
        if (!clip) return;
        AudioSource available = null;
        foreach (var source in distantSources)
            if (source && !source.isPlaying) { available = source; break; }
        if (!available)
        {
            available = gameObject.AddComponent<AudioSource>();
            available.playOnAwake = false;
            available.spatialBlend = 0f;
            available.ignoreListenerPause = true;
            distantSources.Add(available);
        }
        available.clip = clip;
        available.pitch = AudioManager.TunedPitch(clip, 1f, available);
        float volumeGain = 1f;
        distantVolumeGains[available] = volumeGain;
        available.volume = AudioManager.TunedAmbienceVolume(clip, AudioManager.AmbienceVolume * HomeGain * volumeGain, available);
        available.Play();
    }

    void StopDistantDetails()
    {
        nextDistantAt = float.PositiveInfinity;
        foreach (var source in distantSources)
            if (source) { source.volume = 0f; source.Stop(); }
    }

    public static void PlayHomeWaterdrop()
    {
        if (GameAudioLifecycle.IsStopping || !instance || instance.homeLayer != 3 || !instance.homeRunning ||
            instance.loading || instance.homeFadeOutAt >= 0f || !MainMenuController.IsVisible) return;
        if (instance.waterdropClips == null)
            instance.waterdropClips = new[] { Resources.Load<AudioClip>("Audio/HomeWaterdrop02"),
                Resources.Load<AudioClip>("Audio/HomeWaterdrop03") };
        var clip = instance.waterdropClips[Random.Range(0, instance.waterdropClips.Length)];
        if (!clip) return;
        AudioSource source = null;
        foreach (var candidate in instance.distantSources)
            if (candidate && !candidate.isPlaying) { source = candidate; break; }
        if (!source)
        {
            source = instance.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            instance.distantSources.Add(source);
        }
        float gain = 1f;
        source.clip = clip;
        source.pitch = AudioManager.TunedPitch(clip, 1f, source);
        instance.distantVolumeGains[source] = gain;
        source.volume = AudioManager.TunedAmbienceVolume(clip, AudioManager.AmbienceVolume * instance.HomeGain * gain, source);
        source.Play();
    }

    void OnDisable()
    {
        StopMeadow(); meadowGain = 0;
        if (loadingPickaxe) { loadingPickaxe.volume = 0f; loadingPickaxe.Stop(); }
        if (homePreview) homePreview.Stop();
        StopDistantDetails();
        if (yoga) { yoga.volume = 0f; yoga.Stop(); }
        loadingTrackPrepared = false;
        foreach (var source in homeSources)
            if (source) { source.volume = 0f; source.Stop(); }
    }

    float HomeSourceVolume(AudioSource source) => homeVolumeGains.TryGetValue(source, out float gain) ? gain : 1f;

    void PrepareHomeLoop(AudioSource source)
    {
        source.pitch = AudioManager.TunedPitch(source.clip, 1f, source);
        homeVolumeGains[source] = 1f;
    }

    public static void PreviewHomeClip(HomeAudioClipTuning tuning) => GpsAudioPreview.Play(tuning?.clip);
}
