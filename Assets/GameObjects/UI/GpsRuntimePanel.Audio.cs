using System.Linq;
using UnityEngine;

public sealed partial class GpsRuntimePanel
{
    string audioSearch="";
    void BuildAudioCatalog()
    {
        foreach(var pair in GpsSchema.SectionFields("Audio","Gesamtpegel"))
        {
            var value=GpsSettings.GetValue(pair.record.key,pair.field.name);var spec=pair.field;
            DrawValue(pair.record.key,value,value,spec,spec.label,"Audio/"+spec.name,0,null,
                ()=> { committing=true;try{GpsSettings.SetValue(pair.record.key,value,out _);}finally{committing=false;} });
        }
        Label(content,"Clips suchen",new Vector2(0,-rowY),new Vector2(200,32),20);
        Input(content,new Vector2(250,-rowY),new Vector2(1250,32),audioSearch,text=>{audioSearch=text;BuildPage();scroll.verticalNormalizedPosition=1;});
        Button(content,"Stopp",new Vector2(1560,-rowY),new Vector2(120,32),GpsAudioPreview.Stop);rowY+=46;
        var clips=GpsAudio.Record?.fields.Find(v=>v.name=="clips");if(clips==null)return;
        foreach(string category in GpsAudio.Categories)
        {
            var entries=clips.children.Where(e=>GpsAudio.Category(e)==category && GpsAudio.Matches(e,audioSearch)).OrderBy(GpsAudio.Name).ToArray();
            if(entries.Length==0)continue;
            if(!string.IsNullOrWhiteSpace(audioSearch))expanded.Add("Audio/Catalog/"+category);
            bool open=Header("Audio/Catalog/"+category,category+" ("+entries.Length+")",0);
            if(!open && string.IsNullOrWhiteSpace(audioSearch))continue;
            foreach(var entry in entries)
            {
                string key=entry.children.Find(v=>v.name=="clip").text;
                float y=rowY;bool clipOpen=Header("Audio/Clip/"+key,GpsAudio.Name(entry),1);
                Button(content,"Test",new Vector2(1560,-y),new Vector2(120,32),()=>GpsAudioPreview.Play(GpsAudio.Clip(entry)));
                if(!clipOpen)continue;
                foreach(string field in GpsAudio.Fields)
                {
                    var value=entry.children.Find(v=>v.name==field)?.Copy();if(value==null)continue;
                    var spec=GpsSchema.ChildSpec(value,typeof(AudioClipTuningEntry));
                    DrawValue(GpsAudio.Record.key,value,value,spec,spec.label,"Audio/Clip/"+key+"/"+field,2,null,()=>
                    { committing=true;try { if(!GpsAudio.Change(key,field,value.number,out var error))status.text=error; }finally{committing=false;} });
                }
            }
        }
    }
}
