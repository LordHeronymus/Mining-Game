using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;

[InitializeOnLoad]
public static class MiningPerformanceMonitor
{
    const string ArmedKey = "Mining.PerformanceMonitor.Armed";
    const string Folder = "Logs/PerformanceMonitor";
    static ProfilerRecorder playerLoop, mainThread, allocations, collider, headlamp, cache, lighting, discovery;
    static readonly int[] histogram = new int[5001];
    static readonly List<Spike> spikes = new List<Spike>(4096);
    static readonly StringBuilder csvBuffer = new StringBuilder(262144);
    struct Spike
    {
        public DateTime utc;
        public int frame;
        public double ms, loopMs, mainMs, colliderMs, headlampMs, cacheMs, lightingMs, discoveryMs;
        public long bytes;
        public bool mining, hit;
        public Vector2 position;
    }
    static Summary summary;
    static string csvPath;
    static int lastFrame = -1, minedFrame = -100, hitFrame = -100, skipThroughFrame = -1;
    static Vector2 minedPosition;
    static double nextWrite;

    [Serializable]
    public sealed class Summary
    {
        public string session, startedUtc, updatedUtc, state;
        public int frames, minedBlocks, slowFrames33, slowFrames50, miningSlowFrames33, excludedFrames;
        public int belowTarget140Frames, atOrBelow100Frames;
        public int excludedPaused, excludedUnfocused, excludedLoading, excludedTimeStopped, excludedWriting;
        public int excludedDebugMenu;
        public int missedFrames;
        public double averageFps, belowTarget140Percent, atOrBelow100Percent;
        public double longestBelowTargetSeconds, longestAtOrBelow100Seconds;
        public int vSyncCount, targetFrameRate;
        public double averageFrameMs, p95FrameMs, p99FrameMs, maxFrameMs;
        public double maxPlayerLoopMs, maxMainThreadMs, maxColliderMs, maxHeadlampMs, maxHeadlampCacheMs;
        public double maxLightingMs, maxDiscoveryMs;
        public double totalLightingMs, totalDiscoveryMs, averageLightingMs, averageDiscoveryMs;
        public bool lightingAvailable, discoveryAvailable;
        public long maxAllocatedBytes;
        public double totalFrameMs;
        public bool playerLoopAvailable, mainThreadAvailable, allocationsAvailable, colliderAvailable, headlampAvailable;
    }
    static double belowTargetRun, unacceptableRun;

    static MiningPerformanceMonitor()
    {
        PerformanceMonitorControl.Configure(
            () => SessionState.GetBool(ArmedKey, false),
            enabled => { if (enabled) Arm(); else Stop(); });
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += () => Finish("reloading");
        EditorApplication.quitting += () => Finish("editor_closed");
    }

