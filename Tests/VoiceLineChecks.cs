using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Edit-mode checks of the character voice lines: director rules, watchers, craft mapping, library/importers, settings, GPS schema.
public static class VoiceLineChecks
{
    const string Report = "Temp/VoiceLineChecks.txt", LibraryPath = "Assets/Resources/Audio/VoiceLineLibrary.asset", VoiceFolder = "Assets/AC Audio/Voice";
    const string ManifestPath = "Design/Sprachausgabe/Voice-Manifest.json", VoiceKey = "settings.audio.voice";
    const int Seed = 20261020;
    static readonly List<string> passed = new();
    static void Check(bool pass, string label) { if (!pass) throw new Exception(label); passed.Add(label); }

    [Serializable] public sealed class ManifestTake { public string path, guid; public float gain; }
    [Serializable] public sealed class ManifestLine { public string id; public List<ManifestTake> takes = new(); }
    [Serializable] public sealed class ManifestEvent { public string @event, folder; public List<ManifestLine> lines = new(); }
    [Serializable] public sealed class Manifest { public List<ManifestEvent> items = new(); }

    public static object Main()
    {
        passed.Clear();
        try
        {
            var randomState = UnityEngine.Random.state;
            Defaults(); Director(); Draws(); LibraryDraws();
            Check(randomState.Equals(UnityEngine.Random.state), "Director usage leaves UnityEngine.Random.state unchanged");
            Latches(); Watchers(); Crafting(); Library(); Settings(); Schema(); Preference();
            File.WriteAllText(Report, string.Join("\n", passed.Select(p => "PASS " + p)) + "\nPASS " + passed.Count + "\n");
            return "PASS " + passed.Count;
        }
        catch (Exception ex) { File.WriteAllText(Report, string.Join("\n", passed.Select(p => "PASS " + p)) + "\nFAIL " + ex + "\n"); throw; }
    }

    static VoiceLineEventRule[] TestRules()
    {
        var rules = VoiceLineEventRule.Defaults();
        foreach (var r in rules) { r.playChancePercent = 100f; r.delaySeconds = 0f; r.maxWaitSeconds = 1000f; }
        return rules;
    }

    static VoiceLinePlay Draw(VoiceLineDirector d, VoiceLineEvent e, ref double t, VoiceLineConfig cfg)
    {
        if (d.Submit(e, t, cfg, true) != "queued" || !d.Update(t, cfg, true, out var play)) throw new Exception("Forced draw starts: " + e);
        d.Finished(t += 1); return play;
    }

    static void Defaults()
    {
        var defaults = VoiceLineEventRule.Defaults(); var values = (VoiceLineEvent[])Enum.GetValues(typeof(VoiceLineEvent));
        Check(defaults.Length == values.Length && defaults.Select((r, i) => r != null && (int)r.trigger == i).All(v => v), "Defaults() cover every event in enum order");
        Check(!ReferenceEquals(defaults[0], VoiceLineEventRule.Defaults()[0]) && defaults[0].Clone() != defaults[0] && defaults[0].Clone().priority == defaults[0].priority, "Defaults() and Clone() return fresh rule objects");
        var low = defaults[(int)VoiceLineEvent.EnergyLow]; var big = defaults[(int)VoiceLineEvent.FallBig];
        Check(low.priority == 75 && low.cooldownSeconds == 120f && low.ignoreGlobalCooldown && big.priority == 100 && Mathf.Approximately(big.delaySeconds, 1.1f), "Default table values (EnergyLow, FallBig)");
    }

