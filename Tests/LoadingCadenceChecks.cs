using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LoadingCadenceChecks
{
    [Serializable] public class Frame { public int stage; public string phase; public float milliseconds, progress, displayed, buildMs, validateMs, setupMs, gcMs, awakeMs, canvasMs, assetsMs; }
    [Serializable] public class Phase { public int stage, frames; public float p95, p99, maximum; public int over16ms, over50ms; }
    [Serializable] public class Report
    {
        public string error;
        public bool monotone = true, cameraFollow, borderClamp, inputReleased, pausedDuringLoading = true;
        public bool correctLoadingTexts = true;
        public bool frameRateRestored, loadingPriorityRestored;
        public float seconds, maximumAnimationStep;
        public float firstVisibleProgress = -1, maximumVisibleProgressStep;
        public string recorderAvailability;
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
    static bool restoringRun;
    static bool seenContent;
    static int priorFrameRate;
    static UnityEngine.ThreadPriority priorLoadingPriority;
    static float previousDisplayed;
    static Unity.Profiling.ProfilerRecorder buildRecorder, validateRecorder, setupRecorder, gcRecorder;
    static Unity.Profiling.ProfilerRecorder awakeRecorder, canvasRecorder, assetsRecorder;
    static readonly System.Reflection.FieldInfo displayedValue = typeof(LoadingScreen).GetField("displayed",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    static readonly System.Reflection.FieldInfo animationClock = typeof(LoadingScreen).GetField("animationTime",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    public static object Main()
        => Start(false);
    public static object LoadMain()
        => Start(true);
    static object Start(bool restoring)
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Start from the running main menu.";
        frames.Clear(); seen = false; lastFrame = -1; lastProgress = previousAnimation = 0; report = new Report();
        restoringRun = restoring;
        seenContent = false; previousDisplayed = 0;
        priorFrameRate = Application.targetFrameRate;
        priorLoadingPriority = Application.backgroundLoadingPriority;
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadingCadence-" +
            SessionState.GetString("LoadingCadence.Tag", "before") + ".json"));
        began = previous = EditorApplication.timeSinceStartup;
        // New Game now creates a real save. Keep all subsequent test-run saves isolated until Play stops.
        if (!restoring)
        {
            GameSaveSystem.TestDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Temp/LoadingCadenceSaves-" + DateTime.UtcNow.Ticks));
            SessionState.SetString("LoadingCadence.SaveDirectory", GameSaveSystem.TestDirectory);
        }
        else
        {
            var directory = SessionState.GetString("LoadingCadence.SaveDirectory", "");
            if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, "slot-1.thsave")))
                return "No isolated save is available. Run Main first.";
            GameSaveSystem.TestDirectory = directory;
        }
        buildRecorder = Record(Unity.Profiling.ProfilerCategory.Scripts, "Loading.BuildScreen");
        validateRecorder = Record(Unity.Profiling.ProfilerCategory.Scripts, "Loading.ValidateSave");
        setupRecorder = Record(Unity.Profiling.ProfilerCategory.Scripts, "Loading.MapSetup");
        gcRecorder = Record(Unity.Profiling.ProfilerCategory.Memory, "GC.Collect");
        awakeRecorder = RecordAvailable("AwakeFromLoad");
        canvasRecorder = RecordAvailable("Canvas.BuildBatch");
        assetsRecorder = RecordAvailable("PreloadManager.UpdatePreloading");
        report.recorderAvailability = $"Recording={UnityEditorInternal.ProfilerDriver.enabled}, Build={buildRecorder.Valid}, Validate={validateRecorder.Valid}, Setup={setupRecorder.Valid}, GC={gcRecorder.Valid}, Awake={awakeRecorder.Valid}, Canvas={canvasRecorder.Valid}, Assets={assetsRecorder.Valid}";
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        Canvas.willRenderCanvases += Sample;
        EditorApplication.update += Poll;
        if (restoring)
        {
            if (!RunNavigation.LoadGame(1, out string error)) Finish(error ?? "Load could not start");
        }
        else RunNavigation.NewGame();
        return output;
    }
    static void Sample()
    {
        if ((!LoadingProgress.Active && !LoadingScreen.TransitionActive) || Time.frameCount == lastFrame) return;
        double now = EditorApplication.timeSinceStartup;
        float progress = LoadingProgress.Active ? LoadingProgress.Target : 0;
        var sample = new Frame { stage = LoadingProgress.Active ? LoadingProgress.Stage : -1, phase = LoadingScreen.TransitionPhase,
            milliseconds = (float)((now - previous) * 1000), progress = progress,
            buildMs = Milliseconds(buildRecorder), validateMs = Milliseconds(validateRecorder),
            setupMs = Milliseconds(setupRecorder), gcMs = Milliseconds(gcRecorder),
            awakeMs = Milliseconds(awakeRecorder), canvasMs = Milliseconds(canvasRecorder), assetsMs = Milliseconds(assetsRecorder) };
        frames.Add(sample);
        buildRecorder.Reset(); validateRecorder.Reset(); setupRecorder.Reset(); gcRecorder.Reset();
        if (awakeRecorder.Valid) awakeRecorder.Reset();
        if (canvasRecorder.Valid) canvasRecorder.Reset();
        if (assetsRecorder.Valid) assetsRecorder.Reset();
        if (progress < lastProgress) report.monotone = false;
        if (Time.timeScale != 0 || !GameplayInputBlocker.IsBlocked) report.pausedDuringLoading = false;
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreen>();
        if (LoadingProgress.Active && (LoadingProgress.Restoring != restoringRun || (screen &&
            !screen.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(text =>
                text.text == (restoringRun ? "Spielstand laden" : "Mine vorbereiten")))))
            report.correctLoadingTexts = false;
        if (LoadingProgress.Active) seenContent = true;
        if (screen && displayedValue != null && LoadingProgress.Active)
        {
            sample.displayed = (float)displayedValue.GetValue(screen);
            if (LoadingScreen.ContentVisible)
            {
                if (report.firstVisibleProgress < 0) report.firstVisibleProgress = sample.displayed;
                report.maximumVisibleProgressStep = Mathf.Max(report.maximumVisibleProgressStep, sample.displayed - previousDisplayed);
                previousDisplayed = sample.displayed;
            }
        }
        if (screen && animationClock != null && LoadingProgress.Active)
        {
            float animation = (float)animationClock.GetValue(screen);
            if (seen) report.maximumAnimationStep = Mathf.Max(report.maximumAnimationStep, animation - previousAnimation);
            report.strikes = Mathf.Max(0, Mathf.FloorToInt(animation / LoadingCrystalVisual.CycleSeconds - LoadingCrystalVisual.ContactPhase) + 1);
            previousAnimation = animation;
        }
        lastProgress = progress; previous = now; lastFrame = Time.frameCount; seen = true;
    }
    static void Poll()
    {
        if (!EditorApplication.isPlaying) { Finish("Play mode stopped before completion"); return; }
        if (EditorApplication.timeSinceStartup - began > 180) { Finish("Loading timed out"); return; }
        if (seen && !LoadingProgress.Active && !LoadingScreen.TransitionActive && !RunNavigation.IsTransitioning)
            Finish(seenContent ? null : "Load failed before the loading content was shown");
    }
    static void Finish(string error)
    {
        Canvas.willRenderCanvases -= Sample; EditorApplication.update -= Poll;
        buildRecorder.Dispose(); validateRecorder.Dispose(); setupRecorder.Dispose(); gcRecorder.Dispose();
        awakeRecorder.Dispose(); canvasRecorder.Dispose(); assetsRecorder.Dispose();
        Application.runInBackground = oldBackground;
        report.error = error; report.seconds = (float)(EditorApplication.timeSinceStartup - began);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        report.cameraFollow = player && camera && camera.GetComponent<CameraFollow>().enabled &&
            camera.GetComponent<CameraFollow>().target == player.transform && GameplayTestSettings.CameraFollowEnabled;
        report.borderClamp = camera && camera.GetComponent<CameraWorldBorderClamp>().enabled;
        report.inputReleased = !LoadingProgress.Active && !GameplayInputBlocker.IsBlocked && Time.timeScale == 1;
        report.frameRateRestored = Application.targetFrameRate == priorFrameRate;
        report.loadingPriorityRestored = Application.backgroundLoadingPriority == priorLoadingPriority;
        if (string.IsNullOrEmpty(error) && (!report.monotone || !report.correctLoadingTexts ||
            !report.cameraFollow || !report.borderClamp || !report.inputReleased || !report.pausedDuringLoading ||
            !report.frameRateRestored || !report.loadingPriorityRestored || report.firstVisibleProgress < 0 ||
            report.firstVisibleProgress > .01001f || report.maximumVisibleProgressStep > .01001f ||
            report.maximumAnimationStep > 1f / 30f + .00001f))
            report.error = "Loading invariant failed; inspect report flags/progress/animation values.";
        report.phases = frames.GroupBy(x => x.stage).Select(group => {
            var values = group.Select(x => x.milliseconds).OrderBy(x => x).ToArray();
            return new Phase { stage = group.Key, frames = values.Length, maximum = values.Last(),
                p95 = values[Mathf.Min(values.Length - 1, (int)(values.Length * .95f))],
                p99 = values[Mathf.Min(values.Length - 1, (int)(values.Length * .99f))],
                over16ms = values.Count(x => x > LoadingWorkBudget.TargetFrameMilliseconds),
                over50ms = values.Count(x => x > 50) };
        }).ToArray();
        report.frames = frames.ToArray(); File.WriteAllText(output, JsonUtility.ToJson(report, true));
        // Pipeline-loaded types are not all traversed by Unity's nested-type serializer.
        File.WriteAllLines(output + ".csv", new[] { "stage,milliseconds,progress,phase,displayed,buildMs,validateMs,setupMs,gcMs,awakeMs,canvasMs,assetsMs" }.Concat(frames.Select(x =>
            x.stage + "," + x.milliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
            x.progress.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + x.phase + "," +
            string.Join(",", new[] { x.displayed, x.buildMs, x.validateMs, x.setupMs, x.gcMs, x.awakeMs, x.canvasMs, x.assetsMs }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))));
    }
    static Unity.Profiling.ProfilerRecorder Record(Unity.Profiling.ProfilerCategory category, string name)
        => Unity.Profiling.ProfilerRecorder.StartNew(category, name, 1,
            Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame);
    static float Milliseconds(Unity.Profiling.ProfilerRecorder recorder)
        => recorder.Valid && recorder.Count > 0 ? recorder.LastValue / 1000000f : 0;
    static Unity.Profiling.ProfilerRecorder RecordAvailable(string name)
    {
        var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var handle in handles)
        {
            var description = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name == name) return Record(description.Category, name);
        }
        return default;
    }
}
