using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MetaProgressionIntegrationChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible) throw new Exception("Start fresh at MainMenu.");
        var go = new GameObject("Meta progression integration checks");
        Object.DontDestroyOnLoad(go); go.AddComponent<MetaProgressionIntegrationRunner>();
        return "Started; report Temp/MetaProgressionIntegrationChecks.txt";
    }
}

public sealed class MetaProgressionIntegrationRunner : MonoBehaviour
{
    const string Report = "Temp/MetaProgressionIntegrationChecks.txt";
    const string Output = "Assets/Design/Tiefenhall/Metaprogression";
    string previousSave, previousMeta, previousTests, directory;
    int[] previousRecipes;
    bool cleaned;
    int checks;
    void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        checks++; File.AppendAllText(Report, "PASS " + name + "\n");
    }
    IEnumerator Start()
    {
        Directory.CreateDirectory("Temp"); Directory.CreateDirectory(Output);
        File.WriteAllText(Report, "Metaprogression integration\n");
        previousSave = GameSaveSystem.TestDirectory; previousMeta = MetaProgression.TestDirectory;
        previousTests = JsonUtility.ToJson(GpsSettings.Tests); previousRecipes = RecipeUnlocks.CaptureRunState();
        directory = Path.GetFullPath("Temp/MetaIntegration-" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory); GameSaveSystem.TestDirectory = directory;
        MetaProgression.TestDirectory = Path.Combine(directory, "Profile");
        GpsSettings.Document.tests.testModeDisabled = true;
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            if (GameSaveSystem.TestDirectory != directory || MetaProgression.TestDirectory != Path.Combine(directory, "Profile"))
            {
                File.AppendAllText(Report, "INTERRUPTED: another editor task changed the test scope\n");
                Cleanup(); yield break;
            }
            bool more = false; object current = null; Exception failure = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception ex) { failure = ex; }
            if (failure != null) { File.AppendAllText(Report, "FAIL " + failure + "\n"); yield return ReturnHome(); Cleanup(); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n"); Cleanup();
    }
    IEnumerator Ready()
    {
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        { if (Time.realtimeSinceStartup > end) throw new Exception("Loading deadline"); yield return null; }
        yield return null; yield return null;
    }
    IEnumerator ReturnHome()
    {
        if (GameSaveSystem.TestDirectory != directory) yield break;
        if (GameSaveSystem.IsBusy) yield break;
        foreach (var panel in Object.FindObjectsByType<ExoticBlueprintPanel>(FindObjectsSortMode.None)) Object.Destroy(panel.gameObject);
        foreach (var panel in Object.FindObjectsByType<MetaProgressionPanel>(FindObjectsSortMode.None)) panel.Close();
        yield return null;
        if (!MainMenuController.IsVisible) RunNavigation.MainMenu();
        yield return Ready();
    }
    IEnumerator Shot(string name)
    {
        yield return null; yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG()); Object.Destroy(texture);
    }
    IEnumerator Save()
    {
        bool okay = false; string message = null;
        yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot, this, (success, text) => { okay = success; message = text; });
        Check(okay, "World + progression save succeeds: " + message);
    }
    IEnumerator Run()
    {
        Check(MetaProgression.Level == 1, "New isolated profile starts at level 1");
        MetaProgression.BeginRun("integration-fixture", new MetaRunState { runId = "integration-fixture", progressXp = MetaProgressionCatalog.XpForLevel(250) });
        MetaProgression.EndRun();
        Check(MetaProgression.Level == 250, "Prepared isolated veteran profile");
        Check(MetaProgression.Spend("health") && MetaProgression.Spend("energy") && MetaProgression.Spend("movement") && MetaProgression.Spend("jump") && MetaProgression.Spend("money") && MetaProgression.Spend("mining"), "All powerup types purchasable");
        Check(MetaProgression.Spend("capacity") && MetaProgression.Spend("reach") && MetaProgression.Spend("torches") && MetaProgression.Spend("ladders"), "All comfort types purchasable");
        var expected = MetaProgression.CreateLoadout();
        var parent = GameObject.Find("ScreenCanvas").transform;
        var panel = MetaProgressionPanel.Show(parent);
        yield return Shot("Progression-Upgrades");
        Check(panel && !panel.IsReadOnly && GameplayInputBlocker.IsBlocked, "Home progression opens editable modal");
        panel.GetComponentsInChildren<Button>().First(b => b.name == "Open Herausforderungen").onClick.Invoke();
        yield return Shot("Progression-Challenges");
        panel.GetComponentsInChildren<Button>().First(b => b.name == "Challenges Back").onClick.Invoke(); yield return null;
        panel.GetComponentsInChildren<Button>().First(b => b.name == "Open Baupläne").onClick.Invoke();
        yield return Shot("Progression-Blueprints");
        panel.Close(); yield return null;
        Check(!GameplayInputBlocker.IsBlocked && Time.timeScale == 1, "Home modal releases input and pause");
        var resources = StartingResourcesSettings.Load();
        RunNavigation.NewGame("Meta-Prüfung"); yield return Ready();
        Check(!MainMenuController.IsVisible && GameSaveSystem.ActiveSlot == 1, "New run uses isolated free save slot");
        Check(MetaProgression.CurrentRun != null && MetaProgression.CurrentLoadout.startMoney == expected.startMoney, "New run freezes chosen build");
        var stats = StatsManager.Instance;
        var inventory = InventoryManager.Instance;
        var energy = Object.FindFirstObjectByType<EnergyManager>();
        energy.consumptionMultiplier = 0;
        Check(Mathf.Abs(stats.MaxHealth - stats.initialHealth * expected.healthMultiplier) < .01f, "Meta health applied exactly once");
        Check(stats.Money == resources.money + expected.startMoney, "Meta start money added exactly once");
        Check(stats.MaxEnergy > 0 && Mathf.Abs(energy.energy - stats.MaxEnergy) < .1f, "Initial energy fills improved capacity");
        Check(stats.EffectiveMoveSpeed >= stats.MoveSpeed * expected.movementMultiplier * .999f, "Meta movement applied");
        int startingTorches = resources.items.FirstOrDefault(i => i.itemId == (int)Item.Torche).amount;
        int startingLadders = resources.items.FirstOrDefault(i => i.itemId == (int)Item.Ladder).amount;
        Check(inventory.GetCount(StartingResourcesSettings.Resolve((int)Item.Torche)) == startingTorches + expected.startingTorches, "Comfort starting torches granted");
        Check(inventory.GetCount(StartingResourcesSettings.Resolve((int)Item.Ladder)) == startingLadders + expected.startingLadders, "Comfort starting ladders granted");
        Check(!MetaProgression.Spend("health") && !MetaProgression.RefundAll(), "Active run blocks respec and spending");
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var camera = Camera.main;
        Check(camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled, "Fresh run camera follow + border clamp active");
        var pause = Object.FindFirstObjectByType<RunPauseMenu>(); pause.Open();
        panel = MetaProgressionPanel.Show(parent = GameObject.Find("ScreenCanvas").transform, true);
        yield return null;
        Check(panel.IsReadOnly && panel.GetComponentsInChildren<Button>().All(b => !b.name.StartsWith("Buy ")), "Pause progression displays fixed build without purchase controls");
        panel.Close(); yield return null;
        Check(RunPauseMenu.IsOpen && GameplayInputBlocker.IsBlocked && Time.timeScale == 0, "Nested progression close preserves pause gate"); pause.Close();
        var essentials = ExoticCatalog.AllRecipes.Where(r => r.output && (r.output.item == Item.Torche || r.output.item == Item.Ladder || r.output.item == Item.Dynamite)).ToArray();
        Check(essentials.Select(r => r.output.item).Distinct().Count() == 3 && essentials.All(RecipeUnlocks.IsUnlocked), "Three essential recipes exist and have no meta or find gate");
        Check(ExoticCatalog.Recipes.Count >= 3 && ExoticCatalog.Recipes.All(r => !RecipeUnlocks.IsUnlocked(r)), "Exotic recipes start unknown every run");
        var world = ExoticWorldContent.Ensure(map);
        var state = world.CaptureState();
        Check(state.caches.Length > 0 && state.boulders.Length > 0, "Natural caverns contain blueprint caches and boulders");
        Check(world.TryDiscover(0), "Blueprint cache creates a real choice"); yield return null;
        Check(world.PendingChoices.Length == 3 && world.PendingChoices.Distinct().Count() == 3, "Veteran find presents three distinct unlocked recipes");
        yield return Shot("Exotic-Choice");
        var choice = Object.FindFirstObjectByType<ExoticBlueprintPanel>();
        var chosen = world.PendingChoices[0];
        choice.GetComponentsInChildren<Button>().First(b => b.name == "Learn " + chosen.exoticId).onClick.Invoke(); yield return null;
        Check(RecipeUnlocks.IsUnlocked(chosen) && !world.TryDiscover(0), "Choice learns recipe and consumes cache only once");
        foreach (var recipe in ExoticCatalog.Recipes)
        {
            RecipeUnlocks.LearnExotic(recipe);
            foreach (var ingredient in recipe.ingredients) inventory.AddStartingItem(ingredient.item, ingredient.amount);
            Check(CraftingService.TryCraft(recipe, inventory, 1), "Exotic craft executes: " + recipe.output.displayName);
        }
        var ladderItem = StartingResourcesSettings.Resolve((int)Item.IronLadder);
        var lavaItem = StartingResourcesSettings.Resolve((int)Item.LavaLamp);
        Check(CompactHud.IsHotbarItem(ladderItem) && CompactHud.IsHotbarItem(lavaItem), "Exotic building items supported by hotbar");
        var ladderCell = state.caches[0].cell;
        var lampCell = state.caches[1].cell;
        var ladders = map.GetComponent<LadderMap>();
        Check(ladders.TryPlace(ladderCell, inventory, map.Terrain.GetCellCenterWorld(ladderCell), 10, ladderItem), "Crafted iron ladder places through normal transaction");
        Check(PlacedTorch.TryPlace(map, lavaItem, map.Terrain.GetCellCenterWorld(lampCell), map.Terrain.GetCellCenterWorld(lampCell), 10), "Crafted lava lamp places through normal transaction");
        var rock = Object.FindObjectsByType<MineBoulder>(FindObjectsSortMode.None).First(b => b.Map == map && b.Health > 0);
        var rockCell = rock.Capture().cell;
        Check(rock.Hit(), "Crafted hammer damages natural boulder");
        float rockHealth = rock.Health;
        var mine = player.GetComponent<TileMiner>();
        Vector3Int oreCell = default; bool found = false;
        for (int depth = 4; depth < 90 && !found; depth++)
            for (int x = -map.GeneratedWidth / 2; x < map.GeneratedWidth / 2; x++)
            {
                var cell = new Vector3Int(x, -depth, 0); var block = map.GetBlockAt(cell);
                if (map.GetOreAt(cell) && block && block.itemDrop && block.hardnessIndex <= inventory.EquippedPickaxeMaximumHardness && inventory.CanAdd(block.itemDrop, 4))
                { oreCell = cell; found = true; break; }
            }
        Check(found, "Found real mineable natural ore");
        yield return Save();
        var baselineHealth = stats.Health; var baselineMoney = stats.Money; string runId = MetaProgression.CurrentRun.runId;
        long before = MetaProgression.TotalXp;
        Check(mine.CompleteMining(oreCell), "Real mining handler removes ore");
        Check(MetaProgression.TotalXp > before, "Real natural mining awards permanent XP");
        long earned = MetaProgression.TotalXp; MetaProgression.Save();
        Check(RunNavigation.LoadGame(1, out _), "Reload accepted"); yield return Ready();
        player = Object.FindFirstObjectByType<PlayerMovement>(); map = Object.FindFirstObjectByType<MapGenerator>();
        mine = player.GetComponent<TileMiner>(); stats = StatsManager.Instance;
        Object.FindFirstObjectByType<EnergyManager>().consumptionMultiplier = 0;
        Check(MetaProgression.CurrentRun.runId == runId, "Run identity persists on reload");
        Check(Mathf.Abs(stats.Health - baselineHealth) < .1f && stats.Money == baselineMoney, "Reload preserves saved stats without granting starting rewards twice");
        Check(RecipeUnlocks.IsUnlocked(chosen), "Found exotic recipe persists inside saved run");
        Check(map.Terrain.HasTile(oreCell) && mine.CompleteMining(oreCell), "Earlier world snapshot restores mineable ore");
        Check(MetaProgression.TotalXp == earned, "Re-mining same source after rollback grants no duplicate XP");
        Check(!ExoticWorldContent.Ensure(map).TryDiscover(0), "Saved claimed blueprint cache remains consumed");
        Check(map.GetComponent<LadderMap>().Tiles.GetTile(ladderCell) == Resources.Load<UnityEngine.Tilemaps.Tile>("Exotics/IronLadderTile"), "Save reload preserves iron ladder tile identity");
        Check(PlacedTorch.Active.Any(t => t && t.OwnerMap == map && t.Cell == lampCell && t.IsLavaLamp), "Save reload preserves placed lava lamp light identity");
        Check(ExoticWorldContent.Ensure(map).CaptureState().boulders.Any(b => b.cell == rockCell && b.health == rockHealth), "Save reload preserves damaged boulder health");
        Check(Camera.main.GetComponent<CameraFollow>().enabled && Camera.main.GetComponent<CameraFollow>().target == player.transform && Camera.main.GetComponent<CameraWorldBorderClamp>().enabled, "Reload camera follow and clamp remain active");
        yield return ReturnHome();
        Check(MetaProgression.TotalXp >= earned && MetaProgression.CurrentRun == null, "Returning home retains permanent XP and releases build");
        Check(MetaProgression.RefundAll(), "Respec is free between runs");
        RunNavigation.NewGame("Frischer Run"); yield return Ready();
        Check(ExoticCatalog.Recipes.All(r => !RecipeUnlocks.IsUnlocked(r)), "Next run forgets learned exotic recipes");
        Check(MetaProgression.CurrentLoadout.healthMultiplier == 1 && MetaProgression.CurrentLoadout.startMoney == 0, "Next run uses redistributed neutral build");
        string endedRun = MetaProgression.CurrentRun.runId;
        StatsManager.Instance.ApplyDamage(StatsManager.Instance.MaxHealth * 2);
        yield return null;
        Check(GameOverPanel.IsOpen && MetaProgression.CurrentRun.ended, "Real death finalizes current run without losing XP");
        var gameOver = Object.FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        yield return Shot("Progression-RunResult");
        yield return FinishAndRestart(gameOver);
        yield return Ready();
        Check(GameSaveSystem.ActiveSlot == 3 && MetaProgression.CurrentRun.runId != endedRun && !MetaProgression.CurrentRun.ended,
            "Real Retry creates new save slot and independent eligible run");
        Check(File.Exists(Path.Combine(directory, "slot-2.thsave")), "Retry preserves previous saved world");
        endedRun = MetaProgression.CurrentRun.runId;
        Object.FindFirstObjectByType<EnergyManager>().energy = 0;
        yield return null; yield return null;
        Check(GameOverPanel.IsOpen && MetaProgression.CurrentRun.ended, "Energy death finalizes current run");
        gameOver = Object.FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        yield return FinishAndRestart(gameOver);
        yield return Ready();
        Check(GameSaveSystem.ActiveSlot == 4 && MetaProgression.CurrentRun.runId != endedRun && !MetaProgression.CurrentRun.ended && !GameOverPanel.IsOpen,
            "Energy death Retry does not end new run during black transition");
        yield return ReturnHome();
        File.Copy("Temp/SelectionEdit-639266992421592508/slot-3.thsave", Path.Combine(directory, "slot-3.thsave"), true);
        Check(RunNavigation.LoadGame(3, out _), "Legacy save without meta extension accepted"); yield return Ready();
        Check(MetaProgression.CurrentRun.runId.StartsWith("legacy-") && MetaProgression.CurrentLoadout.healthMultiplier == 1, "Legacy save receives stable identity and neutral loadout");
        string legacyId = MetaProgression.CurrentRun.runId;
        Check(Camera.main.GetComponent<CameraFollow>().enabled && Camera.main.GetComponent<CameraWorldBorderClamp>().enabled, "Legacy load camera remains active");
        yield return ReturnHome();
        Check(RunNavigation.LoadGame(3, out _), "Repeated legacy load accepted after header migration"); yield return Ready();
        Check(MetaProgression.CurrentRun.runId == legacyId, "Legacy identity remains stable after date header migration");
        yield return ReturnHome();
    }
    IEnumerator FinishAndRestart(GameOverPanel gameOver)
    {
        gameOver.GetComponentsInChildren<Button>().First(b => b.name == "Continue").onClick.Invoke(); yield return null;
        var result = Object.FindFirstObjectByType<ExpeditionResultPanel>();
        Check(result && ExpeditionResultPanel.IsOpen, "Weiter opens expedition results");
        result.GetComponentsInChildren<Button>().First(b => b.name == "Main Menu").onClick.Invoke();
        yield return Ready(); RunNavigation.NewGame("Frischer Run");
    }
    void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        GpsSettings.Document.tests = JsonUtility.FromJson<GameplayTestSettingsData>(previousTests);
        RecipeUnlocks.RestoreRunState(previousRecipes);
        if (GameSaveSystem.TestDirectory == directory) GameSaveSystem.TestDirectory = previousSave;
        if (MetaProgression.TestDirectory == Path.Combine(directory, "Profile")) MetaProgression.TestDirectory = previousMeta;
        Object.Destroy(gameObject);
    }
    void OnDestroy() { if (!cleaned && previousTests != null) Cleanup(); }
}