    static void Director()
    {
        var rules = TestRules(); var cfg = new VoiceLineConfig { rule = e => rules[(int)e] };
        var d = new VoiceLineDirector(Seed) { lines = e => e == VoiceLineEvent.SaveLoaded ? 0 : 3, takes = (e, l) => 2 };
        // Submit reasons
        cfg.enabled = false;
        Check(d.Submit(VoiceLineEvent.EnergyLow, 0, cfg) == "disabled" && !d.HasPending, "Disabled config rejects submissions");
        cfg.enabled = true;
        Check(d.Submit(VoiceLineEvent.SaveLoaded, 0, cfg) == "no-clips" && d.Submit(VoiceLineEvent.SaveLoaded, 0, cfg, true) == "no-clips" && !d.HasPending, "Event without clips is rejected, also forced");
        Check(d.Submit(VoiceLineEvent.EnergyLow, 0, cfg) == "queued" && d.HasPending && d.PendingEvent == VoiceLineEvent.EnergyLow && !d.PendingForced, "Submission is queued");
        Check(d.Submit(VoiceLineEvent.EnergyLow, 0, cfg) == "duplicate", "Same pending event is a duplicate");
        Check(d.Submit(VoiceLineEvent.CarryStage1, 0, cfg) == "lower-priority" && d.PendingEvent == VoiceLineEvent.EnergyLow, "Lower priority does not replace pending");
        rules[(int)VoiceLineEvent.FallSmall].priority = 75;
        Check(d.Submit(VoiceLineEvent.FallSmall, 0, cfg) == "lower-priority" && d.PendingEvent == VoiceLineEvent.EnergyLow, "Equal priority keeps the waiting request");
        Check(d.Submit(VoiceLineEvent.EnergyCritical, 0, cfg) == "queued" && d.PendingEvent == VoiceLineEvent.EnergyCritical, "Higher priority replaces pending");
        d.ClearPending(); Check(!d.HasPending && !d.PendingForced, "ClearPending forgets the request");
        rules[(int)VoiceLineEvent.OreSold].playChancePercent = 0f; bool never = true;
        for (int i = 0; i < 50; i++) never &= d.Submit(VoiceLineEvent.OreSold, 0, cfg) == "chance" && !d.HasPending;
        Check(never, "Chance 0 % never queues");
        rules[(int)VoiceLineEvent.OreSold].playChancePercent = 100f; bool always = true;
        for (int i = 0; i < 50; i++) { always &= d.Submit(VoiceLineEvent.OreSold, 0, cfg) == "queued"; d.ClearPending(); }
        Check(always, "Chance 100 % always queues");
        rules[(int)VoiceLineEvent.OreSold].playChancePercent = 50f; int queued = 0;
        for (int i = 0; i < 400; i++) { if (d.Submit(VoiceLineEvent.OreSold, 0, cfg) == "queued") queued++; d.ClearPending(); }
        Check(queued > 140 && queued < 260, "Chance 50 % queues about half (" + queued + "/400)");
        Check(new[] { "disabled", "no-clips", "cooldown", "chance" }.All(VoiceLineDirector.IsTerminal) && !new[] { "queued", "lower-priority", "duplicate" }.Any(VoiceLineDirector.IsTerminal), "IsTerminal classifies reason codes");

        // Start, speaking duplicate, interrupt gap
        d.ResetHistory(); rules = TestRules();
        Check(d.Submit(VoiceLineEvent.EnergyLow, 100, cfg) == "queued" && !d.Update(100, cfg, false, out _) && d.HasPending, "canStart false keeps the request waiting");
        Check(d.Update(100, cfg, true, out var p) && p.trigger == VoiceLineEvent.EnergyLow && p.priority == 75 && !p.interrupt && !p.forced && p.line >= 0 && p.line < 3 && p.take >= 0 && p.take < 2
            && d.IsSpeaking && d.CurrentEvent == VoiceLineEvent.EnergyLow && d.CurrentPriority == 75 && !d.HasPending && d.LastLine(VoiceLineEvent.EnergyLow) == p.line, "Update starts the pending line");
        Check(d.Submit(VoiceLineEvent.EnergyLow, 101, cfg) == "duplicate", "Speaking event is a duplicate");
        rules[(int)VoiceLineEvent.FallSmall].priority = 99;
        Check(d.Submit(VoiceLineEvent.FallSmall, 101, cfg) == "queued" && !d.Update(101, cfg, true, out _) && d.HasPending, "Priority gap below interruptPriorityGap does not interrupt");
        d.ClearPending(); rules[(int)VoiceLineEvent.FallSmall].priority = 100;
        Check(d.Submit(VoiceLineEvent.FallSmall, 101, cfg) == "queued" && d.Update(101, cfg, true, out p) && p.interrupt && d.CurrentEvent == VoiceLineEvent.FallSmall && d.CurrentPriority == 100, "Priority gap of interruptPriorityGap interrupts");
        d.Finished(102); Check(!d.IsSpeaking, "Finished ends speaking");

        // Per-event cooldown from line start
        Check(d.Submit(VoiceLineEvent.EnergyLow, 219, cfg) == "cooldown", "Per-event cooldown blocks");
        Check(d.Submit(VoiceLineEvent.EnergyLow, 220, cfg) == "queued", "Per-event cooldown ends after cooldownSeconds from line start");
        d.Reset(); Check(!d.HasPending && !d.IsSpeaking && d.Submit(VoiceLineEvent.EnergyLow, 150, cfg) == "queued", "Reset forgets pending, speaking and cooldowns");
        d.ClearPending();

        // Global / reaction / minimum gap
        Check(d.Submit(VoiceLineEvent.CraftedAxe, 0, cfg) == "queued" && d.Update(0, cfg, true, out _), "First line after Reset needs no gap");
        d.Finished(10);
        Check(d.Submit(VoiceLineEvent.CraftedAxe, 10, cfg) == "queued" && !d.Update(29.9, cfg, true, out _) && d.Update(30, cfg, true, out _), "Global cooldown between lines");
        d.Finished(40);
        Check(d.Submit(VoiceLineEvent.EnergyLow, 40, cfg) == "queued" && !d.Update(43.9, cfg, true, out _) && d.Update(44, cfg, true, out _), "ignoreGlobalCooldown needs only the reaction gap");
        d.Finished(50); cfg.reactionGapSeconds = 0;
        Check(d.Submit(VoiceLineEvent.EnergyCritical, 50, cfg) == "queued" && !d.Update(51.4, cfg, true, out _) && d.Update(51.5, cfg, true, out _), "Minimum gap always applies");
        d.Finished(60); cfg.reactionGapSeconds = 4;

        // Delay and expiry
        d.Reset(); rules[(int)VoiceLineEvent.EnergyRefilled] = VoiceLineEventRule.Defaults()[(int)VoiceLineEvent.EnergyRefilled];
        Check(d.Submit(VoiceLineEvent.EnergyRefilled, 1000, cfg) == "queued" && !d.Update(1000.5, cfg, true, out _) && d.HasPending, "Delay holds the request");
        Check(!d.Update(1006, cfg, true, out _) && !d.HasPending, "Request expires after delay + maxWait");
        Check(d.Submit(VoiceLineEvent.EnergyRefilled, 2000, cfg) == "queued" && !d.Update(2006, cfg, false, out _) && !d.HasPending, "Request expires also while start is blocked");
        Check(d.Submit(VoiceLineEvent.EnergyRefilled, 3000, cfg) == "queued" && d.Update(3003, cfg, true, out p) && p.trigger == VoiceLineEvent.EnergyRefilled, "Request inside its window starts");
        d.Finished(3004);

        // Forced
        d.Reset();
        Check(d.Submit(VoiceLineEvent.FallBig, 0, cfg) == "queued" && d.Update(0, cfg, true, out _), "High priority line starts");
        Check(d.Submit(VoiceLineEvent.CarryStage1, 1, cfg, true) == "queued" && d.PendingForced && d.PendingEvent == VoiceLineEvent.CarryStage1, "Forced request is queued");
        Check(d.Submit(VoiceLineEvent.EnergyCritical, 1, cfg) == "lower-priority" && d.PendingEvent == VoiceLineEvent.CarryStage1, "Forced pending is never replaced by a normal request");
        Check(!d.Update(1, cfg, false, out _) && !d.Update(5001, cfg, false, out _) && d.HasPending, "Forced request does not expire");
        Check(d.Update(5001, cfg, true, out p) && p.forced && p.interrupt && p.trigger == VoiceLineEvent.CarryStage1 && d.CurrentEvent == VoiceLineEvent.CarryStage1, "Forced request interrupts regardless of priority");
        d.Finished(5002); cfg.enabled = false;
        Check(d.Submit(VoiceLineEvent.CarryStage1, 5002, cfg, true) == "queued" && d.Update(5002, cfg, true, out p) && p.forced && !p.interrupt, "Forced ignores disabled, cooldown and gaps");
        cfg.enabled = true; d.Finished(5003);
        Check(d.Submit(VoiceLineEvent.OreSold, 5003, cfg) == "queued" && d.Submit(VoiceLineEvent.CraftedAxe, 5003, cfg, true) == "queued" && d.PendingEvent == VoiceLineEvent.CraftedAxe && d.PendingForced, "Forced replaces a normal pending request");
        d.ClearPending(); Check(!d.HasPending && !d.PendingForced, "ClearPending clears forced request");

        // Reset keeps bags and last lines
        d.ResetHistory(); double t = 10000;
        Check(d.LastLine(VoiceLineEvent.NewWorld) == -1, "ResetHistory forgets last line");
        int first = Draw(d, VoiceLineEvent.NewWorld, ref t, cfg).line;
        d.Reset();
        Check(d.LastLine(VoiceLineEvent.NewWorld) == first && !d.HasPending && !d.IsSpeaking, "Reset keeps last line");
        var cycle = new HashSet<int> { first, Draw(d, VoiceLineEvent.NewWorld, ref t, cfg).line, Draw(d, VoiceLineEvent.NewWorld, ref t, cfg).line };
        Check(cycle.SetEquals(new[] { 0, 1, 2 }), "Reset keeps the sentence bag (cycle completes across Reset)");
        d.ResetHistory(); Check(d.LastLine(VoiceLineEvent.NewWorld) == -1, "ResetHistory clears last line again");
    }

