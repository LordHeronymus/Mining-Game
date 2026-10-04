using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class GpsAudioPreviewChecks
{
    static AudioClip[] clips;static int index;static double started;static bool muted;
    static List<string> checks=new();
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        clips=GpsAudio.Record.fields.Find(v=>v.name=="clips").children.Select(GpsAudio.Clip).ToArray();
        index=0;checks.Clear();muted=EditorUtility.audioMasterMute;EditorUtility.audioMasterMute=true;
        AssemblyReloadEvents.beforeAssemblyReload+=Interrupted;
        EditorApplication.playModeStateChanged+=PlayChanged;
        EditorApplication.update+=Tick;Begin();return "Checking "+clips.Length+" editor previews silently";
    }
    static void Begin(){started=EditorApplication.timeSinceStartup;HomeAudioEditorPreview.Play(clips[index]);}
    static void Tick()
    {
        try
        {
            var rendered=(AudioClip)typeof(HomeAudioEditorPreview).GetField("rendered",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            if(rendered)
            {
                if(rendered.length>10.01f)throw new Exception("Preview duration exceeds limit");
                checks.Add(clips[index].name+" ("+clips[index].loadType+")");HomeAudioEditorPreview.Stop();
                if(++index==clips.Length){Finish("PASS ("+checks.Count+")");return;}Begin();
            }
            else if(EditorApplication.timeSinceStartup-started>15)throw new Exception("Preview failed: "+clips[index].name);
        }
        catch(Exception ex){Finish("FAIL: "+ex);}
    }
    static void Interrupted()=>Finish("INTERRUPTED: editor reload");
    static void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Finish("INTERRUPTED: external Play start");}
    static void Finish(string result)
    {
        AssemblyReloadEvents.beforeAssemblyReload-=Interrupted;EditorApplication.playModeStateChanged-=PlayChanged;
        EditorApplication.update-=Tick;HomeAudioEditorPreview.Stop();EditorUtility.audioMasterMute=muted;
        File.WriteAllText("Temp/GpsAudioPreviewChecks.txt",result+"\n"+string.Join("\n",checks));Debug.Log(result);
    }
}
