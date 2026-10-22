using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

// Profile-only regression suite. Does not enter Play Mode or touch scenes/user profiles.
public static class DailyChallengeSelectionChecks
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
    }
    static bool Validate(MetaProfile value) => (bool)typeof(MetaProgression)
        .GetMethod("ValidatePeriods", Private).Invoke(null, new object[] { value });
    public static object Main()
    {
        if (Application.isPlaying || MetaProgression.CurrentRun != null)
            throw new Exception("Requires Edit Mode without an active run");
        checks = 0;
        var select = typeof(MetaProgression).GetMethod("SelectDaily", Private);
        var groups = (int[])typeof(MetaProgression).GetField("DailyGroups", Private).GetValue(null);
        var metrics = (string[])typeof(MetaProgression).GetField("PeriodMetrics", Private).GetValue(null);
        var valid = typeof(MetaProgression).GetMethod("ValidDailySelection", Private);
        var smallTargets = (int[])typeof(MetaProgression).GetField("DailyTargets", Private).GetValue(null);
        var largeTargets = (int[])typeof(MetaProgression).GetField("DailyLargeTargets", Private).GetValue(null);
        Check(smallTargets.Length == 52 && largeTargets.Length == 52, "Every task has both target sizes");
        Check(Enumerable.Range(0, 27).All(i => largeTargets[i] > smallTargets[i]), "All large variants require more progress");
        Check(smallTargets[20] == 10 && largeTargets[20] == 40 && smallTargets[21] == 25 && largeTargets[21] == 100, "Updated harvest targets");
        Check(smallTargets[23] == 5 && largeTargets[23] == 25, "Artifact targets");
        Check(smallTargets[22] == 150 && largeTargets[22] == 500, "Surface excavation targets");
        Check(smallTargets[25] == 200 && largeTargets[25] == 600, "Layer two targets");
        var l2Layers = new[] { new MapLayer { startDepth = 0 }, new MapLayer { startDepth = 30 }, new MapLayer { startDepth = 150 } };
        Check(!MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, -29, 0), l2Layers), "Layer one excluded");
        Check(MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, -30, 0), l2Layers), "Layer two start included");
        Check(MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, -149, 0), l2Layers), "Layer two bottom included");
        Check(!MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, -150, 0), l2Layers), "Layer three excluded");
        Check(!MetaProgressionRuntime.IsLayerTwoExcavation(Vector3Int.zero, null), "Missing layers excluded");
        Check(!MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, 1, 0), l2Layers), "Above surface excluded from L2");
        Check(MetaProgressionRuntime.IsLayerTwoExcavation(new Vector3Int(0, -50, 0), l2Layers.Reverse().ToArray()), "Layer depth order respected");
        var surfaceBlock = ScriptableObject.CreateInstance<Block>();
        surfaceBlock.hideFlags = HideFlags.HideAndDontSave;
        try {
            var layers = new[] { new MapLayer { startDepth = 0 }, new MapLayer { startDepth = 120 } };
            surfaceBlock.id = BlockType.Dirt;
            Check(MetaProgressionRuntime.IsSurfaceExcavation(new Vector3Int(1, 0, 0), surfaceBlock, layers, false, false), "Surface dirt qualifies");
            Check(MetaProgressionRuntime.IsSurfaceExcavation(new Vector3Int(1, -119, 0), surfaceBlock, layers, false, false), "Bottom of first layer qualifies");
            Check(!MetaProgressionRuntime.IsSurfaceExcavation(new Vector3Int(1, -120, 0), surfaceBlock, layers, false, false), "Next layer excluded");
            Check(!MetaProgressionRuntime.IsSurfaceExcavation(new Vector3Int(1, 1, 0), surfaceBlock, layers, false, false), "Above ground excluded");
            Check(!MetaProgressionRuntime.IsSurfaceExcavation(Vector3Int.zero, surfaceBlock, layers, true, false), "Ore excluded");
            Check(!MetaProgressionRuntime.IsSurfaceExcavation(Vector3Int.zero, surfaceBlock, layers, false, true), "Artifacts excluded");
            surfaceBlock.id = BlockType.Stone;
            Check(MetaProgressionRuntime.IsSurfaceExcavation(Vector3Int.zero, surfaceBlock, layers, false, false), "First layer stone qualifies");
            surfaceBlock.id = BlockType.CopperOre;
            Check(!MetaProgressionRuntime.IsSurfaceExcavation(Vector3Int.zero, surfaceBlock, layers, false, false), "Ore block type excluded");
        } finally { UnityEngine.Object.DestroyImmediate(surfaceBlock); }
        for (int index = 20; index <= 21; index++) for (int size = 0; size <= 1; size++) {
            var oldHarvest = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                dailyVariants = new int[52], awardedXp = new int[52] };
            oldHarvest.dailyVariants[index] = size;
            oldHarvest.counters[index] = index == 20 ? (size == 1 ? 10 : 3) : (size == 1 ? 60 : 20);
            oldHarvest.rewarded[index] = true;
            oldHarvest.awardedXp[index] = size == 1 ? 1500 : 500;
            var oldHarvestProfile = new MetaProfile { periodRewardXp = oldHarvest.awardedXp[index] };
            oldHarvestProfile.challengePeriods.Add(oldHarvest);
            Check(Validate(oldHarvestProfile) && oldHarvest.counters[index] == (size == 1 ? largeTargets[index] : smallTargets[index]), "Previously completed harvest stays completed");
            Check(oldHarvestProfile.periodRewardXp == (size == 1 ? 1500 : 500), "Target migration keeps earned XP");
        }
        var oldHaulProfile = new MetaProfile();
        var oldHaulDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
            dailySelection = new[] { 1, 2, 3, 4, 5, 10, 13, 14 }, dailyVariants = new int[52], awardedXp = new int[52] };
        oldHaulDay.counters[14] = 100;
        oldHaulProfile.challengePeriods.Add(oldHaulDay);
        Check(Validate(oldHaulProfile) && oldHaulDay.counters[14] == 100 && oldHaulDay.dailySelection.Contains(14), "Previously drawn daily haul keeps progress");
        var oreIndices = (int[])typeof(MetaProgression).GetField("SpecificOreIndices", Private).GetValue(null);
        Check(oreIndices.All(i => smallTargets[i] == (i == 18 ? 30 : 25) && largeTargets[i] == 100), "Normal specific ore targets include 30/100 obsidian");
        for (int dayIndex = 0; dayIndex < 730; dayIndex++) {
            long stamp = new DateTime(2038, 1, 1).AddDays(dayIndex).Ticks;
            var shallow = (int[])select.Invoke(null, new object[] { stamp, true, 3, new[] { 2, 5, 3, 2, 2, 20, 10 } });
            Check(!shallow.Contains(16) && !shallow.Contains(18), "Deep ore daily gates before L4");
            Check((bool)valid.Invoke(null, new object[] { shallow }), "Eight valid tasks with deep ores locked");
        }
        var requiredLayers = (int[])typeof(MetaProgression).GetField("OreUnlockLayers", Private).GetValue(null);
        int previousLayer = requiredLayers[7];
        try {
            requiredLayers[7] = 5;
            bool seenAfterUnlock = false;
            for (int dayIndex = 0; dayIndex < 365; dayIndex++) {
                long stamp = new DateTime(2039, 1, 1).AddDays(dayIndex).Ticks;
                var l4 = (int[])select.Invoke(null, new object[] { stamp, true, 4, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                var l5 = (int[])select.Invoke(null, new object[] { stamp, true, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                Check(!l4.Contains(18), "L5 ore remains locked at L4");
                seenAfterUnlock |= l5.Contains(18);
            }
            Check(seenAfterUnlock, "L5 ore reachable after L5 unlock");
        } finally { requiredLayers[7] = previousLayer; }
        var covered = new HashSet<int>();
        var oldSurfaceProfile = new MetaProfile();
        var oldSurfacePeriod = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
            counters = new int[22], rewarded = new bool[22], dailyVariants = new int[22], awardedXp = new int[22],
            dailySelection = new[] { 1, 2, 4, 5, 10, 13, 20, 21 } };
        oldSurfaceProfile.challengePeriods.Add(oldSurfacePeriod);
        Check(Validate(oldSurfaceProfile) && oldSurfacePeriod.counters.Length == 52 && oldSurfacePeriod.dailyVariants.Length == 52 &&
            oldSurfacePeriod.awardedXp.Length == 52 && oldSurfacePeriod.counters[22] == 0, "Twenty-two-task profile expands without progress loss");
        Check(oldSurfacePeriod.dailySelection.SequenceEqual(new[] { 1, 2, 4, 5, 10, 13, 20, 21 }), "Existing daily draw preserved on pool expansion");
        var previousArtifactProfile = new MetaProfile();
        var previousArtifactDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
            counters = new int[23], rewarded = new bool[23], dailyVariants = new int[23], awardedXp = new int[23],
            dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 22 } };
        previousArtifactProfile.challengePeriods.Add(previousArtifactDay);
        Check(Validate(previousArtifactProfile) && previousArtifactDay.counters.Length == 52 &&
            previousArtifactDay.dailyVariants.Length == 52 && previousArtifactDay.awardedXp.Length == 52 &&
            previousArtifactDay.counters[23] == 0, "Twenty-three-task profile expands for artifacts");
        Check(previousArtifactDay.dailySelection.SequenceEqual(new[] { 1, 2, 4, 5, 10, 13, 21, 22 }), "Artifact addition preserves today's draw");
        foreach (bool exotic in new[] { false, true }) {
            var miningCounts = new HashSet<int>();
            var mixes = new HashSet<string>();
            for (int day = 0; day < 730; day++) {
                long start = new DateTime(2026, 1, 1).AddDays(day).Ticks;
                var indices = (int[])select.Invoke(null, new object[] { start, exotic, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                Check((bool)valid.Invoke(null, new object[] { indices }), "Eight unique goals, category bounds and no duplicate actions");
                Check(indices.SequenceEqual((int[])select.Invoke(null, new object[] { start, exotic, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } })), "Deterministic daily draw");
                Check(exotic || !indices.Contains(12), "Exotic gate");
                Check(!indices.Contains(14), "Large haul excluded from new daily draws");
                miningCounts.Add(indices.Count(i => groups[i] == 0));
                mixes.Add(string.Join(",", Enumerable.Range(0, 4).Select(g => indices.Count(i => groups[i] == g))));
                foreach (int i in indices) covered.Add(i);
            }
            Check(miningCounts.SetEquals(new[] { 2, 3, 4 }), "Mining varies between two, three and four");
            Check(mixes.Count > 3, "Multiple daily category mixes");
        }
        Check(covered.Count == 43, "Entire twenty-task pool reachable");
        string previous = MetaProgression.TestDirectory;
        var clock = MetaProgression.ChallengeTestUtc;
        try {
            MetaProgression.TestDirectory = Path.GetFullPath("Temp/DailySelection-" + DateTime.UtcNow.Ticks);
            MetaProgression.ChallengeTestUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            var daily = MetaProgression.GetChallenges(ChallengePeriod.Daily);
            Check(daily.Length == 8 && daily.All(c => c.metric != "exotics"), "Fresh low-level profile has eight eligible tasks");
            Check(MetaProgression.GetChallenges(ChallengePeriod.Weekly).Length == 16, "Weekly list unchanged");
            var weeklyHaul = MetaProgression.GetChallenges(ChallengePeriod.Weekly).Single(c => c.id == "Weekly-large-haul");
            Check(weeklyHaul.name == "Große Ausbeute" && weeklyHaul.target == 2500 && weeklyHaul.xp == 2000, "Large haul uses existing weekly goal without duplication");
            Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).Length == 17, "Lifetime list unchanged");
            var ids = daily.Select(c => c.id).ToArray();
            var variants = daily.Select(c => c.target + ":" + c.xp).ToArray();
            Check(daily.All(c => c.xp == 500 || c.xp == 1500), "Exactly two payout sizes");
            Check(MetaProgression.Save(), "Selection saved before gameplay");
            MetaProgression.Reload();
            Check(ids.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id)), "Reload keeps exact selection");
            Check(variants.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.target + ":" + c.xp)), "Reload keeps target variants and payouts");
            MetaProgression.BeginRun("daily-selection-A");
            foreach (var item in new[] { Item.Coal, Item.Copper, Item.Iron, Item.Silver, Item.Gold, Item.Platinum, Item.Titanium, Item.Obsidian })
                MetaProgression.RecordResource(item, 500, "source-" + item);
            for (int i = 0; i < 100; i++) { MetaProgression.RecordSurfaceHarvest("trees", "tree-" + i); MetaProgression.RecordSurfaceHarvest("grass", "grass-" + i); }
            for (int i = 0; i < 500; i++) MetaProgression.RecordSurfaceBlock(new Vector3Int(i, 0, 0));
            for (int artifactIndex = 0; artifactIndex < 25; artifactIndex++) MetaProgression.RecordArtifact(new Vector3Int(artifactIndex, -10, 0), "Artifact_" + (MetaProgression.Profile.challengePeriods.First(p => p.period == 0).dailyArtifactType + 1).ToString("D2"));
            for (int layerIndex = 0; layerIndex < 600; layerIndex++) MetaProgression.RecordLayerTwoBlock(new Vector3Int(layerIndex, -40, 0));
            for (int layerThreeIndex = 0; layerThreeIndex < 750; layerThreeIndex++) MetaProgression.RecordLayerThreeBlock(new Vector3Int(layerThreeIndex, -160, 0));
            foreach (var gemFixture in new[] { Item.OrangeGarnet, Item.Ruby, Item.Emerald, Item.Diamond }) MetaProgression.RecordResource(gemFixture, 100, "gem-fixture-" + gemFixture);
            MetaProgression.RecordCraftedAmount(Item.Axe, 10);
            foreach (var gearFixture in new[] { Item.Backpack, Item.LoadBelt, Item.CrystalPendant }) MetaProgression.RecordCraftedAmount(gearFixture, 1);
            foreach (var upgradeMetric in new[] { "pickaxe-upgrades", "energy-upgrades", "carrying-upgrades" }) { MetaProgression.RecordCraftUpgrade(upgradeMetric, 1, 2); MetaProgression.RecordCraftUpgrade(upgradeMetric, 2, 3); }
            MetaProgression.RecordCraftedAmount(Item.Dynamite, 20);
            MetaProgression.RecordCraftedAmount(Item.Medkit, 10);
            for (int revealIndex = 0; revealIndex < 1000; revealIndex++) MetaProgression.RecordRevealedCell(revealIndex, 2);
            MetaProgression.RecordExplorationPosition(1, true);
            for (int visitLayer = 2; visitLayer <= 5; visitLayer++) MetaProgression.RecordExplorationPosition(visitLayer, false);
            MetaProgression.RecordExplorationPosition(1, true);
            for (int artifactType = 1; artifactType <= 5; artifactType++) MetaProgression.RecordArtifact(new Vector3Int(artifactType, -70, 0), "Artifact_" + artifactType.ToString("D2"));
            MetaProgression.RecordDiscovery("altar");
            MetaProgression.RecordDepth(1000);
            MetaProgression.RecordCraftedAmount(Item.Torche, 100);
            MetaProgression.RecordCraftedAmount(Item.Ladder, 100);
            MetaProgression.RecordCraftedAmount(Item.IronLadder, 100, true);
            MetaProgression.RecordSale(20000);
            for (int i = 0; i < 60; i++) MetaProgression.RecordExploration(i, 0, 10);
            for (int i = 0; i < 10; i++) MetaProgression.RecordDiscovery("discovery-" + i);
            Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).All(c => c.completed), "Selected tasks complete via real progression APIs");
            Check(ids.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id)), "Level gains do not reroll tasks");
            var state = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            Check(Enumerable.Range(0, 27).Where(i => !state.dailySelection.Contains(i)).All(i => state.counters[i] == 0 && !state.rewarded[i]), "Inactive tasks neither count nor reward");
            long expected = MetaProgression.GetChallenges(ChallengePeriod.Daily).Sum(c => c.xp) +
                MetaProgression.GetChallenges(ChallengePeriod.Weekly).Where(c => c.completed).Sum(c => c.xp);
            Check(MetaProgression.Profile.periodRewardXp == expected, "Only selected daily and completed weekly XP awarded");
            long xp = MetaProgression.TotalXp, rewards = MetaProgression.Profile.periodRewardXp;
            MetaProgression.RecordSale(20000);
            MetaProgression.RecordCraftedAmount(Item.Torche, 100);
            Check(MetaProgression.TotalXp == xp && MetaProgression.Profile.periodRewardXp == rewards, "No double rewards");
            var saved = MetaProgression.CaptureRunState();
            Check(MetaProgression.Save(), "Completed tasks save");
            MetaProgression.Reload();
            MetaProgression.BeginRun(saved.runId, saved);
            Check(MetaProgression.LastError == null && MetaProgression.TotalXp == xp &&
                MetaProgression.GetChallenges(ChallengePeriod.Daily).All(c => c.completed), "Full profile validation/reload preserves rewards");
            MetaProgression.EndRun();
            MetaProgression.BeginRun("daily-selection-B");
            Check(ids.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id)), "New run retains global daily draw");
            MetaProgression.EndRun();
            MetaProgression.ChallengeTestUtc = MetaProgression.ChallengeTestUtc.Value.AddDays(1);
            var tomorrow = MetaProgression.GetChallenges(ChallengePeriod.Daily);
            Check(tomorrow.Length == 8 && tomorrow.All(c => c.current == 0 && !c.completed), "Next day draws eight empty tasks");
            Check(!ids.SequenceEqual(tomorrow.Select(c => c.id)), "Next day changes selection");
            Check(MetaProgression.Profile.periodRewardXp == rewards, "Rollover retains earned XP");
            var tomorrowIds = tomorrow.Select(c => c.id).ToArray();
            MetaProgression.ChallengeTestUtc = MetaProgression.ChallengeTestUtc.Value.AddDays(-1);
            Check(tomorrowIds.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id)), "Clock rollback cannot reroll yesterday");
            foreach (int length in new[] { 6, 16, 20 }) {
                int[] targets = { 50, 100, 5, 20, 1000, 3, 30, 20, 8, 5, 6, 1, 1, 5, 250, 15, 5, 5, 8, 2500 };
                int[] oldRewards = { 150, 200, 150, 100, 200, 100, 100, 150, 180, 200, 180, 200, 300, 120, 350, 250, 220, 240, 250, 350 };
                var old = new MetaProfile { periodRewardXp = oldRewards.Take(length).Sum() };
                old.challengePeriods.Add(new MetaChallengePeriodState {
                    period = 0, startUtc = 1, endUtc = 2, counters = targets.Take(length).ToArray(),
                    rewarded = Enumerable.Repeat(true, length).ToArray(),
                    dailyVariants = length == 20 ? new int[20] : null,
                    awardedXp = length == 20 ? oldRewards.ToArray() : null
                });
                Check(Validate(old), "Legacy period accepted");
                var migrated = old.challengePeriods[0];
                Check(migrated.counters.Length == 52 && migrated.rewarded.Take(length).All(x => x), "Legacy completion flags preserved");
                migrated.dailySelection = (int[])select.Invoke(null, new object[] { 1L, false, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                Check(Validate(old) && old.periodRewardXp == oldRewards.Take(length).Sum(), "Inactive legacy rewards remain valid");
                Check(migrated.awardedXp.Take(length).SequenceEqual(oldRewards.Take(length)), "Legacy payouts retained individually");
                var roundtrip = JsonUtility.FromJson<MetaProfile>(JsonUtility.ToJson(old));
                Check(Validate(roundtrip) && roundtrip.periodRewardXp == old.periodRewardXp, "Migrated payout ledger survives serialization");
                roundtrip.challengePeriods[0].awardedXp[0]++;
                Check(!Validate(roundtrip), "Invalid payout rejected");
                migrated.dailySelection[1] = migrated.dailySelection[0];
                Check(!Validate(old), "Duplicate persisted selection rejected");
            }
            MetaProgression.Profile.reachedMiningLayer = 3;
            var seenSmall = new HashSet<string>();
            var seenLarge = new HashSet<string>();
            for (int day = 0; day < 365; day++) {
                MetaProgression.ChallengeTestUtc = new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day);
                foreach (var goal in MetaProgression.GetChallenges(ChallengePeriod.Daily)) {
                    int actual = Array.IndexOf((string[])typeof(MetaProgression).GetField("PeriodIds", Private).GetValue(null), goal.id.Substring(6));
                    Check(goal.target == (goal.xp == 1500 ? largeTargets[actual] : smallTargets[actual]), "Variant target matches payout");
                    (goal.xp == 1500 ? seenLarge : seenSmall).Add(goal.id);
                }
            }
            Check(seenSmall.Count == 38 && seenLarge.Count == 36, "All twenty tasks occur in both sizes");
            MetaProgression.TestDirectory = Path.GetFullPath("Temp/HarvestLedger-" + DateTime.UtcNow.Ticks);
            MetaProgression.ChallengeTestUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            MetaProgression.BeginRun("surface-harvest");
            MetaProgression.GetChallenges(ChallengePeriod.Daily);
            var harvestDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            harvestDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 20, 21 };
            harvestDay.dailyVariants = new int[52];
            var beforeHarvest = MetaProgression.CaptureRunState();
            MetaProgression.RecordSurfaceHarvest("trees", "tree-generation-A");
            MetaProgression.RecordSurfaceHarvest("grass", "grass-generation-A");
            MetaProgression.RecordSurfaceHarvest("trees", "tree-generation-A");
            MetaProgression.RecordSurfaceHarvest("grass", "grass-generation-A");
            Check(harvestDay.counters[20] == 1 && harvestDay.counters[21] == 1, "Same plant counts once");
            Check(MetaProgression.Save(), "Harvest sources persist");
            MetaProgression.Reload();
            MetaProgression.BeginRun("surface-harvest", beforeHarvest);
            MetaProgression.RecordSurfaceHarvest("trees", "tree-generation-A");
            MetaProgression.RecordSurfaceHarvest("grass", "grass-generation-A");
            harvestDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            Check(harvestDay.counters[20] == 1 && harvestDay.counters[21] == 1, "Older world snapshot cannot replay plant rewards");
            MetaProgression.RecordSurfaceHarvest("trees", "tree-generation-B");
            MetaProgression.RecordSurfaceHarvest("grass", "grass-generation-B");
            Check(harvestDay.counters[20] == 2 && harvestDay.counters[21] == 2, "Regrown plants count separately");
            MetaProgression.RecordSurfaceHarvest("invalid", "source");
            Check(harvestDay.counters[20] == 2 && harvestDay.counters[21] == 2, "Unsupported harvest ignored");
            harvestDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 22 };
            MetaProgression.RecordSurfaceBlock(new Vector3Int(4, -2, 0));
            MetaProgression.RecordSurfaceBlock(new Vector3Int(4, -2, 0));
            Check(harvestDay.counters[22] == 1, "Terrain cell counts once");
            Check(MetaProgression.Save(), "Surface block source saved");
            MetaProgression.Reload(); MetaProgression.BeginRun("surface-harvest", beforeHarvest);
            MetaProgression.RecordSurfaceBlock(new Vector3Int(4, -2, 0));
            harvestDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            Check(harvestDay.counters[22] == 1, "Surface block replay after old world load ignored");
            harvestDay.dailySelection = new[] { 1, 2, 4, 5, 13, 21, 22, 23 };
            MetaProgression.RecordArtifact(new Vector3Int(7, -10, 0));
            MetaProgression.RecordArtifact(new Vector3Int(7, -10, 0));
            MetaProgression.RecordDiscovery("cache-test");
            Check(harvestDay.counters[23] == 1, "Artifact counts once; other discoveries do not count");
            Check(MetaProgression.Save(), "Artifact source saved");
            MetaProgression.Reload(); MetaProgression.BeginRun("surface-harvest", beforeHarvest);
            MetaProgression.RecordArtifact(new Vector3Int(7, -10, 0));
            harvestDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            Check(harvestDay.counters[23] == 1, "Artifact replay after old world load ignored");
            MetaProgression.RecordArtifact(new Vector3Int(8, -10, 0));
            Check(harvestDay.counters[23] == 2, "Another artifact of same type counts separately");
            for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/SpecificArtifact-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var specificDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                specificDay.dailySelection = new[] { 1, 2, 4, 5, 13, 21, 22, 24 };
                specificDay.dailyVariants[24] = size;
                specificDay.dailyArtifactType = 2;
                MetaProgression.BeginRun("specific-artifacts");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.metric == "specific-artifact");
                Check(goal.name == "Spuren der Vorzeit" && goal.subject == "Jademaske" && goal.target == (size == 0 ? 3 : 10) && goal.xp == (size == 0 ? 500 : 1500), "Specific artifact name, target and reward");
                MetaProgression.RecordArtifact(new Vector3Int(100, -20, 0), "Artifact_01");
                Check(specificDay.counters[24] == 0, "Wrong artifact type ignored");
                for (int i = 0; i < goal.target - 1; i++) MetaProgression.RecordArtifact(new Vector3Int(i, -20, 0), "Artifact_03");
                Check(!specificDay.rewarded[24], "Specific artifact threshold not reached early");
                var oldWorld = MetaProgression.CaptureRunState();
                Check(MetaProgression.Save(), "Specific artifact profile saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("specific-artifacts", oldWorld);
                specificDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                Check(specificDay.dailyArtifactType == 2 && specificDay.counters[24] == goal.target - 1, "Type and partial progress persist");
                MetaProgression.RecordArtifact(new Vector3Int(goal.target - 1, -20, 0), "Artifact_03");
                Check(specificDay.rewarded[24] && specificDay.awardedXp[24] == goal.xp, "Specific artifact completion pays exact XP");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordArtifact(new Vector3Int(goal.target - 1, -20, 0), "Artifact_03");
                Check(MetaProgression.Profile.periodRewardXp == paid, "Specific artifact replay pays no extra XP");
            }
            var previousSpecificProfile = new MetaProfile();
            var previousSpecificDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[24], rewarded = new bool[24], dailyVariants = new int[24], awardedXp = new int[24],
                dailySelection = new[] { 1, 2, 4, 5, 13, 21, 22, 23 } };
            previousSpecificProfile.challengePeriods.Add(previousSpecificDay);
            Check(Validate(previousSpecificProfile) && previousSpecificDay.counters.Length == 52 && previousSpecificDay.dailyVariants.Length == 52 &&
                previousSpecificDay.awardedXp.Length == 52 && previousSpecificDay.dailySelection.Last() == 23, "Previous twenty-four-task profile preserves draw");
            for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/LayerTwo-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var layerDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                layerDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 25 };
                layerDay.dailyVariants[25] = size;
                MetaProgression.BeginRun("layer-two");
                int target = size == 0 ? 200 : 600;
                for (int i = 0; i < target - 1; i++) MetaProgression.RecordLayerTwoBlock(new Vector3Int(i, -40, 0));
                Check(layerDay.counters[25] == target - 1 && !layerDay.rewarded[25], "L2 waits for exact threshold");
                var oldWorld = MetaProgression.CaptureRunState();
                Check(MetaProgression.Save(), "L2 source ledger saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("layer-two", oldWorld);
                layerDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                MetaProgression.RecordLayerTwoBlock(new Vector3Int(0, -40, 0));
                Check(layerDay.counters[25] == target - 1, "L2 duplicate ignored after reload");
                MetaProgression.RecordLayerTwoBlock(new Vector3Int(target - 1, -40, 0));
                Check(layerDay.rewarded[25] && layerDay.awardedXp[25] == (size == 0 ? 500 : 1500), "L2 pays exact reward at threshold");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordLayerTwoBlock(new Vector3Int(target, -40, 0));
                Check(layerDay.counters[25] == target && MetaProgression.Profile.periodRewardXp == paid, "L2 completion pays once");
            }
            var oldLayerProfile = new MetaProfile();
            var oldLayerDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[25], rewarded = new bool[25], dailyVariants = new int[25], awardedXp = new int[25],
                dailySelection = new[] { 1, 2, 4, 5, 13, 21, 22, 24 }, dailyArtifactType = 2 };
            oldLayerProfile.challengePeriods.Add(oldLayerDay);
            Check(Validate(oldLayerProfile) && oldLayerDay.counters.Length == 52 && oldLayerDay.dailyVariants.Length == 52 &&
                oldLayerDay.awardedXp.Length == 52 && oldLayerDay.dailySelection.Last() == 24 && oldLayerDay.dailyArtifactType == 2, "L2 migration preserves previous draw and artifact type");
            for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/LayerThree-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var layerDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                layerDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 26 };
                layerDay.dailyVariants[26] = size;
                MetaProgression.BeginRun("layer-three");
                int target = size == 0 ? 250 : 750;
                for (int i = 0; i < target - 1; i++) MetaProgression.RecordLayerThreeBlock(new Vector3Int(i, -160, 0));
                Check(layerDay.counters[26] == target - 1 && !layerDay.rewarded[26], "L3 waits for exact threshold");
                var oldWorld = MetaProgression.CaptureRunState();
                Check(MetaProgression.Save(), "L3 source ledger saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("layer-three", oldWorld);
                layerDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                MetaProgression.RecordLayerThreeBlock(new Vector3Int(0, -160, 0));
                Check(layerDay.counters[26] == target - 1, "L3 duplicate ignored after reload");
                MetaProgression.RecordLayerThreeBlock(new Vector3Int(target - 1, -160, 0));
                Check(layerDay.rewarded[26] && layerDay.awardedXp[26] == (size == 0 ? 500 : 1500), "L3 pays exact reward at threshold");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordLayerThreeBlock(new Vector3Int(target, -160, 0));
                Check(layerDay.counters[26] == target && MetaProgression.Profile.periodRewardXp == paid, "L3 completion pays once");
            }
            var l3Layers = new[] { new MapLayer { startDepth = 0 }, new MapLayer { startDepth = 30 }, new MapLayer { startDepth = 150 }, new MapLayer { startDepth = 450 } };
            Check(smallTargets[26] == 250 && largeTargets[26] == 750, "Layer three targets");
            Check(!MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, -149, 0), l3Layers), "L2 excluded from L3");
            Check(MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, -150, 0), l3Layers), "L3 start included");
            Check(MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, -449, 0), l3Layers), "L3 bottom included");
            Check(!MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, -450, 0), l3Layers), "L4 excluded from L3");
            Check(!MetaProgressionRuntime.IsLayerThreeExcavation(Vector3Int.zero, null), "Missing L3 layers excluded");
            Check(!MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, 1, 0), l3Layers), "Above surface excluded from L3");
            Check(MetaProgressionRuntime.IsLayerThreeExcavation(new Vector3Int(0, -200, 0), l3Layers.Reverse().ToArray()), "L3 depth order respected");
            var oldThreeProfile = new MetaProfile();
            var oldThreeDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[26], rewarded = new bool[26], dailyVariants = new int[26], awardedXp = new int[26],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 25 } };
            oldThreeProfile.challengePeriods.Add(oldThreeDay);
            Check(Validate(oldThreeProfile) && oldThreeDay.counters.Length == 52 && oldThreeDay.dailyVariants.Length == 52 &&
                oldThreeDay.awardedXp.Length == 52 && oldThreeDay.dailySelection.Last() == 25, "L3 migration preserves L2 daily draw");
            var xxlSelect = typeof(MetaProgression).GetMethod("SelectXxl", Private);
            int rareDays = 0; var rareTypes = new HashSet<int>();
            for (int dayIndex = 0; dayIndex < 10000; dayIndex++) {
                long stamp = new DateTime(2030, 1, 1).AddDays(dayIndex).Ticks;
                var regular = (int[])select.Invoke(null, new object[] { stamp, false, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                var locked = (int[])xxlSelect.Invoke(null, new object[] { regular, stamp, 3 });
                Check(locked.SequenceEqual(regular), "XXL locked before L4");
                var l4Only = (int[])xxlSelect.Invoke(null, new object[] { regular, stamp, 4 });
                Check(!l4Only.Contains(28) && !l4Only.Contains(39), "L5 XXL locked until L5 reached");
                var both = (int[])xxlSelect.Invoke(null, new object[] { regular, stamp, 5 });
                Check((bool)valid.Invoke(null, new object[] { both }) && both.Count(i => ((i >= 27 && i <= 29) || (i >= 35 && i <= 39))) <= 1, "XXL keeps eight valid daily tasks");
                Check(both.SequenceEqual((int[])xxlSelect.Invoke(null, new object[] { regular, stamp, 5 })), "XXL draw deterministic");
                if (both.Any(i => ((i >= 27 && i <= 29) || (i >= 35 && i <= 39)))) { rareDays++; rareTypes.Add(both.Single(i => ((i >= 27 && i <= 29) || (i >= 35 && i <= 39)))); }
            }
            Check(rareDays > 400 && rareDays < 600 && rareTypes.SetEquals(new[] { 27, 28, 29, 35, 36, 37, 38, 39 }), "XXL approximately five percent with both layer types");
            var deepLayers = new[] { new MapLayer { startDepth = 0 }, new MapLayer { startDepth = 30 }, new MapLayer { startDepth = 150 }, new MapLayer { startDepth = 450 }, new MapLayer { startDepth = 900 } };
            Check(MetaProgressionRuntime.MiningLayerAt(new Vector3Int(0, -449, 0), deepLayers) == 3, "L4 lower gate");
            Check(MetaProgressionRuntime.MiningLayerAt(new Vector3Int(0, -450, 0), deepLayers) == 4, "L4 start");
            Check(MetaProgressionRuntime.MiningLayerAt(new Vector3Int(0, -899, 0), deepLayers) == 4, "L4 end");
            Check(MetaProgressionRuntime.MiningLayerAt(new Vector3Int(0, -900, 0), deepLayers) == 5, "L5 start");
            for (int layer = 4; layer <= 5; layer++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/XxlLayer-" + layer + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.ChallengeTestUtc = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                MetaProgression.BeginRun("xxl-unlock");
                MetaProgression.RecordReachedMiningLayer(layer);
                MetaProgression.EndRun(); MetaProgression.Save(); MetaProgression.Reload();
                MetaProgression.BeginRun("another-save");
                Check(MetaProgression.Profile.reachedMiningLayer == layer, "Layer unlock survives reload and different run");
                MetaProgression.RecordReachedMiningLayer(2);
                Check(MetaProgression.Profile.reachedMiningLayer == layer, "Shallow run cannot relock XXL");
                var currentDraw = MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id).ToArray();
                MetaProgression.RecordReachedMiningLayer(5);
                Check(currentDraw.SequenceEqual(MetaProgression.GetChallenges(ChallengePeriod.Daily).Select(c => c.id)), "New unlock keeps today's selection");
                var xxlDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                int task = layer == 4 ? 27 : 28;
                xxlDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, task };
                for (int i = 0; i < 1999; i++) MetaProgression.RecordXxlLayerBlock(new Vector3Int(i, -1000, 0), layer);
                MetaProgression.RecordXxlLayerBlock(new Vector3Int(3000, -1000, 0), layer == 4 ? 5 : 4);
                Check(xxlDay.counters[task] == 1999 && !xxlDay.rewarded[task], "XXL requires exact layer and 2000 blocks");
                Check(MetaProgression.Save(), "XXL partial progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("another-save");
                xxlDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                MetaProgression.RecordXxlLayerBlock(new Vector3Int(0, -1000, 0), layer);
                Check(xxlDay.counters[task] == 1999, "XXL duplicate ignored after reload");
                var xxlGoal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id.EndsWith("xxl"));
                Check(xxlGoal.target == 2000 && xxlGoal.xp == 3000 && xxlGoal.name.StartsWith("XXL"), "XXL card target, label and reward");
                MetaProgression.RecordXxlLayerBlock(new Vector3Int(1999, -1000, 0), layer);
                Check(xxlDay.rewarded[task] && xxlDay.awardedXp[task] == 3000, "XXL pays 3000 XP");
                xxlDay.awardedXp[task] = 500;
                Check(!Validate(MetaProgression.Profile), "XXL invalid small payout rejected");
                xxlDay.awardedXp[task] = 3000;
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordXxlLayerBlock(new Vector3Int(2001, -1000, 0), layer);
                Check(MetaProgression.Profile.periodRewardXp == paid, "XXL reward only once");
                Check(MetaProgression.Save(), "Completed XXL profile saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id.EndsWith("xxl")).completed, "XXL completion validates after reload");
            }
            var oldXxlProfile = new MetaProfile();
            var oldXxlDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[27], rewarded = new bool[27], dailyVariants = new int[27], awardedXp = new int[27],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 26 } };
            oldXxlProfile.challengePeriods.Add(oldXxlDay);
            Check(Validate(oldXxlProfile) && oldXxlDay.counters.Length == 52 && oldXxlDay.dailyVariants.Length == 52 &&
                oldXxlDay.awardedXp.Length == 52 && oldXxlDay.dailySelection.Last() == 26, "XXL migration preserves regular daily draw");
            var oreSelect = typeof(MetaProgression).GetMethod("SelectXxlOreType", Private);
            var oreItems = (Item[])typeof(MetaProgression).GetField("XxlOreItems", Private).GetValue(null);
            var oreMetrics = (string[])typeof(MetaProgression).GetField("XxlOreMetrics", Private).GetValue(null);
            var seenOres = new HashSet<int>();
            for (int dayIndex = 0; dayIndex < 1000; dayIndex++) {
                long stamp = new DateTime(2035, 1, 1).AddDays(dayIndex).Ticks;
                var regular = (int[])select.Invoke(null, new object[] { stamp, true, 5, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                int ore = (int)oreSelect.Invoke(null, new object[] { regular, stamp, 5 });
                Check(!regular.Any(task => metrics[task] == oreMetrics[ore]), "XXL avoids duplicate normal ore task");
                Check(ore == (int)oreSelect.Invoke(null, new object[] { regular, stamp, 5 }), "XXL ore choice deterministic");
                seenOres.Add(ore);
            }
            Check(seenOres.Count == oreItems.Length, "All eight specific ores eligible");
            for (int ore = 0; ore < oreItems.Length; ore++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/XxlOre-" + ore + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.ChallengeTestUtc = new DateTime(2035, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                MetaProgression.BeginRun("xxl-ore");
                MetaProgression.RecordReachedMiningLayer(4);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var oreDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                oreDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 29 };
                oreDay.dailyOreType = ore;
                var oreGoal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-specific-ore-xxl");
                Check(oreGoal.target == 500 && oreGoal.xp == 3000 && oreGoal.name.StartsWith("XXL") && oreGoal.metric == oreMetrics[ore], "Specific ore XXL card values");
                MetaProgression.RecordResource(oreItems[(ore + 1) % oreItems.Length], 600, "wrong-ore");
                Check(oreDay.counters[29] == 0, "Other ore does not count for XXL");
                MetaProgression.RecordResource(oreItems[ore], 499, "vein-A");
                Check(oreDay.counters[29] == 499 && !oreDay.rewarded[29], "Ore stack counts actual quantity below threshold");
                Check(MetaProgression.Save(), "XXL ore partial progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("xxl-ore");
                oreDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                Check(oreDay.dailyOreType == ore && oreDay.counters[29] == 499, "XXL ore type and progress persist");
                MetaProgression.RecordResource(oreItems[ore], 499, "vein-A");
                Check(oreDay.counters[29] == 499, "XXL ore source replay ignored");
                MetaProgression.RecordResource(oreItems[ore], 1, "vein-B");
                Check(oreDay.rewarded[29] && oreDay.awardedXp[29] == 3000, "500 specific ores pay 3000 XP");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordResource(oreItems[ore], 100, "vein-C");
                Check(oreDay.counters[29] == 500 && MetaProgression.Profile.periodRewardXp == paid, "XXL ore reward once");
                Check(MetaProgression.Save(), "XXL ore completed profile saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-specific-ore-xxl").completed, "XXL ore completion validates");
            }
            var oldOreProfile = new MetaProfile();
            var oldOreDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[29], rewarded = new bool[29], dailyVariants = new int[29], awardedXp = new int[29],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 27 } };
            oldOreProfile.challengePeriods.Add(oldOreDay);
            Check(Validate(oldOreProfile) && oldOreDay.counters.Length == 52 && oldOreDay.dailyVariants.Length == 52 &&
                oldOreDay.awardedXp.Length == 52 && oldOreDay.dailySelection.Last() == 27, "XXL ore migration keeps earlier XXL draw");
            for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/Gems-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var gemDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                gemDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 30 };
                gemDay.dailyVariants[30] = size;
                MetaProgression.BeginRun("gems");
                var gemGoal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.metric == "gems");
                int target = size == 0 ? 30 : 100;
                Check(gemGoal.name == "Funkelnde Beute" && gemGoal.target == target && gemGoal.xp == (size == 0 ? 500 : 1500), "Gem card target and reward");
                foreach (var excluded in new[] { Item.Copper, Item.Ultronium, Item.Obsidian, Item.Mythril }) MetaProgression.RecordResource(excluded, 100, "excluded-" + excluded);
                Check(gemDay.counters[30] == 0, "Non-gems excluded");
                foreach (var gem in new[] { Item.OrangeGarnet, Item.Ruby, Item.Emerald, Item.Diamond }) MetaProgression.RecordResource(gem, 1, "gem-" + gem);
                Check(gemDay.counters[30] == 4, "All four gem types combine");
                MetaProgression.RecordResource(Item.Ruby, target - 5, "gem-stack");
                Check(gemDay.counters[30] == target - 1 && !gemDay.rewarded[30], "Gem quantity waits for threshold");
                Check(MetaProgression.Save(), "Gem progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("gems");
                gemDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                MetaProgression.RecordResource(Item.Ruby, target - 5, "gem-stack");
                Check(gemDay.counters[30] == target - 1, "Gem replay ignored after reload");
                MetaProgression.RecordResource(Item.Emerald, 1, "last-gem");
                Check(gemDay.rewarded[30] && gemDay.awardedXp[30] == gemGoal.xp, "Gem task pays exact XP");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordResource(Item.Diamond, 20, "more-gems");
                Check(gemDay.counters[30] == target && MetaProgression.Profile.periodRewardXp == paid, "Gem reward pays once");
                Check(MetaProgression.Save(), "Gem completion saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.metric == "gems").completed, "Gem completion validates on reload");
            }
            var oldGemProfile = new MetaProfile();
            var oldGemDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[30], rewarded = new bool[30], dailyVariants = new int[30], awardedXp = new int[30],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 29 }, dailyOreType = 2, oreTargetVersion = 2 };
            oldGemProfile.challengePeriods.Add(oldGemDay);
            Check(Validate(oldGemProfile) && oldGemDay.counters.Length == 52 && oldGemDay.dailyVariants.Length == 52 && oldGemDay.dailySelection.Last() == 29 && oldGemDay.dailyOreType == 2, "Gem migration keeps prior XXL daily");
            var specificGems = new[] { Item.OrangeGarnet, Item.Ruby, Item.Emerald, Item.Diamond };
            for (int gem = 0; gem < specificGems.Length; gem++) for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/SpecificGem-" + gem + "-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var gemDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                int task = 31 + gem;
                gemDay.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, task };
                gemDay.dailyVariants[task] = size;
                MetaProgression.BeginRun("specific-gem");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-" + metrics[task]);
                int target = size == 0 ? 25 : 100;
                Check(goal.target == target && goal.xp == (size == 0 ? 500 : 1500), "Specific gem target and XP");
                MetaProgression.RecordResource(specificGems[(gem + 1) % 4], 200, "other-gem");
                MetaProgression.RecordResource(Item.Copper, 200, "other-ore");
                Check(gemDay.counters[task] == 0, "Only required gem type counts");
                MetaProgression.RecordResource(specificGems[gem], target - 1, "matching-gems");
                Check(gemDay.counters[task] == target - 1 && !gemDay.rewarded[task], "Specific gem partial quantity");
                Check(MetaProgression.Save(), "Specific gem progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("specific-gem");
                gemDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                MetaProgression.RecordResource(specificGems[gem], target - 1, "matching-gems");
                Check(gemDay.counters[task] == target - 1, "Specific gem duplicate ignored after load");
                MetaProgression.RecordResource(specificGems[gem], 1, "final-gem");
                Check(gemDay.rewarded[task] && gemDay.awardedXp[task] == goal.xp, "Specific gem exact reward");
                long paid = MetaProgression.Profile.periodRewardXp;
                MetaProgression.RecordResource(specificGems[gem], 100, "extra-gems");
                Check(gemDay.counters[task] == target && gemDay.awardedXp[task] == goal.xp, "Specific gem reward once");
                Check(MetaProgression.Save(), "Specific gem completion saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == goal.id).completed, "Specific gem completion validates after reload");
            }
            bool deepGemReachable = false;
            for (int dayIndex = 0; dayIndex < 730; dayIndex++) {
                long stamp = new DateTime(2040, 1, 1).AddDays(dayIndex).Ticks;
                var locked = (int[])select.Invoke(null, new object[] { stamp, true, 3, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                var unlocked = (int[])select.Invoke(null, new object[] { stamp, true, 4, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                Check(!locked.Any(i => i >= 32 && i <= 34), "Deep specific gems locked before L4");
                deepGemReachable |= unlocked.Any(i => i >= 32 && i <= 34);
            }
            Check(deepGemReachable, "Deep gem tasks reachable after L4");
            var oldSpecificGemProfile = new MetaProfile();
            var oldSpecificGemDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[31], rewarded = new bool[31], dailyVariants = new int[31], awardedXp = new int[31],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 30 }, oreTargetVersion = 2 };
            oldSpecificGemProfile.challengePeriods.Add(oldSpecificGemDay);
            Check(Validate(oldSpecificGemProfile) && oldSpecificGemDay.counters.Length == 52 && oldSpecificGemDay.dailyVariants.Length == 52 &&
                oldSpecificGemDay.awardedXp.Length == 52 && oldSpecificGemDay.dailySelection.Last() == 30, "Specific gem migration preserves old gem task");
            for (int task = 35; task <= 39; task++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/NewXxl-" + task + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                day.dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, task };
                day.dailyGemType = 3; day.dailyArtifactType = 2;
                MetaProgression.BeginRun("new-xxl");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-" + metrics[task]);
                int[] newXxlTargets = { 300, 150, 75, 25, 100 };
                int target = newXxlTargets[task - 35];
                Check(goal.target == target && goal.xp == 3000 && goal.name.StartsWith("XXL"), "New XXL target and 3000 XP");
                Check(task != 36 || goal.metric == "diamond", "Specific XXL gem named");
                Check(task != 38 || goal.subject == "Jademaske", "Specific XXL artifact named");
                MetaProgression.RecordResource(Item.Copper, 100, "wrong-resource");
                if (task == 36) MetaProgression.RecordResource(Item.Ruby, 500, "wrong-gem");
                if (task == 38) MetaProgression.RecordArtifact(new Vector3Int(-1, -60, 0), "Artifact_01");
                Check(day.counters[task] == 0, "XXL incorrect resource excluded");
                if (task == 37 || task == 38) {
                    for (int i = 0; i < target - 1; i++) MetaProgression.RecordArtifact(new Vector3Int(i, -60, 0), "Artifact_03");
                } else MetaProgression.RecordResource(task == 39 ? Item.Ultronium : Item.Diamond, target - 1, "xxl-source");
                Check(day.counters[task] == target - 1 && !day.rewarded[task], "New XXL threshold not reached early");
                Check(MetaProgression.Save(), "New XXL partial progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("new-xxl");
                day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                Check(day.dailyGemType == 3 && day.dailyArtifactType == 2, "XXL selected types persist");
                if (task == 37 || task == 38) MetaProgression.RecordArtifact(new Vector3Int(0, -60, 0), "Artifact_03");
                else MetaProgression.RecordResource(task == 39 ? Item.Ultronium : Item.Diamond, target - 1, "xxl-source");
                Check(day.counters[task] == target - 1, "New XXL replay ignored after reload");
                if (task == 37 || task == 38) MetaProgression.RecordArtifact(new Vector3Int(target - 1, -60, 0), "Artifact_03");
                else MetaProgression.RecordResource(task == 39 ? Item.Ultronium : Item.Diamond, 1, "xxl-final");
                Check(day.rewarded[task] && day.awardedXp[task] == 3000, "New XXL completion pays 3000 XP");
                if (task == 37 || task == 38) MetaProgression.RecordArtifact(new Vector3Int(target, -60, 0), "Artifact_03");
                else MetaProgression.RecordResource(task == 39 ? Item.Ultronium : Item.Diamond, 100, "xxl-extra");
                Check(day.counters[task] == target && day.awardedXp[task] == 3000, "New XXL counter caps and reward once");
                Check(MetaProgression.Save(), "New XXL completion saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == goal.id).completed, "New XXL completion validates after reload");
            }
            var oldNewXxlProfile = new MetaProfile();
            var oldNewXxlDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[35], rewarded = new bool[35], dailyVariants = new int[35], awardedXp = new int[35],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 34 }, oreTargetVersion = 2 };
            oldNewXxlProfile.challengePeriods.Add(oldNewXxlDay);
            Check(Validate(oldNewXxlProfile) && oldNewXxlDay.counters.Length == 52 && oldNewXxlDay.dailyVariants.Length == 52 &&
                oldNewXxlDay.awardedXp.Length == 52 && oldNewXxlDay.dailySelection.Last() == 34, "Five XXL additions preserve existing daily selection");
            for (int craftTask = 40; craftTask <= 44; craftTask++) for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/CraftQuests-" + craftTask + "-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                day.dailySelection = new[] { 1, 2, 4, 5, 10, 20, 21, craftTask };
                day.dailyVariants[craftTask] = size;
                MetaProgression.BeginRun("craft-quests");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-" + metrics[craftTask]);
                int[] smallCraft = { 1, 2, 1, 1, 1 }, largeCraft = { 2, 5, 3, 2, 2 };
                int target = size == 0 ? smallCraft[craftTask - 40] : largeCraft[craftTask - 40];
                Check(goal.target == target && goal.xp == (size == 0 ? 500 : 1500), "Craft quest target and XP");
                MetaProgression.RecordCraftedAmount(Item.Copper, 100);
                MetaProgression.RecordCraftUpgrade(metrics[craftTask], 3, 3);
                MetaProgression.RecordCraftUpgrade(metrics[craftTask], 3, 2);
                Check(day.counters[craftTask] == 0, "Craft quest excludes ore and equal/lower upgrades");
                for (int i = 0; i < target; i++) {
                    if (craftTask == 41) MetaProgression.RecordCraftedAmount(i % 2 == 0 ? Item.Axe : Item.IronPickaxe, 1);
                    else if (craftTask == 42) MetaProgression.RecordCraftedAmount(new[] { Item.Backpack, Item.HeavyDutyBoots, Item.CrystalPendant }[i], 1);
                    else MetaProgression.RecordCraftUpgrade(metrics[craftTask], i + 1, i + 2);
                    Check(day.counters[craftTask] == i + 1 && day.rewarded[craftTask] == (i == target - 1), "Craft quest exact increments and threshold");
                }
                Check(day.awardedXp[craftTask] == goal.xp, "Craft quest exact payout");
                Check(MetaProgression.Save(), "Craft quest saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("craft-quests");
                day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                if (craftTask == 42) MetaProgression.RecordCraftedAmount(Item.Backpack, 1);
                else if (craftTask != 41) MetaProgression.RecordCraftUpgrade(metrics[craftTask], 1, 2);
                Check(day.counters[craftTask] == target && day.awardedXp[craftTask] == goal.xp && day.rewarded[craftTask], "Craft reward persists with replay ignored");
            }
            MetaProgression.EndRun();
            MetaProgression.TestDirectory = Path.GetFullPath("Temp/UpgradeReplay-" + DateTime.UtcNow.Ticks);
            MetaProgression.GetChallenges(ChallengePeriod.Daily);
            var upgradeDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            upgradeDay.dailySelection = new[] { 1, 2, 4, 5, 10, 20, 21, 40 };
            upgradeDay.dailyVariants[40] = 1;
            MetaProgression.BeginRun("upgrade-replay");
            var beforeUpgrade = MetaProgression.CaptureRunState();
            MetaProgression.RecordCraftUpgrade("pickaxe-upgrades", 1, 2);
            Check(upgradeDay.counters[40] == 1 && !upgradeDay.rewarded[40], "One upgrade does not complete large quest");
            MetaProgression.Save(); MetaProgression.Reload(); MetaProgression.BeginRun("upgrade-replay", beforeUpgrade);
            upgradeDay = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
            MetaProgression.RecordCraftUpgrade("pickaxe-upgrades", 1, 2);
            Check(upgradeDay.counters[40] == 1 && !upgradeDay.rewarded[40], "Old world replay cannot recount same upgrade");
            MetaProgression.RecordCraftUpgrade("pickaxe-upgrades", 2, 4);
            Check(upgradeDay.counters[40] == 2 && upgradeDay.rewarded[40] && upgradeDay.awardedXp[40] == 1500, "Jump in rank counts as one upgrade action");
            Check(MetaProgression.IsCraftTool(Item.Axe) && MetaProgression.IsCraftTool(Item.Scythe) && MetaProgression.IsCraftTool(Item.DiamondPickaxe) && !MetaProgression.IsCraftTool(Item.Torche), "Tools classification excludes consumables");
            Check(MetaProgression.IsCraftGear(Item.Backpack) && MetaProgression.IsCraftGear(Item.SpringGreaves) && MetaProgression.IsCraftGear(Item.CrystalHeart) && !MetaProgression.IsCraftGear(Item.CopperPickaxe), "Gear includes energy/carrying equipment and excludes pickaxes");
            for (int dayIndex = 0; dayIndex < 365; dayIndex++) {
                long stamp = new DateTime(2045, 1, 1).AddDays(dayIndex).Ticks;
                var noCraft = (int[])select.Invoke(null, new object[] { stamp, true, 5, new int[7] });
                Check(!noCraft.Any(i => i >= 40 && i <= 46) && (bool)valid.Invoke(null, new object[] { noCraft }), "No unavailable crafting goals in daily draw");
            }
            var oldCraftProfile = new MetaProfile();
            var oldCraftDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[40], rewarded = new bool[40], dailyVariants = new int[40], awardedXp = new int[40],
                dailySelection = new[] { 1, 2, 4, 5, 10, 13, 21, 39 }, oreTargetVersion = 2 };
            oldCraftProfile.challengePeriods.Add(oldCraftDay);
            Check(Validate(oldCraftProfile) && oldCraftDay.counters.Length == 52 && oldCraftDay.dailyVariants.Length == 52 && oldCraftDay.awardedXp.Length == 52 && oldCraftDay.dailySelection.Last() == 39, "Craft quest migration keeps prior XXL daily draw");
            for (int task = 45; task <= 46; task++) for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/ConsumableCraft-" + task + "-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                day.dailySelection = new[] { 1, 2, 4, 5, 10, 20, 21, task };
                day.dailyVariants[task] = size;
                MetaProgression.BeginRun("consumable-craft");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-" + metrics[task]);
                int target = task == 45 ? (size == 0 ? 5 : 20) : (size == 0 ? 3 : 10);
                Item item = task == 45 ? Item.Dynamite : Item.Medkit;
                Check(goal.target == target && goal.xp == (size == 0 ? 500 : 1500), "Dynamite/medkit variant targets and rewards");
                MetaProgression.RecordCraftedAmount(task == 45 ? Item.Medkit : Item.Dynamite, 100);
                Check(day.counters[task] == 0, "Other consumable does not count");
                MetaProgression.RecordCraftedAmount(item, target - 1);
                Check(day.counters[task] == target - 1 && !day.rewarded[task], "Batch quantity counts below threshold");
                Check(MetaProgression.Save(), "Consumable craft partial progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun("consumable-craft");
                day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                Check(day.counters[task] == target - 1, "Consumable craft progress survives reload");
                MetaProgression.RecordCraftedAmount(item, 1);
                Check(day.rewarded[task] && day.awardedXp[task] == goal.xp, "Consumable craft exact payout");
                MetaProgression.RecordCraftedAmount(item, 50);
                Check(day.counters[task] == target && day.awardedXp[task] == goal.xp, "Consumable craft reward only once");
                Check(MetaProgression.Save(), "Consumable craft completion saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == goal.id).completed, "Consumable craft completed profile validates");
            }
            var oldConsumableProfile = new MetaProfile();
            var oldConsumableDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[45], rewarded = new bool[45], dailyVariants = new int[45], awardedXp = new int[45],
                dailySelection = new[] { 1, 2, 4, 5, 10, 20, 21, 44 }, oreTargetVersion = 2 };
            oldConsumableProfile.challengePeriods.Add(oldConsumableDay);
            Check(Validate(oldConsumableProfile) && oldConsumableDay.counters.Length == 52 && oldConsumableDay.dailyVariants.Length == 52 && oldConsumableDay.awardedXp.Length == 52 && oldConsumableDay.dailySelection.Last() == 44, "Consumable quest migration keeps prior upgrade daily");
            for (int task = 47; task <= 51; task++) for (int size = 0; size < 2; size++) {
                MetaProgression.EndRun();
                MetaProgression.TestDirectory = Path.GetFullPath("Temp/ExploreGoals-" + task + "-" + size + "-" + DateTime.UtcNow.Ticks);
                MetaProgression.GetChallenges(ChallengePeriod.Daily);
                var day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                day.dailySelection = new[] { 1, 2, 4, 5, 13, 20, 21, task };
                day.dailyVariants[task] = size;
                MetaProgression.BeginRun("explore-goals");
                var goal = MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == "Daily-" + metrics[task]);
                int[] smallExplore = { 2, 250, 1, 2, 1 }, largeExplore = { 4, 1000, 2, 5, 1 };
                int target = size == 0 ? smallExplore[task - 47] : largeExplore[task - 47];
                Check(goal.target == target && goal.xp == (size == 0 ? 500 : 1500), "Exploration target and payout");
                if (task == 47) {
                    for (int i = 1; i < target; i++) MetaProgression.RecordExplorationPosition(i, i == 1);
                    MetaProgression.RecordExplorationPosition(1, true);
                    Check(day.counters[task] == target - 1, "Repeated layer counts once per day");
                } else if (task == 48) {
                    MetaProgression.RecordRevealedCell(1, 1, false);
                    MetaProgression.RecordRevealedCell(1, 1);
                    Check(day.counters[task] == 0, "Previously revealed baseline excluded");
                    for (int i = 2; i <= target; i++) MetaProgression.RecordRevealedCell(i, 1);
                } else if (task == 49) {
                    MetaProgression.RecordDiscovery("cache");
                    Check(day.counters[task] == 0, "Non-altar discovery excluded");
                    if (target > 1) { MetaProgression.RecordDiscovery("altar"); MetaProgression.RecordDiscovery("altar"); }
                } else if (task == 50) {
                    MetaProgression.RecordArtifactSight(new Vector3Int(-1, -20, 0), "Artifact_10", false);
                    MetaProgression.RecordArtifactSight(new Vector3Int(-1, -20, 0), "Artifact_10");
                    Check(day.counters[task] == 0, "Known artifact sight excluded");
                    for (int i = 1; i < target; i++) MetaProgression.RecordArtifactSight(new Vector3Int(i, -20, 0), "Artifact_" + i.ToString("D2"));
                    MetaProgression.RecordArtifactSight(new Vector3Int(100, -20, 0), "Artifact_01");
                    Check(day.counters[task] == target - 1, "Duplicate artifact type ignored");
                } else {
                    int layer = size == 0 ? 3 : 5;
                    MetaProgression.RecordExplorationPosition(layer, false);
                    Check(day.counters[task] == 0, "Expedition must start at surface");
                    MetaProgression.RecordExplorationPosition(1, true);
                    MetaProgression.RecordExplorationPosition(layer - 1, false);
                    MetaProgression.RecordExplorationPosition(1, true);
                    Check(day.counters[task] == 0, "Shallow expedition does not complete");
                    MetaProgression.RecordExplorationPosition(layer, false);
                }
                Check(!day.rewarded[task], "Exploration not completed early");
                string activeRun = MetaProgression.CurrentRun.runId;
                var oldWorld = MetaProgression.CaptureRunState();
                Check(MetaProgression.Save(), "Exploration partial progress saved");
                MetaProgression.Reload(); MetaProgression.BeginRun(activeRun, oldWorld);
                day = MetaProgression.Profile.challengePeriods.Single(p => p.period == 0);
                if (task == 47) MetaProgression.RecordExplorationPosition(target, false);
                else if (task == 48) { MetaProgression.RecordRevealedCell(2, 1); Check(day.counters[task] == target - 1, "Revealed cell replay ignored after reload"); MetaProgression.RecordRevealedCell(9999, 1); }
                else if (task == 49) {
                    if (target > 1) { MetaProgression.RecordDiscovery("altar"); Check(day.counters[task] == 1, "Same altar cannot count twice after reload"); MetaProgression.EndRun(); MetaProgression.BeginRun("second-altar-world"); }
                    MetaProgression.RecordDiscovery("altar");
                } else if (task == 50) MetaProgression.RecordArtifactSight(new Vector3Int(target, -20, 0), "Artifact_" + target.ToString("D2"));
                else MetaProgression.RecordExplorationPosition(1, true);
                Check(day.rewarded[task] && day.awardedXp[task] == goal.xp && day.counters[task] == target, "Exploration completion exact reward");
                Check(MetaProgression.Save(), "Exploration completion saved");
                MetaProgression.Reload();
                Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).Single(c => c.id == goal.id).completed, "Exploration completed profile validates");
            }
            for (int dayIndex = 0; dayIndex < 365; dayIndex++) {
                long stamp = new DateTime(2050, 1, 1).AddDays(dayIndex).Ticks;
                var early = (int[])select.Invoke(null, new object[] { stamp, true, 1, new[] { 2, 5, 3, 2, 2, 20, 10 } });
                Check(!early.Contains(47) && !early.Contains(50) && !early.Contains(51), "Exploration layer gates before reaching depth");
            }
            var oldExploreProfile = new MetaProfile();
            var oldExploreDay = new MetaChallengePeriodState { period = 0, startUtc = 1, endUtc = 2,
                counters = new int[47], rewarded = new bool[47], dailyVariants = new int[47], awardedXp = new int[47],
                dailySelection = new[] { 1, 2, 4, 5, 10, 20, 21, 46 }, oreTargetVersion = 2 };
            oldExploreProfile.challengePeriods.Add(oldExploreDay);
            Check(Validate(oldExploreProfile) && oldExploreDay.counters.Length == 52 && oldExploreDay.dailyVariants.Length == 52 && oldExploreDay.awardedXp.Length == 52 && oldExploreDay.dailySelection.Last() == 46, "Exploration migration preserves active medkit quest");
            File.WriteAllText("Temp/DailyChallengeSelectionChecks.txt", "PASS " + checks + " checks\n730 days at both eligibility levels; persistence, event rewards, rollover, legacy migration\n");
            return "PASS " + checks + " checks";
        } finally {
            MetaProgression.EndRun();
            MetaProgression.TestDirectory = previous;
            MetaProgression.ChallengeTestUtc = clock;
        }
    }
}
