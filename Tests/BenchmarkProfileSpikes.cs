using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;

// Edit Mode: reads the profiler parts of a development benchmark player (Tests/BuildPerformanceBenchmark.cs with
// "development", player started with -benchmarkProfile <prefix>, which writes <prefix>-0.raw, <prefix>-1.raw, ...
// with 1500 frames each) and writes the call tree of every frame above the threshold to
// Temp/BenchmarkProfileSpikes.txt. "P<part> F<frame>" maps to row part * 1500 + frame of the benchmark .csv.
// Settings in Temp/BenchmarkProfileSpikes.args: first line the prefix (default Temp/BenchDev), second line the
// threshold in ms (default 9). Player builds show native markers and script entry points only (no deep profile).
public static class BenchmarkProfileSpikes
{
    const string Arguments = "Temp/BenchmarkProfileSpikes.args", Report = "Temp/BenchmarkProfileSpikes.txt";

    public static object Main()
    {
        var lines = File.Exists(Arguments) ? File.ReadAllLines(Arguments) : new string[0];
        string prefix = lines.Length > 0 && lines[0].Trim().Length > 0 ? lines[0].Trim() : "Temp/BenchDev";
        float threshold = lines.Length > 1 && float.TryParse(lines[1].Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 9f;
        var text = new StringBuilder();
        int spikes = 0, parts = 0;
        for (; File.Exists($"{prefix}-{parts}.raw"); parts++)
        {
            if (!ProfilerDriver.LoadProfile(Path.GetFullPath($"{prefix}-{parts}.raw"), false)) { text.AppendLine($"part {parts}: not loadable"); continue; }
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            text.AppendLine($"part {parts} frames {first}-{last}");
            for (int frame = first; frame <= last; frame++)
            {
                using var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false);
                if (!view.valid || view.frameTimeMs < threshold) continue;
                spikes++;
                text.AppendLine($"P{parts} F{frame - first} {view.frameTimeMs:F1} ms");
                Dump(view, view.GetRootItemID(), 1, text, System.Math.Max(.5f, view.frameTimeMs * .03f));
            }
        }
        ProfilerDriver.ClearAllFrames();
        File.WriteAllText(Report, text.ToString());
        return parts == 0 ? $"Keine Profilteile {prefix}-0.raw gefunden" : $"{spikes} Frames ab {threshold} ms in {parts} Teilen; Bericht in {Report}";
    }

    static void Dump(HierarchyFrameDataView view, int id, int depth, StringBuilder text, float minimum)
    {
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children)
        {
            float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            if (total < minimum) continue;
            text.Append(' ', depth).Append(total.ToString("F2")).Append(' ').AppendLine(view.GetItemName(child));
            if (depth < 16) Dump(view, child, depth + 1, text, minimum);
        }
    }
}
