using System.Collections.Generic;
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
        bound = false; boundRun = null; altarDiscovered = false;
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
        }
        if (Time.unscaledTime >= nextSave)
        {
            nextSave = Time.unscaledTime + 5f;
            MetaProgression.RequestSave();
        }
        if (!RewardsAllowed || GameplayInputBlocker.IsBlocked || Time.timeScale <= 0f ||
            GameOverPanel.IsOpen || GameVictoryPanel.IsOpen || !StatsManager.Instance ||
            StatsManager.Instance.Health <= 0f) return;
        if (Application.isFocused) MetaProgression.TickActive(Mathf.Min(Time.unscaledDeltaTime, .25f));
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + .25f;
        var cell = map.Terrain.WorldToCell(player.transform.position);
        if (cell.x >= -map.GeneratedWidth / 2 && cell.x < map.GeneratedWidth - map.GeneratedWidth / 2 &&
            cell.y <= 0 && -cell.y < map.GeneratedHeight)
            MetaProgression.RecordDepth(Mathf.Max(0, -cell.y));
        if (!altarDiscovered && map.AltarChamber && map.AltarChamber.CaptureState().placed &&
            Vector2.Distance(player.transform.position, map.AltarChamber.AltarPosition) <= 8f)
        {
            altarDiscovered = true;
            MetaProgression.RecordDiscovery("altar");
        }
    }

    void CellDiscovered(int x, int depth) => TrackCell(x, depth, RewardsAllowed && !GameplayInputBlocker.IsBlocked);
    void TrackCell(int x, int depth, bool award)
    {
        var currentRun = MetaProgression.CurrentRun;
        if (currentRun == null) return;
        if (!map || x < 0 || x >= map.GeneratedWidth || depth <= 0 || depth >= map.GeneratedHeight) return;
        int index = depth * map.GeneratedWidth + x;
        if (!seen.Add(index)) return;
        var settings = currentRun.settings;
        int regionSize = settings.explorationRegionSize, threshold = settings.explorationCellThreshold;
        var key = new Vector2Int(x / regionSize, depth / regionSize);
        regions.TryGetValue(key, out int count); regions[key] = ++count;
        if (count < threshold || !reported.Add(key)) return;
        if (award) MetaProgression.RecordExploration(key.x, key.y, key.y * regionSize);
    }

    public static void RecordMining(Vector3Int cell, ItemSO item, int count, ArtifactTile artifact)
    {
        if (!RewardsAllowed) return;
        string source = cell.x + ":" + cell.y;
        if (item && item.category == ItemCategory.Ore && count > 0)
            MetaProgression.RecordResource(item.item, count, source);
        if (artifact) MetaProgression.RecordDiscovery("artifact:" + source);
    }

    public static void RecordVictory()
    {
        if (RewardsAllowed) MetaProgression.RecordVictory();
        MetaProgression.Save();
    }
}
