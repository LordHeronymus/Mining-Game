using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Measures the hitch of an autosave: frame times before and during real timed autosaves,
// how long the game stays frozen (timeScale 0), and the main-thread cost of each save step.
// Runs in isolated save/meta directories and returns to the homescreen with both reset. CI: local-only
public static class AutosaveStutterChecks
{
    const int Saves = 3;
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> lines = new List<string>();
    static string output, testDirectory;
    static float oldInterval;
    static bool background;
    static int frame;
    static double began;

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Requires the freshly started homescreen.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/AutosaveStutterChecks.txt"));
        testDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/AutosaveStutter-" + DateTime.UtcNow.Ticks));
        GameSaveSystem.TestDirectory = testDirectory;
        MetaProgression.TestDirectory = Path.Combine(testDirectory, "MetaProfile");
        oldInterval = GpsSettings.Preferences.autosaveIntervalMinutes;
        background = Application.runInBackground; Application.runInBackground = true;
        began = EditorApplication.timeSinceStartup; frame = -1; lines.Clear(); routines.Clear();
        routines.Push(Measure()); EditorApplication.update += Tick;
        return output;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - began > 300)
        { Finish("FAIL: Interrupted or timed out"); return; }
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            while (routines.Count > 0)
            {
                var routine = routines.Peek();
                if (!routine.MoveNext()) { routines.Pop(); continue; }
                if (routine.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish("DONE");
        }
        catch (Exception error) { Finish("FAIL: " + error); }
    }
    static Button Button(Transform parent, string name) => parent.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == name);
    static IEnumerator Settled()
    { while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) yield return null; yield return null; }
    static IEnumerator Wait(float seconds)
    { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
    static double Ms => Time.unscaledDeltaTime * 1000.0;
    static string Stats(List<double> ms)
    {
        if (ms.Count == 0) return "keine Frames";
        var sorted = ms.OrderBy(x => x).ToList();
        return $"{ms.Count} Frames, Median {sorted[sorted.Count / 2]:0.0} ms, P95 {sorted[(int)(sorted.Count * .95)]:0.0} ms, Max {sorted[^1]:0.0} ms";
    }
    static IEnumerator Measure()
    {
        var home = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        Button(home.transform, "New Game").onClick.Invoke();
        var setup = UnityEngine.Object.FindFirstObjectByType<NewGamePanel>();
        setup.GetComponentInChildren<TMP_InputField>().text = "Stutter";
        Button(setup.transform, "Start New Game").onClick.Invoke();
        yield return Settled();
        if (GameSaveSystem.ActiveSlot != 1 || !RunAutosave.Instance) throw new Exception("Run in slot 1 with autosave expected");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        lines.Add($"Welt {map.GeneratedWidth} x {map.GeneratedHeight}, Terrain-Zellen {map.Terrain.cellBounds.size.x} x {map.Terrain.cellBounds.size.y}");
        yield return Wait(3);

        var baseline = new List<double>();
        double until = EditorApplication.timeSinceStartup + 5;
        while (EditorApplication.timeSinceStartup < until) { yield return null; baseline.Add(Ms); }
        lines.Add("Ohne Speichern: " + Stats(baseline));

        // Real timed autosaves through RunAutosave, frame times as the player sees them.
        GpsSettings.Preferences.autosaveIntervalMinutes = 3f / 60f;
        for (int save = 1; save <= Saves; save++)
        {
            double waitStart = EditorApplication.timeSinceStartup;
            while (!GameSaveSystem.IsBusy) { if (EditorApplication.timeSinceStartup - waitStart > 15) throw new Exception("Autosave did not start"); yield return null; }
            double start = Time.realtimeSinceStartupAsDouble;
            var during = new List<double>(); int frozen = 0;
            while (GameSaveSystem.IsBusy) { yield return null; during.Add(Ms); if (Time.timeScale == 0) frozen++; }
            double seconds = Time.realtimeSinceStartupAsDouble - start;
            var after = new List<double>();
            for (int i = 0; i < 10; i++) { yield return null; after.Add(Ms); }
            lines.Add($"Autosave {save}: Spiel steht {seconds * 1000:0} ms ({frozen} Frames angehalten); waehrenddessen {Stats(during)}; " +
                $"Frames > 33 ms: {during.Count(x => x > 33)}; danach Max {after.Max():0.0} ms");
        }
        GpsSettings.Preferences.autosaveIntervalMinutes = 0;
        yield return Wait(1);

        // Main-thread cost of each step of the save coroutine (what can produce one long frame).
        for (int save = 1; save <= Saves; save++)
        {
            var routine = GameSaveSystem.Save(GameSaveSystem.ActiveSlot, RunAutosave.Instance, null);
            var steps = new List<double>(); var watch = new Stopwatch();
            while (true)
            {
                watch.Restart(); bool more = routine.MoveNext(); watch.Stop();
                steps.Add(watch.Elapsed.TotalMilliseconds);
                if (!more) break;
                yield return null;
            }
            int worst = steps.IndexOf(steps.Max());
            lines.Add($"Schritte {save}: {steps.Count} Schritte, Summe {steps.Sum():0.0} ms, laengster Schritt {steps[worst]:0.0} ms (Nr. {worst + 1}), " +
                $"Schritte 1-3: {string.Join(" / ", steps.Take(3).Select(x => x.ToString("0.0")))} ms, uebrige Max {steps.Skip(3).DefaultIfEmpty(0).Max():0.0} ms");
            yield return Wait(1);
        }
        lines.Add($"Spielstand {new FileInfo(GameSaveSystem.SlotPath(1)).Length / 1024} KB");

        RunNavigation.MainMenu(); yield return Settled();
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        GpsSettings.Preferences.autosaveIntervalMinutes = oldInterval;
        Application.runInBackground = background;
        GameSaveSystem.TestDirectory = null; MetaProgression.TestDirectory = null;
        File.WriteAllText(output, result + "\n" + string.Join("\n", lines) + "\nIsolated directory: " + testDirectory);
    }
}
