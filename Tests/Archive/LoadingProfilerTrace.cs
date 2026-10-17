using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEditorInternal;

public static class LoadingProfilerTrace
{
    static int last = -1;
    static bool seen;
    static bool wasEnabled, wasEditor, wasCpu;
    static readonly List<string> rows = new List<string>();
    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires Play mode.";
        wasEnabled = ProfilerDriver.enabled; wasEditor = ProfilerDriver.profileEditor;
        wasCpu = ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU);
        ProfilerDriver.enabled = true; ProfilerDriver.profileEditor = true;
        ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, true);
        rows.Clear(); rows.Add("frame,stage,phase,frame_ms,marker,ms,self_ms"); last = -1; seen = false;
        EditorApplication.update += Tick;
        return "Capturing CPU samples above 5 ms in slow loading frames.";
    }
    static void Tick()
    {
        if (LoadingProgress.Active) seen = true;
        int index = ProfilerDriver.lastFrameIndex;
        if (index != last)
        {
            last = index;
            using var data = ProfilerDriver.GetRawFrameDataView(index, 0);
            if (data.valid && data.frameTimeMs > 40 && LoadingProgress.Active)
            for (int i = 0; i < data.sampleCount; i++)
            {
                float total = data.GetSampleTimeMs(i);
                if (total < 5) continue;
                float self = total;
                int end = i + data.GetSampleChildrenCountRecursive(i) + 1;
                for (int child = i + 1; child < end; child += data.GetSampleChildrenCountRecursive(child) + 1)
                    self -= data.GetSampleTimeMs(child);
                rows.Add(index + "," + LoadingProgress.Stage + "," + LoadingScreen.TransitionPhase + "," +
                    data.frameTimeMs.ToString(CultureInfo.InvariantCulture) + ",\"" + data.GetSampleName(i).Replace("\"", "\"\"") + "\"," +
                    total.ToString(CultureInfo.InvariantCulture) + "," + self.ToString(CultureInfo.InvariantCulture));
            }
        }
        if (EditorApplication.isPlaying && (!seen || LoadingProgress.Active || RunNavigation.IsTransitioning)) return;
        EditorApplication.update -= Tick;
        File.WriteAllLines("Temp/LoadingProfilerTrace.csv", rows);
        ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU, wasCpu);
        ProfilerDriver.profileEditor = wasEditor; ProfilerDriver.enabled = wasEnabled;
    }
}
