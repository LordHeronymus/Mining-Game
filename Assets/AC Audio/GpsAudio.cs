using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

// One tuning entry per clip. Random offsets are held for each source/playback,
// so gain/fade updates never turn volume spread into frame-by-frame noise.
public static class GpsAudio
{
    sealed class Sample { public AudioClip clip; public float volumeRandom, pitchRandom; }
    static ConditionalWeakTable<AudioSource, Sample> samples = new();
    static AudioClipTuningSettingsAsset settings;
    public static readonly string[] Categories = { "Homescreen", "Ladebildschirm", "Abbau", "Geröll", "Bauen und Pflanzen", "Spieler und Gesundheit", "Shop und UI", "Tiere", "Umgebung", "Untergrund-Details", "Altar", "Weitere Clips", "Archiv" };
    public static readonly string[] Fields = { "volume", "volumeSpread", "pitch", "pitchSpread" };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { settings=null; samples=new(); }
    public static AudioClipTuningEntry Tuning(AudioClip clip)
    {
        if (!settings) settings=Resources.Load<AudioClipTuningSettingsAsset>("Audio/AudioClipTuningSettings");
        return settings && settings.TryGet(clip,out var entry) ? entry : null;
    }
    static Sample State(AudioSource source,AudioClip clip)
    {
        if (!source) return new Sample { clip=clip, volumeRandom=UnityEngine.Random.Range(-1f,1f), pitchRandom=UnityEngine.Random.Range(-1f,1f) };
        var sample=samples.GetValue(source,_=>new Sample());
        if(sample.clip!=clip) { sample.clip=clip; Randomize(sample); }
        return sample;
    }
    static void Randomize(Sample sample) { sample.volumeRandom=UnityEngine.Random.Range(-1f,1f); sample.pitchRandom=UnityEngine.Random.Range(-1f,1f); }
    public static float Pitch(AudioClip clip,float basis,AudioSource source=null)
    {
        var sample=State(source,clip); Randomize(sample);
        var tune=Tuning(clip);
        return Mathf.Clamp(basis*(tune?.pitch??1f)*(1f+(tune?.pitchSpread??0f)*sample.pitchRandom),.1f,3f);
    }
    public static float Volume(AudioClip clip,float basis,AudioSource source=null)
    {
        var tune=Tuning(clip);
        return Mathf.Max(0,basis*(tune?.volume??1f)*(1f+(tune?.volumeSpread??0f)*State(source,clip).volumeRandom));
    }
    public static GpsRecord Record => GpsSettings.Document.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);
    public static string Category(GpsValue entry) => entry.children.Find(v=>v.name=="category")?.text ?? "Weitere Clips";
    public static string Name(GpsValue entry) => entry.children.Find(v=>v.name=="displayName")?.text is string name && !string.IsNullOrEmpty(name) ? name : GpsSchema.NodeLabel(entry,GpsSettings.Profile);
    public static AudioClip Clip(GpsValue entry) => GpsSettings.Profile.Resolve(entry.children.Find(v=>v.name=="clip")?.text) as AudioClip;
    public static bool Matches(GpsValue entry,string query) => string.IsNullOrWhiteSpace(query) ||
        (Name(entry)+" "+Category(entry)+" "+Clip(entry)?.name).IndexOf(query.Trim(),StringComparison.CurrentCultureIgnoreCase)>=0;
    public static bool Change(string clipKey,string field,double number,out string error)
    {
        var record=Record; var root=GpsSettings.GetValue(record.key,"clips");
        var entry=root.children.Find(e=>e.children.Find(v=>v.name=="clip")?.text==clipKey);
        var value=entry?.children.Find(v=>v.name==field);
        if(value==null || !Fields.Contains(field)){error="Audioclip fehlt.";return false;}
        value.number=number;return GpsSettings.SetValue(record.key,root,out error);
    }
}
