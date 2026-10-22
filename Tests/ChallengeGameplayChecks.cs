using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
public static class ChallengeGameplayChecks {
 public static object Main() {
  if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)throw new Exception("Start free MainMenu");
  var go=new GameObject("Challenge Gameplay Probe");Object.DontDestroyOnLoad(go);go.AddComponent<ChallengeGameplayProbe>();return "Started";
 }
}
public sealed class ChallengeGameplayProbe:MonoBehaviour {
 const string Report="Temp/ChallengeGameplayChecks.txt";
 string save,meta,gps;bool clean;int checks;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");}
 // Rounds are drawn per day/week/month, so every check covers whatever tasks of that metric were drawn.
 static readonly ChallengePeriod[] Rounds={ChallengePeriod.Daily,ChallengePeriod.Weekly,ChallengePeriod.Monthly};
 static IEnumerable<MetaChallengeProgress> Tasks()=>Rounds.SelectMany(p=>MetaProgression.GetChallenges(p));
 static int Count(params string[] metrics)=>Tasks().Where(c=>metrics.Contains(c.metric)).Sum(c=>c.current);
 static bool Matches(string metric,int amount)=>Tasks().Where(c=>c.metric==metric).All(c=>c.current==Math.Min(c.target,amount));
 // Run tallies are date independent, so they cover the craft and sale wiring even when no matching task was drawn.
 static long Tally(string id)=>MetaProgression.CurrentRun.tallies.Find(t=>t!=null&&t.id==id)?.value??0;
 static int Line(string id)=>MetaProgression.GetChallenges(ChallengePeriod.Lifetime).First(c=>c.id==id).current;
 static string State()=>string.Join("|",Tasks().Select(c=>c.id+":"+c.current+":"+c.completed));
 IEnumerator Start(){
  save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;gps=JsonUtility.ToJson(GpsSettings.Tests);
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/ChallengeGameplay-"+DateTime.UtcNow.Ticks);
  MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Profile");
  GpsSettings.Document.tests.testModeDisabled=true;File.WriteAllText(Report,"");
  var routine=Run();
  while(true){
   bool more=false;object next=null;Exception error=null;
   try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){error=e;}
   if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}
   if(!more)break;yield return next;
  }
  if(!MainMenuController.IsVisible && !RunNavigation.IsTransitioning)RunNavigation.MainMenu();
  float end=Time.realtimeSinceStartup+120;
  while((LoadingProgress.Active||RunNavigation.IsTransitioning)&&Time.realtimeSinceStartup<end)yield return null;
  Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Loading timeout");yield return null;}yield return null;yield return null;}
 IEnumerator Run(){
  RunNavigation.NewGame("Challenge-Test");yield return Ready();
  var player=Object.FindFirstObjectByType<PlayerMovement>();
  Check(player && Camera.main.GetComponent<CameraFollow>().enabled && Camera.main.GetComponent<CameraFollow>().target==player.transform &&
   Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh gameplay camera follow and clamp");
  Check(MetaProgressionRuntime.RewardsAllowed,"Real gameplay event gate open");
  var inventory=InventoryManager.Instance;
  var copper=StartingResourcesSettings.Resolve((int)Item.Copper);
  int oreCount=Count("resources","ore","gems","ore-variety");inventory.AddStartingItem(copper,10);
  Check(Count("resources","ore","gems","ore-variety")==oreCount,"Inventory transfers do not count as mined ore");
  int value=ShopManager.Instance.GetSellValue(copper,10);
  Check(ShopManager.Instance.TrySell(copper,10),"Actual shop transaction succeeds");
  Check(Matches("sales",value)&&Matches("run-sales",value)&&Matches("sale-single",value),"Actual shop sale updates the drawn sales tasks");
  Check(Line("line-sales")==value&&Tally("sales")==value,"Lifetime sales line and run tally count the credited sale");
  var recipe=ExoticCatalog.AllRecipes.First(r=>r && r.output && r.output.item==Item.Torche);
  foreach(var ingredient in recipe.ingredients)inventory.AddStartingItem(ingredient.item,ingredient.amount*3);
  Check(CraftingService.TryCraft(recipe,inventory,1),"Actual torch recipe succeeds");
  Check(Matches("crafted",recipe.outputAmount)&&Matches("torches-crafted",recipe.outputAmount),"Actual output quantity feeds drawn crafting and torch tasks");
  Check(Tally("crafted")==recipe.outputAmount&&Tally("torches-crafted")==recipe.outputAmount&&Line("line-crafted")==recipe.outputAmount,"Torch craft feeds crafted and torch tallies and the lifetime line");
  var exotic=ExoticCatalog.AllRecipes.First(r=>r && r.exotic && r.output && r.output.item==Item.IronLadder);
  RecipeUnlocks.LearnExotic(exotic);
  foreach(var ingredient in exotic.ingredients)inventory.AddStartingItem(ingredient.item,ingredient.amount*2);
  Check(CraftingService.TryCraft(exotic,inventory,1),"Actual exotic ladder recipe succeeds");
  // Exotic tasks count crafting batches, ladder and crafted tasks the actual output.
  Check(Matches("exotics",1)&&Matches("ladders-crafted",exotic.outputAmount)&&Matches("crafted",recipe.outputAmount+exotic.outputAmount),"Drawn exotic, ladder and crafting tasks count the actual craft");
  Check(Tally("exotics")==1&&Tally("ladders-crafted")==exotic.outputAmount&&Tally("crafted")==recipe.outputAmount+exotic.outputAmount,"Exotic craft counts one batch, its ladders and crafted items");
  Check(CraftingService.TryCraft(exotic,inventory,1),"Repeated exotic recipe succeeds");
  Check(Matches("exotics",2)&&Matches("ladders-crafted",exotic.outputAmount*2),"Repeated exotic crafts count in every period");
  Check(Tally("exotics")==2&&Tally("ladders-crafted")==2*exotic.outputAmount&&Line("line-crafted")==recipe.outputAmount+2*exotic.outputAmount,"Repeated exotic craft counts batches, ladders and the lifetime line");
  Check(Line("line-exotics")==1,"Lifetime exotic line counts unique recipes");
  string state=State();long rewards=MetaProgression.Profile.periodRewardXp;
  bool saved=false;yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot,this,(ok,message)=>saved=ok);
  Check(saved,"World and challenge profile save");
  Check(RunNavigation.LoadGame(GameSaveSystem.ActiveSlot,out _),"Real save reload accepted");yield return Ready();
  Check(State()==state&&MetaProgression.Profile.periodRewardXp==rewards,"Restore neither loses nor replays challenge rewards");
  player=Object.FindFirstObjectByType<PlayerMovement>();
  Check(Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Reload camera follows current player");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;JsonUtility.FromJsonOverwrite(gps,GpsSettings.Document.tests);Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