    static void Draws()
    {
        var cfg = new VoiceLineConfig { rule = e => VoiceLineEventRule.Defaults()[(int)e] }; int count = 4;
        var d = new VoiceLineDirector(Seed + 1) { lines = e => count, takes = (e, l) => l + 1 };
        double t = 0; int previous = -1; bool noRepeat = true, cycles = true, takeRange = true, takeRepeat = true;
        var used = new HashSet<(int, int)>(); var lastTake = new Dictionary<int, int>(); var block = new HashSet<int>();
        for (int i = 0; i < 2002; i++)
        {
            var p = Draw(d, VoiceLineEvent.OreSold, ref t, cfg);
            noRepeat &= p.line != previous; previous = p.line;
            if (i < 2000) { block.Add(p.line); if (i % 4 == 3) { cycles &= block.Count == 4; block.Clear(); } }
            takeRange &= p.line >= 0 && p.line < 4 && p.take >= 0 && p.take <= p.line;
            if (p.line > 0 && lastTake.TryGetValue(p.line, out var last)) takeRepeat &= p.take != last;
            lastTake[p.line] = p.take; used.Add((p.line, p.take));
        }
        Check(noRepeat, "No immediate sentence repeat over 2000 draws");
        Check(cycles, "Every sentence once per bag cycle");
        Check(takeRange && takeRepeat, "Takes in range and never the same take twice in a row per sentence");
        Check(used.Count == 1 + 2 + 3 + 4, "Every sentence and every take used");
        count = 3; var refill = new HashSet<int>();
        for (int i = 0; i < 3; i++) { var p = Draw(d, VoiceLineEvent.OreSold, ref t, cfg); refill.Add(p.line); noRepeat &= p.line != previous; previous = p.line; }
        Check(refill.SetEquals(new[] { 0, 1, 2 }) && noRepeat, "Changed sentence count resets the bag");
        var single = new VoiceLineDirector(Seed + 2) { lines = e => 1, takes = (e, l) => 1 }; bool same = true;
        for (int i = 0; i < 20; i++) { var p = Draw(single, VoiceLineEvent.OreSold, ref t, cfg); same &= p.line == 0 && p.take == 0; }
        Check(same, "Single sentence with single take repeats safely");
    }

