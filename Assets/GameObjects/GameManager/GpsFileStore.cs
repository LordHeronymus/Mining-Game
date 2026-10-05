using System;
using System.IO;
using UnityEngine;

public static class GpsFileStore
{
    public static GpsDocument Load(string path,GpsDocument baseline,out string warning)
    {
        warning=null;
        foreach(string candidate in new[]{path,path+".bak"})
        {
            if(!File.Exists(candidate)) continue;
            try
            {
                var data=JsonUtility.FromJson<GpsDocument>(File.ReadAllText(candidate));
                GpsAudioMigration.Merge(data,baseline,GpsSettings.Profile);
                ItemIconLayout.Migrate(data,GpsSettings.Profile);
                MergeMissing(data,baseline);
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
    public static void Save(string path,GpsDocument data)
    {
        if(!GpsSettings.ValidateDocument(data,out string error)) throw new InvalidDataException(error);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonUtility.ToJson(data,true));
        if(File.Exists(path)) File.Replace(temporary,path,path+".bak"); else File.Move(temporary,path);
    }
}
