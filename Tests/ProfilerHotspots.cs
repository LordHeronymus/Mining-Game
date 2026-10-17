using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

// Play Mode: records the next frames with the Unity Profiler (main thread, merged samples) and writes the
// largest self-time, total-time and GC-allocation contributors to Temp/ProfilerHotspots.txt, plus the slowest
// frames with their heaviest samples and the UI elements that were rebuilt most often.
// Optional settings in Temp/ProfilerHotspots.args (one per line): frames=300, delay=0 (frames to wait first),
// phase=Abbau schnell (wait until Tests/PerformanceBenchmarkChecks.cs enters this phase; several phases separated
// by commas, * for every phase), spikes=8 (instead: record while the benchmark is in these phases and keep the
// call tree of every frame whose PlayerLoop takes at least 8 ms; the editor's own loop is excluded because a
// player build has none), loadingSpikes=14 (separate threshold for the loading phase).
public static class ProfilerHotspots
{
    const string Report = "Temp/ProfilerHotspots.txt", Arguments = "Temp/ProfilerHotspots.args";
    static int frames = 300, delay, startFrame, startProfilerFrame, spikeThreshold, loadingSpikeThreshold, nextSpikeFrame;
    static string[] phases;
    static readonly List<string> spikeTrees = new();
    static readonly Dictionary<string, int> layoutRebuilds = new(), graphicRebuilds = new();
    static object registry;
    static FieldInfo layoutQueue, graphicQueue;

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode erforderlich.");
        frames = 300; delay = 0; phases = null; spikeThreshold = loadingSpikeThreshold = 0; spikeTrees.Clear(); frameSplits.Clear();
        if (File.Exists(Arguments))
            foreach (var line in File.ReadAllLines(Arguments))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                if (parts[0].Trim() == "phase") { phases = parts[1].Split(',').Select(p => p.Trim()).ToArray(); continue; }
                if (!int.TryParse(parts[1].Trim(), out int value)) continue;
                if (parts[0].Trim() == "frames") frames = Mathf.Clamp(value, 30, 2000);
                if (parts[0].Trim() == "delay") delay = Mathf.Max(0, value);
                if (parts[0].Trim() == "spikes") spikeThreshold = Mathf.Max(1, value);
                if (parts[0].Trim() == "loadingSpikes") loadingSpikeThreshold = Mathf.Max(1, value);
            }
        if (loadingSpikeThreshold == 0) loadingSpikeThreshold = spikeThreshold;
        layoutRebuilds.Clear(); graphicRebuilds.Clear();
        registry = typeof(CanvasUpdateRegistry).GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        layoutQueue = typeof(CanvasUpdateRegistry).GetField("m_LayoutRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        graphicQueue = typeof(CanvasUpdateRegistry).GetField("m_GraphicRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        startFrame = Time.frameCount + delay; startProfilerFrame = -1;
        File.WriteAllText(Report, "läuft …");
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        Canvas.preWillRenderCanvases -= InspectCanvases; Canvas.preWillRenderCanvases += InspectCanvases;
        return $"Profiler-Aufnahme ({frames} Frames, Verzögerung {delay}) läuft; Bericht in {Report}";
    }

    static void InspectCanvases()
    {
        if (startProfilerFrame < 0 || registry == null) return;
        Count(layoutQueue?.GetValue(registry) as IList<ICanvasElement>, layoutRebuilds);
        Count(graphicQueue?.GetValue(registry) as IList<ICanvasElement>, graphicRebuilds);
    }

    // IndexedSet only implements the generic list interface; its non-generic enumerator throws.
    static void Count(IList<ICanvasElement> queue, Dictionary<string, int> counts)
    {
        if (queue == null) return;
        for (int i = 0; i < queue.Count; i++)
        {
            var element = queue[i];
            string name = element is Component component && component ? Path(component.transform) + " (" + component.GetType().Name + ")" : element?.GetType().Name ?? "null";
            counts[name] = counts.TryGetValue(name, out int value) ? value + 1 : 1;
        }
    }

    static string Path(Transform transform)
    {
        var names = new List<string>();
        for (var current = transform; current && names.Count < 5; current = current.parent) names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    static void Poll()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount < startFrame) return;
        if (startProfilerFrame < 0)
        {
            if (!InPhases) { startFrame = Time.frameCount + 1 + delay; return; }
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            startProfilerFrame = 0;
            return;
        }
        if (startProfilerFrame == 0) { if (ProfilerDriver.lastFrameIndex >= 0) nextSpikeFrame = startProfilerFrame = ProfilerDriver.lastFrameIndex; return; }
        if (spikeThreshold > 0)
        {
            // Inspecting frames costs editor time; batches keep that overhead out of most recorded frames.
            if (InPhases && ProfilerDriver.lastFrameIndex - nextSpikeFrame < 30) return;
            CollectSpikes();
            if (phases == null || InPhases) return;
            Stop();
            File.WriteAllText(Report, Breakdown() + $"\nSpitzen über {spikeThreshold} ms in {string.Join(", ", phases)}: {spikeTrees.Count}\n\n" + string.Join("\n", spikeTrees));
            return;
        }
        if (ProfilerDriver.lastFrameIndex - startProfilerFrame < frames) return;
        Stop();
        try { Analyze(); }
        catch (Exception ex) { File.WriteAllText(Report, "FAIL: " + ex); }
    }

    // Frames leave the profiler buffer after a few hundred frames, so inspect new frames regularly.
    static void CollectSpikes()
    {
        var culture = CultureInfo.InvariantCulture;
        string current = CurrentPhase;
        int threshold = current == "Laden" ? loadingSpikeThreshold : spikeThreshold;
        for (int frame = Math.Max(nextSpikeFrame, ProfilerDriver.firstFrameIndex); frame <= ProfilerDriver.lastFrameIndex; frame++)
        {
            nextSpikeFrame = frame + 1;
            using var raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (raw == null || !raw.valid) continue;
            float playerLoop = SampleTime(raw, "PlayerLoop");
            frameSplits.Add((current, raw.frameTimeMs, playerLoop, SampleTime(raw, "EditorLoop")));
            if (playerLoop < threshold || spikeTrees.Count >= 120) continue;
            using var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnTotalTime, false);
            var text = new StringBuilder(string.Format(culture, "  F{0} {1:F2} ms, PlayerLoop {2:F2} ms ({3}, Ladestufe {4})\n",
                frame, raw.frameTimeMs, playerLoop, current, LoadingProgress.Stage));
            Tree(view, view.GetRootItemID(), 2, text, culture);
            spikeTrees.Add(text.ToString());
        }
    }

    // Frame time, PlayerLoop and EditorLoop of every recorded frame, to tell game work from editor overhead.
    static readonly List<(string phase, float frame, float playerLoop, float editorLoop)> frameSplits = new();

    static string Breakdown()
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder("Aufteilung pro Phase (Ø / p95 / max ms; Frames ab 10 ms; davon mit PlayerLoop unter 7 ms):\n");
        foreach (var group in frameSplits.GroupBy(split => split.phase))
        {
            string Stats(Func<(string phase, float frame, float playerLoop, float editorLoop), float> value)
            {
                var sorted = group.Select(value).OrderBy(x => x).ToList();
                return string.Format(culture, "{0:F2} / {1:F2} / {2:F2}", sorted.Average(), sorted[(int)(sorted.Count * .95)], sorted[sorted.Count - 1]);
            }
            int slow = group.Count(split => split.frame >= 10), editorOnly = group.Count(split => split.frame >= 10 && split.playerLoop < 7);
            text.AppendLine(string.Format(culture, "  {0,-14} {1,5} Frames  Frame {2}  PlayerLoop {3}  EditorLoop {4}  ab 10 ms: {5} (Editor: {6})",
                group.Key, group.Count(), Stats(split => split.frame), Stats(split => split.playerLoop), Stats(split => split.editorLoop), slow, editorOnly));
        }
        return text.ToString();
    }

    static float SampleTime(RawFrameDataView raw, string marker)
    {
        int id = raw.GetMarkerId(marker);
        for (int i = 0; i < raw.sampleCount; i++)
            if (raw.GetSampleMarkerId(i) == id) return raw.GetSampleTimeMs(i);
        return 0f;
    }

    static string CurrentPhase => SessionState.GetString("PerformanceBenchmark.Phase", "");
    static bool InPhases => phases == null || (phases[0] == "*" ? CurrentPhase != "Fertig" : phases.Contains(CurrentPhase));

    static void Stop()
    {
        EditorApplication.update -= Poll;
        Canvas.preWillRenderCanvases -= InspectCanvases;
        ProfilerDriver.enabled = false;
    }

    sealed class Stat { public double self, total, gc, maxTotal; }

    static void Analyze()
    {
        var culture = CultureInfo.InvariantCulture;
        var stats = new Dictionary<string, Stat>();
        var frameTimes = new List<(int frame, float ms, List<(string name, float total)> heavy)>();
        int first = Math.Max(ProfilerDriver.firstFrameIndex, startProfilerFrame + 1), last = ProfilerDriver.lastFrameIndex;
        for (int frame = first; frame <= last; frame++)
        {
            using var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnTotalTime, false);
            if (view == null || !view.valid) continue;
            var heavy = new List<(string, float)>();
            Visit(view, view.GetRootItemID(), "(root)", 0, stats, new HashSet<string>(), heavy);
            frameTimes.Add((frame, view.frameTimeMs, heavy));
        }
        int count = Math.Max(1, frameTimes.Count);
        var text = new StringBuilder();
        var sorted = frameTimes.Select(f => f.ms).OrderBy(x => x).ToList();
        text.AppendLine(string.Format(culture, "Frames {0}, Ø {1:F2} ms, p95 {2:F2} ms, max {3:F2} ms (Profiler-Overhead enthalten)",
            frameTimes.Count, frameTimes.Average(f => f.ms), sorted[(int)(sorted.Count * .95)], sorted[sorted.Count - 1]));
        text.AppendLine();
        text.AppendLine("Grösste Eigenzeit (Ø ms pro Frame):");
        foreach (var pair in stats.OrderByDescending(p => p.Value.self).Take(40))
            text.AppendLine(string.Format(culture, "  {0,8:F3}  {1}", pair.Value.self / count, pair.Key));
        text.AppendLine();
        text.AppendLine("Skripte und Systeme nach Gesamtzeit (Ø ms pro Frame / max ms):");
        foreach (var pair in stats.Where(p => p.Key.Contains("()") || p.Key.StartsWith("Mining.") || p.Key.StartsWith("Loading.")).OrderByDescending(p => p.Value.total).Take(40))
            text.AppendLine(string.Format(culture, "  {0,8:F3} / {1,7:F2}  {2}", pair.Value.total / count, pair.Value.maxTotal, pair.Key));
        text.AppendLine();
        text.AppendLine("GC-Allokation nach aufrufender Methode (Ø Bytes pro Frame):");
        foreach (var pair in stats.Where(p => p.Value.gc > 0).OrderByDescending(p => p.Value.gc).Take(30))
            text.AppendLine(string.Format(culture, "  {0,10:N0}  {1}", pair.Value.gc / count, pair.Key));
        text.AppendLine();
        text.AppendLine("UI-Neuaufbau (Anzahl Frames mit Layout- bzw. Grafik-Rebuild):");
        foreach (var pair in layoutRebuilds.OrderByDescending(p => p.Value).Take(15)) text.AppendLine($"  Layout  {pair.Value,5}  {pair.Key}");
        foreach (var pair in graphicRebuilds.OrderByDescending(p => p.Value).Take(25)) text.AppendLine($"  Grafik  {pair.Value,5}  {pair.Key}");
        text.AppendLine();
        text.AppendLine("Langsamste Frames:");
        foreach (var f in frameTimes.OrderByDescending(f => f.ms).Take(10))
            text.AppendLine(string.Format(culture, "  F{0} {1:F2} ms: {2}", f.frame, f.ms,
                string.Join(" | ", f.heavy.OrderByDescending(t => t.total).Take(10).Select(t => t.name + " " + t.total.ToString("F2", culture)))));
        text.AppendLine();
        text.AppendLine("Aufrufbaum der drei langsamsten Frames (Einträge ab 0,25 ms):");
        foreach (var f in frameTimes.OrderByDescending(f => f.ms).Take(3))
        {
            text.AppendLine(string.Format(culture, "  F{0} {1:F2} ms", f.frame, f.ms));
            using var view = ProfilerDriver.GetHierarchyFrameDataView(f.frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnTotalTime, false);
            Tree(view, view.GetRootItemID(), 2, text, culture);
        }
        File.WriteAllText(Report, text.ToString());
    }

    static void Tree(HierarchyFrameDataView view, int id, int depth, StringBuilder text, CultureInfo culture)
    {
        if (depth > 30) return;
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children.OrderByDescending(c => view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime)))
        {
            float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            if (total < (spikeThreshold > 0 ? .5f : .25f)) continue;
            text.AppendLine(string.Format(culture, "{0}{1:F2} {2}", new string(' ', depth), total, view.GetItemName(child)));
            Tree(view, child, depth + 1, text, culture);
        }
    }

    static void Visit(HierarchyFrameDataView view, int id, string parent, int depth, Dictionary<string, Stat> stats, HashSet<string> seen, List<(string, float)> heavy)
    {
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children)
        {
            string name = view.GetItemName(child);
            float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            float self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
            if (name == "GC.Alloc")
            {
                if (!stats.TryGetValue(parent, out var owner)) stats[parent] = owner = new Stat();
                owner.gc += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
            }
            if (!stats.TryGetValue(name, out var stat)) stats[name] = stat = new Stat();
            stat.self += self;
            if (seen.Add(name)) { stat.total += total; stat.maxTotal = Math.Max(stat.maxTotal, total); }
            if (depth >= 1 && depth <= 5 && total > .5f) heavy.Add((name, total));
            Visit(view, child, name, depth + 1, stats, seen, heavy);
        }
    }
}
