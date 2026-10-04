using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class GameplayLevelUpChecks {
 public static object Main(){
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Start in free MainMenu");
  var go=new GameObject("Level Up Probe");Object.DontDestroyOnLoad(go);go.AddComponent<GameplayLevelUpProbe>();return "Started";
 }
}
public sealed class GameplayLevelUpProbe:MonoBehaviour {
 const string Report="Temp/GameplayLevelUpChecks.txt";
 string save,meta,gps;bool clean;int checks,sequence;float oldScale;
 GameplayLevelUpPresentation view;
 CanvasGroup group;AudioSource sound;
 void Check(bool condition,string name){if(!condition)throw new Exception(name);File.AppendAllText(Report,"PASS "+(++checks)+" "+name+"\n");}
 IEnumerator Start(){
  save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;gps=JsonUtility.ToJson(GpsSettings.Tests);oldScale=Time.timeScale;
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/LevelUp-"+DateTime.UtcNow.Ticks);MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Profile");
  GpsSettings.Document.tests.testModeDisabled=true;File.WriteAllText(Report,"");
  var routine=Run();while(true){bool more=false;object next=null;Exception error=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){error=e;}
   if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}if(!more)break;yield return next;}
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;
  if(!MainMenuController.IsVisible && !RunNavigation.IsTransitioning)RunNavigation.MainMenu();
  float end=Time.realtimeSinceStartup+120;while((LoadingProgress.Active||RunNavigation.IsTransitioning)&&Time.realtimeSinceStartup<end)yield return null;
  Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Loading timeout");yield return null;}yield return null;yield return null;}
 void AwardTo(int level){
  var run=MetaProgression.CurrentRun;run.settings.efficiencyMaxBonus=0;run.settings.xpMultiplier=1;run.settings.discoveryXp=1;
  run.progressXp+=Math.Max(0,MetaProgressionCatalog.XpForLevel(level)-MetaProgression.TotalXp-1);
  MetaProgression.RecordDiscovery("levelup-check-"+(++sequence));
 }
 TextMeshProUGUI Label(string name)=>view.GetComponentsInChildren<TextMeshProUGUI>().First(t=>t.name==name);
 void CameraCheck(string prefix){var player=Object.FindFirstObjectByType<PlayerMovement>();Check(player&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,prefix+" camera follow/clamp");}
 IEnumerator Run(){
  MetaProgression.BeginRun("levelup-seed",new MetaRunState{runId="levelup-seed",progressXp=MetaProgressionCatalog.XpForLevel(24),ended=true});MetaProgression.EndRun();
  RunNavigation.NewGame("Level-up-Test");yield return Ready();CameraCheck("Fresh game");
  view=GameplayLevelUpPresentation.Instance;Check(view,"Presenter installed automatically");group=view.GetComponent<CanvasGroup>();sound=view.GetComponent<AudioSource>();
  Check(!view.IsShowing&&group.alpha==0&&view.AudioPlayCount==0,"Restored initial level stays silent");
  Check(!group.blocksRaycasts&&!group.interactable&&!view.GetComponent<GraphicRaycaster>()&&view.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"Entire decoration ignores pointer input");
  Check(MetaProgressionRuntime.RewardsAllowed,"Real gameplay reward gate open");
  AwardTo(25);yield return null;yield return new WaitForSecondsRealtime(.35f);
  Check(view.IsShowing&&view.FromLevel==24&&view.ToLevel==25,"Live XP event shows 24 to 25");
  Check(Label("Reward").text=="+1 Powerup-Punkt","Actual power point reward");
  Check(sound.clip&&UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(sound.clip))=="40d79d65a6a40954ca6cb271c41ac456"&&sound.isPlaying&&view.AudioPlayCount==1,"Original audio GUID plays once");
  Check(Time.timeScale==1&&!GameplayInputBlocker.IsBlocked,"Celebration does not pause or block gameplay");
  Check(group.alpha>.99f&&Label("Level").text=="25","Medallion readable at full opacity");
  Check(view.GetComponentsInChildren<Image>().Any(i=>i.sprite&&i.sprite.name=="LevelUpMedallion"),"Generated medallion sprite loaded");
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Assets/Design/Tiefenhall/LevelUpV2/Implemented.png");yield return null;
  float age=view.Elapsed;GameplayInputBlocker.SetBlocked(this,true);Time.timeScale=0;yield return new WaitForSecondsRealtime(.3f);
  Check(group.alpha==0&&Mathf.Abs(view.Elapsed-age)<.03f&&!sound.isPlaying,"Pause hides visual and suspends animation/audio");
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;yield return null;yield return null;
  Check(group.alpha>.9f&&sound.isPlaying,"Resume continues same celebration");
  AwardTo(26);AwardTo(28);yield return null;
  Check(view.FromLevel==24&&view.ToLevel==MetaProgression.Level&&view.ToLevel==28&&view.AudioPlayCount==1,"Rapid level ups merge without overlapping sound");
  Check(Label("Reward").text=="+4 Powerup-Punkte","Merged reward total correct");
  MetaProgression.RecordDiscovery("levelup-check-"+sequence);yield return null;
  Check(view.AudioPlayCount==1&&view.ToLevel==28,"Duplicate discovery does not replay");
  yield return new WaitForSecondsRealtime(3.1f);
  Check(!view.IsShowing&&group.alpha==0,"Visual fades after duration");
  Check(sound.isPlaying,"Long audio tail continues after visual");
  AwardTo(29);yield return new WaitForSecondsRealtime(.4f);
  Check(view.IsShowing&&view.ToLevel==29&&view.AudioPlayCount==2&&view.GetComponents<AudioSource>().Length==1,"New level fades old tail and uses one audio source");
  bool saved=false;yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot,this,(ok,message)=>saved=ok);Check(saved,"Isolated world saves");
  Check(RunNavigation.LoadGame(GameSaveSystem.ActiveSlot,out _),"Real save reload accepted");yield return Ready();CameraCheck("Reload");
  Check(!view.IsShowing&&group.alpha==0&&!sound.isPlaying&&view.AudioPlayCount==2,"Loading saved XP never replays celebration");
  AwardTo(199);yield return new WaitForSecondsRealtime(.3f);Check(view.ToLevel==199,"Large award groups all levels");
  yield return new WaitForSecondsRealtime(3f);AwardTo(205);yield return new WaitForSecondsRealtime(.4f);
  Check(view.FromLevel==199&&view.ToLevel==205&&Label("Reward").text=="+1 Powerup-Punkt\n+1 Komfortpunkt","Power and comfort boundary uses actual reward rules");
  Canvas.ForceUpdateCanvases();Check(!Label("Reward").isTextTruncated,"Both reward lines fit");
  yield return new WaitForSecondsRealtime(3f);AwardTo(206);yield return new WaitForSecondsRealtime(.4f);
  Check(view.ToLevel==206&&Label("Reward").text=="","Levels without points do not invent a reward");
  yield return new WaitForSecondsRealtime(3f);AwardTo(1000);yield return new WaitForSecondsRealtime(.4f);Canvas.ForceUpdateCanvases();
  Check(view.ToLevel==1000&&!Label("Level").isTextTruncated,"Level 1000 fits medallion");
  int plays=view.AudioPlayCount;yield return new WaitForSecondsRealtime(3f);MetaProgression.RecordDiscovery("at-cap");yield return null;
  Check(!view.IsShowing&&view.AudioPlayCount==plays,"XP at level cap stays silent");
  RunNavigation.MainMenu();yield return Ready();Check(group.alpha==0&&!sound.isPlaying,"Scene exit clears all presentation");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=oldScale;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;JsonUtility.FromJsonOverwrite(gps,GpsSettings.Document.tests);Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