    public static string Arm()
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(ArmedKey, true);
        if (EditorApplication.isPlaying && summary == null) Begin();
        if (summary == null) File.WriteAllText(Folder + "/summary.json", JsonUtility.ToJson(new Summary { state = "armed", updatedUtc = DateTime.UtcNow.ToString("o") }, true));
        return Path.GetFullPath(Folder + "/summary.json");
    }

    public static void Stop()
    {
        if (File.Exists(Folder + "/arm-next-session.request"))
            File.Delete(Folder + "/arm-next-session.request");
        SessionState.SetBool(ArmedKey, false);
        Finish("stopped");
    }

    static ProfilerRecorder Record(ProfilerCategory category, string name) => ProfilerRecorder.StartNew(category, name, 1,
        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);

    static void Begin()
    {
        // Register both marker sets before creating recorders, even during map loading.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(TerrainColliderChunks).TypeHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(SmoothHeadlampField).TypeHandle);
        Directory.CreateDirectory(Folder);
        summary = new Summary { session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), startedUtc = DateTime.UtcNow.ToString("o"), state = "loading" };
        Array.Clear(histogram, 0, histogram.Length);
        spikes.Clear();
        lastFrame = -1; minedFrame = hitFrame = -100; skipThroughFrame = -1;
        belowTargetRun = unacceptableRun = 0;
        csvPath = Folder + "/" + summary.session + "-spikes.csv";
        File.WriteAllText(csvPath, "utc,frame,frame_ms,player_loop_ms,main_thread_ms,collider_ms,headlamp_ms,headlamp_cache_ms,allocated_bytes,mining_recent,hit_recent,mined_x,mined_y,lighting_ms,discovery_ms\n");
        playerLoop = Record(new ProfilerCategory("PlayerLoop"), "PlayerLoop");
        mainThread = Record(ProfilerCategory.Internal, "Main Thread");
        allocations = Record(ProfilerCategory.Memory, "GC Allocated In Frame");
        collider = Record(ProfilerCategory.Scripts, "Mining.ColliderChunk");
        headlamp = Record(ProfilerCategory.Scripts, "Mining.Headlamp");
        cache = Record(ProfilerCategory.Scripts, "Mining.HeadlampCache");
        lighting = Record(ProfilerCategory.Scripts, "Mining.Lighting");
        discovery = Record(ProfilerCategory.Scripts, "Mining.Discovery");
        TileMiner.OnBlockMined += Mined;
        TileMiner.OnBlockHit += Hit;
        nextWrite = EditorApplication.timeSinceStartup + 5;
        Write();
    }

    static void Mined(Vector2 position, int points)
    {
        if (summary == null) return;
        minedFrame = Time.frameCount; minedPosition = position; summary.minedBlocks++;
    }
    static void Hit(Vector2 position) => hitFrame = Time.frameCount;
    static double Milliseconds(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue / 1000000.0 : 0;

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            if (summary != null) { Finish("finished"); SessionState.SetBool(ArmedKey, false); }
            if (File.Exists(Folder + "/arm-next-session.request"))
            {
                File.Delete(Folder + "/arm-next-session.request");
                Arm();
            }
            return;
        }
        if (!SessionState.GetBool(ArmedKey, false)) return;
        if (summary == null) Begin();
        int frame = Time.frameCount;
        if (frame == lastFrame) return;
        if (lastFrame >= 0) summary.missedFrames += Math.Max(0, frame - lastFrame - 1);
        lastFrame = frame;
        // In the Editor, use the Editor application's focus, not Game View focus.
        bool paused = EditorApplication.isPaused;
        bool unfocused = !UnityEditorInternal.InternalEditorUtility.isApplicationActive;
        bool loading = LoadingProgress.Active;
        bool timeStopped = Time.timeScale <= 0;
        bool writing = frame <= skipThroughFrame;
        bool debugMenu = GameplayDebugPanel.IsOpen;
        if (debugMenu) skipThroughFrame = Math.Max(skipThroughFrame, frame + 2);
        bool excluded = paused || unfocused || loading || timeStopped || writing || debugMenu;
        summary.vSyncCount = QualitySettings.vSyncCount;
        summary.targetFrameRate = Application.targetFrameRate;
        summary.state = LoadingProgress.Active ? "loading" : "playing";
        if (excluded)
        {
            summary.excludedFrames++;
            if (paused) summary.excludedPaused++;
            if (unfocused) summary.excludedUnfocused++;
            if (loading) summary.excludedLoading++;
            if (timeStopped) summary.excludedTimeStopped++;
            if (writing) summary.excludedWriting++;
            if (debugMenu) summary.excludedDebugMenu++;
            belowTargetRun = unacceptableRun = 0;
        }
        else
        {
            double ms = Time.unscaledDeltaTime * 1000.0;
            double loopMs = Milliseconds(playerLoop), mainMs = Milliseconds(mainThread);
            double colliderMs = Milliseconds(collider), headlampMs = Milliseconds(headlamp), cacheMs = Milliseconds(cache);
            long bytes = allocations.Valid && allocations.Count > 0 ? allocations.LastValue : 0;
            summary.frames++; summary.totalFrameMs += ms;
            if (ms > 1000.0 / 140) { summary.belowTarget140Frames++; belowTargetRun += ms / 1000; }
            else belowTargetRun = 0;
            if (ms >= 10) { summary.atOrBelow100Frames++; unacceptableRun += ms / 1000; }
            else unacceptableRun = 0;
            summary.longestBelowTargetSeconds = Math.Max(summary.longestBelowTargetSeconds, belowTargetRun);
            summary.longestAtOrBelow100Seconds = Math.Max(summary.longestAtOrBelow100Seconds, unacceptableRun);
            histogram[Math.Min(5000, Math.Max(0, (int)Math.Ceiling(ms * 10)))]++;
            summary.maxFrameMs = Math.Max(summary.maxFrameMs, ms);
            summary.maxPlayerLoopMs = Math.Max(summary.maxPlayerLoopMs, loopMs);
            summary.maxMainThreadMs = Math.Max(summary.maxMainThreadMs, mainMs);
            summary.maxColliderMs = Math.Max(summary.maxColliderMs, colliderMs);
            summary.maxHeadlampMs = Math.Max(summary.maxHeadlampMs, headlampMs);
            summary.maxHeadlampCacheMs = Math.Max(summary.maxHeadlampCacheMs, cacheMs);
            summary.maxAllocatedBytes = Math.Max(summary.maxAllocatedBytes, bytes);
            double lightingMs = Milliseconds(lighting), discoveryMs = Milliseconds(discovery);
            summary.totalLightingMs += lightingMs;
            summary.totalDiscoveryMs += discoveryMs;
            summary.maxLightingMs = Math.Max(summary.maxLightingMs, lightingMs);
            summary.maxDiscoveryMs = Math.Max(summary.maxDiscoveryMs, discoveryMs);
            summary.lightingAvailable |= lighting.Valid && lighting.Count > 0;
            summary.discoveryAvailable |= discovery.Valid && discovery.Count > 0;
            summary.playerLoopAvailable |= playerLoop.Valid && playerLoop.Count > 0;
            summary.mainThreadAvailable |= mainThread.Valid && mainThread.Count > 0;
            summary.allocationsAvailable |= allocations.Valid && allocations.Count > 0;
            summary.colliderAvailable |= collider.Valid && collider.Count > 0;
            summary.headlampAvailable |= headlamp.Valid && headlamp.Count > 0;
            bool mining = frame - minedFrame >= 0 && frame - minedFrame <= 2;
            if (ms >= 33.333)
            {
                summary.slowFrames33++;
                if (ms >= 50) summary.slowFrames50++;
                if (mining) summary.miningSlowFrames33++;
            }
            if (ms > 1000.0 / 140)
            {
                spikes.Add(new Spike { utc = DateTime.UtcNow, frame = frame, ms = ms,
                    loopMs = loopMs, mainMs = mainMs, colliderMs = colliderMs, headlampMs = headlampMs,
                    cacheMs = cacheMs, lightingMs = lightingMs, discoveryMs = discoveryMs, bytes = bytes,
                    mining = mining, hit = frame - hitFrame >= 0 && frame - hitFrame <= 2,
                    position = minedPosition });
            }
        }
        ResetSamples();
        if (EditorApplication.timeSinceStartup >= nextWrite)
        {
            Write();
            // Exclude frames directly affected by our infrequent disk write.
            skipThroughFrame = frame + 2;
            nextWrite = EditorApplication.timeSinceStartup + 5;
        }
    }

    static void ResetSamples()
    {
        // Markers absent in a frame must not retain the last nonzero sample.
        playerLoop.Reset(); mainThread.Reset(); allocations.Reset(); collider.Reset(); headlamp.Reset(); cache.Reset();
        lighting.Reset(); discovery.Reset();
        // Reset stops collection as well as clearing samples; explicitly resume it.
        playerLoop.Start(); mainThread.Start(); allocations.Start(); collider.Start(); headlamp.Start(); cache.Start();
        lighting.Start(); discovery.Start();
    }

    static double Percentile(double fraction)
    {
        int target = (int)Math.Ceiling(summary.frames * fraction), count = 0;
        for (int i = 0; i < histogram.Length; i++)
        { count += histogram[i]; if (count >= target) return i / 10.0; }
        return 0;
    }
    static void Write()
    {
        summary.updatedUtc = DateTime.UtcNow.ToString("o");
        summary.averageFrameMs = summary.totalFrameMs / Math.Max(1, summary.frames);
        summary.averageFps = summary.totalFrameMs > 0 ? summary.frames * 1000.0 / summary.totalFrameMs : 0;
        summary.averageLightingMs = summary.totalLightingMs / Math.Max(1, summary.frames);
        summary.averageDiscoveryMs = summary.totalDiscoveryMs / Math.Max(1, summary.frames);
        summary.belowTarget140Percent = summary.belowTarget140Frames * 100.0 / Math.Max(1, summary.frames);
        summary.atOrBelow100Percent = summary.atOrBelow100Frames * 100.0 / Math.Max(1, summary.frames);
        summary.p95FrameMs = summary.frames > 0 ? Percentile(.95) : 0;
        summary.p99FrameMs = summary.frames > 0 ? Percentile(.99) : 0;
        if (spikes.Count > 0)
        {
            csvBuffer.Clear();
            foreach (var spike in spikes)
                csvBuffer.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9},{10},{11:F2},{12:F2},{13:F3},{14:F3}\n",
                    spike.utc.ToString("o"), spike.frame, spike.ms, spike.loopMs, spike.mainMs,
                    spike.colliderMs, spike.headlampMs, spike.cacheMs, spike.bytes,
                    spike.mining ? 1 : 0, spike.hit ? 1 : 0, spike.position.x, spike.position.y,
                    spike.lightingMs, spike.discoveryMs);
            File.AppendAllText(csvPath, csvBuffer.ToString());
            spikes.Clear();
        }
        var json = JsonUtility.ToJson(summary, true);
        File.WriteAllText(Folder + "/" + summary.session + "-summary.json", json);
        File.WriteAllText(Folder + "/summary.json", json);
    }
    static void Finish(string state)
    {
        if (summary == null) return;
        TileMiner.OnBlockMined -= Mined; TileMiner.OnBlockHit -= Hit;
        summary.state = state; Write();
        playerLoop.Dispose(); mainThread.Dispose(); allocations.Dispose(); collider.Dispose(); headlamp.Dispose(); cache.Dispose();
        lighting.Dispose(); discovery.Dispose();
        summary = null;
    }
}
