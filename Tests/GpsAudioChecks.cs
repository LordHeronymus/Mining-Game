using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class GpsAudioChecks
{
    static List<string> checks=new();
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks.Add(message);}
    public static object Main()
    {
        checks.Clear();var profile=GpsSettings.Profile;string original=JsonUtility.ToJson(GpsSettings.Document),saved=profile.documentJson;
        GameObject root=null;
        try
        {
            GpsSettings.ApplyAssets();var catalog=GpsAudio.Record.fields.Find(v=>v.name=="clips");
            var all=AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets"}).SelectMany(g=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AudioClip>()).ToArray();
            Check(all.Length==catalog.children.Count,"Catalog includes all "+all.Length+" project clips, including archive");
            Check(catalog.children.Select(GpsAudio.Clip).Distinct().Count()==all.Length,"Each clip occurs exactly once");
            foreach(var clip in all)
            {
                var entry=catalog.children.Single(e=>GpsAudio.Clip(e)==clip);
                Check(GpsAudio.Fields.All(f=>entry.children.Any(v=>v.name==f)) && GpsAudio.Tuning(clip)!=null && GpsAudio.Categories.Contains(GpsAudio.Category(entry)),"Four controls and runtime lookup: "+clip.name);
            }
            Check(GpsSettings.ValidateDocument(GpsSettings.Document,out var error),"Complete profile validates: "+error);
            var selected=Resources.Load<AudioClip>("Audio/HomeClick");string key=profile.Key(selected);
            Check(GpsAudio.Change(key,"volume",.4,out _) && GpsAudio.Change(key,"volumeSpread",.25,out _) && GpsAudio.Change(key,"pitch",1.2,out _) && GpsAudio.Change(key,"pitchSpread",.1,out _),"Four independent edits accepted");
            Check(Mathf.Approximately(GpsAudio.Tuning(selected).volume,.4f) && Mathf.Approximately(GpsAudio.Tuning(selected).pitch,1.2f),"Cache follows replaced GPS lists without stale values");
            root=new GameObject("Audio Sampling Check"){hideFlags=HideFlags.HideAndDontSave};var source=root.AddComponent<AudioSource>();source.playOnAwake=false;
            var gains=new HashSet<float>();var pitches=new HashSet<float>();
            for(int i=0;i<64;i++)
            {
                float pitch=GpsAudio.Pitch(selected,1,source),gain=GpsAudio.Volume(selected,1,source);
                if(pitch<1.08f || pitch>1.32f || gain<.3f || gain>.5f)throw new Exception("Spread outside bounds");
                if(gain!=GpsAudio.Volume(selected,1,source))throw new Exception("Volume rerandomized during playback");
                gains.Add(gain);pitches.Add(pitch);
            }
            Check(gains.Count>40 && pitches.Count>40,"Independent bounded samples per play; stable gain between frames");
            GpsAudio.Change(key,"volumeSpread",0,out _);GpsAudio.Change(key,"pitchSpread",0,out _);
            Check(Mathf.Approximately(GpsAudio.Pitch(selected,1,source),1.2f) && Mathf.Approximately(GpsAudio.Volume(selected,1,source),.4f),"Zero spreads give exact configured values");
            GpsAudio.Change(key,"volume",0,out _);Check(GpsAudio.Volume(selected,1,source)==0,"Zero volume mutes");
            var baseline=JsonUtility.FromJson<GpsDocument>(original);var legacy=JsonUtility.FromJson<GpsDocument>(original);
            var old=legacy.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);old.fields.RemoveAll(v=>v.name=="catalogVersion");
            var oldClips=old.fields.Find(v=>v.name=="clips");
            foreach(var entry in oldClips.children)entry.children.RemoveAll(v=>v.name=="category"||v.name=="displayName"||v.name=="volumeSpread");
            var home=legacy.records.Find(r=>r.type==typeof(LoadingAudioSettingsAsset).AssemblyQualifiedName).fields.Find(v=>v.name=="homeClips");
            var homeEntry=home.children.First();string homeKey=homeEntry.children.Find(v=>v.name=="clip").text;
            oldClips.children.First(e=>e.children.Find(v=>v.name=="clip").text==homeKey).children.Find(v=>v.name=="volume").number=.5;
            homeEntry.children.Find(v=>v.name=="volume").number=.6;
            string file="Temp/GpsAudio-old-settings.json";File.WriteAllText(file,JsonUtility.ToJson(legacy));
            var upgraded=GpsFileStore.Load(file,baseline,out string warning);
            Check(warning==null && GpsSettings.ValidateDocument(upgraded,out _),"Old player JSON upgrades before validation");
            var upgradedEntry=upgraded.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName).fields.Find(v=>v.name=="clips").children.First(e=>e.children.Find(v=>v.name=="clip").text==homeKey);
            Check(Math.Abs(upgradedEntry.children.Find(v=>v.name=="volume").number-.3)<.0001,"Legacy homescreen and global gains combined once");
            string upgradedJson=JsonUtility.ToJson(upgraded);GpsAudioMigration.Merge(upgraded,baseline,profile);
            Check(upgradedJson==JsonUtility.ToJson(upgraded),"Migration idempotent");
            GpsFileStore.Save("Temp/GpsAudio-roundtrip.json",upgraded);var roundtrip=GpsFileStore.Load("Temp/GpsAudio-roundtrip.json",baseline,out warning);
            Check(warning==null && JsonUtility.ToJson(roundtrip)==JsonUtility.ToJson(upgraded),"All tuning and metadata survive save/reload");
            File.WriteAllText("Temp/GpsAudioChecks.txt","PASS ("+checks.Count+")\n"+string.Join("\n",checks));return "PASS ("+checks.Count+")";
        }
        catch(Exception ex){File.WriteAllText("Temp/GpsAudioChecks.txt","FAIL: "+ex+"\n"+string.Join("\n",checks));throw;}
        finally{if(root)Object.DestroyImmediate(root);profile.documentJson=saved;GpsSettings.UseProfile(profile,JsonUtility.FromJson<GpsDocument>(original));GpsSettings.ApplyAssets();}
    }
}