    static VoiceLineLibraryAsset LoadLibrary()
    {
        var lib = AssetDatabase.LoadAssetAtPath<VoiceLineLibraryAsset>(LibraryPath);
        if (!lib) throw new Exception("Library asset loads: " + LibraryPath);
        return lib;
    }

    static void LibraryDraws()
    {
        var lib = LoadLibrary(); var cfg = new VoiceLineConfig { rule = e => VoiceLineEventRule.Defaults()[(int)e] };
        var d = new VoiceLineDirector(Seed + 3) { lines = lib.LineCount, takes = lib.TakeCount }; double t = 0; int total = 0;
        foreach (VoiceLineEvent e in Enum.GetValues(typeof(VoiceLineEvent)))
        {
            int lines = lib.LineCount(e); if (lines <= 0) continue;
            int takes = Enumerable.Range(0, lines).Sum(l => lib.TakeCount(e, l)); var used = new HashSet<(int, int)>(); bool valid = true;
            for (int i = 0; i < takes * 20; i++) { var p = Draw(d, e, ref t, cfg); used.Add((p.line, p.take)); var take = lib.Take(e, p.line, p.take); valid &= take != null && take.clip; }
            Check(valid && used.Count == takes, "Director reaches every sentence and take of the library: " + e);
            total += used.Count;
        }
        Check(total == 169, "Library director coverage sums to 169 takes (" + total + ")");
    }

