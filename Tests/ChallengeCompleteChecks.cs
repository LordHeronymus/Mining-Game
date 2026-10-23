// CI: local-only
using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Challenge-erfüllt-Banner: erkennt Abschlüsse, Ton und Siegel synchron, Pause, Warteschlange, Level-Up wartet, Laden bleibt still.
public static class ChallengeCompleteChecks {
 public static object Main(){
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Start in free MainMenu");
  var go=new GameObject("Challenge Complete Probe");Object.DontDestroyOnLoad(go);go.AddComponent<ChallengeCompleteProbe>();return "Started";
 }
}
public sealed class ChallengeCompleteProbe:MonoBehaviour {
 const string Report="Temp/ChallengeCompleteChecks.txt";
 string save,meta,gps;bool clean;int checks;float oldScale;
 ChallengeCompletePresentation view;CanvasGroup group;AudioSource sound;
 void Check(bool condition,string name){if(!condition)throw new Exception(name);File.AppendAllText(Report,"PASS "+(++checks)+" "+name+"\n");}
 IEnumerator Start(){
  save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;gps=JsonUtility.ToJson(GpsSettings.Tests);oldScale=Time.timeScale;
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/ChallengeComplete-"+DateTime.UtcNow.Ticks);MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Profile");
  GpsSettings.Document.tests.testModeDisabled=true;File.WriteAllText(Report,"");
  var routine=Run();while(true){bool more=false;object next=null;Exception error=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){error=e;}
   if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}if(!more)break;yield return next;}
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;
  if(!MainMenuController.IsVisible && !RunNavigation.IsTransitioning)RunNavigation.MainMenu();
  float end=Time.realtimeSinceStartup+120;while((LoadingProgress.Active||RunNavigation.IsTransitioning)&&Time.realtimeSinceStartup<end)yield return null;
  Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Loading timeout");yield return null;}yield return null;yield return null;}
 TextMeshProUGUI Label(string name)=>view.GetComponentsInChildren<TextMeshProUGUI>(true).First(t=>t.name==name);
 Image Part(string name)=>view.GetComponentsInChildren<Image>(true).First(i=>i.name==name);
 // Marks a run objective as done the way CheckChallenges does, then lets MetaProgression announce the change.
 MetaChallengeDefinitionView Complete(int index){
  var run=MetaProgression.CurrentRun;string id=run.objectiveIds[index];var definition=MetaProgressionCatalog.Challenge(id);
  run.completedObjectives.Add(id);run.challengeXp+=definition.xp;
  string key="Run-"+run.runId+"-"+id;MetaProgression.SetTracked(key,!MetaProgression.IsTracked(key));
  return new MetaChallengeDefinitionView{name=definition.name,xp=definition.xp};
 }
 IEnumerator Until(Func<bool> condition,float seconds,string name){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception("Timeout: "+name);yield return null;}}
 IEnumerator Run(){
  RunNavigation.NewGame("Challenge-Test");yield return Ready();
  view=ChallengeCompletePresentation.Instance;Check(view,"Presenter installed automatically");group=view.GetComponent<CanvasGroup>();sound=view.GetComponent<AudioSource>();
  yield return new WaitForSecondsRealtime(1f);
  Check(!view.IsShowing&&view.Queued==0&&group.alpha==0&&view.AudioPlayCount==0,"Fresh world stays silent");
  Check(!group.blocksRaycasts&&!group.interactable&&view.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget),"Banner ignores pointer input");
  Check(MetaProgression.CurrentRun.objectiveIds.Count>=3,"Run has three objectives");

  var first=Complete(0);yield return null;yield return null;
  Check(view.IsShowing&&view.AudioPlayCount==1&&sound.isPlaying&&sound.clip&&sound.clip.name=="ChallengeComplete","Completion plays the challenge clip once");
  Check(Mathf.Abs(sound.clip.length-3.6f)<.05f,"Clip is the trimmed N1 take");
  Check(Label("Name").text==first.name&&Label("Title").text=="Herausforderung erfüllt"&&Label("Reward").text=="+"+first.xp.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("de-DE"))+" XP","Banner names the challenge and its XP");
  Check(!Part("Seal").enabled&&view.Elapsed<ChallengeCompletePresentation.SoundPeakSeconds-ChallengeCompletePresentation.SealFall,"Seal waits for the charge-up");
  Check(Time.timeScale==1&&!GameplayInputBlocker.IsBlocked,"Banner does not pause or block gameplay");

  // A level reached meanwhile waits for the banner.
  var levelUp=GameplayLevelUpPresentation.Instance;int level=MetaProgression.Level;
  var run=MetaProgression.CurrentRun;run.settings.efficiencyMaxBonus=0;run.settings.xpMultiplier=1;
  run.progressXp+=Math.Max(0,MetaProgressionCatalog.XpForLevel(level+1)-MetaProgression.TotalXp+1);
  string key="Run-"+run.runId+"-"+run.objectiveIds[2];MetaProgression.SetTracked(key,!MetaProgression.IsTracked(key));
  yield return null;yield return null;
  Check(MetaProgression.Level>level&&levelUp&&!levelUp.IsShowing,"Level-up waits while the banner shows");

  yield return Until(()=>view.Elapsed>=ChallengeCompletePresentation.SoundPeakSeconds+.03f,3,"peak");
  var seal=Part("Seal");
  Check(seal.enabled&&seal.color.a>.99f&&seal.rectTransform.localScale.x>=.99f&&seal.rectTransform.localScale.x<1.13f,"Seal has landed at the peak");
  Check(Mathf.Abs(sound.time-view.Elapsed)<.1f,"Seal impact matches the audible hit");
  Check(group.alpha>.99f,"Banner fully visible at the hit");
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Temp/ChallengeComplete-Treffer.png");yield return null;

  float age=view.Elapsed;GameplayInputBlocker.SetBlocked(this,true);Time.timeScale=0;yield return new WaitForSecondsRealtime(.3f);
  Check(group.alpha==0&&Mathf.Abs(view.Elapsed-age)<.05f&&!sound.isPlaying,"Pause hides the banner and suspends animation and audio");
  GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=1;yield return null;yield return null;
  Check(group.alpha>.9f&&sound.isPlaying,"Resume continues the same banner");

  yield return Until(()=>!view.IsShowing,4,"banner end");
  Check(group.alpha==0,"Banner fades after its duration");
  yield return null;yield return null;
  Check(levelUp.IsShowing,"Waiting level-up starts after the banner");

  var second=Complete(1);yield return Until(()=>view.Queued==1,1,"queue");
  Check(levelUp.IsShowing&&!view.IsShowing,"Banner during a level-up waits in the queue");
  yield return Until(()=>view.IsShowing,9,"queued banner");
  Check(view.AudioPlayCount==2&&Label("Name").text==second.name,"Queued banner plays after the level-up");
  yield return Until(()=>!view.IsShowing,4,"second end");

  var medal=new MetaChallengeProgress{name="Erzsammler",kind=2,medal=2,stages=4,stagesDone=2,xp=900,stageXp=new[]{100,250,500,900}};
  var entry=ChallengeCompletePresentation.Describe(medal);
  Check(entry.line=="Silber erreicht"&&entry.xp=="+250 XP","Milestone stage names the medal and its stage XP");
  var project=new MetaChallengeProgress{name="Monatsprojekt",kind=1,stagesDone=1,stageTargets=new[]{10,20,30},stageXp=new[]{1000,2000,3000},xp=2000};
  entry=ChallengeCompletePresentation.Describe(project);
  Check(entry.line=="Stufe 1 von 3 erreicht"&&entry.xp=="+1.000 XP","Project stage shows stage and stage XP");
  Check(ChallengeCompletePresentation.Score(medal)==2&&ChallengeCompletePresentation.Score(project)==1,"Scores count stages");

  bool saved=false;yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot,this,(ok,message)=>saved=ok);Check(saved,"Isolated world saves");
  int plays=view.AudioPlayCount;
  Check(RunNavigation.LoadGame(GameSaveSystem.ActiveSlot,out _),"Real save reload accepted");yield return Ready();
  yield return new WaitForSecondsRealtime(1f);
  Check(!view.IsShowing&&view.Queued==0&&group.alpha==0&&view.AudioPlayCount==plays,"Loading finished challenges never replays the banner");
  Check(ChallengeCompletePresentation.Preview(),"Test settings preview starts");yield return null;yield return null;
  Check(view.IsShowing&&sound.isPlaying&&view.AudioPlayCount==plays+1,"Preview shows banner with sound");
  RunNavigation.MainMenu();yield return Ready();
  Check(!view.IsShowing&&group.alpha==0&&!sound.isPlaying,"Scene exit clears the banner");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;GameplayInputBlocker.SetBlocked(this,false);Time.timeScale=oldScale;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;JsonUtility.FromJsonOverwrite(gps,GpsSettings.Document.tests);Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
public sealed class MetaChallengeDefinitionView{public string name;public int xp;}
