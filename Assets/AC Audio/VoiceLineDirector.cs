using System;
using System.Collections.Generic;

public struct VoiceLinePlay { public VoiceLineEvent trigger; public int line, take, priority; public bool interrupt, forced; }

// Pure voice line scheduling: decides WHAT is said and WHEN, never touches audio. Uses its own System.Random (never
// UnityEngine.Random) so it cannot disturb the seed-stable map generation.
public sealed class VoiceLineDirector
{
    sealed class Bag { public int count; public readonly List<int> items = new(); }

    readonly Random random;
    readonly VoiceLineEventRule[] fallback = VoiceLineEventRule.Defaults();
    readonly Dictionary<VoiceLineEvent, double> lineStarts = new();
    readonly Dictionary<VoiceLineEvent, Bag> bags = new();
    readonly Dictionary<VoiceLineEvent, int> lastLines = new();
    readonly Dictionary<(VoiceLineEvent, int), int> lastTakes = new();
    public Func<VoiceLineEvent, int> lines;      // sentence count for event (0 = no clips)
    public Func<VoiceLineEvent, int, int> takes; // take count (V1/V2...) for a sentence

    bool hasPending, pendingForced, speaking;
    VoiceLineEvent pendingEvent, currentEvent;
    int pendingPriority, currentPriority;
    double notBefore, expires, lastEnd = double.NegativeInfinity;

    public VoiceLineDirector(int seed) => random = new Random(seed);

    public bool HasPending => hasPending;
    public VoiceLineEvent PendingEvent => pendingEvent;
    public bool PendingForced => hasPending && pendingForced;
    public bool IsSpeaking => speaking;
    public VoiceLineEvent CurrentEvent => currentEvent;
    public int CurrentPriority => currentPriority;

    public static bool IsTerminal(string reason) => reason == "disabled" || reason == "no-clips" || reason == "cooldown" || reason == "chance";

    VoiceLineEventRule Rule(VoiceLineConfig config, VoiceLineEvent e)
    {
        var r = config?.rule?.Invoke(e);
        if (r != null) return r;
        int i = (int)e;
        return i >= 0 && i < fallback.Length ? fallback[i] : new VoiceLineEventRule { trigger = e };
    }

    int Lines(VoiceLineEvent e) => lines?.Invoke(e) ?? 0;

    // Reason codes: "queued", "disabled", "no-clips", "cooldown", "chance", "lower-priority", "duplicate".
    public string Submit(VoiceLineEvent e, double now, VoiceLineConfig config, bool force = false)
    {
        var rule = Rule(config, e);
        if (force)
        {
            if (Lines(e) <= 0) return "no-clips";
            Store(e, rule.priority, now, double.PositiveInfinity, true);
            return "queued";
        }
        if (config != null && !config.enabled) return "disabled";
        if (Lines(e) <= 0) return "no-clips";
        if ((hasPending && pendingEvent == e) || (speaking && currentEvent == e)) return "duplicate";
        if (lineStarts.TryGetValue(e, out var t0) && now - t0 < rule.cooldownSeconds) return "cooldown";
        if (hasPending && (pendingForced || pendingPriority >= rule.priority)) return "lower-priority";
        if (!(random.NextDouble() * 100.0 < Math.Clamp(rule.playChancePercent, 0f, 100f))) return "chance";
        double start = now + Math.Max(0.0, rule.delaySeconds);
        Store(e, rule.priority, start, start + Math.Max(0.0, rule.maxWaitSeconds), false);
        return "queued";
    }

    void Store(VoiceLineEvent e, int priority, double from, double until, bool forced)
    {
        hasPending = true; pendingEvent = e; pendingPriority = priority; notBefore = from; expires = until; pendingForced = forced;
    }

    public bool Update(double now, VoiceLineConfig config, bool canStart, out VoiceLinePlay play)
    {
        play = default;
        if (hasPending && !pendingForced && now > expires) hasPending = false;
        if (!hasPending || !canStart || now < notBefore) return false;
        bool interrupt = false;
        if (speaking)
        {
            if (!pendingForced && pendingPriority < currentPriority + (config?.interruptPriorityGap ?? 25)) return false;
            interrupt = true;
        }
        else if (!pendingForced)
        {
            var c = config ?? new VoiceLineConfig();
            double gap = now - lastEnd;
            if (gap < c.minimumGapSeconds) return false;
            if (gap < (Rule(config, pendingEvent).ignoreGlobalCooldown ? c.reactionGapSeconds : c.globalCooldownSeconds)) return false;
        }
        var e = pendingEvent;
        int count = Lines(e);
        if (count <= 0) { hasPending = false; return false; }
        int line = DrawLine(e, count), take = DrawTake(e, line);
        lineStarts[e] = now;
        speaking = true; currentEvent = e; currentPriority = pendingPriority;
        play = new VoiceLinePlay { trigger = e, line = line, take = take, priority = pendingPriority, interrupt = interrupt, forced = pendingForced };
        hasPending = false; pendingForced = false;
        return true;
    }

    int DrawLine(VoiceLineEvent e, int count)
    {
        if (!bags.TryGetValue(e, out var bag)) bags[e] = bag = new Bag { count = count };
        if (bag.count != count) { bag.count = count; bag.items.Clear(); }
        var items = bag.items;
        int last = LastLine(e);
        if (items.Count == 0)
        {
            for (int i = 0; i < count; i++) items.Add(i);
            for (int i = count - 1; i > 0; i--) { int j = random.Next(i + 1); (items[i], items[j]) = (items[j], items[i]); }
            // Drawn from the end: never start a new cycle with the sentence that was just played.
            if (count > 1 && items[count - 1] == last) { int j = random.Next(count - 1); (items[count - 1], items[j]) = (items[j], items[count - 1]); }
        }
        int line = items[items.Count - 1];
        items.RemoveAt(items.Count - 1);
        lastLines[e] = line;
        return line;
    }

    int DrawTake(VoiceLineEvent e, int line)
    {
        int count = takes?.Invoke(e, line) ?? 1, take = 0;
        if (count > 1)
        {
            if (lastTakes.TryGetValue((e, line), out var last) && last >= 0 && last < count) { take = random.Next(count - 1); if (take >= last) take++; }
            else take = random.Next(count);
        }
        lastTakes[(e, line)] = take;
        return take;
    }

    // Current line ended or was stopped (an interrupted line is replaced by the next Update start instead).
    public void Finished(double now) { lastEnd = now; speaking = false; }

    // Run/transition: forget pending, speaking, per-event cooldowns and lastEnd. Keeps bags and last sentences/takes.
    public void Reset()
    {
        hasPending = pendingForced = speaking = false;
        lineStarts.Clear();
        lastEnd = double.NegativeInfinity;
    }

    // Tests only: also forgets bags and last sentence/take.
    public void ResetHistory() { Reset(); bags.Clear(); lastLines.Clear(); lastTakes.Clear(); }

    public void ClearPending() { hasPending = false; pendingForced = false; }

    public int LastLine(VoiceLineEvent e) => lastLines.TryGetValue(e, out var line) ? line : -1;
}