    static void Latches()
    {
        var f = new VoiceThresholdLatch(true); f.Rebaseline(.5, .33);
        Check(f.Armed && !f.Step(.4, .33, .05) && f.Step(.33, .33, .05) && !f.Armed, "Falling latch fires once at <= threshold");
        Check(!f.Step(.2, .33, .05) && !f.Step(.36, .33, .05) && !f.Armed, "Falling latch stays disarmed inside rearm margin");
        Check(!f.Step(.4, .33, .05) && f.Armed && f.Step(.3, .33, .05), "Falling latch rearms beyond margin and fires again");
        f.Rebaseline(.2, .33); Check(!f.Armed && !f.Step(.1, .33, .05), "Falling rebaseline below threshold does not fire");
        var r = new VoiceThresholdLatch(false); r.Rebaseline(.5, 1);
        Check(r.Armed && !r.Step(1, 1, .1) && r.Step(1.01, 1, .1) && !r.Step(1.5, 1, .1), "Rising latch fires once at > threshold");
        Check(!r.Step(.95, 1, .1) && !r.Armed && !r.Step(.85, 1, .1) && r.Armed && r.Step(1.2, 1, .1), "Rising latch rearms below threshold - margin");
        r.Rebaseline(1.5, 1); Check(!r.Armed && !r.Step(1.6, 1, .1), "Rising rebaseline above threshold does not fire");
    }

