using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class LevelUpTestSettingsChecks {
 public static object Main(){
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Start in free MainMenu");
  new GameObject("Levelup Settings Probe").AddComponent<LevelUpSettingsProbe>();return "Started";
 }
}
public sealed class LevelUpSettingsProbe:MonoBehaviour {
 string previousMeta;float previousScale;bool cleaned;int checks;string profile;
 const string Report="Temp/LevelUpTestSettingsChecks.txt";
 void Check(bool value,string name){if(!value)throw new Exception(name);File.AppendAllText(Report,"PASS "+(++checks)+" "+name+"\n");}
 Button Button(string name)=>Object.FindFirstObjectByType<GpsRuntimePanel>().GetComponentsInChildren<Button>().Single(b=>b.name==name);
 IEnumerator Start(){
  previousMeta=MetaProgression.TestDirectory;previousScale=Time.timeScale;
  MetaProgression.TestDirectory=Path.GetFullPath("Temp/LevelupPreview-"+DateTime.UtcNow.Ticks);File.WriteAllText(Report,"");
  MetaProgression.BeginRun("preview-seed",new MetaRunState{runId="preview-seed",progressXp=MetaProgressionCatalog.XpForLevel(13),ended=true});MetaProgression.EndRun();
  profile=JsonUtility.ToJson(MetaProgression.Profile);
  var sequence=Run();while(true){bool more=false;object next=null;Exception error=null;try{more=sequence.MoveNext();if(more)next=sequence.Current;}catch(Exception e){error=e;}
   if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}if(!more)break;yield return next;}
  Cleanup();
 }
 IEnumerator Run(){
  GpsRuntimePanel.Open();var panel=Object.FindFirstObjectByType<GpsRuntimePanel>();panel.SelectTab(GpsSchema.TestTab);Button("+ Overlays").onClick.Invoke();yield return null;
  var button=Button("Level-up-Animation testen");Check(button&&button.IsInteractable(),"Testsettings expose usable preview button");
  button.onClick.Invoke();yield return new WaitForSecondsRealtime(.4f);
  var view=GameplayLevelUpPresentation.Instance;var audio=view.GetComponent<AudioSource>();var group=view.GetComponent<CanvasGroup>();
  Check(!GpsRuntimePanel.IsOpen&&!GameplayInputBlocker.IsBlocked,"Click closes GPS and releases its input blocker");
  Check(view.IsShowing&&view.FromLevel==13&&view.ToLevel==14&&group.alpha>.99f,"Same animation displays next level without active run");
  Check(audio.isPlaying&&view.AudioPlayCount==1,"Preview includes tuned original audio");
  Check(JsonUtility.ToJson(MetaProgression.Profile)==profile&&MetaProgression.CurrentRun==null,"Preview does not change XP points or run ledger");
  GpsRuntimePanel.Open();yield return null;Check(group.alpha==0&&!audio.isPlaying,"Reopening GPS hides and pauses preview");
  panel.SelectTab(GpsSchema.TestTab);Button("Level-up-Animation testen").onClick.Invoke();yield return new WaitForSecondsRealtime(.4f);
  Check(view.IsShowing&&view.Elapsed<.8f&&view.AudioPlayCount==2&&view.GetComponents<AudioSource>().Length==1,"Repeated test restarts one animation and one audio source");
  Check(Time.timeScale==previousScale&&JsonUtility.ToJson(MetaProgression.Profile)==profile,"Repeat preserves time and profile");
  yield return new WaitForSecondsRealtime(3f);Check(!view.IsShowing&&group.alpha==0,"Preview finishes normally outside gameplay");
  Check(audio.isPlaying,"Preview audio tail survives visual fade");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(cleaned)return;cleaned=true;GpsRuntimePanel.Close();var view=GameplayLevelUpPresentation.Instance;if(view){view.enabled=false;view.enabled=true;}Time.timeScale=previousScale;MetaProgression.EndRun();MetaProgression.TestDirectory=previousMeta;Object.Destroy(gameObject);}
 void OnDestroy(){if(!cleaned)Cleanup();}
}
