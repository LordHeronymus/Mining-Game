using System;
using System.Collections.Generic;
using System.Linq;

// Period state of the challenge system before the rework. Only the profile validator reads it: a running day or
// week is converted into a format-1 round and the list is left empty, so older builds still accept the profile.
[Serializable] public sealed class MetaChallengePeriodState
{
    public int period;
    public long startUtc, endUtc;
    public int[] counters = new int[52];
    public bool[] rewarded = new bool[52];
    public int[] dailySelection;
    public int[] awardedXp;
    public int[] dailyVariants;
    public int dailyArtifactType;
    public int dailyOreType;
    public int dailyGemType;
    public int oreTargetVersion;
    public int visitedLayerMask, artifactVarietyMask, expeditionPeakLayer;
    public string expeditionRunId;
}

// Challenge persistence checks. Everything here is a pure function of the candidate profile: it also runs on the
// background save thread and for .bak/.tmp files, so no UnityEngine, settings, clock, statics or caches. Challenge
// data never rejects a profile (it is repaired or dropped); the only rejection is the XP integrity check.
public static partial class MetaProgression
{
    const int MaxLifetimeCounters = 128, MaxMilestoneAwards = 256, MaxRunTallies = 64, MaxChallengeId = 64;
    const long MaxLifetimeValue = 1000000000000000L, MaxTallyValue = 1000000000000L;
    static readonly long ClockLimit = new DateTime(9000, 1, 1).Ticks;

