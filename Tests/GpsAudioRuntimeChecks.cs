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

public static class GpsAudioRuntimeChecks
{
    public static object Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible)throw new Exception("Fresh home Play required");
        var root=new GameObject("GPS Audio Checks");Object.DontDestroyOnLoad(root);root.AddComponent<GpsAudioRuntimeProbe>();return "Running audio UI/playback and fresh camera checks";
    }
}
public sealed class GpsAudioRuntimeProbe:MonoBehaviour
{
    List<string> checks=new();string before,profileJson,previousDirectory,previousMetaDirectory;bool restored;
    GpsRuntimePanel Panel=>Object.FindFirstObjectByType<GpsRuntimePanel>(FindObjectsInactive.Include);
    Button Button(string text)=>Panel.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<TextMeshProUGUI>()?.text==text);
    void Check(bool condition,string text){if(!condition)throw new Exception(text);checks.Add(text);File.WriteAllText("Temp/GpsAudioRuntimeChecks.txt","RUNNING\n"+string.Join("\n",checks));}
    IEnumerator Start()
    {
        before=JsonUtility.ToJson(GpsSettings.Document);profileJson=GpsSettings.Profile.documentJson;previousDirectory=GameSaveSystem.TestDirectory;previousMetaDirectory=MetaProgression.TestDirectory;
        GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/GpsAudioSaves-"+DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"MetaProfile");
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;
            try { var step=stack.Peek();if(!step.MoveNext()){stack.Pop();continue;}next=step.Current; }
            catch(Exception ex){Finish("FAIL: "+ex);yield break;}
            if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Finish("PASS ("+checks.Count+")");
    }
    IEnumerator Settled(){while(LoadingProgress.Active||RunNavigation.IsTransitioning||GameSaveSystem.IsBusy)yield return null;yield return null;}
    IEnumerator Checks()
    {
        var profile=GpsSettings.Profile;var home=Resources.Load<AudioClip>("Audio/HomescreenAmbience");string homeKey=profile.Key(home);
        Check(!AudioManager.Instance,"Homescreen tuning works without gameplay AudioManager");
        GpsAudio.Change(homeKey,"volume",0,out _);GpsAudio.Change(homeKey,"pitch",1.25,out _);GpsAudio.Change(homeKey,"pitchSpread",0,out _);
        LoadingAudio.StartHome();yield return null;yield return null;
        var homeSources=Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(s=>s.clip==home).ToArray();
        Check(homeSources.Length==2 && homeSources.All(s=>s.volume==0 && Mathf.Approximately(s.pitch,1.25f)),"Real homescreen crossfade sources use catalog volume and pitch");
        var click=Resources.Load<AudioClip>("Audio/HomeClick");GpsAudio.Change(profile.Key(click),"pitch",1.4,out _);GpsAudio.Change(profile.Key(click),"pitchSpread",0,out _);HomeClickAudio.Play();
        var clickSource=GameObject.Find("HomeClickAudio").GetComponent<AudioSource>();Check(Mathf.Approximately(clickSource.pitch,1.4f),"Real home click uses tuning before gameplay");
        GpsRuntimePanel.Open();Panel.SelectTab("Audio");yield return null;
        Check(Panel.GetComponentsInChildren<TMP_InputField>().Length==GpsSchema.SectionFields("Audio","Gesamtpegel").Count()+1,"Audio categories default collapsed; master and search visible");
        var search=Panel.GetComponentsInChildren<TMP_InputField>().First(i=>i.GetComponent<RectTransform>().anchoredPosition.x==250);
        search.SetTextWithoutNotify("Lade-Spitzhacke");search.onEndEdit.Invoke(search.text);yield return null;
        Button("+ Lade-Spitzhacke").onClick.Invoke();yield return null;
        var fields=Panel.GetComponentsInChildren<TMP_InputField>().Where(i=>i.GetComponent<RectTransform>().anchoredPosition.x>600 && i.GetComponent<RectTransform>().anchoredPosition.y < -120).OrderByDescending(i=>i.GetComponent<RectTransform>().anchoredPosition.y).ToArray();
        Check(fields.Length==4,"Search finds exact clip with all four controls");
        foreach(var pair in new[]{(0,"0"),(1,"25"),(2,"150"),(3,"0")}){fields[pair.Item1].SetTextWithoutNotify(pair.Item2);fields[pair.Item1].onEndEdit.Invoke(pair.Item2);}
        var clip=Resources.Load<AudioClip>("Audio/LoadingPickaxeHit");var tune=GpsAudio.Tuning(clip);
        Check(tune.volume==0 && tune.volumeSpread==.25f && tune.pitch==1.5f && tune.pitchSpread==0,"UI edits update four shared values without overwriting each other");
        Button("Test").onClick.Invoke();yield return null;
        var preview=Object.FindFirstObjectByType<GpsAudioPreview>().GetComponent<AudioSource>();
        Check(preview.clip==clip && preview.volume==0 && Mathf.Approximately(preview.pitch,1.5f),"Test button uses edited volume and pitch");
        Button("Stopp").onClick.Invoke();Check(!preview.isPlaying,"Stop button ends preview");
        ScreenCapture.CaptureScreenshot("Temp/GpsAudio-runtime.png");yield return null;yield return null;
        GpsRuntimePanel.Close();GpsSettings.UseProfile(profile,JsonUtility.FromJson<GpsDocument>(before));GpsSettings.ApplyAssets();
        var record=GpsSettings.Document.records.Find(r=>r.type==typeof(MapGenerator).AssemblyQualifiedName);
        var width=GpsSettings.GetValue(record.key,"mapWidth");width.number=64;GpsSettings.SetValue(record.key,width,out _);
        RunNavigation.NewGame("GPS Audio Test");yield return Settled();
        var camera=Camera.main;var player=Object.FindFirstObjectByType<PlayerMovement>();
        Check(camera && player && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,"Fresh gameplay camera follows player with border clamp");
        var audio=AudioManager.Instance;Check(audio,"Gameplay AudioManager available");
        var clips=GpsSettings.GetValue(GpsAudio.Record.key,"clips");foreach(var entry in clips.children)entry.children.Find(f=>f.name=="volume").number=0;GpsSettings.SetValue(GpsAudio.Record.key,clips,out _);
        audio.PlayClip(click,1,0);audio.PlayLayerMiningClip(2,SoundType.DigDeepStone,false,0);audio.PlayTorchSound(true);audio.PlayMedkitUseSound();audio.PlayGameOverMusic();audio.SetLowHealthHeartbeat(true);
        foreach(var sound in Enum.GetValues(typeof(SoundType)).Cast<SoundType>().Where(t=>audio.GetSoundClip(t,0)))audio.Play(sound);
        yield return null;
        var sources=audio.GetComponents<AudioSource>().Where(s=>s.clip && s.isPlaying).ToArray();
        Check(sources.Length>3 && sources.All(s=>s.volume==0),"Real gameplay effect, mining, torch, UI and health sources respect clip mute");
        Check(Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(s=>s.clip && s.isPlaying && !(s.GetComponent<GpsAudioPreview>())).All(s=>s.volume==0),"Running environment sources also respect clip mute");
        audio.StopGameOverMusic();audio.SetLowHealthHeartbeat(false,true);
        GpsSettings.UseProfile(profile,JsonUtility.FromJson<GpsDocument>(before));GpsSettings.ApplyAssets();
        RunNavigation.MainMenu();yield return Settled();Check(MainMenuController.IsVisible,"Return to homescreen with isolated test saves");
    }
    void Finish(string result){Restore();File.WriteAllText("Temp/GpsAudioRuntimeChecks.txt",result+"\n"+string.Join("\n",checks));Debug.Log(result);Destroy(gameObject);}
    void Restore(){if(restored)return;restored=true;GpsRuntimePanel.Close();GpsAudioPreview.Stop();GameSaveSystem.TestDirectory=previousDirectory;MetaProgression.TestDirectory=previousMetaDirectory;if(before!=null){var p=GpsSettings.Profile;p.documentJson=profileJson;GpsSettings.UseProfile(p,JsonUtility.FromJson<GpsDocument>(before));GpsSettings.ApplyAssets();}}
    void OnDestroy(){Restore();}
}

