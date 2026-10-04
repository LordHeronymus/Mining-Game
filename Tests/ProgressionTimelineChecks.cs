using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class ProgressionTimelineChecks
{
    public static object Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked) throw new Exception("Start at an unobstructed homescreen.");
        var go=new GameObject("Progression timeline checks"); Object.DontDestroyOnLoad(go); go.AddComponent<ProgressionTimelineRunner>();
        return "Started: Temp/ProgressionTimelineChecks.txt";
    }
}
public sealed class ProgressionTimelineRunner:MonoBehaviour
{
    const string Report="Temp/ProgressionTimelineChecks.txt", Output="Assets/Design/Tiefenhall/ProgressionTimelinePrototypes/Implemented";
    string directory, oldSave, oldMeta, oldTests; int checks; bool cleaned;
    void Check(bool value,string name) { if(!value)throw new Exception(name); File.AppendAllText(Report,"PASS "+(++checks)+" "+name+"\n"); }
    Button Find(Component panel,string name)=>panel.GetComponentsInChildren<Button>(true).First(b=>b.name==name);
    IEnumerator Start()
    {
        oldSave=GameSaveSystem.TestDirectory; oldMeta=MetaProgression.TestDirectory; oldTests=JsonUtility.ToJson(GpsSettings.Tests);
        directory=Path.GetFullPath("Temp/Timeline-"+DateTime.UtcNow.Ticks); Directory.CreateDirectory(directory); Directory.CreateDirectory(Output);
        GameSaveSystem.TestDirectory=directory; MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        GpsSettings.Document.tests.testModeDisabled=true;
        File.WriteAllText(Report,"Progression timeline and real end-of-run flow\n");
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0) {
            if(GameSaveSystem.TestDirectory!=directory) { File.AppendAllText(Report,"INTERRUPTED: test scope changed\n"); Cleanup();yield break; }
            bool more=false; object current=null; Exception failure=null;
            try { more=stack.Peek().MoveNext(); if(more)current=stack.Peek().Current; } catch(Exception e){failure=e;}
            if(failure!=null){File.AppendAllText(Report,"FAIL "+failure+"\n");yield return ReturnHome();Cleanup();yield break;}
            if(!more){stack.Pop();continue;} if(current is IEnumerator nested) stack.Push(nested);else yield return current;
        }
        File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");Cleanup();
    }
    IEnumerator Ready()
    {
        float until=Time.realtimeSinceStartup+180;
        while(LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy){if(Time.realtimeSinceStartup>until)throw new Exception("Transition deadline");yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Shot(string name)
    {
        yield return new WaitForSecondsRealtime(.45f);yield return new WaitForEndOfFrame();
        var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());Object.Destroy(t);
    }
    void Seed(long xp)
    {
        MetaProgression.EndRun(); MetaProgression.Profile.runs.Clear(); MetaProgression.Profile.completedMilestones.Clear(); MetaProgression.Profile.upgrades.Clear();
        MetaProgression.Profile.runs.Add(new MetaRunState { runId="timeline-seed",progressXp=xp,ended=true });
        MetaProgression.BeginRun("timeline-seed"); MetaProgression.EndRun();
        Check(MetaProgression.Save(),"Isolated seed saved"); MetaProgression.Reload();
    }
    IEnumerator Run()
    {
        yield return Ready();
        Seed(MetaProgressionCatalog.XpForLevel(12)+MetaProgressionCatalog.LevelCost(12)/2);
        var canvas=GameObject.Find("ScreenCanvas");
        canvas.GetComponentsInChildren<Button>().First(b=>b.name=="Progression").onClick.Invoke();yield return null;
        var panel=Object.FindFirstObjectByType<MetaProgressionPanel>();
        var timeline=panel.GetComponentInChildren<ProgressionTimeline>();
        Check(panel.ShowingOverview && timeline.DisplayedLevel==12,"Home button opens overview at real profile level");
        Check(Mathf.Abs(timeline.XpFraction-.5f)<.001f,"XP gauge shows current-level fraction");
        Check(timeline.VisibleLevels.Contains(15) && timeline.VisibleLevels.Contains(35),"Upcoming blueprints visible");
        Check(timeline.transform.Find("Rewards/Milestone 15/Blueprint lava-lamp") || timeline.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Lavalampe"),"Lava blueprint uses actual catalog name");
        var badge=(RectTransform)timeline.transform.Find("Level Badge"); var track=(RectTransform)timeline.transform.Find("XP Track");
        Check(Mathf.Abs(badge.anchoredPosition.y-track.anchoredPosition.y)<.1f && badge.anchoredPosition.x+badge.rect.width/2>=track.anchoredPosition.x-track.rect.width/2,"Level badge joins the aligned XP gauge");
        Check(Time.timeScale==0 && GameplayInputBlocker.IsBlocked,"Overview pauses and owns input");
        Canvas.ForceUpdateCanvases();
        foreach(var symbol in timeline.GetComponentsInChildren<ProgressionSymbol>()) {
            var mesh=symbol.canvasRenderer.GetMesh();Check(mesh && mesh.vertexCount>=3,"Rendered navigation/check symbol: "+symbol.name);
        }
        foreach(var button in panel.GetComponentsInChildren<Button>()) if(button.IsInteractable())
            Check(button.navigation.selectOnDown && button.navigation.selectOnDown.transform.IsChildOf(panel.transform),"Navigation stays in modal: "+button.name);
        yield return Shot("Home-Level12");
        var before=timeline.VisibleLevels.ToArray();timeline.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1)});yield return null;
        Check(!before.SequenceEqual(timeline.VisibleLevels),"Wheel browses upcoming levels");
        before=timeline.VisibleLevels.ToArray();
        var drag=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,timeline.transform.position)};
        timeline.OnBeginDrag(drag);drag.position=RectTransformUtility.WorldToScreenPoint(null,timeline.transform.TransformPoint(new Vector3(-120,0,0)));timeline.OnDrag(drag);yield return null;
        Check(!before.SequenceEqual(timeline.VisibleLevels),"Dragging browses upcoming levels");
        timeline.Focus(1000);yield return null;Check(timeline.VisibleLevels.Last()==1000 && !Find(timeline,"Later Levels").interactable,"Browsing ends at level1000");
        Find(timeline,"Level Badge").onClick.Invoke();yield return null;Check(timeline.VisibleLevels.Contains(12),"Level badge returns to current position");
        Find(panel,"Open Upgrades").onClick.Invoke();yield return null;
        Check(!panel.ShowingOverview && !panel.IsReadOnly,"Upgrades are editable at home");
        int points=MetaProgression.AvailablePowerPoints;Find(panel,"Buy money").onClick.Invoke();yield return null;
        Check(MetaProgression.GetRank("money")==1 && MetaProgression.AvailablePowerPoints==points-1,"Real spend updates persisted upgrade");
        Find(panel,"Back").onClick.Invoke();yield return null;Check(panel.ShowingOverview,"Detail Back returns to overview");
        Find(panel,"Open Herausforderungen").onClick.Invoke();yield return null;
        Check(panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Meilensteine"),"Challenges remain reachable");Find(panel,"Challenges Back").onClick.Invoke();yield return null;
        Find(panel,"Open Baupläne").onClick.Invoke();yield return null;Check(panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Im Fundpool"),"Catalog distinguishes find-pool unlocks");
        Find(panel,"Blueprints Back").onClick.Invoke();yield return null;
        // Fit the actual modal into three root sizes without modifying the game camera.
        var root=(RectTransform)panel.transform; var anchorsMin=root.anchorMin;var anchorsMax=root.anchorMax;
        root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);
        foreach(var size in new[]{new Vector2(1920,1080),new Vector2(1280,1024),new Vector2(2560,1080)}) {
            root.sizeDelta=size;yield return null;
            var board=(RectTransform)panel.transform.Find("Progression Overview/Board");var corners=new Vector3[4];board.GetWorldCorners(corners);
            Check(corners.All(v=>{var p=root.InverseTransformPoint(v);return Mathf.Abs(p.x)<=size.x/2 && Mathf.Abs(p.y)<=size.y/2;}),"Overview stays within "+size);
        }
        root.anchorMin=anchorsMin;root.anchorMax=anchorsMax;root.offsetMin=root.offsetMax=Vector2.zero;
        panel.Close();yield return null;Check(!GameplayInputBlocker.IsBlocked && Time.timeScale==1,"Closing overview restores input and time");
        foreach(int level in new[]{1,199,200,201,204,205,999,1000}) {
            Seed(MetaProgressionCatalog.XpForLevel(level));panel=MetaProgressionPanel.Show(canvas.transform);yield return null;
            timeline=panel.GetComponentInChildren<ProgressionTimeline>();
            Check(timeline.DisplayedLevel==level && timeline.VisibleLevels.All(l=>l>=1 && l<=1000),"Boundary level "+level+" renders valid milestones");
            Check(ProgressionTimeline.PowerAt(level)==(level>1 && level<=200?1:0),"Power reward at "+level+" matches catalog");
            Check(ProgressionTimeline.ComfortAt(level)==(level>200 && level%5==0?1:0),"Comfort reward at "+level+" matches catalog");
            if(level==1000) {Check(timeline.XpFraction==1,"Maximum level gauge stays full");yield return Shot("Level1000");}
            panel.Close();yield return null;
        }
        MetaProgression.BeginRun("zero-xp");MetaProgression.RecordRunFinished(false);
        var emptyResult=ExpeditionResultPanel.Show(canvas.transform);yield return null;
        Check(emptyResult && emptyResult.EarnedXp==0 && emptyResult.AnimationComplete,"Zero-XP result at max level completes immediately");
        Check(emptyResult.GetComponentInChildren<ProgressionTimeline>().XpFraction==1,"Result at level1000 has a full gauge");
        Object.Destroy(emptyResult.gameObject);yield return null;MetaProgression.EndRun();
        Seed(MetaProgressionCatalog.XpForLevel(14)+MetaProgressionCatalog.LevelCost(14)-300);
        RunNavigation.NewGame("Timeline-Test");yield return Ready();
        Check(MetaProgression.CurrentRun!=null && !MetaProgression.CurrentRun.ended,"Fresh isolated gameplay run");CheckCamera();
        MetaProgression.RecordDepth(100);MetaProgression.RecordDiscovery("timeline-test");
        long earned=MetaProgression.CurrentRun.earnedXp, total=MetaProgression.TotalXp;
        Check(earned>300,"Real reward events cross upcoming blueprint level");
        StatsManager.Instance.ApplyDamage(StatsManager.Instance.MaxHealth*2);yield return null;
        Check(GameOverPanel.IsOpen && !ExpeditionResultPanel.IsOpen,"Death first opens original game-over panel only");
        var over=Object.FindFirstObjectByType<GameOverPanel>();Check(Find(over,"Continue").GetComponentInChildren<TMP_Text>().text=="Weiter","Game over exposes Weiter");
        yield return Shot("GameOver-Continue");Find(over,"Continue").onClick.Invoke();yield return null;
        var result=Object.FindFirstObjectByType<ExpeditionResultPanel>();
        Check(result && ExpeditionResultPanel.IsOpen && !GameOverPanel.IsOpen && over.GetComponent<CanvasGroup>().alpha==0,"Weiter replaces game over with result");
        Check(result.EarnedXp==earned && result.FinalXp==total,"Result reads committed XP exactly once");
        Check(Time.timeScale==0 && GameplayInputBlocker.IsBlocked,"Result retains pause without an unblocked frame");
        Check(ExpeditionResultPanel.Show(result.transform.parent)==result,"Repeated result request reuses existing view");
        yield return new WaitForSecondsRealtime(3.7f);
        Check(result.AnimationComplete && result.GetComponentInChildren<ProgressionTimeline>().DisplayedLevel==MetaProgression.Level,"XP animation completes while paused");
        Check(MetaProgression.TotalXp==total,"XP animation never grants XP again");
        Check(result.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="NEU") && result.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Im Fundpool"),"New exotic blueprint highlighted as fund-pool unlock");
        yield return Shot("Expedition-Result");Find(result,"Spend Points").onClick.Invoke();yield return Ready();yield return new WaitForSecondsRealtime(.4f);
        panel=Object.FindFirstObjectByType<MetaProgressionPanel>();
        Check(MainMenuController.IsVisible && panel && !panel.IsReadOnly && !panel.ShowingOverview && MetaProgression.CanEdit,"Punkte verteilen transitions home and opens editable upgrades");
        Check(MetaProgression.TotalXp==total,"Result-to-home transition preserves XP");panel.Close();yield return null;
        RunNavigation.NewGame("Timeline-Sieg");yield return Ready();CheckCamera();
        var map=Object.FindFirstObjectByType<MapGenerator>();var player=Object.FindFirstObjectByType<PlayerMovement>();var altar=map.AltarChamber;
        var body=player.GetComponent<Rigidbody2D>();bool inRange=false;
        for(int y=1;y<=4 && !inRange;y++)for(int x=-2;x<=2 && !inRange;x++) {
            var position=altar.AltarPosition+new Vector3(x*altar.CellSize,y*.5f*altar.CellSize,0);
            if(!altar.Layout.IsOpen(map.Terrain.WorldToCell(position)))continue;
            player.transform.position=position;if(body){body.position=position;body.linearVelocity=Vector2.zero;}Physics2D.SyncTransforms();inRange=altar.CanInteract;
        }
        Check(inRange,"Player reaches actual altar");RunNavigation.EnsurePlayerCamera(player.transform);
        InventoryManager.Instance.AddStartingItem(StartingResourcesSettings.Resolve((int)Item.Ultronium),Mathf.Max(1,altar.RequiredUltronium-altar.DepositedUltronium));
        Check(altar.TryDeposit(),"Actual altar starts victory");
        float until=Time.realtimeSinceStartup+35;while(!GameVictoryPanel.IsOpen){if(Time.realtimeSinceStartup>until)throw new Exception("Victory deadline");yield return null;}
        Check(!ExpeditionResultPanel.IsOpen && MetaProgression.CurrentRun.won,"Victory first displays its original panel");
        var victory=Object.FindFirstObjectByType<GameVictoryPanel>();yield return Shot("Victory-Continue");Find(victory,"Continue").onClick.Invoke();yield return null;
        result=Object.FindFirstObjectByType<ExpeditionResultPanel>();Check(result && !GameVictoryPanel.IsOpen,"Victory Weiter opens same result view");
        total=MetaProgression.TotalXp;Find(result,"Main Menu").onClick.Invoke();yield return Ready();
        Check(MainMenuController.IsVisible && !MetaProgressionPanel.IsOpen && !ExpeditionResultPanel.IsOpen && MetaProgression.TotalXp==total,"Early Hauptmenü ends presentation and returns home without duplicate XP");
        // Saving an unfinished expedition must never launch the result flow.
        RunNavigation.NewGame("Timeline-Pause");yield return Ready();CheckCamera();
        var pause=Object.FindFirstObjectByType<RunPauseMenu>();pause.Open();yield return null;Find(pause,"Home").onClick.Invoke();yield return Ready();
        Check(MainMenuController.IsVisible && !ExpeditionResultPanel.IsOpen && MetaProgression.Profile.runs.Any(r=>!r.ended),"Save and home preserves unfinished run without a result screen");
        Check(!GameplayInputBlocker.IsBlocked && Time.timeScale==1 && MetaProgression.CurrentRun==null,"Final home has no input lock or active run");
    }
    void CheckCamera()
    {
        var player=Object.FindFirstObjectByType<PlayerMovement>();var camera=Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh gameplay has active player camera and world clamp");
    }
    IEnumerator ReturnHome()
    {
        if(GameSaveSystem.TestDirectory!=directory)yield break;
        var panel=Object.FindFirstObjectByType<MetaProgressionPanel>();if(panel)panel.Close();yield return null;
        MetaProgressionPanel.OpenUpgradesOnHome=false;if(!MainMenuController.IsVisible)RunNavigation.MainMenu();yield return Ready();
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;
        GpsSettings.Document.tests=JsonUtility.FromJson<GameplayTestSettingsData>(oldTests);
        if(GameSaveSystem.TestDirectory==directory)GameSaveSystem.TestDirectory=oldSave;
        if(MetaProgression.TestDirectory==Path.Combine(directory,"Profile"))MetaProgression.TestDirectory=oldMeta;
        Object.Destroy(gameObject);
    }
}
