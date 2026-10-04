using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class GpsSettingsChecks
{
    static readonly List<string> checks=new();
    static void Check(bool condition,string label) { if(!condition)throw new Exception(label); checks.Add(label); }
    static GpsDocument Copy(GpsDocument doc)=>JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static bool Equivalent(GpsValue a,GpsValue b)
    {
        if(a.kind!=b.kind || a.name!=b.name || a.type!=b.type)return false;
        if(a.kind==GpsValueKind.Number && (float)a.number!=(float)b.number)return false;
        if((a.kind==GpsValueKind.Integer || a.kind==GpsValueKind.Enum) && a.number!=b.number)return false;
        if(a.kind==GpsValueKind.Boolean && a.flag!=b.flag)return false;
        if((a.kind==GpsValueKind.Text || a.kind==GpsValueKind.Reference) && (a.text??"")!=(b.text??""))return false;
        if(a.kind==GpsValueKind.Vector && a.vector!=b.vector)return false;
        if(a.kind==GpsValueKind.Color && a.color!=b.color)return false;
        if(a.kind==GpsValueKind.Curve && JsonUtility.ToJson(a.curve)!=JsonUtility.ToJson(b.curve))return false;
        return a.children.Count==b.children.Count && a.children.Zip(b.children,Equivalent).All(equal=>equal);
    }
    public static object Main()
    {
        if(!EditorApplication.isPlaying) GpsProfileEditor.Install(); checks.Clear();
        var profile=GpsSettings.Profile; var original=Copy(GpsSettings.Document); string originalJson=profile.documentJson;
        var temporary=ScriptableObject.CreateInstance<GpsProfile>(); temporary.assets=profile.assets.ToList(); temporary.documentJson=originalJson;
        string directory=Path.GetFullPath("Temp/GpsSettingsChecks-"+DateTime.UtcNow.Ticks); Directory.CreateDirectory(directory);
        try
        {
            GpsSettings.UseProfile(temporary,Copy(original));
            Check(GpsSettings.ValidateDocument(GpsSettings.Document,out var error),"Existing configuration valid: "+error);
            Check(!GameplaySettingsWindow.TabLabels.Contains(GpsSchema.TestTab),"Editor excludes Testeinstellungen");
            Check(GpsRuntimePanel.TabLabels.SequenceEqual(GameplaySettingsWindow.TabLabels.Concat(new[]{GpsSchema.TestTab})),"Runtime tabs equal Editor plus Testeinstellungen");
            Check(GpsSchema.Sections.Where(s=>s.tab!=GpsSchema.TestTab).SelectMany(s=>s.fields.Select(f=>(s.source,s.role,f.name))).GroupBy(x=>x).All(g=>g.Count()==1),"Normal settings have one catalog entry per source field");
            foreach(var record in original.records)
                foreach(var node in record.fields)
                {
                    var restored=GpsCodec.Read(node.name,GpsCodec.ResolveType(node.type),GpsCodec.Write(node,profile.Resolve),profile.Key);
                    Check(Equivalent(node,restored),"Roundtrip "+record.type.Split(',')[0]+"."+node.name);
                }
            var curve=new AnimationCurve(new Keyframe(0,.2f,-.5f,1.8f,.2f,.8f){weightedMode=WeightedMode.Both},new Keyframe(1,2,1,0,.7f,.4f){weightedMode=WeightedMode.In});
            curve.preWrapMode=WrapMode.Loop; curve.postWrapMode=WrapMode.PingPong;
            var curveNode=GpsCodec.Read("curve",typeof(AnimationCurve),curve,profile.Key);
            var returned=(AnimationCurve)GpsCodec.Write(curveNode.Copy(),profile.Resolve);
            Check(Enumerable.Range(0,201).All(i=>Mathf.Abs(curve.Evaluate(i/100f-.5f)-returned.Evaluate(i/100f-.5f))<.000001f),"Weighted tangents and wrap modes survive JSON");
            var speedRecord=GpsSettings.Document.records.First(record=>record.type==typeof(PlayerBaseStats).AssemblyQualifiedName);
            var speed=GpsSettings.GetValue(speedRecord.key,"miningSpeed"); double before=speed.number; speed.number=before+.5;
            Check(GpsSettings.GetValue(speedRecord.key,"miningSpeed").number==before,"Reading a value does not expose mutable profile data");
            Check(GpsSettings.SetValue(speedRecord.key,speed,out error) && GameplaySettings.BaseDiggingSpeed==(float)speed.number && GpsSettings.HasUnsavedChanges,"Shared mining setting applies and marks dirty");
            speed.number=double.NaN; Check(!GpsSettings.SetValue(speedRecord.key,speed,out error),"NaN rejected");
            speed.number=-1; Check(!GpsSettings.SetValue(speedRecord.key,speed,out error),"Negative mining speed rejected");
            var wrong=GpsCodec.Read("miningSpeed",typeof(string),"bad",profile.Key); Check(!GpsSettings.SetValue(speedRecord.key,wrong,out error),"Incompatible field type rejected");
            string path=Path.Combine(directory,"gps-settings.json"); GpsFileStore.Save(path,GpsSettings.Document);
            var loaded=GpsFileStore.Load(path,Copy(original),out var warning);
            Check(warning==null && loaded.records.First(r=>r.key==speedRecord.key).fields.Find(n=>n.name=="miningSpeed").number==before+.5,"Unified JSON restores changed setting");
            GpsFileStore.Save(path,original); File.WriteAllText(path,"corrupt");
            loaded=GpsFileStore.Load(path,Copy(original),out warning);
            Check(warning!=null && loaded.records.First(r=>r.key==speedRecord.key).fields.Find(n=>n.name=="miningSpeed").number==before+.5,"Corrupt file falls back to atomic backup");
            var invalid=Copy(original); invalid.preferences.panelBackdropAlpha=float.PositiveInfinity;
            Check(!GpsSettings.ValidateDocument(invalid,out error),"Nonfinite panel settings rejected");
            invalid=Copy(original); invalid.records.First(r=>r.type==typeof(MapGenerator).AssemblyQualifiedName).fields.Find(n=>n.name=="layers").children[0].children.Find(n=>n.name=="startDepth").number=5;
            Check(!GpsSettings.ValidateDocument(invalid,out error),"Invalid first map layer rejected");
            Check(GpsSchema.Sections.Single(s=>s.source==typeof(MapGenerator) && s.fields.Any(f=>f.name=="oreScale")).title=="Blockeigenschaften","Ore size belongs to Map block properties");
            Check(GpsSchema.Fields(typeof(MapGenerator)).Single(f=>f.name=="layers").applyTime==GpsApplyTime.NewMap,"Layer changes deferred until map generation");
            File.WriteAllText(Path.Combine(directory,"gameplay-settings.json"),JsonUtility.ToJson(new GameplaySettingsData {baseDiggingSpeed=3.25f,hasLightingOverride=true,lighting=new LightingSettingsData{ambient=.12f}}));
            File.WriteAllText(Path.Combine(directory,"gameplay-test-settings.json"),JsonUtility.ToJson(new GameplayTestSettingsData {diggingMultiplier=4,movementMultiplier=2,hasMiningHitOffsetOverride=true,miningHitOffsetMs=75,dayNightMode=GameplayDayNightMode.Night}));
            var migration=Copy(original); GpsLegacyImport.Import(migration,profile,directory);
            Check(migration.records.First(r=>r.key==speedRecord.key).fields.Find(n=>n.name=="miningSpeed").number==3.25,"Legacy mining override imported");
            Check(migration.records.First(r=>r.type==typeof(MapLighting).AssemblyQualifiedName).fields.Find(n=>n.name=="ambientBrightness").number>.119,"Legacy lighting imported");
            Check(migration.tests.diggingMultiplier==4 && migration.tests.movementMultiplier==2 && !migration.tests.hasMiningHitOffsetOverride && migration.tests.dayNightMode==GameplayDayNightMode.Automatic,"Legacy tests imported without parallel normal overrides");
            Check(migration.records.First(r=>r.type==typeof(MinerPlayerVisual).AssemblyQualifiedName).fields.Find(n=>n.name=="miningHitOffsetMs").number==75,"Hit offset merged into normal setting");
            Check(migration.records.First(r=>r.type==typeof(SkyController).AssemblyQualifiedName).fields.Find(n=>n.name=="isNight").flag,"Day/night override merged into normal sky setting");
            Check(File.Exists(Path.Combine(directory,"gameplay-settings.json")),"Migration preserves original files");
            string report="PASS ("+checks.Count+")\n"+string.Join("\n",checks); File.WriteAllText("Temp/GpsSettingsChecks.txt",report); return report.Split('\n')[0];
        }
        catch(Exception ex) { string report="FAIL: "+ex+"\n"+string.Join("\n",checks);File.WriteAllText("Temp/GpsSettingsChecks.txt",report);throw; }
        finally
        {
            profile.documentJson=originalJson; GpsSettings.UseProfile(profile,original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents(); Object.DestroyImmediate(temporary);
        }
    }
}
