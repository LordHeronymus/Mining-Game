#if MINING_BENCHMARK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

// Only compiled into benchmark players: Tests/BuildPerformanceBenchmark.cs builds them with the MINING_BENCHMARK
// define and a separate product name, so saves, profile and settings stay apart from the real game.
// Start the player with -performanceBenchmark <report.txt> [-benchmarkVsync 0|1]. It plays the phases of
// Tests/PerformanceBenchmarkChecks.cs (loading a new run, idle, normal and fast mining, exploring), records the
// duration of every frame, writes the report (+ .csv) and quits.
[DefaultExecutionOrder(-10000)]
public sealed class PerformanceBenchmarkDriver : MonoBehaviour
{
    enum Phase { Menu, Loading, Idle, MiningNormal, MiningFast, Exploring, Done }
    static readonly string[] PhaseNames = { "Menü", "Laden", "Stillstand", "Abbau normal", "Abbau schnell", "Erkunden", "Fertig" };
    struct Sample { public Phase phase; public int stage; public float ms; public int collections; }

    string report;
    bool vSync;
    Phase phase = Phase.Menu;
    double phaseStarted, nextAction;
    int hitCount, collections, mined;
    readonly List<Sample> samples = new(40000);
    readonly Queue<Vector3Int> path = new();
    MapGenerator map;
    TileMiner miner;
    Transform player;
    Rigidbody2D body;
    MethodInfo applyHit;
    Vector3 exploreFrom, exploreTo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-performanceBenchmark");
        if (index < 0 || index + 1 >= args.Length) return;
        var driver = new GameObject("Performance benchmark").AddComponent<PerformanceBenchmarkDriver>();
        DontDestroyOnLoad(driver.gameObject);
        driver.report = args[index + 1];
        int sync = Array.IndexOf(args, "-benchmarkVsync");
        driver.vSync = sync >= 0 && sync + 1 < args.Length && args[sync + 1] == "1";
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = vSync ? 1 : 0;
        Application.targetFrameRate = -1;
        applyHit = typeof(TileMiner).GetMethod("ApplyMiningHit", BindingFlags.NonPublic | BindingFlags.Instance);
        // Let the main menu settle like a player who just started the game.
        yield return new WaitForSecondsRealtime(3f);
        collections = GC.CollectionCount(0);
        Enter(Phase.Loading);
        RunNavigation.NewGame("Benchmark");
    }

    void Enter(Phase next) { phase = next; phaseStarted = Time.realtimeSinceStartupAsDouble; nextAction = phaseStarted + .25; }

    void Update()
    {
        if (phase == Phase.Menu || phase == Phase.Done) return;
        int count = GC.CollectionCount(0);
        samples.Add(new Sample { phase = phase, stage = LoadingProgress.Stage, ms = Time.unscaledDeltaTime * 1000f, collections = count - collections });
        collections = count;
        try { Drive(); }
        catch (Exception error) { Finish("FAIL: " + error); }
    }

    void Drive()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        switch (phase)
        {
            case Phase.Loading:
                if (now - phaseStarted > 300) throw new Exception("Laden dauerte länger als 300 s");
                if (RunNavigation.IsTransitioning || LoadingProgress.Active || !GameSaveSystem.CanSave) { nextAction = now + .5; return; }
                if (now < nextAction) return;
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
                if (t >= 1f && now - phaseStarted >= 9) Finish("PASS: Benchmark vollständig");
                return;
        }
    }

    void Prepare()
    {
        map = FindFirstObjectByType<MapGenerator>();
        miner = FindFirstObjectByType<TileMiner>();
        var movement = FindFirstObjectByType<PlayerMovement>();
        if (!map || !miner || !movement) throw new Exception("Spielwelt unvollständig");
        player = movement.transform; body = movement.GetComponent<Rigidbody2D>();
        var start = map.Terrain.WorldToCell(player.position);
        path.Clear();
        for (int y = 1; y <= 60; y++) { path.Enqueue(new Vector3Int(start.x, start.y - y, 0)); path.Enqueue(new Vector3Int(start.x + 1, start.y - y, 0)); }
        var bottom = start + new Vector3Int(0, -60, 0);
        for (int x = 2; x <= 120; x++) { path.Enqueue(new Vector3Int(bottom.x + x, bottom.y, 0)); path.Enqueue(new Vector3Int(bottom.x + x, bottom.y + 1, 0)); }
    }

    void Mine(double now, float hitsPerSecond, int hitsPerBlock)
    {
        while (now >= nextAction && path.Count > 0)
        {
            nextAction += 1.0 / hitsPerSecond;
            var cell = path.Peek();
            if (!map.Terrain.HasTile(cell) || map.IsCellProtected(cell)) { path.Dequeue(); continue; }
            hitCount++;
            if (hitCount % hitsPerBlock != 0) { applyHit?.Invoke(miner, new object[] { cell, .01f }); break; }
            path.Dequeue();
            if (miner.CompleteMining(cell)) mined++;
            MovePlayer(map.Terrain.GetCellCenterWorld(cell) + new Vector3(0, .1f, 0));
            break;
        }
    }

    void BeginExplore()
    {
        exploreFrom = player.position;
        float half = map.GeneratedWidth * map.Terrain.cellSize.x * .45f;
        exploreTo = new Vector3(exploreFrom.x > 0 ? -half : half, exploreFrom.y - 25f, 0);
    }

    void MovePlayer(Vector3 position)
    {
        if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        player.position = position;
    }

    static double Percentile(List<float> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p) - 1)];

    void Finish(string result)
    {
        phase = Phase.Done;
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder(result + "\n");
        text.AppendLine($"Player {Application.unityVersion}, IL2CPP={!Application.isEditor}, vSync={QualitySettings.vSyncCount}, Qualität={QualitySettings.names[QualitySettings.GetQualityLevel()]}, " +
            $"Bildschirm={Screen.width}x{Screen.height} {Screen.fullScreenMode}, Abgebaut={mined}");
        text.AppendLine();
        text.AppendLine("Phase           Frames   Ø FPS  Ø ms  p50   p95   p99   max    <100fps  <140fps  GC-Läufe");
        foreach (Phase p in Enum.GetValues(typeof(Phase)))
        {
            var list = samples.Where(s => s.phase == p).ToList();
            if (list.Count == 0) continue;
            var sorted = list.Select(s => s.ms).OrderBy(x => x).ToList();
            double average = list.Average(s => s.ms);
            text.AppendLine(string.Format(culture, "{0,-15} {1,6} {2,7:F1} {3,5:F2} {4,5:F2} {5,5:F2} {6,5:F2} {7,6:F1} {8,8} {9,8} {10,8}",
                PhaseNames[(int)p], list.Count, 1000.0 / average, average, Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99),
                sorted[sorted.Count - 1], list.Count(s => s.ms >= 10f), list.Count(s => s.ms > 1000f / 140f), list.Sum(s => s.collections)));
        }
        text.AppendLine();
        foreach (var group in samples.Where(s => s.phase == Phase.Loading).GroupBy(s => s.stage))
        {
            var sorted = group.Select(s => s.ms).OrderBy(x => x).ToList();
            text.AppendLine(string.Format(culture, "  Ladestufe {0} {1,-30} Frames {2,5}  Dauer {3,6:F2} s  p99 {4,6:F2} ms  max {5,6:F1} ms  <100fps {6}",
                group.Key, LoadingProgress.Labels[Mathf.Clamp(group.Key, 0, 7)], group.Count(), group.Sum(s => s.ms) / 1000f, Percentile(sorted, .99), sorted[sorted.Count - 1], group.Count(s => s.ms >= 10f)));
        }
        text.AppendLine();
        text.AppendLine("Langsamste Frames:");
        foreach (var index in Enumerable.Range(0, samples.Count).OrderByDescending(i => samples[i].ms).Take(30))
            text.AppendLine(string.Format(culture, "  #{0,-6} {1,-14} Stufe {2}  {3,7:F2} ms  GC-Läufe {4}", index, PhaseNames[(int)samples[index].phase], samples[index].stage, samples[index].ms, samples[index].collections));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllText(report, text.ToString());
            var csv = new StringBuilder("index,phase,stage,ms,gc_runs\n");
            for (int i = 0; i < samples.Count; i++)
                csv.Append(string.Format(culture, "{0},{1},{2},{3:F3},{4}\n", i, PhaseNames[(int)samples[i].phase], samples[i].stage, samples[i].ms, samples[i].collections));
            File.WriteAllText(Path.ChangeExtension(report, ".csv"), csv.ToString());
        }
        finally { Application.Quit(); }
    }
}
#endif