    static void Watchers()
    {
        const double low = .33, critical = .15, margin = .05;
        var w = new VoiceEnergyWatcher();
        Check(!w.HasBaseline && w.Step(.5, low, critical, margin) == null && w.HasBaseline, "Energy watcher rebaselines on first step without firing");
        Check(w.Step(.3, low, critical, margin) == VoiceLineEvent.EnergyLow && w.Open == VoiceLineEvent.EnergyLow, "Energy low opens");
        Check(w.Step(.29, low, critical, margin) == VoiceLineEvent.EnergyLow, "Open stays until acknowledged");
        w.Acknowledge(VoiceLineEvent.EnergyCritical); Check(w.Open == VoiceLineEvent.EnergyLow, "Acknowledge of another event keeps Open");
        w.Acknowledge(VoiceLineEvent.EnergyLow); Check(w.Open == null && w.Step(.28, low, critical, margin) == null, "Acknowledge clears Open; latch does not refire");
        Check(w.Step(.1, low, critical, margin) == VoiceLineEvent.EnergyCritical, "Energy critical opens");
        Check(w.Step(.2, low, critical, margin) == null && w.Open == null, "Open critical dropped above critical threshold");
        w.Invalidate(); Check(!w.HasBaseline && w.Open == null, "Invalidate clears baseline and Open");
        w.Step(.5, low, critical, margin);
        Check(w.Step(.3, low, critical, margin) == VoiceLineEvent.EnergyLow && w.Step(.1, low, critical, margin) == VoiceLineEvent.EnergyCritical, "Open low is replaced by critical");
        w.Invalidate(); w.Step(.5, low, critical, margin);
        Check(w.Step(.1, low, critical, margin) == VoiceLineEvent.EnergyCritical, "Low and critical in one step open critical");
        w.Acknowledge(VoiceLineEvent.EnergyCritical);
        Check(w.Step(.2, low, critical, margin) == null, "Critical crossing consumes the low latch");
        Check(w.Step(.4, low, critical, margin) == null && w.Step(.3, low, critical, margin) == VoiceLineEvent.EnergyLow, "Low rearms beyond margin and opens again");
        Check(w.Step(.34, low, critical, margin) == null && w.Open == null && w.Step(.3, low, critical, margin) == null, "Open low dropped above threshold; no refire inside margin");
        w.Invalidate(); w.Rebaseline(.2, low, critical);
        Check(w.HasBaseline && w.Step(.19, low, critical, margin) == null, "Energy rebaseline below low does not fire");
        w.Rebaseline(.1, low, critical); Check(w.Step(.1, low, critical, margin) == null, "Energy rebaseline below critical does not fire");

        var c = new VoiceCarryWatcher();
        Check(VoiceCarryWatcher.Stage1Ratio == 1.0 && VoiceCarryWatcher.Stage2Ratio == 2.0, "Carry stages match inventory stages");
        Check(!c.HasBaseline && c.Step(.5, .1) == null && c.HasBaseline, "Carry watcher rebaselines on first step without firing");
        Check(c.Step(1.2, .1) == VoiceLineEvent.CarryStage1 && c.Step(2.5, .1) == VoiceLineEvent.CarryStage2, "Carry stage 1 opens and is replaced by stage 2");
        c.Acknowledge(VoiceLineEvent.CarryStage1); Check(c.Open == VoiceLineEvent.CarryStage2, "Acknowledge of another stage keeps Open");
        c.Acknowledge(VoiceLineEvent.CarryStage2); Check(c.Open == null && c.Step(2.4, .1) == null, "Acknowledge clears Open; stage 2 does not refire");
        c.Invalidate(); Check(!c.HasBaseline && c.Open == null, "Carry Invalidate clears baseline and Open");
        c.Step(.5, .1); Check(c.Step(2.5, .1) == VoiceLineEvent.CarryStage2, "Jump over both stages opens stage 2");
        c.Acknowledge(VoiceLineEvent.CarryStage2); Check(c.Step(1.5, .1) == null, "Stage 2 crossing consumes the stage 1 latch");
        Check(c.Step(.8, .1) == null && c.Step(1.2, .1) == VoiceLineEvent.CarryStage1, "Stage 1 rearms below margin and opens again");
        Check(c.Step(.95, .1) == null && c.Open == null, "Open stage 1 dropped below stage ratio");
        c.Rebaseline(1.5); Check(c.Step(1.6, .1) == null && c.Step(2.1, .1) == VoiceLineEvent.CarryStage2, "Carry rebaseline above stage 1 does not fire, stage 2 still armed");
    }

    static void Crafting()
    {
        Check(VoiceLinePlayer.CraftEvent(Item.Axe, true, true, true) == VoiceLineEvent.UpgradeEnergy, "Energy upgrade wins");
        Check(VoiceLinePlayer.CraftEvent(Item.Axe, false, true, true) == VoiceLineEvent.UpgradeCarry, "Carry upgrade before pickaxe");
        Check(VoiceLinePlayer.CraftEvent(Item.Coal, false, false, true) == VoiceLineEvent.CraftedPickaxe, "Pickaxe upgrade flag maps to CraftedPickaxe");
        var picks = new[] { Item.CopperPickaxe, Item.IronPickaxe, Item.SteelPickaxe, Item.TitaniumPickaxe, Item.TungstenPickaxe, Item.ObsidianPickaxe, Item.MythrilPickaxe, Item.DiamondPickaxe };
        Check(picks.All(i => VoiceLinePlayer.CraftEvent(i, false, false, false) == VoiceLineEvent.CraftedPickaxe), "Every pickaxe output maps to CraftedPickaxe");
        Check(VoiceLinePlayer.CraftEvent(Item.Axe, false, false, false) == VoiceLineEvent.CraftedAxe && VoiceLinePlayer.CraftEvent(Item.Scythe, false, false, false) == VoiceLineEvent.CraftedScythe
            && VoiceLinePlayer.CraftEvent(Item.Medkit, false, false, false) == VoiceLineEvent.CraftedMedkit && VoiceLinePlayer.CraftEvent(Item.Torche, false, false, false) == VoiceLineEvent.CraftedTorches, "Axe, scythe, medkit and torches map to their events");
        Check(VoiceLinePlayer.CraftEvent(Item.Coal, false, false, false) == null && VoiceLinePlayer.CraftEvent(Item.Ladder, false, false, false) == null, "Other outputs stay silent");
    }