    // Legacy pool (append-only positions). Names, targets and rewards only convert a running legacy day or week.
    static readonly string[] PeriodMetrics = { "resources", "depth", "crafted", "copper", "sales", "torches",
        "coal", "iron", "silver", "gold", "regions", "discoveries", "exotics", "ladders", "resources", "crafted",
        "platinum", "titanium", "obsidian", "sales", "trees", "grass", "surface-blocks", "artifacts", "specific-artifact", "l2-blocks", "l3-blocks", "l4-xxl", "l5-xxl", "specific-ore-xxl", "gems", "garnet", "ruby", "emerald", "diamond", "gems-xxl", "specific-gem-xxl", "artifacts-xxl", "specific-artifact-xxl", "ultronium-xxl", "pickaxe-upgrades", "tools-crafted", "gear-crafted", "energy-upgrades", "carrying-upgrades", "dynamite-crafted", "medkits-crafted", "visited-layers", "revealed-cells", "altars-found", "artifact-variety", "round-trip" };
    static readonly string[] DailyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "Schwarze Adern", "Eisenherz", "Silbermond", "Goldfieber", "Unbetretene Pfade", "Geheimnisse im Stein", "Fremde Künste", "Himmelsleiter", "Grosse Ausbeute", "Feuer der Schmiede",
        "Platinglanz", "Titankraft", "Obsidianfluch", "Goldener Handschlag", "Ruf der Wälder", "Grüne Ernte", "Unter der Grasnarbe", "Relikte der Vorzeit", "Spuren der Vorzeit", "Steinernes Erwachen", "In der Bergesmitte",
        "XXL · Herz des Berges", "XXL · Ruf des Abgrunds", "XXL · Erzrausch", "Funkelnde Beute", "Glut des Granats", "Rubinfeuer", "Smaragdtraum", "Diamantenglanz",
        "XXL · Edelsteinrausch", "XXL · Kristalljagd", "XXL · Reliktenjäger", "XXL · Reliktvermächtnis", "XXL · Ultroniumrausch", "Die nächste Klinge", "Werkzeugschmiede", "Für den Aufbruch", "Funken des Lebens", "Schultern aus Stahl", "Donner der Schmiede", "Heilende Vorräte", "Grenzgänger", "Verborgene Weiten", "Ruf des Altars", "Spuren der Alten", "Licht und Tiefe" };
    static readonly string[] WeeklyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "König der Kohle", "Eiserne Dynastie", "Silberne Träume", "Goldene Ära", "Jenseits der Karten", "Geheimnisse des Berges", "Fremde Künste", "Wege zum Himmel", "Grosse Ausbeute", "Feuer der Schmiede" };
    // Unit texts of the old challenge panel; null = depends on the state (see LegacyUnit).
    static readonly string[] LegacyUnits = { "Erze", "Blöcke Tiefe", "Gegenstände hergestellt", "Kupfer", "$ verkauft", "Fackeln hergestellt",
        "Kohle", "Eisen", "Silber", "Gold", "Gebiete", "Entdeckungen", "Exotics hergestellt", "Leitern hergestellt", "Erze", "Gegenstände hergestellt",
        "Platin", "Titan", "Obsidian", "$ verkauft", "Bäume gefällt", "Grasbüschel geerntet", "Oberflächenblöcke", "Artefakte abgebaut", null, "Blöcke aus L2", "Blöcke aus L3",
        "Blöcke aus L4", "Blöcke aus L5", null, "Edelsteine", "Granat", "Rubine", "Smaragde", "Diamanten", "Edelsteine", null, "Artefakte abgebaut", null, "Ultronium",
        "Spitzhacken-Upgrades", "Werkzeuge hergestellt", "Ausrüstungen hergestellt", "Energie-Upgrades", "Traglast-Upgrades", "Dynamit hergestellt", "Medkits hergestellt",
        "Schichten besucht", "Kartenfelder", "Altäre entdeckt", "Artefakttypen", null };
    // Legacy index -> template id and item/layer subject (spec map; -1 none). 24/29/36/38/51 depend on the state.
    static readonly string[] LegacyTemplates = { "ores", "legacy-depth-gain", "crafted", "ore", "sales", "torches-crafted",
        "ore", "ore", "ore", "ore", "regions", "discoveries", "exotics", "ladders-crafted", "ores", "crafted",
        "ore", "ore", "legacy-impossible", "sales", "trees", "grass", "layer-blocks", "artifacts", "artifact", "layer-blocks", "layer-blocks",
        "layer-blocks", "layer-blocks", "ore", "gems", "gem", "gem", "legacy-impossible", "gem", "gems", "gem", "artifacts", "artifact", "ultronium",
        "upgrades", "tools", "gear", "upgrades", "upgrades", "dynamite", "medkits", "layers", "cells", "altars", "legacy-artifact-sighted", "round-trips" };
    static readonly int[] LegacySubjects = { -1, -1, -1, 0, -1, -1, 3, 1, 4, 2, -1, -1, -1, -1, -1, -1, 8, 28, 31, -1, -1, -1, 1, -1, -1, 2, 3,
        4, 5, -1, -1, 32, 40, 36, 33, -1, -1, -1, -1, 24, 0, -1, -1, 1, 2, -1, -1, -1, -1, -1, -1, -1 };
    static readonly int[] DailyTargets = { 50, 100, 5, 25, 1000, 3, 25, 25, 25, 25, 6, 1, 1, 5, 250, 15, 25, 25, 30, 2500, 10, 25, 150, 5, 3, 200, 250, 2000, 2000, 500, 30, 25, 25, 25, 25, 300, 150, 75, 25, 100, 1, 2, 1, 1, 1, 5, 3, 2, 250, 1, 2, 1 };
    static readonly int[] WeeklyTargets = { 500, 750, 25, 150, 10000, 20, 300, 200, 80, 50, 50, 5, 3, 30, 2500, 100 };
    // Historical payouts stay valid: changing rewards must never invalidate earned XP.
    static readonly int[] LegacyDailyRewards = { 150, 200, 150, 100, 200, 100, 100, 150, 180, 200, 180, 200, 300, 120, 350, 250, 220, 240, 250, 350, 500, 500, 500, 500, 500, 500, 500, 3000, 3000, 3000, 500, 500, 500, 500, 500, 3000, 3000, 3000, 3000, 3000, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500 };
    static readonly int[] DailyLargeTargets = { 250, 300, 15, 100, 5000, 15, 100, 100, 100, 100, 18, 3, 3, 25, 1250, 45, 100, 100, 100, 7500, 40, 100, 500, 25, 10, 600, 750, 2000, 2000, 500, 100, 100, 100, 100, 100, 300, 150, 75, 25, 100, 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
    static readonly int[] WeeklyRewards = { 600, 1000, 800, 500, 1000, 500, 400, 600, 750, 900, 800, 1000, 1400, 500, 2000, 1600 };
    static readonly string[] SpecificArtifactNames = { "Sonnenscheibe", "Alter Kompass", "Jademaske", "Bernsteinskarabäus", "Astrolabium", "Antike Vase", "Siegelring", "Fossile Spirale", "Kupfertafel", "Kristall-Sanduhr" };
    static readonly Item[] XxlOreItems = { Item.Coal, Item.Copper, Item.Iron, Item.Silver, Item.Gold, Item.Platinum, Item.Titanium, Item.Obsidian };
    static readonly string[] XxlOreNames = { "Kohle", "Kupfer", "Eisen", "Silber", "Gold", "Platin", "Titan", "Obsidian" };
    static readonly Item[] GemItems = { Item.OrangeGarnet, Item.Ruby, Item.Emerald, Item.Diamond };
    static readonly string[] GemUnits = { "Granat", "Rubine", "Smaragde", "Diamanten" };
    static readonly int[] SpecificOreIndices = { 6, 3, 7, 8, 9, 16, 17, 18 };
    static readonly int[] PreviousOreSmall = { 30, 20, 20, 8, 5, 5, 5, 8 }, PreviousOreLarge = { 150, 100, 100, 24, 15, 15, 15, 24 };
    // Mining, crafting, exploration, trade/special: the legacy daily mix rules a stored selection must satisfy.
    static readonly int[] DailyGroups = { 0, 2, 1, 0, 3, 1, 0, 0, 0, 0, 2, 2, 3, 1, 0, 1, 0, 0, 0, 3, 0, 0, 0, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 };
    static readonly int[] DailyMinimum = { 2, 1, 1, 1 }, DailyMaximum = { 4, 3, 2, 2 };

    static bool IsXxl(int index) => (index >= 27 && index <= 29) || (index >= 35 && index <= 39);
    static int DailyReward(MetaChallengePeriodState state, int index) => IsXxl(index) ? 3000 : state.dailyVariants[index] == 1 ? 1500 : 500;
    static int DailyTarget(MetaChallengePeriodState state, int index)
    {
        if (state.oreTargetVersion == 1 && index == 18 && state.dailyVariants[index] == 0) return 25;
        if (state.oreTargetVersion == 0) {
            int slot = Array.IndexOf(SpecificOreIndices, index);
            if (slot >= 0) return state.dailyVariants[index] == 1 ? PreviousOreLarge[slot] : PreviousOreSmall[slot];
        }
        return state.dailyVariants[index] == 1 ? DailyLargeTargets[index] : DailyTargets[index];
    }
    static void PreserveCompletedHarvests(MetaChallengePeriodState state)
    {
        if (state.period != 0) return;
        for (int i = 20; i <= 21; i++) {
            int previous = i == 20 ? (state.dailyVariants[i] == 1 ? 10 : 3) : (state.dailyVariants[i] == 1 ? 60 : 20);
            if (state.rewarded[i] && state.counters[i] == previous) state.counters[i] = DailyTarget(state, i);
        }
    }
    static bool ValidDailySelection(int[] selection)
    {
        if (selection == null || selection.Length != 8 || selection.Any(i => i < 0 || i >= PeriodMetrics.Length) ||
            selection.Count(IsXxl) > 1 || selection.Distinct().Count() != 8 || selection.Select(i => PeriodMetrics[i]).Distinct().Count() != 8) return false;
        for (int group = 0; group < 4; group++) {
            int count = selection.Count(i => DailyGroups[i] == group);
            if (count < DailyMinimum[group] || count > DailyMaximum[group]) return false;
        }
        return true;
    }
    static bool EnsureAwardLedger(MetaChallengePeriodState state)
    {
        if (state.awardedXp == null || state.awardedXp.Length == 0) {
            state.awardedXp = new int[PeriodMetrics.Length];
            var legacy = state.period == 0 ? LegacyDailyRewards : WeeklyRewards;
            for (int i = 0; i < legacy.Length; i++)
                if (state.rewarded[i]) state.awardedXp[i] = legacy[i];
        }
        if (!IsStoredPeriodLength(state.awardedXp.Length, 6)) return false;
        if (state.awardedXp.Length < PeriodMetrics.Length) Array.Resize(ref state.awardedXp, PeriodMetrics.Length);
        return true;
    }
    // Period arrays are append-only, so every length from the first stored format up to the full pool is valid.
    static bool IsStoredPeriodLength(int length, int firstFormatLength) => length >= firstFormatLength && length <= PeriodMetrics.Length;

    // Converts legacy states, repairs rounds, counters and awards. False only if paid challenge XP exceeds periodRewardXp.
    static bool ValidatePeriods(MetaProfile value)
    {
        if (value.periodRewardXp < 0 || value.periodRewardXp > 1000000000000L) return false;
        try {
            value.challengeClockUtc = Math.Min(ClockLimit, Math.Max(0, value.challengeClockUtc));
            value.challengePeriods ??= new List<MetaChallengePeriodState>();
            var rounds = new List<MetaChallengeRound>();
            if (value.challengeRounds != null) foreach (var round in value.challengeRounds) if (SanitizeRound(round)) rounds.Add(round);
            var periods = new HashSet<int>();
            foreach (var state in value.challengePeriods) {
                try {
                    // A failing legacy state is dropped: its paid XP already is part of periodRewardXp.
                    if (state == null || !periods.Add(state.period) || !ValidLegacyState(state) ||
                        rounds.Any(round => round.period == state.period && round.startUtc >= state.startUtc)) continue;
                    var converted = ConvertLegacy(state);
                    if (SanitizeRound(converted)) rounds.Add(converted);
                } catch (Exception) { }
            }
            value.challengePeriods.Clear();
            // One round per period; the newest start wins.
            value.challengeRounds = rounds.GroupBy(round => round.period).Select(group => group.OrderByDescending(round => round.startUtc).First()).OrderBy(round => round.period).ToList();
            value.lifetimeCounters = RepairCounters(value.lifetimeCounters);
            value.milestoneAwards = RepairAwards(value.milestoneAwards);
            long paid = value.milestoneAwards.Sum(award => (long)award.xp);
            foreach (var round in value.challengeRounds) paid += Math.Max(0, ChallengeEngine.PaidXp(round));
            if (paid > value.periodRewardXp) return false;
            RebuildAwards(value, value.periodRewardXp - paid);
            return true;
        } catch (Exception) { return false; }
    }

    static bool SanitizeRound(MetaChallengeRound round)
    {
        if (round == null) return false;
        try {
            round.tasks ??= new List<MetaChallengeTask>(); round.masks ??= new List<MetaChallengeMask>(); round.runValues ??= new List<MetaChallengeRunValue>();
            return ChallengeEngine.Sanitize(round);
        } catch (Exception) { return false; }
    }

    // The checks the legacy code applied to a stored state; false drops the state.
    static bool ValidLegacyState(MetaChallengePeriodState state)
    {
        if (state.visitedLayerMask < 0 || state.visitedLayerMask > 31 || state.artifactVarietyMask < 0 || state.artifactVarietyMask > 1023 || state.expeditionPeakLayer < 0 || state.expeditionPeakLayer > 100 || (state.expeditionRunId != null && state.expeditionRunId.Length > 160) || state.oreTargetVersion < 0 || state.oreTargetVersion > 2 || state.dailyGemType < 0 || state.dailyGemType >= GemItems.Length || state.dailyOreType < 0 || state.dailyOreType >= XxlOreItems.Length || state.dailyArtifactType < 0 || state.dailyArtifactType >= 10 || state.period < 0 || state.period > 1 || state.startUtc < 0 ||
            state.endUtc <= state.startUtc || state.endUtc > DateTime.MaxValue.Ticks ||
            state.counters == null || state.rewarded == null || state.counters.Length != state.rewarded.Length ||
            !IsStoredPeriodLength(state.counters.Length, 6)) return false;
        // Preserve legacy counters/rewards; appended pool entries start at zero.
        if (state.counters.Length < PeriodMetrics.Length) {
            Array.Resize(ref state.counters, PeriodMetrics.Length);
            Array.Resize(ref state.rewarded, PeriodMetrics.Length);
        }
        // JsonUtility writes null arrays as []; absent legacy/weekly selections stay optional.
        if (state.dailySelection != null && state.dailySelection.Length == 0) state.dailySelection = null;
        if (state.dailySelection != null && (state.period != 0 || !ValidDailySelection(state.dailySelection))) return false;
        if (state.dailyVariants == null || state.dailyVariants.Length == 0) state.dailyVariants = new int[PeriodMetrics.Length];
        if (IsStoredPeriodLength(state.dailyVariants.Length, 20)) Array.Resize(ref state.dailyVariants, PeriodMetrics.Length);
        if (state.dailyVariants.Length != PeriodMetrics.Length || state.dailyVariants.Any(v => v < 0 || v > 1) ||
            (state.period != 0 && state.dailyVariants.Any(v => v != 0))) return false;
        if (!EnsureAwardLedger(state)) return false;
        PreserveCompletedHarvests(state);
        var targets = state.period == 0 ? DailyTargets : WeeklyTargets;
        for (int i = 0; i < PeriodMetrics.Length; i++) {
            if (i >= targets.Length) {
                if (state.counters[i] != 0 || state.rewarded[i] || state.awardedXp[i] != 0) return false;
                continue;
            }
            int target = state.period == 0 ? DailyTarget(state, i) : targets[i];
            if (state.counters[i] < 0 || state.counters[i] > target || state.rewarded[i] != (state.counters[i] == target)) return false;
            int paid = state.awardedXp[i];
            if (!state.rewarded[i] && paid != 0) return false;
            if (state.rewarded[i]) {
                int current = state.period == 0 ? DailyReward(state, i) : WeeklyRewards[i];
                int legacy = (state.period == 0 ? LegacyDailyRewards : WeeklyRewards)[i];
                if (paid != current && paid != legacy && !(state.period == 0 && !IsXxl(i) && (paid == 500 || paid == 1500))) return false;
            }
        }
        return true;
    }

    // A running legacy day (its 8 selected tasks) or week (all 16) continues as a format-1 round until it ends.
    static MetaChallengeRound ConvertLegacy(MetaChallengePeriodState state)
    {
        bool daily = state.period == 0;
        if (daily && state.dailySelection == null) return null;
        var round = new MetaChallengeRound { period = state.period, format = 1, startUtc = state.startUtc, endUtc = state.endUtc };
        foreach (int index in daily ? state.dailySelection : Enumerable.Range(0, WeeklyTargets.Length)) round.tasks.Add(LegacyTask(state, index));
        if (state.visitedLayerMask > 0) round.masks.Add(new MetaChallengeMask { metric = "layers", mask = state.visitedLayerMask });
        if (state.artifactVarietyMask > 0) round.masks.Add(new MetaChallengeMask { metric = "artifact-sighted", mask = state.artifactVarietyMask });
        return round;
    }
    static MetaChallengeTask LegacyTask(MetaChallengePeriodState state, int index)
    {
        bool daily = state.period == 0;
        string id = LegacyTemplates[index];
        int subject = LegacySubjects[index];
        if (index == 24 || index == 38) subject = state.dailyArtifactType + 1;
        else if (index == 51) subject = state.dailyVariants[index] == 1 ? 5 : 3;
        else if (index == 29) subject = (int)XxlOreItems[state.dailyOreType];
        else if (index == 36) subject = (int)GemItems[state.dailyGemType];
        if ((index == 29 || index == 36) && (subject == (int)Item.Obsidian || subject == (int)Item.Emerald)) id = "legacy-impossible";
        string metric = id switch { "ores" => "resources", "ore" or "gem" or "ultronium" => "ore", "legacy-depth-gain" => "depth-gain",
            "legacy-impossible" => "unobtainable", "legacy-artifact-sighted" => "artifact-sighted", _ => id };
        int reward = daily ? DailyReward(state, index) : WeeklyRewards[index];
        bool done = state.rewarded[index];
        return new MetaChallengeTask {
            id = id, name = (daily ? DailyNames : WeeklyNames)[index], metric = metric, unit = LegacyUnit(state, index),
            mode = (int)(id == "layers" || id == "legacy-artifact-sighted" ? ChallengeMode.Distinct : id == "altars" ? ChallengeMode.Max : ChallengeMode.Add),
            subject = subject, atLeast = id == "round-trips",
            tier = daily ? (IsXxl(index) ? 3 : state.dailyVariants[index] == 1 ? 1 : 0) : reward < 1000 ? 0 : reward < 1500 ? 1 : 2,
            target = daily ? DailyTarget(state, index) : WeeklyTargets[index], progress = state.counters[index],
            done = done, paid = done ? state.awardedXp[index] : 0, xp = done ? state.awardedXp[index] : reward
        };
    }
    static string LegacyUnit(MetaChallengePeriodState state, int index) => index switch {
        24 or 38 => SpecificArtifactNames[state.dailyArtifactType], 29 => XxlOreNames[state.dailyOreType], 36 => GemUnits[state.dailyGemType],
        51 => state.dailyVariants[index] == 1 ? "L5 und zurück" : "L3 und zurück", _ => LegacyUnits[index] };

    static bool ValidChallengeId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxChallengeId;
    static List<MetaLifetimeCounter> RepairCounters(List<MetaLifetimeCounter> values)
    {
        var result = new List<MetaLifetimeCounter>(); var ids = new HashSet<string>();
        if (values != null) foreach (var counter in values) {
            if (result.Count >= MaxLifetimeCounters) break;
            if (counter == null || !ValidChallengeId(counter.id) || !ids.Add(counter.id)) continue;
            counter.value = Math.Min(MaxLifetimeValue, Math.Max(0, counter.value)); result.Add(counter);
        }
        return result;
    }
    static List<MetaMilestoneAward> RepairAwards(List<MetaMilestoneAward> values)
    {
        var result = new List<MetaMilestoneAward>(); var ids = new HashSet<string>();
        if (values != null) foreach (var award in values) {
            if (result.Count >= MaxMilestoneAwards) break;
            if (award == null || !ValidChallengeId(award.id) || !ids.Add(award.id)) continue;
            award.xp = Math.Min(10000000, Math.Max(0, award.xp)); result.Add(award);
        }
        return result;
    }
    static List<MetaRunTally> RepairTallies(List<MetaRunTally> values)
    {
        var result = new List<MetaRunTally>(); var ids = new HashSet<string>();
        if (values != null) foreach (var tally in values) {
            if (result.Count >= MaxRunTallies) break;
            if (tally == null || !ValidChallengeId(tally.id) || !ids.Add(tally.id)) continue;
            tally.value = Math.Min(MaxTallyValue, Math.Max(0, tally.value)); tally.peak = Math.Min(MaxTallyValue, Math.Max(0, tally.peak));
            result.Add(tally);
        }
        return result;
    }
    // Older builds drop milestoneAwards but keep run ledgers: "cm:" markers restore the awards without paying again.
    // A marker whose XP is not covered by periodRewardXp (profile restored from an older backup) stays unpaid instead.
    static void RebuildAwards(MetaProfile value, long budget)
    {
        try {
            HashSet<string> awarded = null;
            foreach (var state in value.runs) {
                if (state?.resourceSources == null) continue;
                foreach (string key in state.resourceSources) {
                    if (key == null || key.Length <= 3 || key[0] != 'c' || key[1] != 'm' || key[2] != ':') continue;
                    string id = key.Substring(3);
                    awarded ??= new HashSet<string>(value.milestoneAwards.Select(award => award.id));
                    var stage = MilestoneStage(id);
                    if (stage == null || stage.legacy || awarded.Contains(id) || value.milestoneAwards.Count >= MaxMilestoneAwards) continue;
                    int xp = Math.Min(10000000, Math.Max(0, stage.xp));
                    if (xp > budget) continue;
                    value.milestoneAwards.Add(new MetaMilestoneAward { id = id, xp = xp });
                    awarded.Add(id); budget -= xp;
                }
            }
        } catch (Exception) { }
    }
    static ChallengeMilestoneStage MilestoneStage(string id)
    {
        foreach (var line in ChallengeCatalog.Milestones)
            if (line?.stages != null) foreach (var stage in line.stages) if (stage != null && stage.id == id) return stage;
        return null;
    }
}
