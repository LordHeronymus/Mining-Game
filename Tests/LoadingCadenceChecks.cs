using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LoadingCadenceChecks
{
    [Serializable] public class Frame { public int stage; public float milliseconds, progress; }
    [Serializable] public class Phase { public int stage, frames; public float p95, maximum; public int over50ms; }
    [Serializable] public class Report
    {
        public string error;
        public bool monotone = true, cameraFollow, borderClamp, inputReleased, pausedDuringLoading = true;
        public float seconds, maximumAnimationStep;
        public int strikes;
        public Phase[] phases;
        public Frame[] frames;
    }
    static readonly List<Frame> frames = new List<Frame>();
    static bool seen, oldBackground;
    static int lastFrame;
    static float lastProgress;
    static double began, previous;
    static string output;
    static Report report;
    static float previousAnimation;
    static readonly System.Reflection.FieldInfo animationClock = typeof(LoadingScreen).GetField("animationTime",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Start from the running main menu.";
        frames.Clear(); seen = false; lastFrame = -1; lastProgress = previousAnimation = 0; report = new Report();
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadingCadence-" +
            SessionState.GetString("LoadingCadence.Tag", "before") + ".json"));
        began = previous = EditorApplication.timeSinceStartup;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        Canvas.willRenderCanvases += Sample;
        EditorApplication.update += Poll;
        RunNavigation.NewGame();
        return output;
    }
    static void Sample()
    {
        if (!LoadingProgress.Active || Time.frameCount == lastFrame) return;
        double now = EditorApplication.timeSinceStartup;
        if (seen) frames.Add(new Frame { stage = LoadingProgress.Stage,
            milliseconds = (float)((now - previous) * 1000), progress = LoadingProgress.Target });
        if (LoadingProgress.Target < lastProgress) report.monotone = false;
        if (Time.timeScale != 0 || !GameplayInputBlocker.IsBlocked) report.pausedDuringLoading = false;
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreen>();
        if (screen && animationClock != null)
        {
            float animation = (float)animationClock.GetValue(screen);
            if (seen) report.maximumAnimationStep = Mathf.Max(report.maximumAnimationStep, animation - previousAnimation);
            report.strikes = Mathf.Max(0, Mathf.FloorToInt(animation / LoadingCrystalVisual.CycleSeconds - LoadingCrystalVisual.ContactPhase) + 1);
            previousAnimation = animation;
        }
        lastProgress = LoadingProgress.Target; previous = now; lastFrame = Time.frameCount; seen = true;
    }
    static void Poll()
    {
        if (!EditorApplication.isPlaying) { Finish("Play mode stopped before completion"); return; }
        if (EditorApplication.timeSinceStartup - began > 180) { Finish("Loading timed out"); return; }
        if (seen && !LoadingProgress.Active) Finish(null);
    }
    static void Finish(string error)
    {
        Canvas.willRenderCanvases -= Sample; EditorApplication.update -= Poll;
        Application.runInBackground = oldBackground;
        report.error = error; report.seconds = (float)(EditorApplication.timeSinceStartup - began);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        report.cameraFollow = player && camera && camera.GetComponent<CameraFollow>().enabled &&
            camera.GetComponent<CameraFollow>().target == player.transform && GameplayTestSettings.CameraFollowEnabled;
        report.borderClamp = camera && camera.GetComponent<CameraWorldBorderClamp>().enabled;
        report.inputReleased = !LoadingProgress.Active && !GameplayInputBlocker.IsBlocked && Time.timeScale == 1;
        report.phases = frames.GroupBy(x => x.stage).Select(group => {
            var values = group.Select(x => x.milliseconds).OrderBy(x => x).ToArray();
            return new Phase { stage = group.Key, frames = values.Length, maximum = values.Last(),
                p95 = values[Mathf.Min(values.Length - 1, (int)(values.Length * .95f))],
                over50ms = values.Count(x => x > 50) };
        }).ToArray();
        report.frames = frames.ToArray(); File.WriteAllText(output, JsonUtility.ToJson(report, true));
        // Pipeline-loaded types are not all traversed by Unity's nested-type serializer.
        File.WriteAllLines(output + ".csv", new[] { "stage,milliseconds,progress" }.Concat(frames.Select(x =>
            x.stage + "," + x.milliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
            x.progress.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
}
