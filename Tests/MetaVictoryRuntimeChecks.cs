using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Pipeline run_script entry: MetaVictoryRuntimeChecks.Main.
// Requires an already running isolated gameplay fixture; root owns its eventual cleanup.
public static class MetaVictoryRuntimeChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || MainMenuController.IsVisible || LoadingProgress.Active || RunNavigation.IsTransitioning)
            throw new Exception("An active gameplay fixture is required.");
        if (string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) throw new Exception("An isolated save directory is required.");
        string temporary = Path.GetFullPath("Temp") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(GameSaveSystem.TestDirectory).StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(MetaProgression.DirectoryPath).StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Both world and profile must use workspace Temp fixtures.");
        if (GameSaveSystem.NextFreeSlot() < 1 || GameSaveSystem.ActiveSlot < 1) throw new Exception("A saved active run and one free retry slot are required.");
        if (!MetaProgressionRuntime.RewardsAllowed || MetaProgression.CurrentRun.ended || GameplayInputBlocker.IsBlocked)
            throw new Exception("The isolated run must be active with normal reward rules and no modal open.");
        var go = new GameObject("Meta Victory Runtime Checks");
        Object.DontDestroyOnLoad(go); go.AddComponent<MetaVictoryRuntimeProbe>();
        return "Started; report Temp/MetaVictoryRuntimeChecks.txt";
    }
}

public sealed class MetaVictoryRuntimeProbe : MonoBehaviour
{
    const string Report = "Temp/MetaVictoryRuntimeChecks.txt";
    const string Screenshot = "Assets/Design/Tiefenhall/Metaprogression/Progression-Victory.png";
    string saveDirectory, profileDirectory;
    int checks;
    bool ScopeValid => GameSaveSystem.TestDirectory == saveDirectory && MetaProgression.DirectoryPath == profileDirectory;

