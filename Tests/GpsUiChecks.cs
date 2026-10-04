using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class GpsUiChecks
{
    static readonly List<string> checks=new();
    static readonly Stack<IEnumerator> work=new();
    static string previousDirectory;
    static double started;
    static int frame;
    static GpsRuntimePanel Panel=>Object.FindFirstObjectByType<GpsRuntimePanel>(FindObjectsInactive.Include);
    static string Caption(Button button)=>button.GetComponentInChildren<TextMeshProUGUI>()?.text;
    static Button Button(string text)=>Panel.GetComponentsInChildren<Button>().First(button=>Caption(button)==text);
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);checks.Add(text);}
    public static object Main()
    {
        if(!EditorApplication.isPlaying || !MainMenuController.IsVisible)return "Requires fresh Play from MainMenu.";
        previousDirectory=GameSaveSystem.TestDirectory;GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/GpsFinalUiSaves-"+DateTime.UtcNow.Ticks);
        frame=-1;started=EditorApplication.timeSinceStartup;checks.Clear();work.Clear();work.Push(Checks());EditorApplication.update+=Tick;
        return "Final GPS UI and original reload options checks running.";
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup-started>180){Finish("FAIL: interrupted or timeout");return;}
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{while(work.Count>0){var next=work.Peek();if(!next.MoveNext()){work.Pop();continue;}if(next.Current is IEnumerator nested){work.Push(nested);continue;}return;}Finish("PASS ("+checks.Count+")");}
        catch(Exception ex){Finish("FAIL: "+ex);}
    }
    static IEnumerator Settled(){while(LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)yield return null;yield return null;}
    static IEnumerator DropdownFade(){float end=Time.realtimeSinceStartup+.3f;while(Time.realtimeSinceStartup<end)yield return null;}
    static IEnumerator Checks()
    {
        Check(EditorSettings.enterPlayModeOptionsEnabled && (int)EditorSettings.enterPlayModeOptions==3,"Original Editor reload options restored");
        var record=GpsSettings.Document.records.First(record=>record.type==typeof(MapGenerator).AssemblyQualifiedName);
        var width=GpsSettings.GetValue(record.key,"mapWidth");width.number=64;Check(GpsSettings.SetValue(record.key,width,out _),"Temporary small map configured without saving GPS");
        RunNavigation.NewGame("GPS-UI-Test");yield return Settled();
        Check(GpsSettings.HasUnsavedChanges,"Scene initialization does not silently save pending GPS edits");
        var camera=Camera.main;var player=Object.FindFirstObjectByType<PlayerMovement>();
        Check(camera && player && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,"Fresh gameplay camera follows and clamps with original reload options");
        GpsRuntimePanel.Open();yield return null;
        Check(Panel.GetComponentsInChildren<TMP_InputField>().Length==0,"Player sections default collapsed");
        ScreenCapture.CaptureScreenshot("Temp/GpsRuntime-final.png");yield return null;yield return null;
        Panel.SelectTab("Erzverteilung");Button("+ Globale Verteilung").onClick.Invoke();yield return null;
        var graphic=Panel.GetComponentInChildren<GpsCurveGraphic>();var dropdown=Panel.GetComponentInChildren<TMP_Dropdown>();
        Check(graphic && dropdown && dropdown.options.Count>=2,"Curve graph and point selector render");
        Check(graphic.canvasRenderer,"Curve graph has a CanvasRenderer for masking and rendering");
        var curve=GpsSettings.GetValue(record.key,"oreDensityCurve");
        var inputs=Panel.GetComponentsInChildren<TMP_InputField>();
        var yInput=inputs.First(input=>Mathf.Approximately(input.GetComponent<RectTransform>().anchoredPosition.x,584));
        dropdown.value=1;yield return null;
        Check(yInput.text==curve.curve.keys[1].value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),"Selecting a curve point refreshes the numeric value");
        yInput.SetTextWithoutNotify("0.75");yInput.onEndEdit.Invoke("0.75");yield return null;
        Check(Mathf.Approximately(GpsSettings.GetValue(record.key,"oreDensityCurve").curve.keys[1].value,.75f),"Numeric Y edits update the shared curve");
        Check(Object.FindFirstObjectByType<MapGenerator>().oreDensityCurve.keys[1].value==curve.curve.keys[1].value,"Curve edits wait for next generation");
        dropdown.Show();yield return null;
        Check(Panel.GetComponentsInChildren<Toggle>().Length>=2,"Dropdown creates real selectable entries");
        yield return DropdownFade();ScreenCapture.CaptureScreenshot("Temp/GpsRuntime-dropdown.png");yield return null;yield return null;dropdown.Hide();yield return DropdownFade();
        yInput.SetTextWithoutNotify(curve.curve.keys[1].value.ToString(System.Globalization.CultureInfo.InvariantCulture));yInput.onEndEdit.Invoke(yInput.text);yield return null;
        ScreenCapture.CaptureScreenshot("Temp/GpsRuntime-curve.png");yield return null;yield return null;
        GpsRuntimePanel.Close();RunNavigation.MainMenu();yield return Settled();
        Check(MainMenuController.IsVisible,"Final isolated UI check returns home");
    }
    static void Finish(string message)
    {
        EditorApplication.update-=Tick;GpsRuntimePanel.Close();GameSaveSystem.TestDirectory=previousDirectory;
        File.WriteAllText("Temp/GpsUiChecks.txt",message+"\n"+string.Join("\n",checks));Debug.Log(message);
    }
}
