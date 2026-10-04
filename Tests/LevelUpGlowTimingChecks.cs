using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class LevelUpGlowTimingChecks {
 public static object Main(){
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Start in free MainMenu");
  var go=new GameObject("Levelup Glow Timing Probe");Object.DontDestroyOnLoad(go);go.AddComponent<LevelUpGlowTimingProbe>();return "Started";
 }
}
public sealed class LevelUpGlowTimingProbe:MonoBehaviour {
 const string Report="Temp/LevelUpGlowTimingChecks.txt";
 string save,meta,committed;GpsDocument original;GpsProfile gpsProfile;bool dirty,clean;int checks,power,comfort;long xp;
 GameplayLevelUpPresentation view;CanvasGroup group;AudioSource sound;
 void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Report,"PASS "+(++checks)+" "+message+"\n");}
 void Offset(float value){var node=GpsSettings.GetValue("preferences","levelUpAnimationOffsetSeconds");node.number=value;Check(GpsSettings.SetValue("preferences",node,out var error),"Offset accepted "+value+" "+error);}
 IEnumerator Start(){
  save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;original=GpsSettings.Document;gpsProfile=GpsSettings.Profile;dirty=GpsSettings.HasUnsavedChanges;committed=GpsSettings.CommittedJson;
  GpsSettings.UseProfile(gpsProfile,JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(original)));
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/LevelupGlow-"+DateTime.UtcNow.Ticks);MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Profile");GpsSettings.Document.tests.testModeDisabled=true;File.WriteAllText(Report,"");
  var routine=Run();while(true){bool more=false;object next=null;Exception error=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){error=e;}
   if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}if(!more)break;yield return next;}
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;GpsRuntimePanel.Close();
  if(!MainMenuController.IsVisible&&!RunNavigation.IsTransitioning)RunNavigation.MainMenu();yield return Ready();Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Loading timeout");yield return null;}yield return null;yield return null;}
 void Preview(){GpsTestActions.PreviewLevelUp();}
 IEnumerator Run(){
  var section=GpsSchema.Sections.Single(s=>s.title=="Level-up"&&s.tab=="UI");var spec=section.fields.Single();Check(spec.min==-5&&spec.max==5,"Shared GPS schema exposes signed seconds");
  var invalid=GpsSettings.GetValue("preferences",spec.name);invalid.number=6;Check(!GpsSettings.SetValue("preferences",invalid,out _),"Out-of-range offset rejected");invalid.number=double.NaN;Check(!GpsSettings.SetValue("preferences",invalid,out _),"Nonfinite offset rejected");
  MetaProgression.BeginRun("glow-seed",new MetaRunState{runId="glow-seed",progressXp=MetaProgressionCatalog.XpForLevel(24),ended=true});MetaProgression.EndRun();
  RunNavigation.NewGame("Levelup-Glow-Test");yield return Ready();
  var player=Object.FindFirstObjectByType<PlayerMovement>();Check(player&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh game camera follows and clamps");
  view=GameplayLevelUpPresentation.Instance;group=view.GetComponent<CanvasGroup>();sound=view.GetComponent<AudioSource>();xp=MetaProgression.TotalXp;power=MetaProgression.AvailablePowerPoints;comfort=MetaProgression.AvailableComfortPoints;
  GpsRuntimePanel.Open();var panel=Object.FindFirstObjectByType<GpsRuntimePanel>();panel.SelectTab("UI");panel.GetComponentsInChildren<Button>().Single(b=>b.name=="+ Level-up").onClick.Invoke();yield return null;
  var input=panel.GetComponentsInChildren<TMP_InputField>().Single();input.onEndEdit.Invoke("0.8");yield return null;
  Check(Mathf.Abs(GpsSettings.Preferences.levelUpAnimationOffsetSeconds-.8f)<.001f,"Actual GPS seconds field updates setting");GpsRuntimePanel.Close();Preview();yield return new WaitForSecondsRealtime(.2f);
  Check(group.alpha==0&&sound.isPlaying&&view.Elapsed<0,"Positive offset starts audio before hidden animation");
  float age=view.Elapsed;GameplayInputBlocker.SetBlocked(this,true);Time.timeScale=0;yield return new WaitForSecondsRealtime(.25f);
  Check(Mathf.Abs(view.Elapsed-age)<.03f&&!sound.isPlaying,"Pause freezes positive delay and audio");GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;yield return new WaitForSecondsRealtime(.8f);
  Check(group.alpha>.95f&&sound.isPlaying,"Animation appears after positive delay");
  var lights=view.GetComponentsInChildren<LevelUpGlowGraphic>();Check(lights.Length==2&&lights.All(g=>g.material.shader.name=="UI/Tiefenhall/LevelUpGlow"&&g.material.shader.isSupported),"Both glow layers use supported additive shader");
  Check(lights.All(g=>g.canvasRenderer.GetMesh()!=null&&g.canvasRenderer.GetMesh().vertexCount>0),"Glow generates live orbit and halo geometry");
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Assets/Design/Tiefenhall/LevelUpV2/Glow-implemented.png");yield return null;
  Offset(-.8f);Preview();yield return new WaitForSecondsRealtime(.25f);int plays=view.AudioPlayCount;
  Check(group.alpha>.9f&&!sound.isPlaying,"Negative offset starts animation before sound");
  GameplayInputBlocker.SetBlocked(this,true);Time.timeScale=0;yield return new WaitForSecondsRealtime(.8f);Check(!sound.isPlaying&&view.AudioPlayCount==plays,"Paused deferred audio never starts early");
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;yield return new WaitForSecondsRealtime(.75f);Check(sound.isPlaying&&view.AudioPlayCount==plays+1,"Deferred audio begins once after resume");
  Offset(-4f);Preview();yield return new WaitForSecondsRealtime(3.1f);Check(!view.IsShowing&&!sound.isPlaying,"Long negative delay permits visual to finish before audio");yield return new WaitForSecondsRealtime(1.2f);Check(sound.isPlaying,"Audio still begins after visual finished");
  Offset(4f);Preview();yield return new WaitForSecondsRealtime(3.2f);Check(view.IsShowing&&group.alpha==0&&sound.isPlaying,"Long positive delay keeps full animation pending");yield return new WaitForSecondsRealtime(1.2f);Check(group.alpha>.9f&&view.IsShowing,"Long positive delay reveals animation at configured time");yield return new WaitForSecondsRealtime(2.1f);Check(view.IsShowing,"Delayed visual retains full duration beyond original six-second cap");
  Offset(0);Preview();yield return new WaitForSecondsRealtime(.25f);Check(group.alpha>.9f&&sound.isPlaying,"Zero offset starts visual and sound together");
  var roundtrip=JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(GpsSettings.Document));Check(roundtrip.preferences.levelUpAnimationOffsetSeconds==0,"Offset survives GPS document serialization");
  Check(MetaProgression.TotalXp==xp&&MetaProgression.AvailablePowerPoints==power&&MetaProgression.AvailableComfortPoints==comfort,"Timing and preview never alter XP or points");
  RunNavigation.MainMenu();yield return Ready();Check(!view.IsShowing&&!sound.isPlaying&&group.alpha==0,"Scene change cancels delayed presentation");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;GpsSettings.UseProfile(gpsProfile,original);typeof(GpsSettings).GetField("dirty",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,dirty);typeof(GpsSettings).GetField("savedJson",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,committed);GpsSettings.NotifyConfigurationChanged();Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
