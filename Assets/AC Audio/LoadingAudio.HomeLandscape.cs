using UnityEngine;

public sealed partial class LoadingAudio
{
    readonly AudioSource[] meadowSources=new AudioSource[2];
    readonly AudioSource[] layer2Sources=new AudioSource[2];
    int homeLayer=1, meadowCurrent, layer2Current;
    float meadowGain, layer2Gain;
    double meadowNextStart, layer2NextStart;
    bool meadowRunning, layer2Running;
    public static int HomeLayer => instance ? instance.homeLayer : 1;
    public static void SetHomeLayer(int layer)
    {
        if(!instance)return;
        instance.homeLayer=Mathf.Max(1,layer);
        if(instance.homeLayer==1)instance.StopDistantDetails();
        if(layer!=3)foreach(var source in instance.distantSources)
            if(source && source.clip && source.clip.name.StartsWith("HomeWaterdrop"))source.Stop();
    }
    void UpdateMeadow()
    {
        UpdateHomeAmbience(meadowSources,1,HomeLandscape.MeadowClip,HomeLandscape.MeadowVolume*HomeLandscape.SurfaceVolume,
            ref meadowCurrent,ref meadowGain,ref meadowNextStart,ref meadowRunning);
        UpdateHomeAmbience(layer2Sources,2,HomeLandscape.Layer2Clip,HomeLandscape.Layer2Volume*HomeLandscape.UndergroundVolume,
            ref layer2Current,ref layer2Gain,ref layer2NextStart,ref layer2Running);
    }
    void UpdateHomeAmbience(AudioSource[] sources,int layer,AudioClip clip,float volume,
        ref int current,ref float levelGain,ref double nextStart,ref bool running)
    {
        levelGain=Mathf.MoveTowards(levelGain,homeRunning && homeLayer==layer && !loading ? 1 : 0,Time.unscaledDeltaTime/.8f);
        if(!homeRunning || levelGain<=0 || !clip) { StopHomeAmbience(sources);running=false;return; }
        float fade=Mathf.Min(3,clip.length/3f),gain=levelGain*HomeGain*volume*AudioManager.AmbienceVolume;
        if(!running) {
            current=0;
            for(int i=0;i<2;i++) {
                if(!sources[i]) {
                    sources[i]=gameObject.AddComponent<AudioSource>();
                    sources[i].playOnAwake=false;sources[i].spatialBlend=0;sources[i].ignoreListenerPause=true;
                }
                sources[i].clip=clip;
                sources[i].volume=0;sources[i].pitch=AudioManager.TunedPitch(clip,1,sources[i]);
            }
            sources[0].Play();
            nextStart=AudioSettings.dspTime+clip.length/sources[0].pitch-fade;
            sources[1].PlayScheduled(nextStart);running=true;
        }
        float blend=Mathf.Clamp01((float)(AudioSettings.dspTime-nextStart)/Mathf.Max(.01f,fade));
        sources[current].volume=AudioManager.TunedAmbienceVolume(clip,gain*Mathf.Cos(blend*Mathf.PI*.5f),sources[current]);
        sources[1-current].volume=AudioManager.TunedAmbienceVolume(clip,gain*Mathf.Sin(blend*Mathf.PI*.5f),sources[1-current]);
        if(blend<1)return;
        sources[current].Stop();current=1-current;
        nextStart+=clip.length/sources[current].pitch-fade;
        var next=sources[1-current];next.pitch=AudioManager.TunedPitch(clip,1,next);next.volume=0;next.PlayScheduled(nextStart);
    }
    static void StopHomeAmbience(AudioSource[] sources)
    {
        foreach(var source in sources)if(source) { source.Stop();source.volume=0; }
    }
    void StopMeadow()
    {
        StopHomeAmbience(meadowSources);StopHomeAmbience(layer2Sources);
        meadowRunning=layer2Running=false;
        meadowGain=layer2Gain=0;
    }
}