    void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++; File.AppendAllText(Report, "PASS " + name + "\n");
    }
    IEnumerator Start()
    {
        saveDirectory = GameSaveSystem.TestDirectory; profileDirectory = MetaProgression.DirectoryPath;
        File.WriteAllText(Report, "Actual altar victory and retry checks\n");
        var routines = new Stack<IEnumerator>(); routines.Push(Run());
        while (routines.Count > 0)
        {
            if (!ScopeValid) { File.AppendAllText(Report, "INTERRUPTED: fixture scope changed\n"); Object.Destroy(gameObject); yield break; }
            object current = null; bool more = false; Exception failure = null;
            try { more = routines.Peek().MoveNext(); if (more) current = routines.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                File.AppendAllText(Report, "FAIL " + failure + "\n");
                yield return ReturnHome(); Object.Destroy(gameObject); yield break;
            }
            if (!more) { routines.Pop(); continue; }
            if (current is IEnumerator nested) routines.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n"); Object.Destroy(gameObject);
    }

    IEnumerator Run()
    {
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var stats = StatsManager.Instance;
        var inventory = InventoryManager.Instance;
        var altar = map ? map.AltarChamber : null;
        Check(map && player && stats && inventory && altar && altar.CaptureState().placed, "Existing fixture contains a placed real altar");
        Check(stats.Health > 0 && !stats.HasWon && !GameVictoryPanel.IsOpen && !GameOverPanel.IsOpen, "Fixture starts alive and unfinished");
        Check(!MetaProgression.Profile.completedMilestones.Contains("first-victory"), "First-victory milestone is initially unclaimed");
        Object.FindFirstObjectByType<EnergyManager>()?.ResetForNewRun();
        string oldRun = MetaProgression.CurrentRun.runId;
        int oldSlot = GameSaveSystem.ActiveSlot, nextSlot = GameSaveSystem.NextFreeSlot();
        string oldSave = GameSaveSystem.SlotPath(oldSlot);
        Check(File.Exists(oldSave), "Old fixture save exists before victory");
        long oldLength = new FileInfo(oldSave).Length;
        var body = player.GetComponent<Rigidbody2D>();
        bool inRange = false;
        // Search only the altar's actual interaction rectangle, then invoke its real action.
        for (int y = 1; y <= 4 && !inRange; y++)
            for (int x = -2; x <= 2 && !inRange; x++)
            {
                var position = altar.AltarPosition + new Vector3(x * altar.CellSize, y * .5f * altar.CellSize, 0);
                if (!altar.Layout.IsOpen(map.Terrain.WorldToCell(position))) continue;
                player.transform.position = position;
                if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
                Physics2D.SyncTransforms(); inRange = altar.CanInteract;
            }
        Check(inRange, "Player is inside the actual altar interaction region");
        RunNavigation.EnsurePlayerCamera(player.transform);
        var ultronium = StartingResourcesSettings.Resolve((int)Item.Ultronium);
        int required = Mathf.Max(0, altar.RequiredUltronium - altar.DepositedUltronium);
        Check(required > 0 && inventory.AddStartingItem(ultronium, required), "Isolated fixture supplies required Ultronium");
        long before = MetaProgression.TotalXp;
        float started = Time.realtimeSinceStartup;
        Check(altar.TryDeposit(), "Real altar TryDeposit succeeds");
        Check(altar.IsCompleting && UltroniumAltarChamber.VictorySequenceActive, "Deposit starts the real completion sequence");
        float timeout = Time.realtimeSinceStartup + Mathf.Max(20, altar.victoryDelaySeconds + 10);
        while (!GameVictoryPanel.IsOpen)
        {
            if (Time.realtimeSinceStartup > timeout) throw new Exception("Victory presentation deadline");
            yield return null;
        }
        Check(Time.realtimeSinceStartup - started >= altar.victoryDelaySeconds * .9f, "Victory waited for configured completion duration");
        Check(stats.HasWon && MetaProgression.CurrentRun.ended && MetaProgression.CurrentRun.won, "Altar completion marks world and profile victory");
        Check(MetaProgression.Profile.completedMilestones.Contains("first-victory") && MetaProgression.CurrentRun.milestones.Contains("first-victory"), "First victory milestone is committed to profile and run");
        Check(MetaProgression.TotalXp >= before + 3000, "Actual first victory awards at least 3000 XP");
        long wonXp = MetaProgression.TotalXp;
        MetaProgressionRuntime.RecordVictory();
        Check(MetaProgression.TotalXp == wonXp, "Repeated victory callback grants no duplicate XP");
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Victory presentation pauses and blocks gameplay");
        yield return new WaitForSecondsRealtime(.4f); yield return new WaitForEndOfFrame();
        Directory.CreateDirectory(Path.GetDirectoryName(Screenshot));
        var picture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Screenshot, picture.EncodeToPNG()); Object.Destroy(picture);
        var victory = Object.FindFirstObjectByType<GameVictoryPanel>();
        victory.GetComponentsInChildren<Button>().First(button => button.name == "Continue").onClick.Invoke(); yield return null;
        var result = Object.FindFirstObjectByType<ExpeditionResultPanel>();
        Check(result && ExpeditionResultPanel.IsOpen, "Actual victory Weiter opens expedition results");
        result.GetComponentsInChildren<Button>().First(button => button.name == "Main Menu").onClick.Invoke();
        yield return Ready(); RunNavigation.NewGame("Nach dem Sieg");
        Check(RunNavigation.IsTransitioning, "New expedition after results begins a fresh run transition");
        yield return Ready();
        Check(GameSaveSystem.ActiveSlot == nextSlot && File.Exists(GameSaveSystem.SlotPath(nextSlot)), "Retry creates and initially saves the reserved free slot");
        Check(File.Exists(oldSave) && new FileInfo(oldSave).Length == oldLength, "Retry preserves the original fixture save");
        Check(MetaProgression.CurrentRun != null && MetaProgression.CurrentRun.runId != oldRun && !MetaProgression.CurrentRun.ended && !MetaProgression.CurrentRun.won, "Victory Retry has a fresh unfinished run identity");
        Check(ExoticCatalog.Recipes.All(recipe => !RecipeUnlocks.IsUnlocked(recipe)), "Victory Retry forgets run-local exotic recipes");
        player = Object.FindFirstObjectByType<PlayerMovement>();
        Check(Camera.main && Camera.main.GetComponent<CameraFollow>().enabled && Camera.main.GetComponent<CameraFollow>().target == player.transform &&
            Camera.main.GetComponent<CameraWorldBorderClamp>().enabled, "Fresh retry retains active player camera and world clamp");
        Check(MetaProgression.TotalXp >= wonXp, "Victory XP survives fresh-run transition");
        yield return ReturnHome();
        Check(MainMenuController.IsVisible && MetaProgression.CurrentRun == null && !GameplayInputBlocker.IsBlocked, "Test returns home and releases run input gates");
    }

    IEnumerator Ready()
    {
        float deadline = Time.realtimeSinceStartup + 180;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (!ScopeValid) yield break;
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Scene transition deadline");
            yield return null;
        }
        yield return null; yield return null;
    }
    IEnumerator ReturnHome()
    {
        if (!ScopeValid) yield break;
        float deadline = Time.realtimeSinceStartup + 180;
        while (GameSaveSystem.IsBusy || LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (!ScopeValid || Time.realtimeSinceStartup > deadline) yield break;
            yield return null;
        }
        if (!MainMenuController.IsVisible) RunNavigation.MainMenu();
        yield return Ready();
    }
}
