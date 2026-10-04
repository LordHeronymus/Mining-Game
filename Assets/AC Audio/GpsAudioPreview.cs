using UnityEngine;

public sealed class GpsAudioPreview : MonoBehaviour
{
    static GpsAudioPreview instance;
    AudioSource source;
    float end;
    public static AudioClip Playing => instance && instance.source && instance.source.isPlaying ? instance.source.clip : null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => instance=null;
    public static void Play(AudioClip clip)
    {
        if(!Application.isPlaying || !clip)return;
        if(!instance) { var root=new GameObject("GPS Audio Preview");DontDestroyOnLoad(root);instance=root.AddComponent<GpsAudioPreview>();instance.source=root.AddComponent<AudioSource>();instance.source.playOnAwake=false;instance.source.ignoreListenerPause=true; }
        var s=instance.source;s.Stop();s.clip=clip;s.loop=false;s.spatialBlend=0;
        s.pitch=GpsAudio.Pitch(clip,1,s);s.volume=GpsAudio.Volume(clip,1,s);s.Play();
        instance.end=Time.unscaledTime+Mathf.Min(10,clip.length/s.pitch);
    }
    public static void Stop(){if(instance && instance.source)instance.source.Stop();}
    void Update(){if(Time.unscaledTime>=end || GameAudioLifecycle.IsStopping)Stop();}
    void OnDestroy(){if(instance==this)instance=null;}
}
