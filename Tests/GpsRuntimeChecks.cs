using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class GpsRuntimeChecks
{
    static readonly List<string> checks=new();
    static readonly Stack<IEnumerator> work=new();
    static string oldDirectory;
    static int frame;
    static double started;
    static string baseline;
    public static object Main()
    {
        if(!EditorApplication.isPlaying || !MainMenuController.IsVisible)return "Requires fresh Play from MainMenu.";
        baseline=JsonUtility.ToJson(GpsSettings.Document); File.WriteAllText("Temp/GpsRuntimeBaseline.json",baseline);
        oldDirectory=GameSaveSystem.TestDirectory; GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/GpsRuntimeSaves");
        frame=-1; started=EditorApplication.timeSinceStartup; checks.Clear(); work.Clear(); work.Push(Checks());
        EditorApplication.update+=Tick;
        return "Runtime GPS checks running with isolated saves.";
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup-started>180) { Finish("FAIL: interrupted or timeout");return; }
        if(frame==Time.frameCount)return; frame=Time.frameCount;
        try
        {
            while(work.Count>0)
            {
                var next=work.Peek(); if(!next.MoveNext()){work.Pop();continue;}
                if(next.Current is IEnumerator nested){work.Push(nested);continue;}return;
            }
            Finish("PASS ("+checks.Count+")");
        }
        catch(Exception ex){Finish("FAIL: "+ex);}
    }
    static void Check(bool condition,string label){if(!condition)throw new Exception(label); checks.Add(label);}
    static IEnumerator Settled(){while(LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)yield return null;yield return null;}
    static GpsRecord Record(Type type)=>GpsSettings.Document.records.First(record=>record.type==type.AssemblyQualifiedName);
    static void Number(Type type,string field,double value)
    { var record=Record(type); var node=GpsSettings.GetValue(record.key,field); node.number=value; Check(GpsSettings.SetValue(record.key,node,out var error),"Set "+type.Name+"."+field+": "+error); }
    static GpsRuntimePanel Panel=>Object.FindFirstObjectByType<GpsRuntimePanel>(FindObjectsInactive.Include);
    static string Caption(Button button)=>button.GetComponentInChildren<TextMeshProUGUI>()?.text;
    static Button Button(string caption)=>Panel.GetComponentsInChildren<Button>().First(button=>Caption(button)==caption);
    static TMP_InputField Field(string label)
    {
        var text=Panel.GetComponentsInChildren<TextMeshProUGUI>().First(text=>text.text==label);
        return Panel.GetComponentsInChildren<TMP_InputField>().First(input=>input.transform.parent==text.transform.parent && Mathf.Abs(input.GetComponent<RectTransform>().anchoredPosition.y-text.rectTransform.anchoredPosition.y)<.1f);
    }
    static IEnumerator Checks()
    {
        bool blockedBefore=GameplayInputBlocker.IsBlocked;
        GpsRuntimePanel.Close(); GpsRuntimePanel.Open(); yield return null;
        Check(Panel && GpsRuntimePanel.IsOpen && GameplayInputBlocker.IsBlocked,"GPS opens and blocks gameplay input");
        Check(Panel.GetComponentsInChildren<TMP_InputField>().Length==0,"Player sections initially collapsed");
        Button("+ Abbauen").onClick.Invoke(); yield return null;
        var speed=Field("Basis-Abbaugeschwindigkeit"); double previous=GameplaySettings.BaseDiggingSpeed;
        speed.SetTextWithoutNotify((previous+.25).ToString(System.Globalization.CultureInfo.InvariantCulture));speed.onEndEdit.Invoke(speed.text);
        Check(Mathf.Approximately(GameplaySettings.BaseDiggingSpeed,(float)(previous+.25)),"Real runtime input updates shared digging speed");
        GpsRuntimePanel.Close();Check(GameplayInputBlocker.IsBlocked==blockedBefore,"GPS closes and releases its input block");
        Number(typeof(MapGenerator),"mapWidth",64);
        RunNavigation.NewGame("GPS-Testlauf");yield return Settled();
        var map=Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.IsGenerated && map.GeneratedWidth==64,"New map uses pending GPS width before generation");
        CameraCheck();
        int width=map.mapWidth,seed=map.ActiveSeed;
        GpsRuntimePanel.Open(); Panel.SelectTab("Map");yield return null;
        Number(typeof(MapGenerator),"mapWidth",96);
        Check(map.mapWidth==width && map.GeneratedWidth==width && map.ActiveSeed==seed,"Editing generation settings leaves current world intact");
        int originalLayerStart=map.layers[1].startDepth;
        var mapKey=Record(typeof(MapGenerator)).key;var layers=GpsSettings.GetValue(mapKey,"layers");
        layers.children[1].children.Find(field=>field.name=="startDepth").number=originalLayerStart+1;
        Check(GpsSettings.SetValue(mapKey,layers,out var layerError),"Pending layer boundary changes: "+layerError);
        Check(map.layers[1].startDepth==originalLayerStart,"Pending layer boundaries leave active world unchanged");
        var generation=GpsSettings.CaptureGeneration(map);
        Check(GpsSettings.ValidateGeneration(generation,64,map.GeneratedHeight,out var snapshotError),"Active generation snapshot validates: "+snapshotError);
        generation.First(field=>field.name=="layers").children[0].children.Find(field=>field.name=="startDepth").kind=GpsValueKind.Text;
        Check(!GpsSettings.ValidateGeneration(generation,64,map.GeneratedHeight,out _),"Malformed saved generation snapshot is rejected before loading");
        var oldTorch=map.torchBrightness;Number(typeof(MapGenerator),"torchBrightness",.7);
        Check(Mathf.Approximately(map.torchBrightness,.7f),"Torch setting applies live");
        var oldFollow=Object.FindFirstObjectByType<CameraFollow>().smoothSpeed;Number(typeof(CameraFollow),"smoothSpeed",oldFollow+1);
        Check(Mathf.Approximately(Object.FindFirstObjectByType<CameraFollow>().smoothSpeed,oldFollow+1),"Camera setting applies live");
        foreach(string tab in GpsRuntimePanel.TabLabels)
        { Panel.SelectTab(tab);yield return null;Check(Panel.GetComponentsInChildren<Button>().Any(button=>Caption(button)?.StartsWith("+ ")==true),"Runtime tab renders "+tab); }
        Panel.SelectTab("UI"); Button("+ UI Map").onClick.Invoke();yield return null;
        var alpha=Field("Panel-Alpha");alpha.SetTextWithoutNotify("0.62");alpha.onEndEdit.Invoke("0.62");
        Check(Mathf.Approximately(Object.FindFirstObjectByType<PlayerMapDiscovery>().panelAlpha,.62f),"Map-alpha input uses shared normal field");
        Panel.SelectTab("Testeinstellungen");Button("+ Testmodus").onClick.Invoke();yield return null;
        Check(Panel.GetComponentsInChildren<TextMeshProUGUI>().Any(text=>text.text=="God Mode"),"Test controls exist in runtime tab");
        Panel.SelectTab("Audio");Button("+ Einzelclips").onClick.Invoke();yield return null;
        Check(Panel.GetComponentsInChildren<Button>().Any(button=>Caption(button)?.Contains("1")!=true && Caption(button)?.StartsWith("+ ")==true),"Audio clips are available");
        GpsRuntimePanel.Close();
        var cell=new Vector3Int(3,-5,0); Check(map.RemoveBlock(cell),"Isolated terrain cell removed");
        bool saved=false;string saveError=null;var owner=Object.FindFirstObjectByType<RunPauseMenu>();
        yield return GameSaveSystem.Save(1,owner,(ok,error)=>{saved=ok;saveError=error;});Check(saved,"Isolated world save: "+saveError);
        Check(RunNavigation.LoadGame(1,out var loadError),"Load isolated saved world: "+loadError);yield return Settled();
        map=Object.FindFirstObjectByType<MapGenerator>();
        Check(map.IsGenerated && map.GeneratedWidth==64 && !map.Terrain.HasTile(cell),"Loading preserves saved dimensions and mined terrain while GPS width is 96");
        Check(GpsSettings.GetValue(Record(typeof(MapGenerator)).key,"mapWidth").number==96,"Saved world does not overwrite GPS generation configuration");
        Check(map.layers[1].startDepth==originalLayerStart,"Loading restores saved layer boundaries");
        Check(GpsSettings.GetValue(mapKey,"layers").children[1].children.Find(field=>field.name=="startDepth").number==originalLayerStart+1,"Loading retains pending GPS layer boundaries for the next world");
        CameraCheck();
        GpsRuntimePanel.Open();Button("Speichern").onClick.Invoke();
        Check(!GpsSettings.HasUnsavedChanges,"Runtime Save commits the unified profile");
        var committed=JsonUtility.FromJson<GpsDocument>(GpsSettings.Profile.documentJson);
        Check(committed.records.First(r=>r.type==typeof(CameraFollow).AssemblyQualifiedName).fields.Find(n=>n.name=="smoothSpeed").number==oldFollow+1,"Runtime camera setting saved in Editor profile");
        File.WriteAllText("Temp/GpsRuntimeCommitted.json",GpsSettings.Profile.documentJson);
        GpsRuntimePanel.Close();RunNavigation.MainMenu();yield return Settled();
        Check(MainMenuController.IsVisible,"Returned to homescreen after isolated checks");
    }
    static void CameraCheck()
    {
        var player=Object.FindFirstObjectByType<PlayerMovement>();var camera=Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,"Fresh player camera follows and clamps");
    }
    static void Finish(string message)
    {
        EditorApplication.update-=Tick;GpsRuntimePanel.Close();GameSaveSystem.TestDirectory=oldDirectory;
        File.WriteAllText("Temp/GpsRuntimeChecks.txt",message+"\n"+string.Join("\n",checks));Debug.Log(message);
        if(message.StartsWith("FAIL") && !string.IsNullOrEmpty(baseline))
        { GpsSettings.UseProfile(GpsSettings.Profile,JsonUtility.FromJson<GpsDocument>(baseline));GpsSettings.Save(out _); }
    }
    public static object AfterStop()
    {
        if(EditorApplication.isPlaying)return "Requires Edit Mode.";
        string committed=File.ReadAllText("Temp/GpsRuntimeCommitted.json"),original=File.ReadAllText("Temp/GpsRuntimeBaseline.json");
        var expected=JsonUtility.FromJson<GpsDocument>(committed);
        var key=expected.records.First(r=>r.type==typeof(CameraFollow).AssemblyQualifiedName).key;
        double value=expected.records.First(r=>r.key==key).fields.Find(n=>n.name=="smoothSpeed").number;
        bool persisted=GpsSettings.GetValue(key,"smoothSpeed").number==value;
        var profile=GpsSettings.Profile;GpsSettings.UseProfile(profile,JsonUtility.FromJson<GpsDocument>(original));
        bool restored=GpsSettings.Save(out var error);GpsSettings.ApplyAssets();GpsSettings.ApplyLoadedComponents();
        File.AppendAllText("Temp/GpsRuntimeChecks.txt","\n"+(persisted ? "PASS" : "FAIL")+": runtime save survives Play stop\n"+(restored ? "PASS" : "FAIL")+": original GPS restored "+error);
        return new{persisted,restored,error};
    }
}
