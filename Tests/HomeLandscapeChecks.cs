using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static class HomeLandscapeChecks {
 public static object Main() {
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Requires free MainMenu");
  var go=new GameObject("Home landscape checks");Object.DontDestroyOnLoad(go);go.AddComponent<HomeLandscapeProbe>();return "Started";
 }
}
public sealed class HomeLandscapeProbe:MonoBehaviour {
 const string Report="Temp/HomeLandscapeChecks.txt";
 const BindingFlags Hidden=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic;
 string save,meta,gps;int checks;bool clean;
 void Check(bool ok,string name){if(!ok)throw new Exception(name);File.AppendAllText(Report,"PASS "+(++checks)+" "+name+"\n");}
 void Fixture(int slot,int layer,int deepest,bool legacy=false) {
  var summary=new GameSaveSystem.Summary {slot=slot,name="Home Test "+slot,depth=0,homeLayer=legacy ? 0 : layer,savedUtc=DateTime.UtcNow.Ticks,lastOpenedUtc=DateTime.UtcNow.AddMinutes(-slot).Ticks};
  byte[] payload;
  using(var packed=new MemoryStream()) {
   using(var zip=new GZipStream(packed,System.IO.Compression.CompressionLevel.Fastest,true))using(var w=new BinaryWriter(zip))
    w.Write(JsonUtility.ToJson(new RunSaveState {progression=new MetaRunState {maxDepth=deepest},generationSettings=GpsSettings.Document.records.Find(r=>r.type==typeof(MapGenerator).AssemblyQualifiedName).fields.Where(v=>v.name=="layers").ToArray()}));
   payload=packed.ToArray();
  }
  typeof(GameSaveSystem).GetMethod("WriteEnvelopeAtomic",Hidden).Invoke(null,new object[]{GameSaveSystem.SlotPath(slot),JsonUtility.ToJson(summary),payload,true});
 }
 IEnumerator Start() {
  save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;gps=JsonUtility.ToJson(GpsSettings.Tests);
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/HomeLandscape-"+DateTime.UtcNow.Ticks);MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Meta");
  File.WriteAllText(Report,"");var routine=Run();
  while(true){bool more=false;object next=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){File.AppendAllText(Report,"FAILED "+e+"\n");break;}if(!more)break;yield return next;}
  if(!MainMenuController.IsVisible && !RunNavigation.IsTransitioning)RunNavigation.MainMenu();
  yield return Ready();Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Timeout");yield return null;}yield return null;yield return null;}
 IEnumerator Shot(string name){yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Design/Tiefenhall/SurfaceBackgroundPrototypes/Stylized-v2/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
 IEnumerator Run() {
  var layers=GpsSettings.Document.records.Find(r=>r.type==typeof(MapGenerator).AssemblyQualifiedName).fields.Find(v=>v.name=="layers").children;
  int third=(int)layers[2].children.Find(v=>v.name=="startDepth").number;
  Check(HomeLandscape.LayerAtDepth(0)==1 && HomeLandscape.LayerAtDepth(third)==3,"Actual GPS layer boundaries");
  Fixture(1,1,0);Fixture(2,3,third);Fixture(3,3,third,true);
  Check(GameSaveSystem.GetHomeLayer(3)==3,"Legacy save uses deepest reached depth despite surface position");
  Check(!GameSaveSystem.HasPendingLoad && MetaProgression.CurrentRun==null,"Legacy preview never prepares or starts a run");
  var menu=Object.FindFirstObjectByType<MainMenuController>();menu.SelectSave(1);yield return new WaitForSecondsRealtime(1);
  var visual=Object.FindFirstObjectByType<HomeCaveVisual>();
  Check(visual.Layer==1 && !visual.WaterEffectsEnabled,"L1 has no water effects");
  Check(visual.GetComponentsInChildren<RawImage>().Any(i=>i.texture==Resources.Load<Texture2D>("Homescreen/Layer1")&&i.color.a==1),"Approved L1 art visible");
  Check(HomeLandscape.MeadowClip && HomeLandscape.MeadowClip.name=="MeadowAmbience","Original meadow clip reused");
  var audio=Object.FindFirstObjectByType<LoadingAudio>();
  var meadow=(AudioSource[])typeof(LoadingAudio).GetField("meadowSources",Hidden).GetValue(audio);
  var music=(AudioSource[])typeof(LoadingAudio).GetField("homeSources",Hidden).GetValue(audio);
  Check(meadow.Any(s=>s&&s.isPlaying&&s.volume>0),"L1 meadow plays with tuned ambience volume");
  Check(music.All(s=>s&&s.clip.name=="HomescreenAmbience"),"Homescreen music clips unchanged");
  var oldMusic=music[0];int sample=oldMusic.timeSamples;
  LoadingAudio.PlayHomeWaterdrop();
  Check(!audio.GetComponents<AudioSource>().Any(s=>s.isPlaying&&s.clip&&s.clip.name.StartsWith("HomeWaterdrop")),"L1 rejects waterdrop sound");
  yield return Shot("Home-L1-implemented");
  var panel=SaveSlotPanel.Show(GameObject.Find("ScreenCanvas").transform,false);yield return null;
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Slot 2").onClick.Invoke();
  yield return new WaitForSecondsRealtime(1);
  Check(menu.SelectedSaveSlot==2 && visual.Layer==3,"Real save selection drives L3");
  Check(!LoadingProgress.Active && !RunNavigation.IsTransitioning,"Selecting changes background without loading");
  Check(visual.WaterEffectsEnabled && LoadingAudio.HomeLayer==3,"L3 enables visual and audio drop gate");
  Check(meadow.All(s=>!s.isPlaying&&s.volume==0),"Meadow stops after L3 crossfade");
  Check(music[0]==oldMusic && oldMusic.timeSamples>sample,"Changing save does not restart music");
  LoadingAudio.PlayHomeWaterdrop();Check(audio.GetComponents<AudioSource>().Any(s=>s.isPlaying&&s.clip&&s.clip.name.StartsWith("HomeWaterdrop")),"L3 drop sound plays");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Back").onClick.Invoke();yield return null;
  yield return Shot("Home-L3-implemented");
  menu.SelectSave(1);yield return new WaitForSecondsRealtime(.15f);menu.SelectSave(2);yield return new WaitForSecondsRealtime(.15f);menu.SelectSave(1);
  yield return new WaitForSecondsRealtime(1);
  Check(visual.Layer==1 && meadow.Any(s=>s.isPlaying&&s.volume>0),"Rapid switching settles on selected L1 and meadow");
  Check(!audio.GetComponents<AudioSource>().Any(s=>s.isPlaying&&s.clip&&s.clip.name.StartsWith("HomeWaterdrop")),"Changing to L1 stops outstanding waterdrop");
  menu.SelectSave(3);Check(visual.Layer==3,"Legacy selected save drives L3");
  Check(GameSaveSystem.RenameSlot(3,"Preview renamed",out _) && menu.SelectedSaveSlot==3,"Rename retains logical selection");
  Check(GameSaveSystem.DeleteSlot(3,out _) && menu.SelectedSaveSlot==1,"Deleting selection falls back to newest save");
  visual.SetLayer(2);Check(!visual.WaterEffectsEnabled && LoadingAudio.HomeLayer==2,"Unprovided layer fallback never gets L3-specific drops");
  menu.SelectSave(1);
  GpsSettings.Document.tests.testModeDisabled=true;
  RunNavigation.NewGame("Home Landscape Test");yield return Ready();
  var player=Object.FindFirstObjectByType<PlayerMovement>();
  Check(player&&Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh gameplay camera follow and clamp");
  Check(meadow.All(s=>!s.isPlaying),"Homescreen meadow stops during gameplay");
  MetaProgression.RecordDepth(third);bool saved=false;
  yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot,this,(ok,msg)=>saved=ok);
  int slot=GameSaveSystem.ActiveSlot;
  Check(saved && GameSaveSystem.GetSummary(slot).homeLayer==3,"Saving on surface retains deepest L3 in summary");
  Check(RunNavigation.LoadGame(slot,out _),"Fresh actual save loads");yield return Ready();
  player=Object.FindFirstObjectByType<PlayerMovement>();
  Check(Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Reload camera follows restored player");
  RunNavigation.MainMenu();yield return Ready();yield return new WaitForSecondsRealtime(1);
  menu=Object.FindFirstObjectByType<MainMenuController>();visual=Object.FindFirstObjectByType<HomeCaveVisual>();
  Check(menu.SelectedSaveSlot==slot && visual.Layer==3,"Returning home chooses newest saved run and deepest layer");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;GameSaveSystem.TestDirectory=save;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;JsonUtility.FromJsonOverwrite(gps,GpsSettings.Document.tests);Object.FindFirstObjectByType<MainMenuController>()?.SendMessage("Refresh");Object.Destroy(gameObject);}
 void OnDestroy(){Cleanup();}
}
