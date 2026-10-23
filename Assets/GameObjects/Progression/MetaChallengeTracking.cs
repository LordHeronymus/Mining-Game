using System;
using System.Collections.Generic;

// Challenges marked with the star in "Herausforderungen" and followed in the HUD (ChallengeTrackerHud).
// Keys come from MetaChallengeProgress.track: round tasks carry their round start, so a mark ends with its round;
// lifetime lines keep theirs, run objectives belong to their run.
public static partial class MetaProgression
{
    public const int TrackedShown = 4;
    static readonly string[] RoundTrackPrefixes = { "Daily-", "Weekly-", "Monthly-" };

    public static bool IsTracked(string key)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(key) && profile.trackedChallenges != null && profile.trackedChallenges.Contains(key);
    }

    public static void SetTracked(string key, bool tracked)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key) || key.Length > 160) return;
        var list = profile.trackedChallenges ??= new List<string>();
        if (tracked == list.Contains(key)) return;
        if (tracked) {
            list.Add(key);
            if (list.Count > MaxTrackedChallenges) list.RemoveRange(0, list.Count - MaxTrackedChallenges);
        } else list.Remove(key);
        MarkChanged(true);
    }

    // Marked challenges in marking order, finished ones included. Marks of past rounds and unknown lines are dropped.
    public static List<MetaChallengeProgress> TrackedChallenges()
    {
        EnsureLoaded();
        var result = new List<MetaChallengeProgress>();
        var keys = profile.trackedChallenges;
        if (keys == null || keys.Count == 0) return result;
        bool rounds = false, lifetime = false;
        foreach (var key in keys) {
            if (key == null) continue;
            if (IsRoundTrack(key)) rounds = true; else lifetime = true;
        }
        var views = new Dictionary<string, MetaChallengeProgress>();
        if (rounds) {
            foreach (var period in RoundPeriods) Collect(views, GetChallenges(period));
            var project = GetMonthlyProject();
            if (project?.track != null) views[project.track] = project;
        }
        if (lifetime) Collect(views, GetChallenges(ChallengePeriod.Lifetime));
        bool pruned = false;
        for (int i = keys.Count - 1; i >= 0; i--) {
            string key = keys[i];
            // Run objectives of a run that is not loaded stay marked; everything else must still exist.
            bool keep = key != null && (views.ContainsKey(key) || key.StartsWith("Run-", StringComparison.Ordinal));
            if (!keep) { keys.RemoveAt(i); pruned = true; }
        }
        if (pruned) MarkDirty();
        foreach (var key in keys) if (views.TryGetValue(key, out var view)) result.Add(view);
        return result;
    }

    // At most TrackedShown unfinished marks; a finished one frees its place for the next mark.
    public static List<MetaChallengeProgress> TrackedForHud()
    {
        var result = new List<MetaChallengeProgress>();
        foreach (var view in TrackedChallenges()) {
            if (view.completed) continue;
            result.Add(view);
            if (result.Count == TrackedShown) break;
        }
        return result;
    }

    static bool IsRoundTrack(string key)
    {
        foreach (var prefix in RoundTrackPrefixes) if (key.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }
    static void Collect(Dictionary<string, MetaChallengeProgress> views, MetaChallengeProgress[] values)
    {
        if (values != null) foreach (var view in values) if (view?.track != null) views[view.track] = view;
    }
}