    static void Library()
    {
        var lib = LoadLibrary();
        Check(Resources.Load<VoiceLineLibraryAsset>("Audio/VoiceLineLibrary") == lib, "Library loads through Resources");
        var withClips = lib.sets.Where(s => s != null && lib.LineCount(s.trigger) > 0).Select(s => s.trigger).ToList();
        Check(withClips.Count == 17 && withClips.Distinct().Count() == 17, "17 distinct sets with clips");
        Check(Enum.GetValues(typeof(VoiceLineEvent)).Cast<VoiceLineEvent>().All(e => e == VoiceLineEvent.SaveLoaded ? lib.LineCount(e) == 0 : lib.LineCount(e) > 0 && lib.Set(e)?.trigger == e), "Every event except SaveLoaded has clips, SaveLoaded has none");
        var references = new Dictionary<AudioClip, int>(); int takes = 0; bool gains = true, clips = true;
        foreach (var set in lib.sets) foreach (var line in set.lines) foreach (var take in line.takes)
        {
            takes++; clips &= take != null && take.clip; if (!clips) continue;
            gains &= take.gain > 0f && take.gain <= 1f;
            references[take.clip] = references.TryGetValue(take.clip, out var n) ? n + 1 : 1;
        }
        Check(clips && takes == 169, "Library holds 169 takes, all with clip (" + takes + ")");
        Check(gains, "All take gains in (0, 1]");
        int filtered = withClips.Sum(e => Enumerable.Range(0, lib.LineCount(e)).Sum(l => lib.TakeCount(e, l)));
        Check(filtered == 169 && lib.AllClips().Count() == 169, "Filtered take count and AllClips match 169");
        Check(lib.Take(VoiceLineEvent.EnergyLow, 0, 99) == null && lib.Take(VoiceLineEvent.EnergyLow, 99, 0) == null && lib.TakeCount(VoiceLineEvent.EnergyLow, -1) == 0, "Out-of-range lookups return nothing");
        var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { VoiceFolder }).Select(g => AssetDatabase.GUIDToAssetPath(g)).Distinct().ToList();
        var folder = paths.Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToList();
        Check(paths.Count == 169 && folder.All(c => c), "169 voice clips under " + VoiceFolder + " (" + paths.Count + ")");
        Check(folder.All(c => references.TryGetValue(c, out var n) && n == 1) && references.Keys.All(c => folder.Contains(c)), "Every voice clip referenced exactly once");

