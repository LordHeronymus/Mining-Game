using System.Linq;
using UnityEngine;

public static class GpsAudioMigration
{
    // Upgrade old player JSON before shape validation. Array entries merge by clip key,
    // keeping user tuning while adding new shipped clips and catalog metadata.
    public static void Merge(GpsDocument data,GpsDocument baseline,GpsProfile profile)
    {
        if(data?.records==null || baseline?.records==null || !profile)return;
        var old=data.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);
        var current=baseline.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);
        if(old==null || current==null)return;
        var clips=old.fields.Find(v=>v.name=="clips");var defaults=current.fields.Find(v=>v.name=="clips");
        if(clips==null || defaults==null)return;
        bool migrate=(old.fields.Find(v=>v.name=="catalogVersion")?.number??0)<1;
        var home=data.records.Find(r=>r.type==typeof(LoadingAudioSettingsAsset).AssemblyQualifiedName)?.fields.Find(v=>v.name=="homeClips");
        foreach(var entry in defaults.children)
        {
            string key=entry.children.Find(v=>v.name=="clip")?.text;
            var existing=clips.children.Find(e=>e.children.Find(v=>v.name=="clip")?.text==key);
            if(existing==null){clips.children.Add(entry.Copy());continue;}
            var tune=(AudioClipTuningEntry)GpsCodec.Write(existing,profile.Resolve);
            var metadata=(AudioClipTuningEntry)GpsCodec.Write(entry,profile.Resolve);
            tune.category=metadata.category;tune.displayName=metadata.displayName;
            if(migrate)
            {
                var legacy=home?.children.Find(e=>e.children.Find(v=>v.name=="clip")?.text==key);
                if(legacy!=null)
                {
                    var previous=(HomeAudioClipTuning)GpsCodec.Write(legacy,profile.Resolve);
                    tune.volume*=previous.volume;tune.pitch*=previous.pitch;
                    tune.volumeSpread=previous.volumeSpread;tune.pitchSpread=Mathf.Max(tune.pitchSpread,previous.pitchSpread);
                }
            }
            clips.children[clips.children.IndexOf(existing)]=GpsCodec.Read(existing.name,typeof(AudioClipTuningEntry),tune,profile.Key);
        }
        var version=old.fields.Find(v=>v.name=="catalogVersion");
        if(version==null)old.fields.Add(GpsCodec.Read("catalogVersion",typeof(int),1,profile.Key));else version.number=1;
    }
}
