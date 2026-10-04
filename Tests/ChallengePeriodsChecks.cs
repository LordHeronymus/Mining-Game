using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class ChallengePeriodsChecks {
 public static object Main() {
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked || MetaProgression.CurrentRun!=null)
   throw new Exception("Requires free MainMenu Play Mode");
  new GameObject("Challenge V3 checks").AddComponent<ChallengePeriodsProbe>();return "Started";
 }
}
public sealed class ChallengePeriodsProbe:MonoBehaviour {
 const string Report="Temp/ChallengePeriodsChecks.txt";
 const string Output="Assets/Design/Tiefenhall/ChallengePeriods/UpgradeStyle";
 string previous;DateTime? previousClock;MetaProgressionPanel owner;int checks;bool clean;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");}
 int Count(ChallengePeriod period,string metric)=>MetaProgression.GetChallenges(period).First(c=>c.metric==metric).current;
 IEnumerator Start() {
  previous=MetaProgression.TestDirectory;previousClock=MetaProgression.ChallengeTestUtc;
  MetaProgression.TestDirectory=Path.GetFullPath("Temp/ChallengeV3-"+DateTime.UtcNow.Ticks);
  File.WriteAllText(Report,"");
  var routine=Run();
  while(true) {
   bool more=false;object next=null;
   try{more=routine.MoveNext();if(more)next=routine.Current;}
   catch(Exception e){File.AppendAllText(Report,"FAILED "+e+"\n");Cleanup();yield break;}
   if(!more)break;yield return next;
  }
  Cleanup();
 }
 IEnumerator Run() {
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,5,10,0,0,DateTimeKind.Utc);
  MetaProgression.BeginRun("period-core");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Length==16,"Sixteen daily tasks");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).Length==16,"Sixteen weekly tasks");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).Length==17,"Seventeen lifetime milestones");
  MetaProgression.RecordResource(Item.Copper,50,"core-source");
  Check(Count(ChallengePeriod.Daily,"resources")==50 && Count(ChallengePeriod.Weekly,"resources")==50,"Mining feeds both periods");
  Check(MetaProgression.Profile.periodRewardXp==250,"Daily ore and copper rewards exactly once");
  MetaProgression.RecordResource(Item.Copper,50,"core-source");
  Check(Count(ChallengePeriod.Weekly,"resources")==50 && MetaProgression.Profile.periodRewardXp==250,"Stable source deduplicates replay");
  MetaProgression.RecordDepth(100);MetaProgression.RecordDepth(100);
  Check(Count(ChallengePeriod.Weekly,"depth")==100,"Repeated depth is not double counted");
  MetaProgression.RecordCraftedAmount(Item.Torche,3);MetaProgression.RecordCraftedAmount(Item.IronPickaxe,2);
  MetaProgression.RecordSale(1000);
  Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Take(6).All(c=>c.completed),"Original six event counters complete");
  Check(MetaProgression.Profile.periodRewardXp==900,"Daily rewards sum to 900");
  var saved=MetaProgression.CaptureRunState();
  Check(MetaProgression.Save(),"Profile save succeeds");
  long xp=MetaProgression.TotalXp;
  MetaProgression.Reload();MetaProgression.BeginRun("period-core",saved);
  Check(MetaProgression.TotalXp==xp && MetaProgression.Profile.periodRewardXp==900,"Reload retains period XP without duplicate awards");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Take(6).All(c=>c.completed),"Reload retains counters and completed flags");
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,6,10,0,0,DateTimeKind.Utc);
  Check(Count(ChallengePeriod.Daily,"resources")==0,"Daily rollover clears daily counters");
  Check(Count(ChallengePeriod.Weekly,"resources")==50,"Daily rollover preserves weekly counters");
  Check(MetaProgression.Profile.periodRewardXp==900,"Rollover preserves earned XP");
  MetaProgression.RecordResource(Item.Copper,50,"next-day-source");
  Check(MetaProgression.Profile.periodRewardXp==1150,"Next day grants fresh daily reward");
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,5,10,0,0,DateTimeKind.Utc);
  Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).First(c=>c.metric=="resources").completed,"Clock rollback cannot reopen previous day");
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,12,10,0,0,DateTimeKind.Utc);
  Check(Count(ChallengePeriod.Weekly,"resources")==0,"Monday rolls weekly counters");
  MetaProgression.RecordResource(Item.Copper,500,"weekly-source");
  Check(Count(ChallengePeriod.Weekly,"resources")==500,"New week accepts events");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).First(c=>c.metric=="resources").completed,"Weekly rewards complete independently");
  MetaProgression.EndRun();Check(MetaProgression.Save(),"Period snapshot saves after rollover");
  MetaProgression.Reload();
  Check(MetaProgression.LastError==null && MetaProgression.GetChallenges(ChallengePeriod.Weekly).First(c=>c.metric=="resources").completed,"Rolled profile loads and validates");
  MetaProgression.RecordSale(99999);
  Check(Count(ChallengePeriod.Daily,"sales")==0,"Menu cannot accrue sales");
  // Fresh isolated visual fixture.
  MetaProgression.TestDirectory=Path.GetFullPath("Temp/ChallengeV3-view-"+DateTime.UtcNow.Ticks);
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,5,12,0,0,DateTimeKind.Utc);
  MetaProgression.BeginRun("v3-preview");
  MetaProgression.RecordResource(Item.Iron,10,"iron");MetaProgression.RecordResource(Item.Copper,8,"copper");
  MetaProgression.RecordDepth(42);MetaProgression.RecordCraftedAmount(Item.Torche,3);MetaProgression.RecordSale(350);
  MetaProgression.EndRun();
  owner=MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform);
  owner.GetComponentsInChildren<Button>(true).First(b=>b.name=="Open Herausforderungen").onClick.Invoke();
  yield return null;yield return null;Canvas.ForceUpdateCanvases();
  var panel=Object.FindFirstObjectByType<ChallengePanel>();
  Check(panel!=null,"Real overview button opens V3");
  var scroll=panel.GetComponentInChildren<ScrollRect>();
  Check(scroll.content.Cast<Transform>().Count(t=>t.gameObject.activeSelf)==16,"Sixteen daily cards present");
  Check(scroll.verticalScrollbar.gameObject.activeInHierarchy,"Sixteen cards have scrollbar");
  Check(panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Challenge Tab"))==3,"Three real category controls");
  var tile=scroll.content.Find("Challenge Daily-resources");
  Check(tile.Find("Icon").GetComponent<Image>().sprite.name=="Challenge Art 0","Freestanding reference icon");
  Check(tile.Find("XP Plaque/Reward").GetComponent<TMP_Text>().text=="+150 XP","XP plaque displays live reward");
  var fill=tile.Find("Progress Track/Interior/Progress Fill").GetComponent<RectTransform>();
  Check(Mathf.Abs(fill.anchorMax.x-.36f)<.001f,"Progress fill matches 18 of 50");
  Check(scroll.content.Find("Challenge Daily-torches/Completed")!=null,"Completed card has gold seal");
  Check(panel.GetComponentsInChildren<TMP_Text>().All(t=>!t.isTextOverflowing),"Visible text fits");
  yield return new WaitForEndOfFrame();Save("Challenges-V3-Daily");
  scroll.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
  var lastDaily=scroll.content.Find("Challenge Daily-production").GetComponent<RectTransform>();
  var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,lastDaily);
  Check(bounds.min.y>=scroll.viewport.rect.yMin && bounds.max.y<=scroll.viewport.rect.yMax,"Last added daily tile fully accessible by scrolling");
  yield return new WaitForEndOfFrame();Save("Challenges-V3-Daily-Additional");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenge Tab Weekly").onClick.Invoke();
  yield return null;yield return null;
  Check(scroll.content.Find("Challenge Weekly-resources")!=null,"Weekly tab switches content");
  Check(panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="Countdown"&&t.text.Contains(" T ")),"Weekly countdown");
  MetaProgression.ChallengeTestUtc=new DateTime(2026,10,19,12,0,0,DateTimeKind.Utc);
  yield return null;yield return null;yield return null;
  Check(scroll.content.Find("Challenge Weekly-resources/Progress").GetComponent<TMP_Text>().text.StartsWith("0 /"),"Open paused panel refreshes after a weekly rollover");
  yield return new WaitForEndOfFrame();Save("Challenges-V3-Weekly");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenge Tab Lifetime").onClick.Invoke();
  yield return null;yield return null;
  Check(!panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="Countdown"),"Lifetime has no countdown");
  Check(scroll.content.Cast<Transform>().Count(t=>t.gameObject.activeSelf)==17,"All seventeen milestones accessible");
  scroll.verticalNormalizedPosition=0;yield return null;
  Check(scroll.content.Find("Challenge lifetime-exotics")!=null && scroll.verticalScrollbar.gameObject.activeInHierarchy,"Last added lifetime tile scrollable");
  yield return new WaitForEndOfFrame();Save("Challenges-V3-Lifetime-Additional");
  scroll.verticalNormalizedPosition=1;
  yield return new WaitForEndOfFrame();Save("Challenges-V3-Lifetime");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenges Back").onClick.Invoke();yield return null;
  Check(owner.ShowingOverview && GameplayInputBlocker.IsBlocked && Time.timeScale==0,"Back preserves parent pause and input ownership");
  owner.Close();yield return null;
  Check(!GameplayInputBlocker.IsBlocked && Time.timeScale==1,"Closing restores input and time");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Save(string name){var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
 void Cleanup(){if(clean)return;clean=true;if(owner)owner.Close();MetaProgression.EndRun();MetaProgression.TestDirectory=previous;MetaProgression.ChallengeTestUtc=previousClock;Object.Destroy(gameObject);}
 void OnDestroy(){Cleanup();}
}
