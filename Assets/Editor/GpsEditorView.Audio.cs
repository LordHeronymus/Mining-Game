using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class GpsEditorView
{
    string audioSearch="";
    void DrawAudioCatalog()
    {
        using(new EditorGUILayout.VerticalScope(Theme.Card))
        {
            foreach(var pair in GpsSchema.SectionFields("Audio","Gesamtpegel"))
            {
                var value=GpsSettings.GetValue(pair.record.key,pair.field.name);var spec=pair.field;
                Theme.Row(spec.label,out var rect);EditorGUI.BeginChangeCheck();
                float number=EditorGUI.FloatField(rect,(float)value.number*spec.factor,Theme.Field);
                if(EditorGUI.EndChangeCheck()) { value.number=Mathf.Clamp(number/spec.factor,spec.min,spec.max);GpsSettings.SetValue(pair.record.key,value,out _); }
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Clips suchen",Theme.Label,GUILayout.Width(130));
                audioSearch=GUILayout.TextField(audioSearch,Theme.Field,GUILayout.Height(30));
                if(GUILayout.Button("Stopp",Theme.Button,GUILayout.Width(80)))HomeAudioEditorPreview.Stop();
            }
        }
        var record=GpsAudio.Record;
        var clips=record?.fields.Find(v=>v.name=="clips");if(clips==null)return;
        foreach(string category in GpsAudio.Categories)
        {
            var entries=clips.children.Where(e=>GpsAudio.Category(e)==category && GpsAudio.Matches(e,audioSearch)).OrderBy(GpsAudio.Name).ToArray();
            if(entries.Length==0)continue;
            if(!string.IsNullOrWhiteSpace(audioSearch))expanded.Add("Audio/Catalog/"+category);
            using(new EditorGUILayout.VerticalScope(Theme.Card))
            {
                bool open=Foldout("Audio/Catalog/"+category,category+" ("+entries.Length+")");
                if(!open && string.IsNullOrWhiteSpace(audioSearch))continue;
                foreach(var entry in entries)
                {
                    string key=entry.children.Find(v=>v.name=="clip").text;
                    bool clipOpen;
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        clipOpen=Foldout("Audio/Clip/"+key,GpsAudio.Name(entry));
                        var clip=GpsAudio.Clip(entry);
                        bool playing=Application.isPlaying ? GpsAudioPreview.Playing==clip : HomeAudioEditorPreview.Playing==clip;
                        if(GUILayout.Button(playing?"Stopp":"Test",Theme.Button,GUILayout.Width(80)))
                        { if(playing)HomeAudioEditorPreview.Stop();else HomeAudioEditorPreview.Play(clip); }
                    }
                    if(!clipOpen)continue;
                    using(new EditorGUI.IndentLevelScope())
                        foreach(string field in GpsAudio.Fields)
                        {
                            var value=entry.children.Find(v=>v.name==field);if(value==null)continue;
                            var spec=GpsSchema.ChildSpec(value,typeof(AudioClipTuningEntry));
                            Theme.Row(spec.label,out var rect);
                            EditorGUI.BeginChangeCheck();
                            float number=EditorGUI.FloatField(rect,(float)value.number*spec.factor,Theme.Field);
                            if(EditorGUI.EndChangeCheck() && !GpsAudio.Change(key,field,Mathf.Clamp(number/spec.factor,spec.min,spec.max),out string error))window.SetStatus(error);
                        }
                }
            }
        }
    }
}
