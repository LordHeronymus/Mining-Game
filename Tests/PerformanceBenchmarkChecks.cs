using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

// Play Mode benchmark (fresh Play from MainMenu, isolated saves). Measures every frame of
// 1) the loading screen of a new run (frame times and progress-bar motion),
// 2) standing idle, 3) normal and 4) fast mining through TileMiner.CompleteMining while the
// player follows the shaft, and 5) fast exploration across the map.
// Report: Temp/PerformanceBenchmark.txt (+ .csv with every frame).
// The isolated save directories stay set until Play Mode ends; reset them afterwards in Edit Mode.
public static class PerformanceBenchmarkChecks
{
    const string Report = "Temp/PerformanceBenchmark.txt", Csv = "Temp/PerformanceBenchmark.csv";
    enum Phase { Start, Loading, Idle, MiningNormal, MiningFast, Exploring, Done }
    static readonly string[] PhaseNames = { "Start", "Laden", "Stillstand", "Abbau normal", "Abbau schnell", "Erkunden", "Fertig" };
    struct Sample
    {
        public int frame; public Phase phase; public float ms, mainMs, loopMs; public long gc;
        public int stage; public float target, displayed; public int mined;
        public float[] markers;
    }
    static readonly List<Sample> samples = new(40000);
    static readonly List<string> markerNames = new();
    static readonly List<ProfilerRecorder> markerRecorders = new();
    static ProfilerRecorder mainThread, playerLoop, gcAlloc;
    static Phase phase, previousPhase;
    static double phaseStarted, nextAction;
    static int settleFrames, minedInPhase, previousStage, previousMined, hitCount;
    static float previousTarget, previousDisplayed;
    static MapGenerator map;
    static TileMiner miner;
    static Transform player;
    static Rigidbody2D body;
    static readonly Queue<Vector3Int> path = new();
    static Vector3 exploreFrom, exploreTo;
    static MethodInfo applyHit;
    static FieldInfo loadingInstance, loadingDisplayed;
    static string failure;

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode ab MainMenu erforderlich.");
        if (Object.FindFirstObjectByType<MapGenerator>()) throw new Exception("Frischer Play-Start im MainMenu erforderlich.");
        string directory = Path.GetFullPath("Temp/PerformanceBenchmark-" + DateTime.UtcNow.Ticks);
        GameSaveSystem.TestDirectory = Path.Combine(directory, "saves");
        MetaProgression.TestDirectory = Path.Combine(directory, "meta");
        Application.runInBackground = true;
        samples.Clear(); failure = null; hitCount = 0;
        StartRecorders();
        loadingInstance = typeof(LoadingScreen).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        loadingDisplayed = typeof(LoadingScreen).GetField("displayed", BindingFlags.NonPublic | BindingFlags.Instance);
        applyHit = typeof(TileMiner).GetMethod("ApplyMiningHit", BindingFlags.NonPublic | BindingFlags.Instance);
        InstallTick();
        largeSyncs.Clear();
        UnityEngine.Tilemaps.Tilemap.tilemapPositionsChanged -= ObserveSync; UnityEngine.Tilemaps.Tilemap.tilemapPositionsChanged += ObserveSync;
        Enter(Phase.Loading);
        RunNavigation.NewGame("Benchmark");
        File.WriteAllText(Report, "läuft …");
        return "Benchmark gestartet; Bericht in " + Report;
    }

    // Very large tile-change batches are expensive for every tile-change subscriber; record their origin.
    static readonly List<string> largeSyncs = new();
    static void ObserveSync(UnityEngine.Tilemaps.Tilemap tilemap, Unity.Collections.NativeArray<Vector3Int> changes)
    {
        if (changes.Length < 20000 || largeSyncs.Count >= 12) return;
        largeSyncs.Add($"F{Time.frameCount} {PhaseNames[(int)phase]} Stufe {LoadingProgress.Stage}: {tilemap.name} {changes.Length} Änderungen\n" +
            string.Join("\n", Environment.StackTrace.Split('\n').Skip(2).Take(8).Select(line => "      " + line.Trim())));
    }

    static void StartRecorders()
    {
        DisposeRecorders();
        // Static marker fields register their names on first use of the class; load them before looking up handles.
        foreach (var type in new[] { typeof(TerrainColliderChunks), typeof(OreVeinField), typeof(UniformStoneAppearance), typeof(MapLighting),
            typeof(PlayerMapDiscovery), typeof(OreSparkles), typeof(MapGenerator), typeof(LoadingScreen), typeof(GameSaveSystem) })
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        playerLoop = ProfilerRecorder.StartNew(new ProfilerCategory("PlayerLoop"), "PlayerLoop", 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        var wanted = new List<string> { "Mining.Lighting", "Mining.Discovery", "Mining.ColliderChunk", "Mining.Headlamp", "Mining.HeadlampCache",
            "Mining.OreSparkles", "Mining.OreVeins.Rebuild", "Mining.OreVeins.Topology", "Mining.OreVeins.Outline", "Mining.TerrainUpload",
            "Mining.TerrainProperties", "Mining.TerrainRubble", "Loading.MapSetup", "GC.Collect", "Camera.Render", "Physics2D.Simulate",
            "Canvas.SendWillRenderCanvases", "Application.Integrate Assets in Background", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread",
            "WaitForTargetFPS" };
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var handle in handles)
        {
            var description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name.IndexOf("Tilemap", StringComparison.OrdinalIgnoreCase) >= 0 && !wanted.Contains(description.Name)) wanted.Add(description.Name);
        }
        foreach (var name in wanted)
        {
            var handle = handles.FirstOrDefault(h => ProfilerRecorderHandle.GetDescription(h).Name == name);
            if (!handle.Valid) continue;
            markerNames.Add(name);
            markerRecorders.Add(ProfilerRecorder.StartNew(ProfilerRecorderHandle.GetDescription(handle).Category, name, 1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame));
        }
    }

    static void DisposeRecorders()
    {
        mainThread.Dispose(); playerLoop.Dispose(); gcAlloc.Dispose();
        foreach (var recorder in markerRecorders) recorder.Dispose();
        markerRecorders.Clear(); markerNames.Clear();
    }

    static void InstallTick()
    {
        var root = PlayerLoop.GetCurrentPlayerLoop();
        var systems = root.subSystemList;
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;
            var list = systems[i].subSystemList.Where(s => s.type != typeof(PerformanceBenchmarkChecks)).ToList();
            list.Add(new PlayerLoopSystem { type = typeof(PerformanceBenchmarkChecks), updateDelegate = Tick });
            systems[i].subSystemList = list.ToArray();
        }
        root.subSystemList = systems;
        PlayerLoop.SetPlayerLoop(root);
    }

    static void RemoveTick()
    {
        var root = PlayerLoop.GetCurrentPlayerLoop();
        var systems = root.subSystemList;
        for (int i = 0; i < systems.Length; i++)
            if (systems[i].subSystemList != null)
                systems[i].subSystemList = systems[i].subSystemList.Where(s => s.type != typeof(PerformanceBenchmarkChecks)).ToArray();
        root.subSystemList = systems;
        PlayerLoop.SetPlayerLoop(root);
    }

    static void Enter(Phase next)
    {
        phase = next; phaseStarted = Time.realtimeSinceStartupAsDouble; minedInPhase = 0; settleFrames = 0;
        nextAction = phaseStarted + .25;
        // Lets Tests/ProfilerHotspots.cs (args: phase=…) record exactly one benchmark phase.
        SessionState.SetString("PerformanceBenchmark.Phase", PhaseNames[(int)next]);
    }

    static float Ms(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue / 1e6f : 0f;

    static void Tick()
    {
        try
        {
            if (phase == Phase.Done) return;
            Record();
            Drive();
        }
        catch (Exception ex) { failure = ex.ToString(); Finish(); }
    }

    // Recorder values and unscaledDeltaTime both describe the previous frame.
    static void Record()
    {
        if (Time.frameCount <= 2) return;
        var markers = new float[markerRecorders.Count];
        for (int i = 0; i < markers.Length; i++) markers[i] = Ms(markerRecorders[i]);
        samples.Add(new Sample { frame = Time.frameCount - 1, phase = previousPhase, ms = Time.unscaledDeltaTime * 1000f,
            mainMs = Ms(mainThread), loopMs = Ms(playerLoop), gc = gcAlloc.Valid && gcAlloc.Count > 0 ? gcAlloc.LastValue : 0,
            stage = previousStage, target = previousTarget, displayed = previousDisplayed, mined = previousMined, markers = markers });
        previousPhase = phase;
        previousStage = LoadingProgress.Stage; previousTarget = LoadingProgress.Target;
        var screen = loadingInstance?.GetValue(null);
        previousDisplayed = screen != null ? (float)loadingDisplayed.GetValue(screen) : -1f;
        previousMined = 0;
    }

    static void Drive()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        switch (phase)
        {
            case Phase.Loading:
                if (now - phaseStarted > 300) throw new Exception("Laden dauerte länger als 300 s");
                if (RunNavigation.IsTransitioning || LoadingProgress.Active || !GameSaveSystem.CanSave) { settleFrames = 0; return; }
                if (++settleFrames < 30) return;
                Prepare();
                Enter(Phase.Idle);
                return;
            case Phase.Idle:
                if (now - phaseStarted >= 3) Enter(Phase.MiningNormal);
                return;
            case Phase.MiningNormal:
                Mine(now, 4f, 2);
                if (now - phaseStarted >= 12) Enter(Phase.MiningFast);
                return;
            case Phase.MiningFast:
                Mine(now, 12f, 1);
                if (now - phaseStarted >= 10) { BeginExplore(); Enter(Phase.Exploring); }
                return;
            case Phase.Exploring:
                float t = (float)((now - phaseStarted) / 8.0);
                MovePlayer(Vector3.Lerp(exploreFrom, exploreTo, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t))));
                if (t >= 1f && now - phaseStarted >= 9) Finish();
                return;
        }
    }

    static void Prepare()
    {
        map = Object.FindFirstObjectByType<MapGenerator>();
        miner = Object.FindFirstObjectByType<TileMiner>();
        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        if (!map || !miner || !movement) throw new Exception("Spielwelt unvollständig");
        player = movement.transform; body = movement.GetComponent<Rigidbody2D>();
        // Two-wide shaft below the player, then a tunnel sideways: covers vertical and horizontal camera motion.
        var start = map.Terrain.WorldToCell(player.position);
        path.Clear();
        for (int y = 1; y <= 60; y++) { path.Enqueue(new Vector3Int(start.x, start.y - y, 0)); path.Enqueue(new Vector3Int(start.x + 1, start.y - y, 0)); }
        var bottom = start + new Vector3Int(0, -60, 0);
        for (int x = 2; x <= 120; x++) { path.Enqueue(new Vector3Int(bottom.x + x, bottom.y, 0)); path.Enqueue(new Vector3Int(bottom.x + x, bottom.y + 1, 0)); }
    }

    static void Mine(double now, float hitsPerSecond, int hitsPerBlock)
    {
        while (now >= nextAction && path.Count > 0)
        {
            nextAction += 1.0 / hitsPerSecond;
            var cell = path.Peek();
            if (!map.Terrain.HasTile(cell) || map.IsCellProtected(cell)) { path.Dequeue(); continue; }
            hitCount++;
            if (hitCount % hitsPerBlock != 0) { applyHit?.Invoke(miner, new object[] { cell, .01f }); break; }
            path.Dequeue();
            if (miner.CompleteMining(cell)) { minedInPhase++; previousMined++; }
            MovePlayer(map.Terrain.GetCellCenterWorld(cell) + new Vector3(0, .1f, 0));
            break;
        }
    }

    static void BeginExplore()
    {
        exploreFrom = player.position;
        float half = map.GeneratedWidth * map.Terrain.cellSize.x * .45f;
        exploreTo = new Vector3(exploreFrom.x > 0 ? -half : half, exploreFrom.y - 25f, 0);
    }

    static void MovePlayer(Vector3 position)
    {
        if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        player.position = position;
    }

    static void Finish()
    {
        phase = Phase.Done;
        SessionState.SetString("PerformanceBenchmark.Phase", PhaseNames[(int)Phase.Done]);
        UnityEngine.Tilemaps.Tilemap.tilemapPositionsChanged -= ObserveSync;
        RemoveTick();
        try { WriteReport(); }
        finally { DisposeRecorders(); }
    }

    static double Percentile(List<float> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p) - 1)];

    static void WriteReport()
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(failure == null ? "PASS: Benchmark vollständig" : "FAIL: " + failure);
        text.AppendLine($"Unity {Application.unityVersion}, Editor={Application.isEditor}, vSync={QualitySettings.vSyncCount}, targetFrameRate={Application.targetFrameRate}, Qualität={QualitySettings.names[QualitySettings.GetQualityLevel()]}, Bildschirm={Screen.width}x{Screen.height}");
        text.AppendLine($"Karte {(map ? map.GeneratedWidth + "x" + map.GeneratedHeight : "-")}, Marker: {string.Join(", ", markerNames)}");
        text.AppendLine();
        text.AppendLine("Phase           Frames   Ø FPS  Ø ms  p50   p95   p99   max    <100fps  <140fps  ØMain  Ø GC/Frame  Abgebaut");
        foreach (Phase p in Enum.GetValues(typeof(Phase)))
        {
            var list = samples.Where(s => s.phase == p).ToList();
            if (list.Count == 0) continue;
            var sorted = list.Select(s => s.ms).OrderBy(x => x).ToList();
            double avg = list.Average(s => s.ms);
            text.AppendLine(string.Format(culture, "{0,-15} {1,6} {2,7:F1} {3,5:F2} {4,5:F2} {5,5:F2} {6,5:F2} {7,6:F1} {8,8} {9,8} {10,6:F2} {11,10:N0} {12,8}",
                PhaseNames[(int)p], list.Count, 1000.0 / avg, avg, Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99), sorted[sorted.Count - 1],
                list.Count(s => s.ms >= 10f), list.Count(s => s.ms > 1000f / 140f), list.Average(s => s.mainMs), list.Average(s => (double)s.gc), list.Sum(s => s.mined)));
        }
        var loading = samples.Where(s => s.phase == Phase.Loading && s.displayed >= 0).ToList();
        if (loading.Count > 1)
        {
            text.AppendLine();
            float total = loading.Sum(s => s.ms) / 1000f;
            int stalled = 0, longest = 0, run = 0; float maxStep = 0, maxStall = 0, stallTime = 0;
            for (int i = 1; i < loading.Count; i++)
            {
                float step = loading[i].displayed - loading[i - 1].displayed;
                maxStep = Mathf.Max(maxStep, step);
                bool moving = step > 1e-5f || loading[i].displayed <= 0f || loading[i].displayed >= 1f;
                if (!moving) { stalled++; run++; stallTime += loading[i].ms; longest = Math.Max(longest, run); maxStall = Mathf.Max(maxStall, stallTime); }
                else { run = 0; stallTime = 0; }
            }
            text.AppendLine(string.Format(culture, "Ladebalken: Dauer {0:F2} s, {1} Frames ohne Bewegung (längste Pause {2} Frames / {3:F0} ms), grösster Sprung {4:P1} pro Frame",
                total, stalled, longest, maxStall, maxStep));
            foreach (var group in loading.GroupBy(s => s.stage))
            {
                var sorted = group.Select(s => s.ms).OrderBy(x => x).ToList();
                text.AppendLine(string.Format(culture, "  Ladestufe {0} {1,-30} Frames {2,5}  Dauer {3,6:F2} s  p99 {4,6:F2} ms  max {5,6:F1} ms  <100fps {6}",
                    group.Key, LoadingProgress.Labels[Mathf.Clamp(group.Key, 0, 7)], group.Count(), group.Sum(s => s.ms) / 1000f, Percentile(sorted, .99), sorted[sorted.Count - 1], group.Count(s => s.ms >= 10f)));
            }
        }
        if (largeSyncs.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Grosse Tilemap-Änderungslisten:");
            foreach (var entry in largeSyncs) text.AppendLine("  " + entry);
        }
        text.AppendLine();
        text.AppendLine("Langsamste Frames (ms, Main, GC, Marker > 0,2 ms):");
        foreach (var s in samples.OrderByDescending(s => s.ms).Take(25))
        {
            var parts = new List<string>();
            for (int i = 0; i < markerNames.Count; i++) if (s.markers[i] > .2f) parts.Add(markerNames[i] + "=" + s.markers[i].ToString("F2", culture));
            text.AppendLine(string.Format(culture, "  F{0,-6} {1,-14} {2,7:F2} ms  main {3,6:F2}  gc {4,8:N0}  stufe {5} abgebaut {6}  {7}",
                s.frame, PhaseNames[(int)s.phase], s.ms, s.mainMs, s.gc, s.stage, s.mined, string.Join(" ", parts)));
        }
        text.AppendLine();
        text.AppendLine("Ø Marker pro Phase (ms):");
        foreach (Phase p in Enum.GetValues(typeof(Phase)))
        {
            var list = samples.Where(s => s.phase == p).ToList();
            if (list.Count == 0) continue;
            var parts = new List<string>();
            for (int i = 0; i < markerNames.Count; i++)
            {
                double avg = list.Average(s => s.markers[i]), max = list.Max(s => s.markers[i]);
                if (max > .05) parts.Add(string.Format(culture, "{0} Ø{1:F2}/max{2:F1}", markerNames[i], avg, max));
            }
            text.AppendLine("  " + PhaseNames[(int)p] + ": " + string.Join(" | ", parts));
        }
        File.WriteAllText(Report, text.ToString());
        var csv = new StringBuilder("frame,phase,ms,main_ms,loop_ms,gc,stage,target,displayed,mined," + string.Join(",", markerNames.Select(n => n.Replace(",", " "))) + "\n");
        foreach (var s in samples)
            csv.Append(string.Format(culture, "{0},{1},{2:F3},{3:F3},{4:F3},{5},{6},{7:F4},{8:F4},{9},{10}\n", s.frame, PhaseNames[(int)s.phase], s.ms, s.mainMs, s.loopMs, s.gc,
                s.stage, s.target, s.displayed, s.mined, string.Join(",", s.markers.Select(m => m.ToString("F3", culture)))));
        File.WriteAllText(Csv, csv.ToString());
    }
}
