using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Player files store only the values that differ from the shipped profile, so rebalancing in a
// later build reaches existing players for every value they never changed themselves.
public static class GpsFileStore
{
    const string OverridesFormat = "gps-overrides";
    const string PreferencesKey = "preferences", TestsKey = "tests";
    [Serializable] sealed class GpsOverrides { public string format; public int version = 1; public List<GpsRecord> records = new(); }

    public static GpsDocument Load(string path,GpsDocument baseline,out string warning) => Load(path,baseline,out warning,out _);
    public static GpsDocument Load(string path,GpsDocument baseline,out string warning,out bool legacyFormat)
    {
        warning=null; legacyFormat=false;
        foreach(string candidate in new[]{path,path+".bak"})
        {
            if(!File.Exists(candidate)) continue;
            try
            {
                string json=File.ReadAllText(candidate);
                var overrides=JsonUtility.FromJson<GpsOverrides>(json);
                GpsDocument data;
                if(overrides?.format==OverridesFormat)
                {
                    data=Copy(baseline);
                    Apply(overrides,data);
                    // An overridden clip list still receives clips and metadata added in later builds.
                    GpsAudioMigration.Merge(data,baseline,GpsSettings.Profile);
                    legacyFormat=false;
                }
                else
                {
                    // Older builds wrote the whole document. Keep its values once; the next save
                    // converts it to overrides against the current shipped profile.
                    data=JsonUtility.FromJson<GpsDocument>(json);
                    GpsAudioMigration.Merge(data,baseline,GpsSettings.Profile);
                    MergeMissing(data,baseline);
                    UpgradeShapes(data);
                    legacyFormat=true;
                }
                ItemIconLayout.Migrate(data,GpsSettings.Profile);
                if(!GpsSettings.ValidateDocument(data,out string error)) throw new InvalidDataException(error);
                if(candidate!=path) warning="GPS-Sicherung geladen.";
                return data;
            }
            catch(Exception ex) { warning="GPS konnten nicht geladen werden: "+ex.Message; }
        }
        return baseline;
    }
    static void MergeMissing(GpsDocument data,GpsDocument baseline)
    {
        if(data?.version!=baseline.version || data.records==null) return;
        foreach(var record in baseline.records)
        {
            var found=data.records.Find(entry=>entry.key==record.key);
            if(found==null) data.records.Add(JsonUtility.FromJson<GpsRecord>(JsonUtility.ToJson(record)));
            else foreach(var field in record.fields) if(!found.fields.Exists(entry=>entry.name==field.name)) found.fields.Add(field.Copy());
        }
    }
    static void UpgradeShapes(GpsDocument data)
    {
        if(data?.records==null) return;
        foreach(var record in data.records)
        {
            var type=GpsCodec.ResolveType(record?.type);
            if(type==null || record.fields==null) continue;
            record.fields.RemoveAll(field=>field==null || GpsCodec.MemberType(type,field.name)==null || GpsSchema.Spec(record,field.name)==null);
            for(int i=0;i<record.fields.Count;i++)
                record.fields[i]=GpsCodec.Upgrade(record.fields[i],GpsCodec.MemberType(type,record.fields[i].name),GpsSettings.Profile.Key);
        }
    }
    // Save and Load must use the same baseline; the game always uses the shipped profile.
    public static void Save(string path,GpsDocument data,GpsDocument baseline=null)
    {
        if(!GpsSettings.ValidateDocument(data,out string error)) throw new InvalidDataException(error);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonUtility.ToJson(Diff(data,baseline ?? GpsSettings.ShippedDocument()),true));
        if(File.Exists(path)) File.Replace(temporary,path,path+".bak"); else File.Move(temporary,path);
    }

    static GpsDocument Copy(GpsDocument data) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(data));
    static string Json(GpsValue node) => JsonUtility.ToJson(node);

    static GpsOverrides Diff(GpsDocument data,GpsDocument baseline)
    {
        var result=new GpsOverrides{format=OverridesFormat};
        foreach(var record in data.records)
        {
            var shipped=baseline.records.Find(entry=>entry.key==record.key);
            if(shipped==null) continue;
            var changed=record.fields.Where(field=>shipped.fields.Find(entry=>entry.name==field.name) is GpsValue original && Json(original)!=Json(field))
                .Select(field=>field.Copy()).ToList();
            if(changed.Count>0) result.records.Add(new GpsRecord{key=record.key,type=record.type,assetKey=record.assetKey,name=record.name,fields=changed});
        }
        AddObjectDiff(result,PreferencesKey,data.preferences,baseline.preferences);
        AddObjectDiff(result,TestsKey,data.tests,baseline.tests);
        return result;
    }
    static void AddObjectDiff(GpsOverrides result,string key,object value,object shipped)
    {
        if(value==null || shipped==null) return;
        var changed=new List<GpsValue>();
        foreach(var field in GpsCodec.SerializableFields(value.GetType()))
        {
            var current=GpsCodec.Read(field.Name,field.FieldType,field.GetValue(value),GpsSettings.Profile.Key);
            var original=GpsCodec.Read(field.Name,field.FieldType,field.GetValue(shipped),GpsSettings.Profile.Key);
            if(Json(current)!=Json(original)) changed.Add(current);
        }
        if(changed.Count>0) result.records.Add(new GpsRecord{key=key,type=value.GetType().AssemblyQualifiedName,fields=changed});
    }
    // Overrides whose field no longer exists or changed its shape are skipped one by one, so a schema
    // change in a later build drops only the affected values instead of the whole file.
    static void Apply(GpsOverrides overrides,GpsDocument data)
    {
        foreach(var record in overrides.records ?? new List<GpsRecord>())
        {
            if(record?.fields==null) continue;
            if(record.key==PreferencesKey || record.key==TestsKey)
            {
                object target=record.key==PreferencesKey ? data.preferences : data.tests;
                foreach(var node in record.fields)
                {
                    var type=GpsCodec.MemberType(target.GetType(),node.name);
                    if(type==null || !GpsCodec.Validate(node,out _) || !GpsCodec.ValidateShape(node,type,out _)) continue;
                    try { GpsCodec.Set(target,node.name,GpsCodec.Write(node,GpsSettings.Profile.Resolve)); } catch(Exception) { }
                }
                continue;
            }
            var destination=data.records.Find(entry=>entry.key==record.key);
            if(destination==null) continue;
            foreach(var node in record.fields)
            {
                int index=destination.fields.FindIndex(field=>field.name==node.name);
                if(index<0 || !GpsSettings.IsValidOverride(destination,node)) continue;
                destination.fields[index]=node.Copy();
            }
        }
    }
}
