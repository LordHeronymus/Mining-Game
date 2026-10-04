using System;
using System.Collections;
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
 int Count(string metric)=>MetaProgression.GetChallenges(ChallengePeriod.Daily).First(c=>c.metric==metric).current;
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
  int oreCount=Count("resources");inventory.AddStartingItem(copper,10);
  Check(Count("resources")==oreCount,"Inventory transfers do not count as mined ore");
  int value=ShopManager.Instance.GetSellValue(copper,10);
  Check(ShopManager.Instance.TrySell(copper,10),"Actual shop transaction succeeds");
  Check(Count("sales")==Math.Min(1000,value),"Actual shop sale updates challenge proceeds");
  var recipe=ExoticCatalog.AllRecipes.First(r=>r && r.output && r.output.item==Item.Torche);
  foreach(var ingredient in recipe.ingredients)inventory.AddStartingItem(ingredient.item,ingredient.amount*3);
  Check(CraftingService.TryCraft(recipe,inventory,1),"Actual torch recipe succeeds");
  Check(Count("crafted")==Math.Min(5,recipe.outputAmount) && Count("torches")==Math.Min(3,recipe.outputAmount),"Actual output quantity feeds crafting and torch tasks");
  var exotic=ExoticCatalog.AllRecipes.First(r=>r && r.exotic && r.output && r.output.item==Item.IronLadder);
  RecipeUnlocks.LearnExotic(exotic);
  foreach(var ingredient in exotic.ingredients)inventory.AddStartingItem(ingredient.item,ingredient.amount*2);
  Check(CraftingService.TryCraft(exotic,inventory,1),"Actual exotic ladder recipe succeeds");
  Check(Count("exotics")==1 && Count("ladders")==Math.Min(5,exotic.outputAmount),"Exotic and ladder tasks count actual output");
  Check(CraftingService.TryCraft(exotic,inventory,1),"Repeated exotic recipe succeeds");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).First(c=>c.metric=="exotics").current==Math.Min(3,exotic.outputAmount*2),"Repeated exotic crafts count in weekly goal");
  Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).First(c=>c.id=="lifetime-exotics").current==1,"Lifetime exotic collection counts unique recipes");
  int crafted=Count("crafted"),sold=Count("sales");long rewards=MetaProgression.Profile.periodRewardXp;
  bool saved=false;yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot,this,(ok,message)=>saved=ok);
  Check(saved,"World and challenge profile save");
  Check(RunNavigation.LoadGame(GameSaveSystem.ActiveSlot,out _),"Real save reload accepted");yield return Ready();
  Check(Count("crafted")==crafted&&Count("sales")==sold&&MetaProgression.Profile.periodRewardXp==rewards,"Restore neither loses nor replays challenge rewards");
  player=Object.FindFirstObjectByType<PlayerMovement>();
  Check(Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Reload camera follows current player");
  File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 void Cleanup(){if(clean)return;clean=true;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;JsonUtility.FromJsonOverwrite(gps,GpsSettings.Document.tests);Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
