using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
// Pipeline run_script entry: ChallengeTilePreview.Main; isolated profile and automatic cleanup.
public static class ChallengeTilePreview {
 public static object Main() {
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked || MetaProgression.CurrentRun!=null) throw new Exception("Start on free home");
  new GameObject("Challenge preview").AddComponent<ChallengeTileProbe>();return "Started";
 }
}
public class ChallengeTileProbe:MonoBehaviour {
 string previous; MetaProgressionPanel owner; bool cleaned; int checks;
 const string Report="Temp/ChallengeTilePreview.txt", Output="Assets/Design/Tiefenhall/ProgressionTimelinePrototypes/Implemented";
 void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");}
 Button Button(string name)=>owner.GetComponentsInChildren<Button>(true).First(b=>b.name==name);
 IEnumerator Start(){
  previous=MetaProgression.TestDirectory;MetaProgression.TestDirectory=Path.GetFullPath("Temp/ChallengePreview-"+DateTime.UtcNow.Ticks);
  File.WriteAllText(Report,"");
  var routine=Run();try { while(routine.MoveNext()) yield return routine.Current; } finally {Cleanup();}
 }
 IEnumerator Run(){
  MetaProgression.BeginRun("challenge-preview");MetaProgression.RecordDepth(150);MetaProgression.EndRun();
  owner=MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform);
  Button("Open Herausforderungen").onClick.Invoke();yield return null;Canvas.ForceUpdateCanvases();
  var panel=Object.FindFirstObjectByType<ChallengePanel>();var scroll=panel.GetComponentInChildren<ScrollRect>();
  var completed=scroll.content.Find("Challenge depth-100");var partial=scroll.content.Find("Challenge depth-250");
  Check(completed.Find("Completed").GetComponent<ProgressionSymbol>().canvasRenderer.GetMesh().vertexCount>0,"Completion check is rendered");
  Check(Mathf.Abs(partial.Find("Progress Track/Progress Fill").GetComponent<RectTransform>().anchorMax.x-.6f)<.001f,"Partial progress displays 150 of 250");
  Check(completed.Find("Frame").GetComponent<Image>().sprite!=partial.Find("Frame").GetComponent<Image>().sprite,"Completed card receives distinct gold edge");
  Check(scroll.verticalNormalizedPosition>.999f,"Completed overview opens at top");
  yield return new WaitForEndOfFrame();Save("Challenges-Tiles");
  MetaProgression.BeginRun("challenge-preview-active");yield return null;yield return null;
  Check(panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Run-Ziele"),"Active run objectives appear as their own group");
  int count=MetaProgression.GetChallenges().Length;
  Check(scroll.content.Cast<Transform>().Count(t=>t.gameObject.activeSelf && t.name.StartsWith("Challenge "))==count,"Every run objective and milestone has a tile");
  scroll.verticalNormalizedPosition=.3f;MetaProgression.RecordDepth(80);yield return null;yield return null;
  Check(Mathf.Abs(scroll.verticalNormalizedPosition-.3f)<.02f,"Live progress refresh preserves scroll position");
  scroll.verticalNormalizedPosition=1;yield return new WaitForEndOfFrame();Save("Challenges-Run-Tiles");
  Button("Challenges Back").onClick.Invoke();yield return null;
  Check(owner.ShowingOverview && GameplayInputBlocker.IsBlocked && Time.timeScale==0,"Return preserves overview pause");
  owner.Close();yield return null;MetaProgression.EndRun();
  Check(!GameplayInputBlocker.IsBlocked && Time.timeScale==1,"Final close releases pause");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Save(string name){var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());Object.Destroy(image);}
 void Cleanup(){if(cleaned)return;cleaned=true;if(owner)owner.Close();MetaProgression.EndRun();MetaProgression.TestDirectory=previous;Destroy(gameObject);}
 void OnDestroy(){if(!cleaned)Cleanup();}
}