        var manifest = JsonUtility.FromJson<Manifest>("{\"items\":" + File.ReadAllText(ManifestPath) + "}");
        Check(manifest.items.Count == 17, "Manifest lists 17 events");
        foreach (var entry in manifest.items)
        {
            Check(Enum.TryParse<VoiceLineEvent>(entry.@event, out var e) && lib.Set(e) != null, "Manifest event has a set: " + entry.@event);
            var set = lib.Set(e);
            bool same = set.lines.Count == entry.lines.Count && entry.lines.All(m =>
            {
                var line = set.lines.FirstOrDefault(l => l.id == m.id);
                return line != null && line.takes.Count == m.takes.Count && m.takes.All(mt => line.takes.Any(lt => lt.clip && AssetDatabase.GetAssetPath(lt.clip) == mt.path
                    && AssetDatabase.AssetPathToGUID(mt.path) == mt.guid && Mathf.Abs(lt.gain - mt.gain) < .001f && mt.path.StartsWith(VoiceFolder + "/" + entry.folder + "/", StringComparison.Ordinal)));
            });
            Check(same, "Library matches manifest (sentences, clips, gains): " + entry.@event);
        }
        foreach (var path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            var sample = importer ? importer.defaultSampleSettings : default;
            if (!importer || sample.loadType != AudioClipLoadType.CompressedInMemory || sample.preloadAudioData || !importer.forceToMono || !importer.loadInBackground)
                Check(false, "Voice clip import settings (CompressedInMemory, no preload, mono, background): " + path);
        }
        Check(true, "Import settings of all " + paths.Count + " voice clips");
    }

    static void Settings()
    {
        var settings = Resources.Load<VoiceLineSettingsAsset>("Audio/VoiceLineSettings");
        Check(settings, "Settings asset loads through Resources");
        var defaults = VoiceLineEventRule.Defaults();
        Check(settings.events != null && settings.events.Length == defaults.Length && settings.events.Select((r, i) => r != null && r.trigger == defaults[i].trigger).All(v => v), "Asset events match Defaults() triggers");
        Check(Enum.GetValues(typeof(VoiceLineEvent)).Cast<VoiceLineEvent>().All(e => settings.Rule(e) != null && settings.Rule(e).trigger == e), "Rule(e) works for every event");
        var config = settings.Config();
        Check(config == settings.Config() && config.rule != null && config.enabled == settings.enabled && Math.Abs(config.globalCooldownSeconds - Mathf.Max(0f, settings.globalCooldownSeconds)) < 1e-6
            && config.interruptPriorityGap == Mathf.Max(0, settings.interruptPriorityGap) && config.rule(VoiceLineEvent.OreSold) == settings.Rule(VoiceLineEvent.OreSold), "Config() is cached and mirrors the asset");
        var original = settings.events;
        try
        {
            settings.events = new[] { new VoiceLineEventRule { trigger = VoiceLineEvent.OreSold, priority = 7 } };
            Check(settings.Rule(VoiceLineEvent.OreSold).priority == 7 && settings.Rule(VoiceLineEvent.EnergyLow).priority == defaults[0].priority && config.rule(VoiceLineEvent.OreSold).priority == 7, "Rule cache follows a replaced events array, missing events fall back to Defaults()");
        }
        finally { settings.events = original; }
        Check(settings.Rule(VoiceLineEvent.OreSold) == original.First(r => r.trigger == VoiceLineEvent.OreSold), "Rule cache returns to restored array");
    }

    static void Schema()
    {
        var sections = GpsSchema.Sections; int index = sections.FindIndex(s => s.tab == "Audio" && s.title == "Stimme");
        Check(index >= 0 && sections[index].source == typeof(VoiceLineSettingsAsset), "GPS section Audio/Stimme for VoiceLineSettingsAsset");
        Check(index > 0 && sections[index - 1].title == "Einzelclips", "Stimme section follows Einzelclips");
        var names = sections[index].fields.Select(f => f.name).ToHashSet();
        var missing = typeof(VoiceLineSettingsAsset).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(f => !f.IsNotSerialized && !names.Contains(f.Name)).Select(f => f.Name).ToArray();
        Check(missing.Length == 0, "GPS Stimme section has every settings field" + (missing.Length > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
        Check(sections.Any(s => s.source == typeof(StatsManager) && s.title == "Gesundheit und Fallschaden" && s.fields.Any(f => f.name == "bigFallDamageThreshold")), "bigFallDamageThreshold in GPS fall damage section");
        int voice = Array.IndexOf(GpsAudio.Categories, "Stimme"), archive = Array.IndexOf(GpsAudio.Categories, "Archiv");
        Check(voice >= 0 && voice < archive, "GPS audio category Stimme before Archiv");
    }

    static void Preference()
    {
        bool had = PlayerPrefs.HasKey(VoiceKey); float old = PlayerPrefs.GetFloat(VoiceKey, 1f);
        try
        {
            PlayerSettings.Voice = .42f; Check(Mathf.Approximately(PlayerSettings.Voice, .42f) && Mathf.Approximately(PlayerPrefs.GetFloat(VoiceKey), .42f), "PlayerSettings.Voice stores the preference");
            PlayerSettings.Voice = 2f; Check(PlayerSettings.Voice == 1f, "PlayerSettings.Voice clamps above 1");
            PlayerSettings.Voice = -1f; Check(PlayerSettings.Voice == 0f, "PlayerSettings.Voice clamps below 0");
            PlayerPrefs.DeleteKey(VoiceKey); Check(PlayerSettings.Voice == 1f, "PlayerSettings.Voice defaults to 1");
        }
        finally { if (had) PlayerPrefs.SetFloat(VoiceKey, old); else PlayerPrefs.DeleteKey(VoiceKey); PlayerPrefs.Save(); }
    }
}
