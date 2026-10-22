using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bridges actual world events to the persistent profile. No XP comes from inventory transfers.
public sealed class MetaProgressionRuntime : MonoBehaviour
{
    static MetaProgressionRuntime instance;
    MapGenerator map;
    PlayerMovement player;
    PlayerMapDiscovery discovery;
    readonly HashSet<int> seen = new();
    readonly Dictionary<Vector2Int, int> regions = new();
    readonly HashSet<Vector2Int> reported = new();
    float nextSample, nextSave;
    bool bound, altarDiscovered;
    string boundRun;
    // One excursion from the surface. Position metrics count only during a trip that started at the surface,
    // so loading a deep save completes nothing. Blocked input and open panels skip samples but keep the trip.
    bool tripKnown, tripFromSurface;
    int tripPeakDepth, tripPeakLayer, tripStartBlocks;
    double tripStartWeight;
    public const string TorchesPlaced = "torches-placed", LaddersPlaced = "ladders-placed";
    const int VeinLimit = 1024;
    static readonly HashSet<Vector3Int> veinSeen = new();
    static readonly Queue<Vector3Int> veinOpen = new();
    static readonly Vector3Int[] veinSteps = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance) return;
        var root = new GameObject("Metaprogression");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<MetaProgressionRuntime>();
    }

    void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
    void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        Unbind();
        MetaProgression.Save();
    }
    void OnApplicationPause(bool paused) { if (paused) MetaProgression.Save(); }
    void OnApplicationQuit() => MetaProgression.Save();
    void SceneLoaded(Scene scene, LoadSceneMode mode) => Unbind();

    void Unbind()
    {
        if (discovery) discovery.CellChanged -= CellDiscovered;
        map = null; player = null; discovery = null;
        bound = false; boundRun = null; altarDiscovered = false; tripKnown = false;
        seen.Clear(); regions.Clear(); reported.Clear();
    }

    public static bool RewardsAllowed => Application.isPlaying && MetaProgression.CurrentRun != null &&
        !LoadingProgress.Active && !RunNavigation.IsTransitioning &&
        !GameplayTestSettings.FlyMode && !GameplayTestSettings.NoClipMode &&
        !GameplayTestSettings.GodMode && !GameplayTestSettings.NoEnergyConsume &&
        !GameplayTestSettings.InfiniteMoney && !GameplayTestSettings.NoWeight &&
        GameplayTestSettings.DiggingMultiplier <= 1.001f && GameplayTestSettings.MovementMultiplier <= 1.001f;

    void Update()
    {
        if (LoadingProgress.Active || RunNavigation.IsTransitioning ||
            SceneManager.GetActiveScene().name != RunNavigation.GameScene) return;
        if (MetaProgression.CurrentRun == null) return;
        if (boundRun != MetaProgression.CurrentRun.runId) Unbind();
        if (!bound)
        {
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .25f;
            map = FindFirstObjectByType<MapGenerator>();
            player = FindFirstObjectByType<PlayerMovement>();
            if (!map || !player || !map.IsGenerated || map.IsGenerationStreaming) return;
            discovery = map.GetComponent<PlayerMapDiscovery>();
            if (discovery)
            {
                // Count existing visibility without awarding it again on load.
                var snapshot = discovery.CaptureState();
                if (snapshot != null && snapshot.discoveredBits != null)
                    for (int i = 0; i < snapshot.width * snapshot.height; i++)
                        if ((snapshot.discoveredBits[i >> 3] & (1 << (i & 7))) != 0)
                            TrackCell(i % snapshot.width, i / snapshot.width, false);
                discovery.CellChanged += CellDiscovered;
            }
            boundRun = MetaProgression.CurrentRun.runId;
            bound = true;
            // Placements already in the world (restored saves, debug sessions) only raise the high-water marks.
            MetaProgression.SyncPlacedCount(TorchesPlaced, PlacedTorch.CountTorches(map));
            var ladders = map.GetComponent<LadderMap>();
            if (ladders) MetaProgression.SyncPlacedCount(LaddersPlaced, ladders.RecountPlaced());
        }
        if (Time.unscaledTime >= nextSave)
        {
            nextSave = Time.unscaledTime + 5f;
            MetaProgression.RequestSave();
        }
        if (!RewardsAllowed) { tripKnown = false; return; }
        if (GameplayInputBlocker.IsBlocked || Time.timeScale <= 0f ||
            GameOverPanel.IsOpen || GameVictoryPanel.IsOpen || !StatsManager.Instance ||
            StatsManager.Instance.Health <= 0f) return;
        if (Application.isFocused) MetaProgression.TickActive(Mathf.Min(Time.unscaledDeltaTime, .25f));
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + .25f;
        var cell = map.Terrain.WorldToCell(player.transform.position);
        bool inside = cell.x >= -map.GeneratedWidth / 2 && cell.x < map.GeneratedWidth - map.GeneratedWidth / 2 &&
            -cell.y < map.GeneratedHeight;
        int miningLayer = cell.y <= 0 ? MiningLayerAt(cell, map.layers) : 0;
        if (inside && cell.y <= 0)
        {
            MetaProgression.RecordDepth(Mathf.Max(0, -cell.y));
            MetaProgression.RecordReachedMiningLayer(miningLayer);
        }
        bool atSurface = cell.y >= 0;
        int visitedLayer = atSurface ? 1 : miningLayer;
        if (inside && visitedLayer > 0)
            MetaProgression.RecordExplorationPosition(visitedLayer, atSurface);
        bool firstSample = !tripKnown;
        if (firstSample) { tripKnown = true; tripFromSurface = atSurface; }
        bool tripping = tripFromSurface && !atSurface && inside;
        bool nearAltar = (!altarDiscovered || tripping) && NearAltar();
        if (!altarDiscovered && nearAltar)
        {
            altarDiscovered = true;
            MetaProgression.RecordDiscovery("altar");
        }
        if (tripping)
        {
            int depth = -cell.y;
            tripPeakDepth = Mathf.Max(tripPeakDepth, depth);
            tripPeakLayer = Mathf.Max(tripPeakLayer, visitedLayer);
            MetaProgression.RecordDepthReached(depth);
            if (visitedLayer > 0) MetaProgression.RecordLayerVisited(visitedLayer);
            if (nearAltar) MetaProgression.RecordAltarReached();
        }
        else if (atSurface)
        {
            var inventory = InventoryManager.Instance;
            double weight = inventory ? inventory.TotalWeight : 0d;
            if (!firstSample && tripFromSurface && tripPeakDepth > 0) FinishTrip(inventory, weight);
            tripFromSurface = true; tripPeakDepth = 0; tripPeakLayer = 1;
            tripStartWeight = weight; tripStartBlocks = MetaProgression.CreditedBlocks;
        }
    }

    bool NearAltar()
    {
        var altar = map.AltarChamber;
        return altar && Vector2.Distance(player.transform.position, altar.AltarPosition) <= 8f && altar.CaptureState().placed;
    }

    void FinishTrip(InventoryManager inventory, double weight)
    {
        var settings = MetaProgression.CurrentChallengeSettings;
        bool fullBag = false;
        if (inventory)
        {
            double reference = Math.Min(inventory.CarryingCapacity, settings.fullBagCapKg);
            fullBag = reference > 0d && weight >= reference * settings.fullBagPercent / 100d &&
                weight - tripStartWeight >= reference * settings.fullBagGainPercent / 100d &&
                tripPeakDepth >= settings.fullBagMinDepth;
        }
        MetaProgression.RecordReturn(tripPeakDepth, tripPeakLayer, fullBag,
            Mathf.Max(0, MetaProgression.CreditedBlocks - tripStartBlocks));
    }

    void CellDiscovered(int x, int depth) => TrackCell(x, depth, RewardsAllowed && !GameplayInputBlocker.IsBlocked);
    void TrackCell(int x, int depth, bool award)
    {
        var currentRun = MetaProgression.CurrentRun;
        if (currentRun == null) return;
        if (!map || x < 0 || x >= map.GeneratedWidth || depth <= 0 || depth >= map.GeneratedHeight) return;
        int index = depth * map.GeneratedWidth + x;
        if (!seen.Add(index)) return;
        MetaProgression.RecordRevealedCell(x, depth, award);
        if (discovery && discovery.TryGetSnapshot(x, depth, out _, out var visibleArtifact) && visibleArtifact)
            MetaProgression.RecordArtifactSight(new Vector3Int(x - map.GeneratedWidth / 2, -depth, 0), visibleArtifact.name, award);
        var settings = currentRun.settings;
        int regionSize = settings.explorationRegionSize, threshold = settings.explorationCellThreshold;
        var key = new Vector2Int(x / regionSize, depth / regionSize);
        regions.TryGetValue(key, out int count); regions[key] = ++count;
        if (count < threshold || !reported.Add(key)) return;
        if (award) MetaProgression.RecordExploration(key.x, key.y, key.y * regionSize);
    }

    public static bool IsSurfaceExcavation(Vector3Int cell, Block block, MapLayer[] layers, bool hasOre, bool hasArtifact)
    {
        if (cell.y > 0 || !block || (!block.IsStone && block.id != BlockType.Dirt) || hasOre || hasArtifact || layers == null) return false;
        bool hasSurface = false;
        int endDepth = int.MaxValue;
        foreach (var layer in layers) {
            if (layer == null) continue;
            if (layer.startDepth == 0) hasSurface = true;
            if (layer.startDepth > 0) endDepth = Mathf.Min(endDepth, layer.startDepth);
        }
        return hasSurface && -(long)cell.y < endDepth;
    }

    public static bool IsLayerTwoExcavation(Vector3Int cell, MapLayer[] layers)
    {
        if (cell.y > 0 || layers == null) return false;
        var ordered = layers.Where(layer => layer != null).OrderBy(layer => layer.startDepth).ToArray();
        if (ordered.Length < 2 || ordered[0].startDepth != 0 || ordered[1].startDepth <= 0) return false;
        long depth = -(long)cell.y;
        return depth >= ordered[1].startDepth && (ordered.Length < 3 || depth < ordered[2].startDepth);
    }
    public static bool IsLayerThreeExcavation(Vector3Int cell, MapLayer[] layers)
    {
        if (cell.y > 0 || layers == null) return false;
        var ordered = layers.Where(layer => layer != null).OrderBy(layer => layer.startDepth).ToArray();
        if (ordered.Length < 3 || ordered[0].startDepth != 0 || ordered[2].startDepth <= ordered[1].startDepth) return false;
        long depth = -(long)cell.y;
        return depth >= ordered[2].startDepth && (ordered.Length < 4 || depth < ordered[3].startDepth);
    }
    // Runs per mined cell and per sample: counts the layers starting above the cell instead of sorting them.
    public static int MiningLayerAt(Vector3Int cell, MapLayer[] layers)
    {
        if (cell.y > 0 || layers == null) return 0;
        long depth = -(long)cell.y;
        int first = int.MaxValue, result = 0;
        foreach (var layer in layers)
        {
            if (layer == null) continue;
            first = Mathf.Min(first, layer.startDepth);
            if (depth >= layer.startDepth) result++;
        }
        return first == 0 ? result : 0;
    }
    public static void RecordMining(Vector3Int cell, ItemSO item, int count, ArtifactTile artifact,
        Block block = null, MapLayer[] layers = null, bool hasOre = false, bool explosive = false, MapGenerator map = null)
    {
        if (!RewardsAllowed) return;
        string source = cell.x + ":" + cell.y;
        bool ore = item && item.category == ItemCategory.Ore && count > 0;
        if (ore) MetaProgression.RecordResource(item.item, count, source);
        if (ore && hasOre && block && map) RecordVein(map, cell, block, item.item);
        if (artifact) {
            MetaProgression.RecordDiscovery("artifact:" + source);
            MetaProgression.RecordArtifact(cell, artifact.name);
        }
        if (block || artifact || hasOre) MetaProgression.RecordMinedBlock(cell, MiningLayerAt(cell, layers), explosive);
    }

    // Called before the cell is removed. The vein is the 4-connected set of cells that still hold this ore block or
    // were mined in this run (ore ledger); it is cleared when none of them holds ore any more. Restored cells hold
    // ore again, so reloading never clears a vein early, and the representative cell keeps the glue key stable.
    static void RecordVein(MapGenerator map, Vector3Int start, Block block, Item item)
    {
        try
        {
            var terrain = map.Terrain;
            if (!terrain) return;
            veinSeen.Add(start); veinOpen.Enqueue(start);
            var representative = start;
            while (veinOpen.Count > 0)
            {
                var cell = veinOpen.Dequeue();
                foreach (var step in veinSteps)
                {
                    var next = cell + step;
                    if (veinSeen.Contains(next)) continue;
                    if (terrain.HasTile(next))
                    {
                        var ore = map.GetOreAt(next);
                        if (ore && ore.block == block) return;
                        continue;
                    }
                    if (!MetaProgression.HasMinedOre(item, next)) continue;
                    if (veinSeen.Count >= VeinLimit) return;
                    veinSeen.Add(next); veinOpen.Enqueue(next);
                    if (next.y < representative.y || (next.y == representative.y && next.x < representative.x)) representative = next;
                }
            }
            if (veinSeen.Count >= 2) MetaProgression.RecordVeinCleared((int)item, representative, veinSeen.Count);
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally { veinSeen.Clear(); veinOpen.Clear(); }
    }

    // Placements made while rewards are off only raise the high-water mark, so they are never credited later.
    public static void RecordPlaced(string metric, int count)
    {
        if (RewardsAllowed) MetaProgression.RecordPlacedCount(metric, count);
        else MetaProgression.SyncPlacedCount(metric, count);
    }

    public static void RecordVictory()
    {
        if (RewardsAllowed) MetaProgression.RecordVictory();
        MetaProgression.Save();
    }
}
