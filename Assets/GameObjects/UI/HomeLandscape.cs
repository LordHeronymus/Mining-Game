using System;
using System.Linq;
using UnityEngine;

public static class HomeLandscape
{
    public static int LayerAtDepth(int depth,GpsValue[] generation=null)
    {
        var fields=generation ?? GpsSettings.Document.records.Find(r=>r.type==typeof(MapGenerator).AssemblyQualifiedName)?.fields.ToArray();
        var layers=fields?.FirstOrDefault(v=>v.name=="layers")?.children;
        int result=1;
        if(layers!=null)for(int i=0;i<layers.Count;i++) {
            var start=layers[i].children.Find(v=>v.name=="startDepth");
            if(start!=null && depth>=start.number)result=i+1;
        }
        return result;
    }
    public static AudioClip MeadowClip {
        get {
            var record=GpsSettings.Document.records.Find(r=>r.type==typeof(SurfaceAmbience).AssemblyQualifiedName);
            return GpsSettings.Profile.Resolve(record?.fields.Find(v=>v.name=="clip")?.text) as AudioClip ??
                GpsSettings.Profile.assets.Select(a=>a.asset).OfType<AudioClip>().FirstOrDefault(c=>c.name=="MeadowAmbience");
        }
    }
    public static float MeadowVolume => Number(typeof(SurfaceAmbience),"volume",.5f);
    public static float SurfaceVolume => Number(typeof(AudioManager),"surfaceVolume",1);
    public static AudioClip Layer2Clip {
        get {
            var record=GpsSettings.Document.records.Find(r=>r.type==typeof(FirstLayerAmbience).AssemblyQualifiedName);
            return GpsSettings.Profile.Resolve(record?.fields.Find(v=>v.name=="clip")?.text) as AudioClip ??
                GpsSettings.Profile.assets.Select(a=>a.asset).OfType<AudioClip>().FirstOrDefault(c=>c.name=="UndergroundClay");
        }
    }
    public static float Layer2Volume => Number(typeof(FirstLayerAmbience),"volume",.5f);
    public static float UndergroundVolume => Number(typeof(AudioManager),"undergroundVolume",1);
    static float Number(Type type,string name,float fallback) {
        var value=GpsSettings.Document.records.Find(r=>r.type==type.AssemblyQualifiedName)?.fields.Find(v=>v.name==name);
        return value==null ? fallback : Mathf.Clamp01((float)value.number);
    }
}
