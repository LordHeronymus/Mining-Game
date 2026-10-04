using UnityEngine;

public static class HomeClickAudio
{
    static AudioSource source;
    static AudioClip clip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { source = null; clip = null; }

    public static void Play()
    {
        if (!Application.isPlaying || GameAudioLifecycle.IsStopping || !MainMenuController.IsVisible) return;
        if (!clip) clip = Resources.Load<AudioClip>("Audio/HomeClick");
        if (!clip) return;
        if (!source)
        {
            var root = new GameObject("HomeClickAudio");
            UnityEngine.Object.DontDestroyOnLoad(root);
            source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
        }
        source.pitch = AudioManager.TunedPitch(clip, 1f);
        source.PlayOneShot(clip, AudioManager.TunedVolume(clip, 1f));
    }
}
