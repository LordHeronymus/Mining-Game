using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

// CI: local-only
// Play Mode (fresh Play from MainMenu, isolated saves): starts a new run, then fells every tree of the surface
// one after another (player placed beside each tree, one hit every 0.3 s, half of the trees fully grown) while
// the Unity Profiler records. Report Temp/TreeChopSpikes.txt: PlayerLoop per event (idle, hit, felling hit,
// fall, removal) and the call tree of every frame whose PlayerLoop reaches the threshold.
// Optional Temp/TreeChopSpikes.args: spikes=5 (ms), trees=12 (maximum number of trees).
// The isolated save directories stay set until Play Mode ends; reset them afterwards in Edit Mode.
public static class TreeChopSpikes
{
    const string Report = "Temp/TreeChopSpikes.txt", Arguments = "Temp/TreeChopSpikes.args";
    enum Step { Loading, Idle, Approach, Chop, Falling, Done }
    static Step step;
    static double stepStarted, nextHit;
    static int settle, threshold, maximumTrees, felled, profilerStart, nextProfilerFrame;
    static string failure;
    static ChoppableTree tree;
    static readonly Queue<ChoppableTree> queue = new();
    static Transform player;
    static Rigidbody2D body;
    static ProfilerRecorder playerLoop, gcAlloc;
    static string pendingLabel = "Stillstand", currentLabel = "Stillstand";
    static readonly List<(string label, float ms, long gc)> frames = new();
    static readonly List<(string label, float ms, string tree)> spikes = new();
    static readonly ProfilerMarker hitMarker = new("TreeChop.Hit");
    static readonly List<string> lateClips = new();
    static readonly List<AudioClip> unloaded = new();

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode ab MainMenu erforderlich.");
        if (Object.FindFirstObjectByType<MapGenerator>()) throw new Exception("Frischer Play-Start im MainMenu erforderlich.");
        threshold = 5; maximumTrees = 12;
        if (File.Exists(Arguments))
            foreach (var line in File.ReadAllLines(Arguments))
            {
                var parts = line.Split('=');
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int value)) continue;
                if (parts[0].Trim() == "spikes") threshold = Mathf.Max(1, value);
                if (parts[0].Trim() == "trees") maximumTrees = Mathf.Max(1, value);
            }
        string directory = Path.GetFullPath("Temp/TreeChopSpikes-" + DateTime.UtcNow.Ticks);
        GameSaveSystem.TestDirectory = Path.Combine(directory, "saves");
        MetaProgression.TestDirectory = Path.Combine(directory, "meta");
        Application.runInBackground = true;
        frames.Clear(); spikes.Clear(); queue.Clear(); lateClips.Clear(); audioLog.Clear(); lastAmbience = null; ambience = null; failure = null; felled = 0; tree = null;
        pendingLabel = currentLabel = "Laden";
        playerLoop.Dispose(); gcAlloc.Dispose();
        playerLoop = ProfilerRecorder.StartNew(new ProfilerCategory("PlayerLoop"), "PlayerLoop", 1,
            ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        InstallTick(true);
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        Enter(Step.Loading);
        RunNavigation.NewGame("TreeChop");
        File.WriteAllText(Report, "läuft …");
        return "Baumfäll-Messung gestartet; Bericht in " + Report;
    }

    static void InstallTick(bool install)
    {
        var root = PlayerLoop.GetCurrentPlayerLoop();
        var systems = root.subSystemList;
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;
            var list = systems[i].subSystemList.Where(s => s.type != typeof(TreeChopSpikes)).ToList();
            if (install) list.Add(new PlayerLoopSystem { type = typeof(TreeChopSpikes), updateDelegate = Tick });
            systems[i].subSystemList = list.ToArray();
        }
        root.subSystemList = systems;
        PlayerLoop.SetPlayerLoop(root);
    }

    static void Enter(Step next) { step = next; stepStarted = Time.realtimeSinceStartupAsDouble; settle = 0; }

    static void Tick()
    {
        try
        {
            if (step == Step.Done) return;
            // Recorder values describe the previous frame, which carried the previous label.
            if (step != Step.Loading && playerLoop.Valid && playerLoop.Count > 0)
            {
                float ms = playerLoop.LastValue / 1e6f;
                frames.Add((currentLabel, ms, gcAlloc.Valid && gcAlloc.Count > 0 ? gcAlloc.LastValue : 0));
                if (ms >= 50) audioLog.Add($"F{Time.frameCount - 1} {ms:F0} ms ({currentLabel}), Ambience {AmbienceState()}");
            }
            string state = AmbienceState();
            if (state != lastAmbience) audioLog.Add($"F{Time.frameCount} t={Time.realtimeSinceStartup:F1} s {step}: {state}");
            lastAmbience = state;
            currentLabel = pendingLabel;
            pendingLabel = step == Step.Falling ? "Fallen" : step == Step.Chop ? "zwischen Schlägen" : step == Step.Approach ? "Annähern" : "Stillstand";
            Drive();
        }
        catch (Exception ex) { failure = ex.ToString(); Finish(); }
    }

    static void Drive()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        switch (step)
        {
            case Step.Loading:
                if (now - stepStarted > 300) throw new Exception("Laden dauerte länger als 300 s");
                if (RunNavigation.IsTransitioning || LoadingProgress.Active || !GameSaveSystem.CanSave) { settle = 0; return; }
                if (++settle < 60) return;
                Prepare();
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.profileEditor = false;
                ProfilerDriver.enabled = true;
                profilerStart = -1;
                Enter(Step.Idle);
                return;
            case Step.Idle:
                if (now - stepStarted >= 3) NextTree();
                return;
            case Step.Approach:
                if (now - stepStarted < .6) return;
                // Audio clips without loaded data, listed before the hits (the lookup costs about a millisecond).
                unloaded.Clear();
                unloaded.AddRange(Resources.FindObjectsOfTypeAll<AudioClip>().Where(c => c.loadState == AudioDataLoadState.Unloaded));
                Enter(Step.Chop); nextHit = now;
                return;
            case Step.Chop:
                if (!tree) { NextTree(); return; }
                if (now < nextHit) return;
                nextHit = now + .3;
                bool last = tree.Health <= (forest ? forest.HitDamage : 1f);
                var wood = woodItem;
                if (last && wood && !InventoryManager.Instance.CanAdd(wood, 60)) InventoryManager.Instance.ResetAll();
                using (hitMarker.Auto()) tree.Hit((Vector2)player.position);
                foreach (var clip in unloaded.ToArray())
                    if (clip.loadState != AudioDataLoadState.Unloaded && unloaded.Remove(clip))
                        lateClips.Add($"{clip.name} ({clip.length:F1} s, {clip.loadType}, Hintergrund {clip.loadInBackground}) beim {(tree.IsFalling ? "Fällschlag" : "Schlag")} von Baum {felled + 1}");
                currentLabel = tree.IsFalling ? "Fällschlag" : "Schlag";
                if (tree.IsFalling) Enter(Step.Falling);
                return;
            case Step.Falling:
                if (tree) return;
                currentLabel = "Entfernen";
                felled++;
                if (now - stepStarted < 1.6) return;
                NextTree();
                return;
        }
    }

    static SurfaceTrees forest;
    static ItemSO woodItem;
    static readonly List<string> audioLog = new();
    static string lastAmbience;
    static SurfaceAmbience ambience;

    // Load state of the surface ambience clip and which of its two sources play (long clip, decoded on demand).
    static string AmbienceState()
    {
        if (!ambience) ambience = Object.FindFirstObjectByType<SurfaceAmbience>();
        if (!ambience) return "keine";
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var loop = typeof(SurfaceAmbience).GetField("day", flags).GetValue(ambience);
        var clip = loop.GetType().GetField("clip").GetValue(loop) as AudioClip;
        var sources = (AudioSource[])loop.GetType().GetField("sources").GetValue(loop);
        return $"{(clip ? clip.name + " " + clip.loadState : "-")}, running {loop.GetType().GetField("running").GetValue(loop)}, " +
            string.Join("/", sources.Select(s => s && s.isPlaying ? "spielt" : "still"));
    }

    static void Prepare()
    {
        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        if (!movement) throw new Exception("Spieler fehlt");
        player = movement.transform; body = movement.GetComponent<Rigidbody2D>();
        forest = Object.FindFirstObjectByType<SurfaceTrees>();
        woodItem = forest ? typeof(SurfaceTrees).GetField("wood", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(forest) as ItemSO : null;
        var growth = typeof(ChoppableTree).GetField("sizeBonusRatio", BindingFlags.NonPublic | BindingFlags.Instance);
        var maximum = typeof(ChoppableTree).GetField("maximumSizeBonusRatio", BindingFlags.NonPublic | BindingFlags.Instance);
        int index = 0;
        foreach (var candidate in ChoppableTree.ActiveTrees.Where(t => t && t.CanChop).OrderBy(t => t.transform.position.x).Take(maximumTrees))
        {
            // Every second tree fully grown: larger sprite on screen, more wood.
            if (index++ % 2 == 1) growth.SetValue(candidate, maximum.GetValue(candidate));
            queue.Enqueue(candidate);
        }
        if (queue.Count == 0) throw new Exception("Keine fällbaren Bäume");
    }

    static void NextTree()
    {
        while (queue.Count > 0 && !(queue.Peek() && queue.Peek().CanChop)) queue.Dequeue();
        if (queue.Count == 0) { Finish(); return; }
        tree = queue.Dequeue();
        var position = tree.transform.position + new Vector3(-1.4f, .2f, 0);
        if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        player.position = position;
        Enter(Step.Approach);
    }

    static void Poll()
    {
        if (!EditorApplication.isPlaying) { Cleanup(); return; }
        if (!ProfilerDriver.enabled || step == Step.Loading) return;
        if (profilerStart < 0) { if (ProfilerDriver.lastFrameIndex >= 0) nextProfilerFrame = profilerStart = ProfilerDriver.lastFrameIndex; return; }
        if (ProfilerDriver.lastFrameIndex - nextProfilerFrame < 30 && step != Step.Done) return;
        CollectSpikes();
        if (step == Step.Done) { Cleanup(); WriteReport(); }
    }

    static void CollectSpikes()
    {
        var culture = CultureInfo.InvariantCulture;
        for (int frame = Math.Max(nextProfilerFrame, ProfilerDriver.firstFrameIndex); frame <= ProfilerDriver.lastFrameIndex; frame++)
        {
            nextProfilerFrame = frame + 1;
            using var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnTotalTime, false);
            if (view == null || !view.valid) continue;
            var children = new List<int>();
            view.GetItemChildren(view.GetRootItemID(), children);
            float loop = children.Where(c => view.GetItemName(c) == "PlayerLoop")
                .Sum(c => view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime));
            float hit = 0f;
            using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0))
                if (raw != null && raw.valid)
                {
                    int id = raw.GetMarkerId("TreeChop.Hit");
                    for (int i = 0; i < raw.sampleCount; i++) if (raw.GetSampleMarkerId(i) == id) hit += raw.GetSampleTimeMs(i);
                }
            // Felling hits that cost at least a millisecond are kept as well, to see what they consist of.
            if ((loop < threshold && hit < 1f) || spikes.Count >= 60) continue;
            var text = new StringBuilder();
            Tree(view, view.GetRootItemID(), 4, text, culture);
            string label = text.ToString().Contains("TreeChop.Hit") ? "mit Schlag" : "ohne Schlag";
            spikes.Add((label, loop, string.Format(culture, "  P{0} PlayerLoop {1:F2} ms ({2})\n{3}", frame, loop, label, text)));
        }
    }

    static void Tree(HierarchyFrameDataView view, int id, int depth, StringBuilder text, CultureInfo culture)
    {
        if (depth > 34) return;
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children.OrderByDescending(c => view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime)))
        {
            float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            string name = view.GetItemName(child);
            if (total < .3f && name != "TreeChop.Hit") continue;
            float gc = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
            text.AppendLine(string.Format(culture, "{0}{1:F2} {2}{3}", new string(' ', depth), total, name, gc > 0 ? $"  [GC {gc:N0} B]" : ""));
            Tree(view, child, depth + 1, text, culture);
        }
    }

    static void Finish()
    {
        step = Step.Done;
        InstallTick(false);
    }

    static void Cleanup()
    {
        EditorApplication.update -= Poll;
        InstallTick(false);
        ProfilerDriver.enabled = false;
        playerLoop.Dispose(); gcAlloc.Dispose();
    }

    static void WriteReport()
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(failure == null ? $"PASS: {felled} Bäume gefällt" : "FAIL: " + failure);
        text.AppendLine($"Unity {Application.unityVersion}, Schwelle {threshold} ms, vSync={QualitySettings.vSyncCount}");
        text.AppendLine();
        text.AppendLine("Ereignis             Frames   Ø ms   p95    max   ab 10 ms   Ø GC/Frame   max GC");
        foreach (var group in frames.GroupBy(f => f.label))
        {
            var sorted = group.Select(f => f.ms).OrderBy(x => x).ToList();
            text.AppendLine(string.Format(culture, "{0,-20} {1,6} {2,6:F2} {3,6:F2} {4,6:F2} {5,8} {6,12:N0} {7,10:N0}",
                group.Key, sorted.Count, sorted.Average(), sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * .95))], sorted[^1],
                sorted.Count(x => x >= 10), group.Average(f => (double)f.gc), group.Max(f => f.gc)));
        }
        text.AppendLine();
        text.AppendLine("Oberflächen-Ambience und Frames ab 50 ms:");
        foreach (var entry in audioLog) text.AppendLine("  " + entry);
        text.AppendLine();
        text.AppendLine($"Erst beim Schlag geladene Audio-Clips: {lateClips.Count}");
        foreach (var clip in lateClips) text.AppendLine("  " + clip);
        text.AppendLine();
        text.AppendLine($"Profiler-Frames mit PlayerLoop ab {threshold} ms: {spikes.Count} (Profiler-Overhead enthalten)");
        foreach (var spike in spikes) text.AppendLine(spike.tree);
        File.WriteAllText(Report, text.ToString());
    }
}
