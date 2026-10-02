using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class TiefenhallChecks
{
    public static object Main()
    {
        var probe = new GameObject("Tiefenhall Checks").AddComponent<TiefenhallProbe>();
        UnityEngine.Object.DontDestroyOnLoad(probe.gameObject);
        return "Checking real home buttons, animations, world saves, recovery and scene transitions. Results: Temp/TiefenhallChecks.txt";
    }
}
public sealed class TiefenhallProbe : MonoBehaviour
{
    readonly List<string> passed = new();
    void Start() => StartCoroutine(Observe());
    IEnumerator Observe()
    {
        var checks = Checks();
        while (true)
        {
            bool next; object yielded;
            try { next = checks.MoveNext(); yielded = next ? checks.Current : null; }
            catch (Exception ex) { Finish("FAIL: " + ex + "\n" + string.Join("\n", passed)); yield break; }
            if (!next) break;
            yield return yielded;
        }
        Finish("PASS (" + passed.Count + ")\n" + string.Join("\n", passed));
    }
    void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
    Button Button(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.name == name && x.GetComponent<HomeButtonFeedback>());
    bool RaycastButton(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, results);
        return results.Count > 0 && results[0].gameObject.GetComponentInParent<Button>() == button;
    }
    IEnumerator Until(Func<bool> condition, string name)
    {
        float end = Time.realtimeSinceStartup + 180;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new Exception("Timeout: " + name); yield return null; }
    }
    IEnumerator Checks()
    {
        Check(SceneManager.GetActiveScene().name == "MainMenu" && MainMenuController.IsVisible && !LoadingProgress.Active, "Home starts without gameplay loading screen");
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/TiefenhallSaveChecks-" + DateTime.UtcNow.Ticks);
        Check(!Button("Continue").interactable, "Continue disabled without a save");
        Check(RaycastButton(Button("New Game")), "New game is a real raycastable button");
        Button("Settings").onClick.Invoke();
        var settings = FindFirstObjectByType<SettingsPanel>();
        Check(settings.IsOpen && Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Home settings open and gate input");
        settings.GetComponentsInChildren<Button>().First(x => x.name == "Schließen").onClick.Invoke();
        Check(!settings.IsOpen && Time.timeScale == 1 && !GameplayInputBlocker.IsBlocked, "Home settings close and restore input");
        Button("Save Games").onClick.Invoke(); yield return null;
        Check(SaveSlotPanel.IsOpen && !Button("Slot 1").interactable, "Empty load slots cannot be loaded");
        Button("Back").onClick.Invoke(); yield return null;
        var cave = FindFirstObjectByType<HomeCaveVisual>(); float previous = cave.AnimationTime;
        yield return new WaitForSecondsRealtime(.3f);
        Check(cave.AnimationTime > previous, "Cave animation uses live unscaled time");
        var feedback = Button("New Game").GetComponent<HomeButtonFeedback>();
        feedback.OnPointerEnter(new PointerEventData(EventSystem.current)); yield return new WaitForSecondsRealtime(.25f);
        Check(Button("New Game").transform.localScale.x > 1 && Button("New Game").GetComponent<Image>().sprite.name.Contains("Active"), "Hover animates scale and golden button state");
        feedback.OnPointerExit(new PointerEventData(EventSystem.current));
        Button("New Game").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "SampleScene" && !LoadingProgress.Active && !RunNavigation.IsTransitioning, "new run");
        var map = FindFirstObjectByType<MapGenerator>(); var player = FindFirstObjectByType<PlayerMovement>();
        var camera = Camera.main.GetComponent<CameraFollow>();
        Check(map.IsGenerated && camera.enabled && camera.target == player.transform && Camera.main.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,
            "New run has camera follow and border clamp");
        var pause = FindFirstObjectByType<RunPauseMenu>(); pause.Open();
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Pause freezes gameplay");
        var iron = StartingResourcesSettings.Resolve((int)Item.Iron);
        InventoryManager.Instance.AddStartingItem(iron, 7);
        InventoryManager.Instance.AddStartingItem(StartingResourcesSettings.Resolve((int)Item.CrystalPendant), 1);
        StatsManager.Instance.AddMoney(4321); StatsManager.Instance.SetHealthForDebug(67);
        FindFirstObjectByType<EnergyManager>().energy = 73;
        var recipe = Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes").First(x => x.output && x.output.item == Item.CrystalPendant);
        RecipeUnlocks.Unlock(recipe);
        var altarState = map.AltarChamber.CaptureState(); altarState.deposited = 7;
        map.AltarChamber.RestoreState(altarState);
        var storage = FindFirstObjectByType<SurfaceStorageBuilding>(); if (storage) storage.TryDeposit(iron, 2);
        var cell = new Vector3Int(8, -3, 0); map.RemoveBlock(cell);
        var ladderMap = map.GetComponent<LadderMap>(); ladderMap.EnsureTiles().SetTile(cell, ladderMap.segment);
        var torchCell = new Vector3Int(10, -3, 0); map.RemoveBlock(torchCell);
        PlacedTorch.CreateAt(map, StartingResourcesSettings.Resolve((int)Item.Torche), torchCell);
        var grass = map.GetComponent<SurfaceTallGrass>(); var firstGrass = grass.ActivePatches.FirstOrDefault();
        if (firstGrass) grass.RemoveWithoutYieldAt(firstGrass.SurfaceCellX);
        var trees = FindFirstObjectByType<SurfaceTrees>();
        var tree = ChoppableTree.ActiveTrees.FirstOrDefault(); if (tree) tree.Hit(player.transform.position);
        var forestState = trees.CaptureRunState();
        var miner = player.GetComponent<TileMiner>();
        miner.RestoreRunState(new[] { new SavedMiningCell { cell = new Vector3Int(12, -3), progress = .65f, hitAgo = 1, pendingDrop = 2 } });
        var hud = FindFirstObjectByType<CompactHud>();
        int[] hotbar = Enumerable.Repeat(-1, 8).ToArray(); hotbar[0] = (int)Item.Torche;
        hud.RestoreRunSlots(hotbar, 1);
        yield return null;
        int seed = map.ActiveSeed, money = StatsManager.Instance.Money, ironCount = InventoryManager.Instance.GetCount(iron);
        bool hadStorage = storage;
        int stored = storage ? storage.GetStoredCount(iron) : 0, grassCount = grass.PatchCount;
        var state = map.GetComponent<PlayerMapDiscovery>().CaptureState();
        Vector3 position = player.transform.position;
        int terrainHash = Hash(map.Terrain), oreHash = Hash(map.OreOverlay), artifactHash = Hash(map.ArtifactOverlay);
        bool saved = false; string message = null;
        yield return GameSaveSystem.Save(1, this, (ok, text) => { saved = ok; message = text; });
        Check(saved, "Save slot writes successfully: " + message);
        Check(File.Exists(GameSaveSystem.SlotPath(1)) && !File.Exists(GameSaveSystem.SlotPath(1) + ".tmp"), "Atomic save leaves completed file");
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Saving preserves pause state");
        // A second write creates a recovery copy. Corruption must load the first complete write.
        StatsManager.Instance.AddMoney(10);
        yield return GameSaveSystem.Save(1, this, (ok, text) => saved = ok);
        Check(saved && File.Exists(GameSaveSystem.SlotPath(1) + ".bak"), "Overwrite retains prior recovery copy");
        byte[] damaged = File.ReadAllBytes(GameSaveSystem.SlotPath(1)); damaged[damaged.Length - 1] ^= 1;
        File.WriteAllBytes(GameSaveSystem.SlotPath(1), damaged);
        Check(GameSaveSystem.GetSummary(1)?.recovered == true, "Checksum rejects damaged current file and finds backup");
        // Use the actual navigation path, destroying persistent run managers and restoring another scene.
        Check(RunNavigation.LoadGame(1, out string error), "Validated load starts: " + error);
        yield return Until(() => !LoadingProgress.Active && !RunNavigation.IsTransitioning && FindFirstObjectByType<MapGenerator>() != map, "save reload");
        map = FindFirstObjectByType<MapGenerator>(); player = FindFirstObjectByType<PlayerMovement>();
        pause = FindFirstObjectByType<RunPauseMenu>(); pause.Open();
        Check(map.ActiveSeed == seed && Hash(map.Terrain) == terrainHash && Hash(map.OreOverlay) == oreHash && Hash(map.ArtifactOverlay) == artifactHash,
            "All terrain, ore and artifact cells survive real scene reload");
        Check(StatsManager.Instance.Money == money && Mathf.Abs(StatsManager.Instance.Health - 67) < .1f, "Money and health restored from recovery copy");
        Check(InventoryManager.Instance.GetCount(iron) == ironCount && InventoryManager.Instance.EnergyCapacityLevel == 2 && RecipeUnlocks.IsUnlocked(recipe), "Inventory, upgrade level and purchased recipe restored");
        Check(map.AltarChamber.DepositedUltronium == 7 && map.AltarChamber.CaptureState().x == altarState.x && map.AltarChamber.CaptureState().y == altarState.y, "Altar progress and chamber position restored");
        Check(Mathf.Abs(FindFirstObjectByType<EnergyManager>().energy - 73) < .2f, "Current energy restored without refill");
        Check(Vector3.Distance(player.transform.position, position) < .2f, "Player position restored");
        Check(map.GetComponent<LadderMap>().Has(cell) && PlacedTorch.Active.Any(x => x.OwnerMap == map && x.Cell == torchCell), "Built ladder and placed torch restored");
        Check(!hadStorage || FindFirstObjectByType<SurfaceStorageBuilding>().GetStoredCount(iron) == stored, "Surface storage restored");
        Check(map.GetComponent<SurfaceTallGrass>().PatchCount == grassCount, "Cut grass does not reappear on load");
        var restoredForest = FindFirstObjectByType<SurfaceTrees>().CaptureRunState();
        Check(restoredForest.trees.Length == forestState.trees.Length && forestState.trees.All(x => restoredForest.trees.Any(y => x.cell == y.cell && x.health == y.health && x.variant == y.variant)), "Tree population, variant and damage restored");
        var mined = player.GetComponent<TileMiner>().CaptureRunState();
        Check(mined.Any(x => x.cell == new Vector3Int(12, -3) && Mathf.Abs(x.progress - .65f) < .001f && x.pendingDrop == 2), "Partial mining progress and predetermined drop restored");
        hud = FindFirstObjectByType<CompactHud>();
        Check(hud.SelectedSlot == 1 && hud.slots[0] && hud.slots[0].item == Item.Torche && hud.slots.Skip(1).All(x => !x), "Hotbar layout and selection restored");
        var discovered = map.GetComponent<PlayerMapDiscovery>().CaptureState();
        Check(discovered.seed == state.seed && discovered.discoveredBits.SequenceEqual(state.discoveredBits), "Explored map restored");
        Check(Camera.main.GetComponent<CameraFollow>().target == player.transform && Camera.main.GetComponent<CameraFollow>().enabled, "Loaded run follows player");
        Button("Save").onClick.Invoke(); yield return null;
        Check(SaveSlotPanel.IsOpen && Button("Slot 2").interactable && RaycastButton(Button("Slot 2")), "Pause opens interactive manual save slots");
        Button("Back").onClick.Invoke(); yield return null;
        Button("Home").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu" && !RunNavigation.IsTransitioning, "saved return home");
        Check(GameSaveSystem.GetSummary(0) != null && Button("Continue").interactable, "Return home creates autosave and enables Continue");
        Check(!StatsManager.Instance && !InventoryManager.Instance && !AudioManager.Instance && !GameplayInputBlocker.IsBlocked && Time.timeScale == 1, "Home has no persistent gameplay managers or input gates");
        Button("Continue").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "SampleScene" && !LoadingProgress.Active && !RunNavigation.IsTransitioning, "continue autosave");
        Check(FindFirstObjectByType<MapGenerator>().ActiveSeed == seed, "Continue loads most recent actual run");
        GameSaveSystem.TestDirectory = null;
    }
    int Hash(Tilemap tilemap)
    {
        unchecked { int hash = 17; foreach (var tile in tilemap.GetTilesBlock(tilemap.cellBounds)) hash = hash * 31 + (tile ? tile.name.GetHashCode() : 0); return hash; }
    }
    void Finish(string report)
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/TiefenhallChecks.txt", report); Debug.Log(report);
        GameSaveSystem.TestDirectory = null; Destroy(gameObject);
    }
}
