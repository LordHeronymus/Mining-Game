using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

// Pipeline run_script, entry MetaProgressionPersistenceBenchmark.Main.
// Models 500 active hours: 125 worlds, 8000 unique natural ore cells per world.
public static class MetaProgressionPersistenceBenchmark
{
    [Serializable] public sealed class Result
    {
        public int runs, uniqueOreCells, regions;
        public double activeHours, firstSaveMs, secondSaveMs, thirdSaveMs, reloadMs, requestSaveMainThreadMs, asyncCompletionMs;
        public long fileBytes, saveAllocatedBytes, requestAllocatedBytes;
        public string directory;
    }
    public static object Main()
    {
        Directory.CreateDirectory("Temp");
        string previousDirectory = MetaProgression.TestDirectory;
        MetaRunState previousRun = MetaProgression.CaptureRunState();
        var result = new Result { runs = 125, uniqueOreCells = 1000000, regions = 100000, activeHours = 500,
            directory = Path.GetFullPath("Temp/MetaProfile500HourBenchmark-" + DateTime.UtcNow.Ticks) };
        try {
            MetaProgression.EndRun(); MetaProgression.TestDirectory = result.directory;
            var profile = MetaProgression.Profile;
            for (int world = 0; world < result.runs; world++) {
                var state = new MetaRunState { runId = "benchmark-world-" + world, maxDepth = 1200,
                    activeSeconds = 4d * 60d * 60d, progressXp = 40000d, workWindowSeconds = 300d };
                state.resourceSources = new List<string>(8000);
                for (int cell = 0; cell < 8000; cell++) state.resourceSources.Add("2:" + cell % 600 + ":" + (-600 - cell / 600));
                state.resources.Add(new MetaResourceCount { item = (int)Item.Gold, count = 8000 });
                for (int region = 0; region < 800; region++) state.regions.Add(region % 50 + ":" + (40 + region / 50));
                profile.runs.Add(state);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            if (!MetaProgression.Save()) throw new Exception(MetaProgression.LastError);
            stopwatch.Stop(); result.firstSaveMs = stopwatch.Elapsed.TotalMilliseconds;
            result.saveAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            result.fileBytes = new FileInfo(MetaProgression.ProfilePath).Length;
            MetaProgression.BeginRun("benchmark-world-124");
            MetaProgression.TickActive(.01f);
            stopwatch.Restart(); if (!MetaProgression.Save()) throw new Exception(MetaProgression.LastError);
            stopwatch.Stop(); result.secondSaveMs = stopwatch.Elapsed.TotalMilliseconds;
            MetaProgression.TickActive(.01f);
            stopwatch.Restart(); if (!MetaProgression.Save()) throw new Exception(MetaProgression.LastError);
            stopwatch.Stop(); result.thirdSaveMs = stopwatch.Elapsed.TotalMilliseconds;
            MetaProgression.TickActive(.01f);
            allocated = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Restart(); if (!MetaProgression.RequestSave()) throw new Exception(MetaProgression.LastError);
            stopwatch.Stop(); result.requestSaveMainThreadMs = stopwatch.Elapsed.TotalMilliseconds;
            result.requestAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            stopwatch.Restart(); if (!MetaProgression.Save()) throw new Exception(MetaProgression.LastError);
            stopwatch.Stop(); result.asyncCompletionMs = stopwatch.Elapsed.TotalMilliseconds;
            MetaProgression.EndRun(); MetaProgression.Reload();
            stopwatch.Restart(); int loaded = MetaProgression.Profile.runs.Count; stopwatch.Stop();
            if (loaded != result.runs || MetaProgression.TotalXp != result.runs * 40000L) throw new Exception("Benchmark profile did not reload intact.");
            result.reloadMs = stopwatch.Elapsed.TotalMilliseconds;
            File.WriteAllText("Temp/MetaProgressionPersistenceBenchmark.json", JsonUtility.ToJson(result, true));
            return result;
        } finally {
            MetaProgression.EndRun(); MetaProgression.TestDirectory = previousDirectory;
            if (previousRun != null) MetaProgression.BeginRun(previousRun.runId, previousRun);
        }
    }
}
