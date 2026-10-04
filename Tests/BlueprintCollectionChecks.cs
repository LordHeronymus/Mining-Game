using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class BlueprintCollectionChecks
{
    public static object Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked || MetaProgression.CurrentRun!=null)
            throw new Exception("Start at an unobstructed homescreen.");
        new GameObject("Blueprint collection checks").AddComponent<BlueprintCollectionProbe>();return "Started";
    }
}
public sealed class BlueprintCollectionProbe:MonoBehaviour
{
    const string Report="Temp/BlueprintCollectionChecks.txt";
    string previous; MetaProgressionPanel owner; int checks; bool cleaned;
    Button Button(string name)=>owner.GetComponentsInChildren<Button>(true).First(b=>b.name==name);
    void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Report,"PASS "+(++checks)+" "+message+"\n");}
    IEnumerator Start()
    {
        previous=MetaProgression.TestDirectory;MetaProgression.TestDirectory=Path.GetFullPath("Temp/BlueprintUi-"+DateTime.UtcNow.Ticks);
        File.WriteAllText(Report,"");var routine=Run();
        try{while(routine.MoveNext())yield return routine.Current;}finally{Cleanup();}
    }
    IEnumerator Run()
    {
        MetaProgression.Profile.runs.Add(new MetaRunState{runId="blueprint-ui",progressXp=MetaProgressionCatalog.XpForLevel(15),ended=true});
        MetaProgression.BeginRun("blueprint-ui");MetaProgression.EndRun();
        Check(MetaProgression.Level==15,"Isolated profile at level 15");
        long xp=MetaProgression.TotalXp;
        owner=MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform);
        Button("Open Baupläne").onClick.Invoke();yield return null;
        var panel=Object.FindFirstObjectByType<BlueprintCollectionPanel>();var scroll=panel.GetComponentInChildren<ScrollRect>();
        foreach(var recipe in ExoticCatalog.Recipes){
            string state=scroll.content.Find("Blueprint "+recipe.exoticId+"/State").GetComponent<TMP_Text>().text;
            Check(state==(recipe.metaUnlockLevel<=15?"Im Fundpool":"Gesperrt"),recipe.exoticId+" matches level gate without teaching recipe");
        }
        Check(panel.GetComponentsInChildren<Button>().Length==1,"Collection has only Back; no crafting or unlock side effects");
        yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Assets/Design/Tiefenhall/ProgressionTimelinePrototypes/Implemented/Blueprints-Level15.png",texture.EncodeToPNG());Object.Destroy(texture);
        Button("Blueprints Back").onClick.Invoke();yield return null;
        Check(owner.ShowingOverview && MetaProgression.TotalXp==xp,"Return preserves XP and returns to timeline");
        owner.OpenUpgrades();yield return null;
        Check(owner.transform.Find("Upgrades Layout").gameObject.activeSelf && !owner.GetComponentsInChildren<Button>(true).Any(b=>b.name.StartsWith("Tab ")),"Direct points entry opens standalone upgrades without tabs");
        int points=MetaProgression.AvailablePowerPoints;
        Button("Buy money").onClick.Invoke();yield return null;
        Check(MetaProgression.GetRank("money")==1 && MetaProgression.AvailablePowerPoints==points-1,"Standalone purchase spends one point");
        Button("Refund Upgrades").onClick.Invoke();yield return null;
        Check(MetaProgression.GetRank("money")==0 && MetaProgression.AvailablePowerPoints==points,"Standalone refund returns points");
        Button("Back").onClick.Invoke();yield return null;
        Check(owner.ShowingOverview && MetaProgression.TotalXp==xp,"Upgrades return preserves profile experience");
        owner.Close();yield return null;
        Check(!GameplayInputBlocker.IsBlocked && Time.timeScale==1,"Parent close releases pause and input");
        File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
    }
    void Cleanup(){if(cleaned)return;cleaned=true;if(owner)owner.Close();MetaProgression.EndRun();MetaProgression.TestDirectory=previous;Destroy(gameObject);}
    void OnDestroy(){if(!cleaned)Cleanup();}
}
