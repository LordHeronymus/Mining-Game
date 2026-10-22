using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
  new GameObject("Challenge V4 checks").AddComponent<ChallengePeriodsProbe>();return "Started";
 }
}
public sealed class ChallengePeriodsProbe:MonoBehaviour {
 const string Report="Temp/ChallengePeriodsChecks.txt";
 const string Output="Design/Tiefenhall/ChallengePeriods/UpgradeStyle";
 const int Budget=2000;
 static readonly CultureInfo German=CultureInfo.GetCultureInfo("de-DE");
 static readonly ChallengePeriod[] Periods={ChallengePeriod.Daily,ChallengePeriod.Weekly,ChallengePeriod.Monthly,ChallengePeriod.Lifetime};
 static readonly ChallengePeriod[] Rounds={ChallengePeriod.Daily,ChallengePeriod.Weekly,ChallengePeriod.Monthly};
 static readonly Item[] OreOrder={Item.Coal,Item.Copper,Item.Iron,Item.Silver,Item.OrangeGarnet,Item.Gold,Item.Titanium,Item.Platinum,Item.Tungsten,Item.Diamond,Item.Ruby,Item.Ultronium};
 static readonly Item[] GearItems={Item.Backpack,Item.LoadBelt,Item.ReinforcedBackpack,Item.HeavyDutyBoots,Item.SpringGreaves,Item.LoadFrame,Item.Exoskeleton,Item.CrystalPendant};
 static readonly HashSet<string> BulkMetrics=new(){"resources","ore","gems","depth","altars","torches-placed","ladders-placed","crafted","tools","dynamite","medkits","torches-crafted","ladders-crafted","exotics","sales","run-sales","sale-single"};
 static readonly HashSet<string> UnitMetrics=new(){"blocks","layer-blocks","blasted","ore-variety","veins","trees","grass","regions","cells","layers","discoveries","round-trips","returns","full-returns","gear","upgrades","artifacts","artifact","artifact-variety"};
 string previous;DateTime? previousClock;TimeZoneInfo previousZone;ChallengeSettings previousSettings;MetaProgressionPanel owner;int checks;bool clean;
 readonly Dictionary<string,int> serials=new();
 void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");}
 static ChallengeSettings Settings=>MetaProgression.CurrentChallengeSettings;
 static int Slots(ChallengePeriod period){var s=Settings.Period(period);return s.countEasy+s.countMedium+s.countHard;}
 static string Xp(int xp)=>"+"+xp.ToString("N0",German)+" XP";
 static DateTime Utc(int year,int month,int day,int hour)=>new DateTime(year,month,day,hour,0,0,DateTimeKind.Utc);
 // Rounds are drawn lazily on first query; draw through the public API before reading the profile.
 static MetaChallengeRound Round(ChallengePeriod period)=>MetaProgression.GetChallenges(period)!=null?MetaProgression.Profile.challengeRounds.Find(r=>r!=null&&r.period==(int)period):null;
 static bool IsProject(MetaChallengeTask task)=>task.tier==(int)ChallengeTier.Project||task.stageTargets!=null&&task.stageTargets.Length>0;
 static string Snapshot(ChallengePeriod period) {
  var project=period==ChallengePeriod.Monthly?MetaProgression.GetMonthlyProject():null;
  return string.Join("|",MetaProgression.GetChallenges(period).Select(c=>c.id+":"+c.current+":"+c.completed))+(project!=null?"|"+project.id+":"+project.current+":"+project.stagesDone:"");
 }
 static string State()=>string.Join("#",Rounds.Select(Snapshot))+"#"+string.Join(",",MetaProgression.Profile.lifetimeCounters.Where(c=>c!=null).Select(c=>c.id+"="+c.value));
 static long Ledger(){var p=MetaProgression.Profile;return p.challengeRounds.Where(r=>r!=null).Sum(r=>r.tasks.Where(t=>t!=null).Sum(t=>(long)t.paid)+(r.bonusPaid?r.bonusXp:0))+p.milestoneAwards.Where(a=>a!=null).Sum(a=>(long)a.xp);}
 static Transform Tile(ScrollRect scroll,string id)=>scroll.content.Cast<Transform>().FirstOrDefault(t=>t.gameObject.activeSelf&&t.name=="Challenge "+id);
 static int Tiles(ScrollRect scroll)=>scroll.content.Cast<Transform>().Count(t=>t.gameObject.activeSelf);
 static bool Visible(ScrollRect scroll,Transform tile){var b=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,tile);return b.min.y>=scroll.viewport.rect.yMin-1&&b.max.y<=scroll.viewport.rect.yMax+1;}
 static string Overflow(Component root){foreach(var t in root.GetComponentsInChildren<TMP_Text>()){t.ForceMeshUpdate();if(t.isTextOverflowing||t.isTextTruncated)return t.name+" '"+t.text+"'";}return null;}
 static string Countdown(Component panel)=>panel.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t=>t.name=="Countdown")?.text;
 static string Bonus(Component panel){var bar=panel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Bonus Bar");var node=bar?bar.Find("Bonus"):null;var text=node?node.GetComponent<TMP_Text>():null;return text?text.text:null;}
 static string BonusText(ChallengePeriod period) {
  var b=MetaProgression.ChallengeBonus(period);
  return period==ChallengePeriod.Lifetime?b.done+" / "+b.required+" Stufen erreicht":b.awarded?"Bonus erhalten · "+Xp(b.xp):b.xp>0?b.done+" / "+b.required+" erledigt → "+Xp(b.xp):b.done+" / "+b.required+" erledigt";
 }
 // View id as in spec §6.3: "<period>-<template>[-subject]", plus "-<index>" for a repeated id.
 static MetaChallengeProgress View(ChallengePeriod period,int index) {
  var round=Round(period);var ids=new List<string>();string id=null;
  for(int i=0;round!=null&&i<round.tasks.Count&&i<=index;i++){var t=round.tasks[i];if(t==null||IsProject(t))continue;id=period+"-"+t.id+(t.subject>=0?"-"+t.subject:"");if(ids.Contains(id))id+="-"+i;ids.Add(id);}
  return MetaProgression.GetChallenges(period).FirstOrDefault(c=>c.id==id);
 }
 // Generic driver: feeds a drawn task's metric through the public recorders, so no test depends on a fixed draw.
 int Next(string key){serials.TryGetValue(key,out int v);serials[key]=v+1;return v;}
 string Source()=>"probe-"+Next("source");
 Vector3Int Cell(){int n=Next("cell");return new Vector3Int(n%400,-2000-n/400,0);}
 void Placed(string metric,int add){serials.TryGetValue("placed:"+metric,out int total);serials["placed:"+metric]=total+=add;MetaProgression.RecordPlacedCount(metric,total);}
 static int Cost(MetaChallengeTask t,int goal)=>goal<=t.progress?0:BulkMetrics.Contains(t.metric)?1:UnitMetrics.Contains(t.metric)?goal-t.progress:int.MaxValue;
 int Step(string metric,int subject,int missing,int goal) {
  switch(metric) {
   case "blocks":case "layer-blocks":case "blasted":MetaProgression.RecordMinedBlock(Cell(),metric=="layer-blocks"&&subject>0?subject:1,metric=="blasted");return 1;
   case "resources":MetaProgression.RecordResource(Item.Coal,missing,Source());return 2;
   case "ore":MetaProgression.RecordResource(subject>=0?(Item)subject:Item.Coal,missing,Source());return 2;
   case "gems":MetaProgression.RecordResource(Item.Diamond,missing,Source());return 2;
   case "ore-variety":MetaProgression.RecordResource(OreOrder[Next(metric)%OreOrder.Length],1,Source());return 1;
   case "veins":MetaProgression.RecordVeinCleared(subject>=0?subject:(int)Item.Iron,Cell(),3);return 1;
   case "trees":case "grass":MetaProgression.RecordSurfaceHarvest(metric,Source());return 1;
   case "depth":MetaProgression.RecordDepthReached(goal);return 2;
   case "regions":{int n=Next(metric);MetaProgression.RecordExploration(n%500,7000+n/500,60);return 1;}
   case "cells":{int n=Next(metric);MetaProgression.RecordRevealedCell(n%500,1+n/500);return 1;}
   case "layers":MetaProgression.RecordLayerVisited(1+Next(metric)%5);return 1;
   case "discoveries":MetaProgression.RecordDiscovery(Source());return 1;
   case "altars":MetaProgression.RecordAltarReached();return 2;
   case "round-trips":MetaProgression.RecordReturn(1000,Math.Min(5,Math.Max(2,subject)),false,500);return 1;
   case "returns":MetaProgression.RecordReturn(1000,5,false,500);return 1;
   case "full-returns":MetaProgression.RecordReturn(1000,5,true,500);return 1;
   case "torches-placed":case "ladders-placed":Placed(metric,missing);return 2;
   case "crafted":MetaProgression.RecordCraftedAmount(Item.BridgePart,missing);return 2;
   case "tools":MetaProgression.RecordCraftedAmount(Item.Axe,missing);return 2;
   case "gear":MetaProgression.RecordCraftedAmount(GearItems[Next(metric)%GearItems.Length],1);return 1;
   case "upgrades":{string kind=subject==1?"energy-upgrades":subject==2?"carrying-upgrades":"pickaxe-upgrades";int level=1+Next(kind);MetaProgression.RecordCraftUpgrade(kind,level,level+1);return 1;}
   case "dynamite":MetaProgression.RecordCraftedAmount(Item.Dynamite,missing);return 2;
   case "medkits":MetaProgression.RecordCraftedAmount(Item.Medkit,missing);return 2;
   case "torches-crafted":MetaProgression.RecordCraftedAmount(Item.Torche,missing);return 2;
   case "ladders-crafted":MetaProgression.RecordCraftedAmount(Item.Ladder,missing);return 2;
   case "exotics":MetaProgression.RecordCraftedAmount(Item.PercussionHammer,missing,true,missing);return 2;
   case "sales":case "run-sales":MetaProgression.RecordSale(missing);return 2;
   case "sale-single":MetaProgression.RecordSale(goal);return 2;
   case "artifacts":MetaProgression.RecordArtifact(Cell(),"Artifact_01");return 1;
   case "artifact":MetaProgression.RecordArtifact(Cell(),"Artifact_"+Math.Max(1,subject).ToString("D2"));return 1;
   case "artifact-variety":MetaProgression.RecordArtifact(Cell(),"Artifact_"+(1+Next(metric)%10).ToString("D2"));return 1;
   default:return 0;
  }
 }
 bool Drive(ChallengePeriod period,int index,int goal) {
  var task=Round(period)?.tasks[index];if(task==null)return false;
  string metric=task.metric;int subject=task.subject;
  for(int step=0,guard=Math.Min(goal-task.progress,Budget)+8;step<guard;step++) {
   task=Round(period)?.tasks[index];if(task==null||task.progress>=goal)break;
   int kind=Step(metric,subject,goal-task.progress,goal);if(kind!=1)break;
  }
  task=Round(period)?.tasks[index];return task!=null&&task.progress>=goal;
 }
 int Pick(ChallengePeriod period,Func<MetaChallengeTask,int> goal) {
  var round=Round(period);if(round==null)return -1;
  return Enumerable.Range(0,round.tasks.Count).Where(i=>round.tasks[i]!=null&&!IsProject(round.tasks[i])&&!round.tasks[i].done&&goal(round.tasks[i])>round.tasks[i].progress&&Cost(round.tasks[i],goal(round.tasks[i]))<=Budget)
   .OrderBy(i=>Cost(round.tasks[i],goal(round.tasks[i]))).DefaultIfEmpty(-1).First();
 }
 MetaChallengeProgress Complete(ChallengePeriod period){int i=Pick(period,t=>t.target);return i>=0&&Drive(period,i,Round(period).tasks[i].target)?View(period,i):null;}
 MetaChallengeProgress Partial(ChallengePeriod period){int i=Pick(period,t=>t.target>=2?t.target/2:0);return i>=0&&Drive(period,i,Round(period).tasks[i].target/2)?View(period,i):null;}
 bool ProjectStage() {
  var round=Round(ChallengePeriod.Monthly);int i=round?.tasks.FindIndex(t=>t!=null&&IsProject(t))??-1;
  if(i<0||round.tasks[i].stageTargets==null||round.tasks[i].stageTargets.Length==0)return false;
  int goal=round.tasks[i].stageTargets[0];return Cost(round.tasks[i],goal)<=Budget&&Drive(ChallengePeriod.Monthly,i,goal);
 }
 IEnumerator Start() {
  previous=MetaProgression.TestDirectory;previousClock=MetaProgression.ChallengeTestUtc;
  previousZone=MetaProgression.ChallengeTimeZoneOverride;previousSettings=MetaProgression.ChallengeSettingsOverride;
  MetaProgression.TestDirectory=Path.GetFullPath("Temp/ChallengeV4-"+DateTime.UtcNow.Ticks);
  MetaProgression.ChallengeTimeZoneOverride=TimeZoneInfo.Utc;MetaProgression.ChallengeSettingsOverride=null;
  File.WriteAllText(Report,"");Directory.CreateDirectory(Output);
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
  MetaProgression.ChallengeTestUtc=Utc(2026,10,5,10);
  MetaProgression.BeginRun("period-core");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Length==Slots(ChallengePeriod.Daily),"Daily round has "+Slots(ChallengePeriod.Daily)+" tasks");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).Length==Slots(ChallengePeriod.Weekly),"Weekly round has "+Slots(ChallengePeriod.Weekly)+" tasks");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Monthly).Length==Slots(ChallengePeriod.Monthly)&&MetaProgression.GetMonthlyProject()!=null,"Monthly round has "+Slots(ChallengePeriod.Monthly)+" tasks and a project");
  var lifetime=MetaProgression.GetChallenges(ChallengePeriod.Lifetime);
  Check(lifetime.Count(c=>c.kind==2)==ChallengeCatalog.Milestones.Count&&ChallengeCatalog.Milestones.Count>0&&lifetime.Count(c=>c.kind==3)==3,"One lifetime tile per milestone line plus three run objectives");
  Check(lifetime.Where(c=>c.kind==2).Select(c=>c.id).SequenceEqual(ChallengeCatalog.Milestones.Select(l=>l.id))&&lifetime.All(c=>c.kind!=2||c.id.StartsWith("line-")),"Lifetime tiles use the line ids");
  Check(Rounds.All(p=>MetaProgression.GetChallenges(p).All(c=>c.kind==0&&c.id.StartsWith(p+"-")&&c.tier>=0&&c.tier<=3&&c.xp==Settings.Period(p).Xp(c.tier))),"Period task ids and tier XP follow the settings");
  Check(Rounds.All(p=>Enumerable.Range(0,Round(p).tasks.Count).Where(i=>!IsProject(Round(p).tasks[i])).All(i=>View(p,i)!=null)),"Every drawn task has a view with the spec id");
  Check(MetaProgression.Profile.challengePeriods.Count==0,"No legacy period states written");
  long before=MetaProgression.Profile.periodRewardXp;
  var done=Complete(ChallengePeriod.Daily);
  Check(done!=null&&done.completed,"A drawn daily task completes through the real recorders: "+done?.id);
  Check(done.xp==Settings.daily.Xp(done.tier)&&MetaProgression.Profile.periodRewardXp-before>=done.xp,"Completion pays the tier reward "+Xp(done.xp));
  Check(MetaProgression.Profile.periodRewardXp==Ledger(),"Period XP equals the paid tasks, bonuses and milestone stages");
  MetaProgression.RecordResource(Item.Copper,50,"core-source");
  var state=State();long xp=MetaProgression.Profile.periodRewardXp;
  MetaProgression.RecordResource(Item.Copper,50,"core-source");
  Check(State()==state&&MetaProgression.Profile.periodRewardXp==xp,"Stable source deduplicates replay");
  MetaProgression.RecordDepthReached(100);state=State();
  MetaProgression.RecordDepthReached(100);MetaProgression.RecordDepth(100);MetaProgression.RecordDepth(100);
  Check(State()==state,"Repeated depth is not double counted");
  int credited=MetaProgression.CreditedBlocks;var cell=new Vector3Int(3,-40,0);
  MetaProgression.RecordMinedBlock(cell,2,false);state=State();MetaProgression.RecordMinedBlock(cell,2,false);
  Check(State()==state&&MetaProgression.CreditedBlocks==credited+1,"A mined cell counts once");
  MetaProgression.RecordPlacedCount("torches-placed",5);state=State();
  MetaProgression.RecordPlacedCount("torches-placed",5);MetaProgression.RecordPlacedCount("torches-placed",3);
  Check(State()==state,"Placed torches count only above the high-water mark");
  var saved=MetaProgression.CaptureRunState();state=State();
  Check(MetaProgression.Save(),"Profile save succeeds");
  long total=MetaProgression.TotalXp,reward=MetaProgression.Profile.periodRewardXp;
  MetaProgression.Reload();MetaProgression.BeginRun("period-core",saved);
  Check(MetaProgression.TotalXp==total&&MetaProgression.Profile.periodRewardXp==reward,"Reload retains period XP without duplicate awards");
  Check(State()==state,"Reload keeps the drawn rounds, counters and completed flags");
  string weekly=Snapshot(ChallengePeriod.Weekly),monthly=Snapshot(ChallengePeriod.Monthly);
  MetaProgression.ChallengeTestUtc=Utc(2026,10,6,10);
  var day=MetaProgression.GetChallenges(ChallengePeriod.Daily);
  Check(day.Length==Slots(ChallengePeriod.Daily)&&day.All(c=>c.current==0&&!c.completed),"Daily rollover draws a fresh round");
  Check(Snapshot(ChallengePeriod.Weekly)==weekly&&Snapshot(ChallengePeriod.Monthly)==monthly,"Daily rollover preserves weekly and monthly progress");
  Check(MetaProgression.Profile.periodRewardXp==reward,"Rollover preserves earned XP");
  before=MetaProgression.Profile.periodRewardXp;var next=Complete(ChallengePeriod.Daily);
  Check(next!=null&&MetaProgression.Profile.periodRewardXp-before>=next.xp,"Next day grants a fresh daily reward");
  string dayTwo=Snapshot(ChallengePeriod.Daily);
  MetaProgression.ChallengeTestUtc=Utc(2026,10,5,10);
  Check(Snapshot(ChallengePeriod.Daily)==dayTwo,"Clock rollback cannot reopen the previous day");
  monthly=Snapshot(ChallengePeriod.Monthly);
  MetaProgression.ChallengeTestUtc=Utc(2026,10,12,10);
  Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).All(c=>c.current==0&&!c.completed),"Monday rolls the weekly round");
  Check(Snapshot(ChallengePeriod.Monthly)==monthly,"A new week keeps the monthly round");
  var started=Partial(ChallengePeriod.Weekly);
  Check(started!=null&&started.current>0&&!started.completed,"New week accepts events");
  string project=MetaProgression.GetMonthlyProject()?.id;
  MetaProgression.ChallengeTestUtc=Utc(2026,11,2,10);
  var fresh=MetaProgression.GetMonthlyProject();
  Check(MetaProgression.GetChallenges(ChallengePeriod.Monthly).All(c=>c.current==0&&!c.completed)&&fresh!=null&&fresh.current==0&&fresh.stagesDone==0,"Month change draws a fresh monthly round and project");
  Check(fresh.id!=project,"The next month avoids the previous project");
  Check(MetaProgression.ChallengePeriodEnd(ChallengePeriod.Monthly)==Utc(2026,12,1,0).Ticks&&MetaProgression.ChallengePeriodEnd(ChallengePeriod.Lifetime)==0,"Monthly round ends on the first of the next month");
  MetaProgression.EndRun();Check(MetaProgression.Save(),"Period snapshot saves after rollover");
  weekly=Snapshot(ChallengePeriod.Weekly);
  MetaProgression.Reload();
  Check(MetaProgression.LastError==null&&Snapshot(ChallengePeriod.Weekly)==weekly,"Rolled profile loads and validates");
  state=State();MetaProgression.RecordSale(99999);MetaProgression.RecordMinedBlock(new Vector3Int(9,-9,0),1,false);
  Check(State()==state,"Menu cannot accrue challenge progress");
  Check(MetaProgression.Profile.challengePeriods.Count==0&&Ledger()<=MetaProgression.Profile.periodRewardXp,"Profile stays free of legacy states and within its XP ledger");
  // Fresh isolated visual fixture.
  MetaProgression.TestDirectory=Path.GetFullPath("Temp/ChallengeV4-view-"+DateTime.UtcNow.Ticks);
  MetaProgression.ChallengeTestUtc=Utc(2026,10,5,12);
  MetaProgression.BeginRun("v4-preview");
  Complete(ChallengePeriod.Daily);Partial(ChallengePeriod.Daily);Partial(ChallengePeriod.Weekly);ProjectStage();
  MetaProgression.EndRun();
  owner=MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform);
  owner.GetComponentsInChildren<Button>(true).First(b=>b.name=="Open Herausforderungen").onClick.Invoke();
  yield return null;yield return null;Canvas.ForceUpdateCanvases();
  var panel=Object.FindFirstObjectByType<ChallengePanel>();
  Check(panel!=null,"Real overview button opens the challenge panel");
  var scroll=panel.GetComponentInChildren<ScrollRect>();
  var daily=MetaProgression.GetChallenges(ChallengePeriod.Daily);
  Check(Tiles(scroll)==daily.Length&&daily.Length==Slots(ChallengePeriod.Daily),"Daily cards present ("+daily.Length+")");
  Check(scroll.verticalScrollbar.gameObject.activeInHierarchy,"Daily cards beyond two rows have a scrollbar");
  var tabs=panel.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("Challenge Tab")).ToList();
  Check(tabs.Count==4&&Periods.All(p=>tabs.Any(b=>b.name=="Challenge Tab "+p)),"Four real category controls");
  Check(tabs.Select(b=>b.GetComponentInChildren<TMP_Text>().text).SequenceEqual(new[]{"Täglich","Wöchentlich","Monatlich","Langzeit"}),"Tabs read Täglich, Wöchentlich, Monatlich, Langzeit");
  var tile=Tile(scroll,daily[0].id);
  Check(tile&&tile.Find("Icon").GetComponent<Image>().sprite,"Freestanding reference icon");
  Check(tile.Find("XP Plaque/Reward").GetComponent<TMP_Text>().text==Xp(Settings.daily.Xp(daily[0].tier)),"XP plaque displays the tier reward "+Xp(Settings.daily.Xp(daily[0].tier)));
  foreach(var c in daily) {
   var card=Tile(scroll,c.id);var fill=card?card.Find("Progress Track/Interior/Progress Fill") as RectTransform:null;
   Check(fill&&Mathf.Abs(fill.anchorMax.x-Mathf.Clamp01((float)c.current/c.target))<.001f&&(card.Find("Completed")!=null)==c.completed,c.id+" fill and seal match "+c.current+"/"+c.target);
  }
  Check(daily.Any(c=>c.completed)&&daily.Any(c=>!c.completed&&c.current>0),"Daily view shows a completed and a started task");
  var bad=Overflow(panel);Check(bad==null,"Daily text fits"+(bad==null?"":": "+bad));
  Check(Bonus(panel)==BonusText(ChallengePeriod.Daily),"Daily bonus bar reads '"+BonusText(ChallengePeriod.Daily)+"'");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Daily");
  scroll.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
  Check(Visible(scroll,Tile(scroll,daily.Last().id)),"Last daily tile fully accessible by scrolling");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Daily-End");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenge Tab Weekly").onClick.Invoke();
  yield return null;yield return null;
  var weeklyViews=MetaProgression.GetChallenges(ChallengePeriod.Weekly);
  Check(Tiles(scroll)==weeklyViews.Length&&Tile(scroll,weeklyViews[0].id),"Weekly tab switches content");
  Check(Countdown(panel)?.Contains(" T ")==true,"Weekly countdown");
  bad=Overflow(panel);Check(bad==null,"Weekly text fits"+(bad==null?"":": "+bad));
  Check(Bonus(panel)==BonusText(ChallengePeriod.Weekly),"Weekly bonus bar reads '"+BonusText(ChallengePeriod.Weekly)+"'");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Weekly");
  MetaProgression.ChallengeTestUtc=Utc(2026,10,12,12);
  yield return null;yield return null;yield return null;
  weeklyViews=MetaProgression.GetChallenges(ChallengePeriod.Weekly);
  Check(Tiles(scroll)==weeklyViews.Length&&weeklyViews.All(c=>Tile(scroll,c.id) is Transform t&&t.Find("Progress").GetComponent<TMP_Text>().text.StartsWith("0 /")),"Open paused panel refreshes after a weekly rollover");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenge Tab Monthly").onClick.Invoke();
  yield return null;yield return null;
  var monthlyViews=MetaProgression.GetChallenges(ChallengePeriod.Monthly);var monthProject=MetaProgression.GetMonthlyProject();
  Check(monthProject!=null&&Tiles(scroll)==monthlyViews.Length+1&&monthlyViews.All(c=>Tile(scroll,c.id)),"Monthly tab shows its tasks and the project");
  var projectTile=Tile(scroll,monthProject.id) as RectTransform;
  Check(projectTile&&Mathf.Abs(projectTile.rect.width-1312)<.5f,"Monthly project tile spans the grid (1312 wide)");
  for(int i=0;i<3;i++) {
   var stage=projectTile.Find("Stage "+(i+1));
   Check(stage&&stage.Find("XP Plaque/Reward").GetComponent<TMP_Text>().text==Xp(Settings.ProjectStageXp(i))&&stage.Find("Progress Track/Interior/Progress Fill"),"Project stage "+(i+1)+" shows "+Xp(Settings.ProjectStageXp(i))+" and its track");
  }
  Check(monthProject.stagesDone>=1&&projectTile.Find("Stage 1/Completed"),"Finished project stage is sealed");
  Check(Countdown(panel)?.Contains(" T ")==true,"Monthly countdown shows days");
  bad=Overflow(panel);Check(bad==null,"Monthly text fits"+(bad==null?"":": "+bad));
  Check(Bonus(panel)==BonusText(ChallengePeriod.Monthly),"Monthly bonus bar reads '"+BonusText(ChallengePeriod.Monthly)+"'");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Monthly");
  scroll.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
  Check(Visible(scroll,Tile(scroll,monthlyViews.Last().id)),"Last monthly tile fully accessible by scrolling");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Monthly-End");
  MetaProgression.ChallengeTestUtc=Utc(2026,11,2,12);
  yield return null;yield return null;yield return null;
  var nextProject=MetaProgression.GetMonthlyProject();
  Check(nextProject!=null&&Tile(scroll,nextProject.id) is Transform fresh2&&fresh2.Find("Progress").GetComponent<TMP_Text>().text.StartsWith("0 /"),"Open paused panel refreshes after a monthly rollover");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenge Tab Lifetime").onClick.Invoke();
  yield return null;yield return null;
  Check(!panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="Countdown"),"Lifetime has no countdown");
  var lines=MetaProgression.GetChallenges(ChallengePeriod.Lifetime);
  Check(Tiles(scroll)==lines.Length&&lines.Length==ChallengeCatalog.Milestones.Count,"All "+lines.Length+" milestone lines accessible");
  Check(Tile(scroll,"line-exotics"),"Lifetime exotics line listed");
  bad=Overflow(panel);Check(bad==null,"Lifetime text fits"+(bad==null?"":": "+bad));
  Check(Bonus(panel)==BonusText(ChallengePeriod.Lifetime),"Lifetime bar reads '"+BonusText(ChallengePeriod.Lifetime)+"'");
  scroll.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
  Check(Visible(scroll,Tile(scroll,lines.Last().id))&&scroll.verticalScrollbar.gameObject.activeInHierarchy,"Last lifetime tile scrollable");
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Lifetime-End");
  scroll.verticalNormalizedPosition=1;
  yield return new WaitForEndOfFrame();Save("Challenges-V4-Lifetime");
  panel.GetComponentsInChildren<Button>().First(b=>b.name=="Challenges Back").onClick.Invoke();yield return null;
  Check(owner.ShowingOverview&&GameplayInputBlocker.IsBlocked&&Time.timeScale==0,"Back preserves parent pause and input ownership");
  owner.Close();yield return null;
  Check(!GameplayInputBlocker.IsBlocked&&Time.timeScale==1,"Closing restores input and time");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Save(string name){var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
 void Cleanup(){
  if(clean)return;clean=true;if(owner)owner.Close();MetaProgression.EndRun();
  MetaProgression.ChallengeSettingsOverride=previousSettings;MetaProgression.ChallengeTimeZoneOverride=previousZone;
  MetaProgression.TestDirectory=previous;MetaProgression.ChallengeTestUtc=previousClock;Object.Destroy(gameObject);
 }
 void OnDestroy(){Cleanup();}
}
