using System;
using System.Collections.Generic;
using System.Linq;



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

public static partial class MetaProgression
{
    // Override is deliberately restricted to isolated profiles.
    public static DateTime? ChallengeTestUtc { get; set; }
    // Append only: persisted counter positions are stable. Distinct IDs also allow tiered goals.
    static readonly string[] PeriodIds = { "resources", "depth", "crafted", "copper", "sales", "torches",
        "coal", "iron", "silver", "gold", "regions", "discoveries", "exotics", "ladders", "large-haul", "production",
        "platinum", "titanium", "obsidian", "big-sales", "trees", "grass", "surface-blocks", "artifacts", "specific-artifact", "l2-blocks", "l3-blocks", "l4-xxl", "l5-xxl", "specific-ore-xxl", "gems", "garnet", "ruby", "emerald", "diamond", "gems-xxl", "specific-gem-xxl", "artifacts-xxl", "specific-artifact-xxl", "ultronium-xxl", "pickaxe-upgrades", "tools-crafted", "gear-crafted", "energy-upgrades", "carrying-upgrades", "dynamite-crafted", "medkits-crafted", "visited-layers", "revealed-cells", "altars-found", "artifact-variety", "round-trip" };
    static readonly string[] PeriodMetrics = { "resources", "depth", "crafted", "copper", "sales", "torches",
        "coal", "iron", "silver", "gold", "regions", "discoveries", "exotics", "ladders", "resources", "crafted",
        "platinum", "titanium", "obsidian", "sales", "trees", "grass", "surface-blocks", "artifacts", "specific-artifact", "l2-blocks", "l3-blocks", "l4-xxl", "l5-xxl", "specific-ore-xxl", "gems", "garnet", "ruby", "emerald", "diamond", "gems-xxl", "specific-gem-xxl", "artifacts-xxl", "specific-artifact-xxl", "ultronium-xxl", "pickaxe-upgrades", "tools-crafted", "gear-crafted", "energy-upgrades", "carrying-upgrades", "dynamite-crafted", "medkits-crafted", "visited-layers", "revealed-cells", "altars-found", "artifact-variety", "round-trip" };
    static readonly string[] DailyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "Schwarze Adern", "Eisenherz", "Silbermond", "Goldfieber", "Unbetretene Pfade", "Geheimnisse im Stein", "Fremde Künste", "Himmelsleiter", "Große Ausbeute", "Feuer der Schmiede",
        "Platinglanz", "Titankraft", "Obsidianfluch", "Goldener Handschlag", "Ruf der Wälder", "Grüne Ernte", "Unter der Grasnarbe", "Relikte der Vorzeit", "Spuren der Vorzeit", "Steinernes Erwachen", "In der Bergesmitte",
        "XXL · Herz des Berges", "XXL · Ruf des Abgrunds", "XXL · Erzrausch", "Funkelnde Beute", "Glut des Granats", "Rubinfeuer", "Smaragdtraum", "Diamantenglanz",
        "XXL · Edelsteinrausch", "XXL · Kristalljagd", "XXL · Reliktenjäger", "XXL · Reliktvermächtnis", "XXL · Ultroniumrausch", "Die nächste Klinge", "Werkzeugschmiede", "Für den Aufbruch", "Funken des Lebens", "Schultern aus Stahl", "Donner der Schmiede", "Heilende Vorräte", "Grenzgänger", "Verborgene Weiten", "Ruf des Altars", "Spuren der Alten", "Licht und Tiefe" };
    static readonly string[] WeeklyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "König der Kohle", "Eiserne Dynastie", "Silberne Träume", "Goldene Ära", "Jenseits der Karten", "Geheimnisse des Berges", "Fremde Künste", "Wege zum Himmel", "Große Ausbeute", "Feuer der Schmiede" };
    static readonly int[] DailyTargets = { 50, 100, 5, 25, 1000, 3, 25, 25, 25, 25, 6, 1, 1, 5, 250, 15, 25, 25, 30, 2500, 10, 25, 150, 5, 3, 200, 250, 2000, 2000, 500, 30, 25, 25, 25, 25, 300, 150, 75, 25, 100, 1, 2, 1, 1, 1, 5, 3, 2, 250, 1, 2, 1 };
    static readonly int[] WeeklyTargets = { 500, 750, 25, 150, 10000, 20, 300, 200, 80, 50, 50, 5, 3, 30, 2500, 100 };
    // Keep historical payouts for migration; changing today's rewards must not invalidate earned XP.
    static readonly int[] LegacyDailyRewards = { 150, 200, 150, 100, 200, 100, 100, 150, 180, 200, 180, 200, 300, 120, 350, 250, 220, 240, 250, 350, 500, 500, 500, 500, 500, 500, 500, 3000, 3000, 3000, 500, 500, 500, 500, 500, 3000, 3000, 3000, 3000, 3000, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500 };
    static readonly int[] DailyLargeTargets = { 250, 300, 15, 100, 5000, 15, 100, 100, 100, 100, 18, 3, 3, 25, 1250, 45, 100, 100, 100, 7500, 40, 100, 500, 25, 10, 600, 750, 2000, 2000, 500, 100, 100, 100, 100, 100, 300, 150, 75, 25, 100, 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
    static readonly string[] SpecificArtifactNames = { "Sonnenscheibe", "Alter Kompass", "Jademaske", "Bernsteinskarabäus", "Astrolabium", "Antike Vase", "Siegelring", "Fossile Spirale", "Kupfertafel", "Kristall-Sanduhr" };
    static readonly Item[] XxlOreItems = { Item.Coal, Item.Copper, Item.Iron, Item.Silver, Item.Gold, Item.Platinum, Item.Titanium, Item.Obsidian };
    static readonly string[] XxlOreMetrics = { "coal", "copper", "iron", "silver", "gold", "platinum", "titanium", "obsidian" };
    static readonly string[] XxlOreNames = { "Kohle", "Kupfer", "Eisen", "Silber", "Gold", "Platin", "Titan", "Obsidian" };
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
    static readonly int[] SpecificOreIndices = { 6, 3, 7, 8, 9, 16, 17, 18 };
    static readonly int[] PreviousOreSmall = { 30, 20, 20, 8, 5, 5, 5, 8 }, PreviousOreLarge = { 150, 100, 100, 24, 15, 15, 15, 24 };
    static readonly int[] OreUnlockLayers = { 1, 1, 1, 2, 3, 4, 3, 4 };
    static readonly Item[] GemItems = { Item.OrangeGarnet, Item.Ruby, Item.Emerald, Item.Diamond };
    static readonly string[] GemMetrics = { "garnet", "ruby", "emerald", "diamond" };
    static readonly string[] GemNames = { "Granat", "Rubin", "Smaragd", "Diamant" };
    static readonly int[] GemUnlockLayers = { 2, 4, 4, 4 };
    static void RefreshOreUnlockLayers()
    {
        if (!string.IsNullOrWhiteSpace(TestDirectory)) return;
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) return;
#endif
        var settings = GpsSettings.Document.records.Find(record => record.type == typeof(MapGenerator).AssemblyQualifiedName)
            ?.fields.Find(field => field.name == "oreSettings")?.children;
        if (settings == null) return;
        var blocks = GpsSettings.Profile.assets.Select(entry => entry.asset).OfType<Block>().ToArray();
        for (int ore = 0; ore < XxlOreItems.Length + GemItems.Length; ore++) {
            var block = blocks.FirstOrDefault(candidate => candidate.itemDrop && candidate.itemDrop.item == (ore < XxlOreItems.Length ? XxlOreItems[ore] : GemItems[ore - XxlOreItems.Length]));
            if (!block) continue;
            var distribution = settings.Find(entry => entry.children.Find(field => field.name == "ore")?.number == (int)block.id);
            var layers = distribution?.children.Find(field => field.name == "layerIndices")?.children;
            if (layers == null || layers.Count == 0) continue;
            int firstLayer = Math.Max(1, (int)layers.Min(layer => layer.number) + 1);
            if (ore < XxlOreItems.Length) OreUnlockLayers[ore] = firstLayer;
            else GemUnlockLayers[ore - XxlOreItems.Length] = firstLayer;
        }
    }
    static bool OreEligible(int index, int reachedLayer)
    {
        if (index >= 31 && index <= 34) {
            int firstLayer = GemUnlockLayers[index - 31];
            return firstLayer < 4 || reachedLayer >= firstLayer;
        }
        int ore = Array.IndexOf(SpecificOreIndices, index);
        return ore < 0 || OreUnlockLayers[ore] < 4 || reachedLayer >= OreUnlockLayers[ore];
    }
    static void PreserveCompletedHarvests(MetaChallengePeriodState state)
    {
        if (state.period != 0) return;
        for (int i = 20; i <= 21; i++) {
            int previous = i == 20 ? (state.dailyVariants[i] == 1 ? 10 : 3) : (state.dailyVariants[i] == 1 ? 60 : 20);
            if (state.rewarded[i] && state.counters[i] == previous) state.counters[i] = DailyTarget(state, i);
        }
    }
    static readonly int[] WeeklyRewards = { 600, 1000, 800, 500, 1000, 500, 400, 600, 750, 900, 800, 1000, 1400, 500, 2000, 1600 };

    // Mining, crafting, exploration, trade/special. Pool positions are append-only.
    static readonly int[] DailyGroups = { 0, 2, 1, 0, 3, 1, 0, 0, 0, 0, 2, 2, 3, 1, 0, 1, 0, 0, 0, 3, 0, 0, 0, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 };
    static readonly int[] DailyMinimum = { 2, 1, 1, 1 }, DailyMaximum = { 4, 3, 2, 2 };

    static int[] SelectDaily(long startUtc, bool allowExotics, int reachedLayer, int[] craftLimits)
    {
        var random = new System.Random(unchecked((int)(startUtc ^ (startUtc >> 32))));
        var pools = Enumerable.Range(0, 4).Select(group => Enumerable.Range(0, PeriodIds.Length)
            .Where(i => i != 14 && (i < 40 || i > 46 || craftLimits[i - 40] >= DailyTargets[i]) && !IsXxl(i) && ExplorationEligible(i, reachedLayer) && OreEligible(i, reachedLayer) && DailyGroups[i] == group && (i != 12 || allowExotics)).ToList()).ToArray();
        // Enumerate feasible mixes before drawing: low-level profiles have only one trade slot.
        var mixes = new List<int[]>();
        for (int mining = 2; mining <= 4; mining++)
        for (int crafting = 1; crafting <= 3; crafting++)
        for (int exploration = 1; exploration <= 2; exploration++)
        for (int special = 1; special <= (allowExotics ? 2 : 1); special++)
            if (mining + crafting + exploration + special == 8)
                mixes.Add(new[] { mining, crafting, exploration, special });
        var mix = mixes[random.Next(mixes.Count)];
        var selection = new List<int>();
        for (int group = 0; group < pools.Length; group++) {
            var pool = pools[group];
            for (int count = 0; count < mix[group]; count++) {
                int picked = pool[random.Next(pool.Count)];
                selection.Add(picked);
                // Alternate tiers of the same action never appear together.
                pool.RemoveAll(i => PeriodMetrics[i] == PeriodMetrics[picked]);
            }
        }
        return selection.OrderBy(i => i).ToArray();
    }

    static bool ValidDailySelection(int[] selection)
    {
        if (selection == null || selection.Length != 8 || selection.Any(i => i < 0 || i >= PeriodIds.Length) ||
            selection.Count(IsXxl) > 1 || selection.Distinct().Count() != 8 || selection.Select(i => PeriodMetrics[i]).Distinct().Count() != 8) return false;
        for (int group = 0; group < 4; group++) {
            int count = selection.Count(i => DailyGroups[i] == group);
            if (count < DailyMinimum[group] || count > DailyMaximum[group]) return false;
        }
        return true;
    }

    static void EnsureDailySelection(MetaChallengePeriodState state)
    {
        if (state.period != (int)ChallengePeriod.Daily) return;
        // Existing periods keep their original (small) targets when variants are introduced.
        if (state.dailySelection != null) {
            if (state.dailyVariants == null || state.dailyVariants.Length == 0) state.dailyVariants = new int[PeriodMetrics.Length];
            return;
        }
        state.dailyArtifactType = new System.Random(unchecked((int)(state.startUtc ^ (state.startUtc >> 32) ^ 0x415254))).Next(10);
        ImportReachedMiningLayers();
        RefreshOreUnlockLayers();
        var craftLimits = CraftChallengeLimits();
        state.dailySelection = SelectXxl(SelectDaily(state.startUtc, Level >= 5, profile.reachedMiningLayer, craftLimits), state.startUtc, profile.reachedMiningLayer);
        state.dailyOreType = SelectXxlOreType(state.dailySelection, state.startUtc, profile.reachedMiningLayer);
        state.dailyGemType = SelectXxlGemType(state.dailySelection, state.startUtc, profile.reachedMiningLayer);
        if (state.dailyVariants == null || state.dailyVariants.Length == 0) {
            state.dailyVariants = new int[PeriodMetrics.Length];
            var random = new System.Random(unchecked((int)(state.startUtc ^ (state.startUtc >> 32) ^ 0x53495A45)));
            foreach (int index in state.dailySelection) {
                state.dailyVariants[index] = random.Next(2);
                if (index >= 40 && index <= 46 && craftLimits[index - 40] < DailyLargeTargets[index]) state.dailyVariants[index] = 0;
                if ((index == 47 && profile.reachedMiningLayer < 4) || (index == 50 && profile.reachedMiningLayer < 3) || (index == 51 && profile.reachedMiningLayer < 5)) state.dailyVariants[index] = 0;
            }
        }
        if (!writeBlocked) { MarkDirty(); RequestSave(); }
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

    // Period arrays are append-only, so every length from the first stored format up to the current
    // pool is a valid older profile. Appending a task therefore never needs a whitelist update.
    static bool IsStoredPeriodLength(int length, int firstFormatLength) => length >= firstFormatLength && length <= PeriodMetrics.Length;

    static DateTime ChallengeNow()
    {
        DateTime now = !string.IsNullOrWhiteSpace(TestDirectory) && ChallengeTestUtc.HasValue
            ? DateTime.SpecifyKind(ChallengeTestUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        // Moving the OS clock backwards must not reopen an already rewarded period.
        return new DateTime(Math.Max(now.Ticks, profile.challengeClockUtc), DateTimeKind.Utc);
    }

    static MetaChallengePeriodState PeriodState(ChallengePeriod period)
    {
        EnsureLoaded();
        profile.challengePeriods ??= new List<MetaChallengePeriodState>();
        DateTime now = ChallengeNow();
        var state = profile.challengePeriods.Find(p => p.period == (int)period);
        if (state != null && now.Ticks < state.endUtc) { EnsureAwardLedger(state); EnsureDailySelection(state); PreserveCompletedHarvests(state); return state; }
        var date = now.ToLocalTime().Date;
        if (period == ChallengePeriod.Weekly) date = date.AddDays(-((int)date.DayOfWeek + 6) % 7);
        var next = new MetaChallengePeriodState {
            oreTargetVersion = 2, period = (int)period, startUtc = date.ToUniversalTime().Ticks,
            endUtc = date.AddDays(period == ChallengePeriod.Weekly ? 7 : 1).ToUniversalTime().Ticks,
            awardedXp = new int[PeriodMetrics.Length]
        };
        if (!writeBlocked) {
            if (state != null) profile.challengePeriods.Remove(state);
            profile.challengePeriods.Add(next);
            profile.challengeClockUtc = now.Ticks;
            MarkDirty();
        }
        EnsureDailySelection(next);
        return next;
    }

    public static TimeSpan ChallengeRemaining(ChallengePeriod period)
    {
        if (period == ChallengePeriod.Lifetime) return TimeSpan.Zero;
        var state = PeriodState(period);
        return TimeSpan.FromTicks(Math.Max(0, state.endUtc - ChallengeNow().Ticks));
    }

    public static MetaChallengeProgress[] GetChallenges(ChallengePeriod period)
    {
        if (period == ChallengePeriod.Lifetime)
            return GetChallenges().Where(c => c.permanent).ToArray();
        var state = PeriodState(period);
        var targets = period == ChallengePeriod.Daily ? DailyTargets : WeeklyTargets;
        var names = period == ChallengePeriod.Daily ? DailyNames : WeeklyNames;
        var indices = period == ChallengePeriod.Daily ? state.dailySelection : Enumerable.Range(0, WeeklyTargets.Length);
        return indices.Select(i => new MetaChallengeProgress {
            id = period + "-" + PeriodIds[i], metric = period == ChallengePeriod.Daily && i == 29 ? XxlOreMetrics[state.dailyOreType] : period == ChallengePeriod.Daily && i == 36 ? GemMetrics[state.dailyGemType] : PeriodMetrics[i], name = names[i],
            subject = period == ChallengePeriod.Daily && (i == 24 || i == 38) ? SpecificArtifactNames[state.dailyArtifactType] : period == ChallengePeriod.Daily && i == 51 ? (state.dailyVariants[i] == 1 ? "L5 und zurück" : "L3 und zurück") : null,
            current = state.counters[i], target = period == ChallengePeriod.Daily ? DailyTarget(state, i) : targets[i],
            xp = period == ChallengePeriod.Daily ? DailyReward(state, i) : WeeklyRewards[i], completed = state.rewarded[i]
        }).ToArray();
    }

    static void RecordPeriodProgress(string metric, int amount)
    {
        if (amount <= 0 || writeBlocked) return;
        foreach (var period in new[] { ChallengePeriod.Daily, ChallengePeriod.Weekly }) {
            var state = PeriodState(period);
            var indices = period == ChallengePeriod.Daily ? state.dailySelection : Enumerable.Range(0, WeeklyTargets.Length);
            foreach (int index in indices) {
            if (PeriodMetrics[index] != metric) continue;
            int target = period == ChallengePeriod.Daily ? DailyTarget(state, index) : WeeklyTargets[index];
            state.counters[index] = (int)Math.Min(target, state.counters[index] + (long)amount);
            if (state.counters[index] == target && !state.rewarded[index]) {
                state.rewarded[index] = true;
                state.awardedXp[index] = period == ChallengePeriod.Daily ? DailyReward(state, index) : WeeklyRewards[index];
                profile.periodRewardXp += state.awardedXp[index];
                milestonePending = true;
            }
            }
        }
        profile.challengeClockUtc = ChallengeNow().Ticks;
    }

    public static bool IsCraftTool(Item item) => item == Item.Axe || item == Item.Scythe || item == Item.PercussionHammer ||
        ((int)item >= (int)Item.CopperPickaxe && (int)item <= (int)Item.DiamondPickaxe);
    public static bool IsCraftGear(Item item) => item == Item.Backpack || item == Item.LoadBelt || item == Item.ReinforcedBackpack ||
        item == Item.HeavyDutyBoots || item == Item.SpringGreaves || item == Item.LoadFrame || item == Item.Exoskeleton ||
        item == Item.CrystalPendant || item == Item.CopperEnergyBracelet || item == Item.EnergyStorageVial || item == Item.RuneBelt ||
        item == Item.CrystalHeart || item == Item.CrystalHarness || item == Item.TravelMonolith;

    static int[] CraftChallengeLimits()
    {
        if (!string.IsNullOrWhiteSpace(TestDirectory)) return new[] { 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
        var catalog = UnityEngine.Resources.Load<CraftingCatalog>("CraftingCatalog");
        var upgrades = UnityEngine.Resources.Load<UpgradeSettings>("UpgradeSettings");
        var inventory = InventoryManager.Instance;
        var limits = new int[7];
        if (!catalog || !upgrades) return limits;
        int pickaxe = inventory ? inventory.EquippedPickaxeLevel : 1;
        int energy = inventory ? inventory.EnergyCapacityLevel : 1;
        int carrying = inventory ? inventory.CarryingCapacityLevel : 1;
        var pickaxeRanks = new HashSet<int>(); var energyRanks = new HashSet<int>(); var carryingRanks = new HashSet<int>();
        foreach (var recipe in catalog.recipes) {
            if (!recipe || !recipe.output || !RecipeUnlocks.IsUnlocked(recipe) || (inventory && inventory.IsPowerupUnlocked(recipe.output))) continue;
            var item = recipe.output;
            if (item.item == Item.Dynamite) limits[5] = 20;
            if (item.item == Item.Medkit) limits[6] = 10;
            if (IsCraftTool(item.item)) limits[1] += item.category == ItemCategory.Powerup ? 1 : 5;
            if (IsCraftGear(item.item)) limits[2]++;
            for (int i = 0; i < upgrades.PickaxeLevelCount; i++)
                if (upgrades.pickaxeLevels[i]?.pickaxe == item && i + 1 > pickaxe) pickaxeRanks.Add(i + 1);
            if (item.energyCapacityUpgradeLevel > energy && item.energyCapacityUpgradeLevel <= upgrades.EnergyCapacityLevelCount) energyRanks.Add(item.energyCapacityUpgradeLevel);
            if (item.carryingCapacityUpgradeLevel > carrying && item.carryingCapacityUpgradeLevel <= upgrades.CarryingCapacityLevelCount) carryingRanks.Add(item.carryingCapacityUpgradeLevel);
        }
        limits[0] = pickaxeRanks.Count; limits[3] = energyRanks.Count; limits[4] = carryingRanks.Count;
        return limits;
    }

    public static void RecordCraftUpgrade(string metric, int previousLevel, int newLevel)
    {
        if (!HasRun() || run.ended || newLevel <= previousLevel || previousLevel < 0 || newLevel > 100 ||
            (metric != "pickaxe-upgrades" && metric != "energy-upgrades" && metric != "carrying-upgrades")) return;
        string key = "craft-upgrade:" + metric + ":" + newLevel;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress(metric, 1); MarkChanged(false);
    }
    public static void RecordCraftedAmount(Item item, int amount, bool exotic = false, int crafts = 0)
    {
        if (!HasRun() || run.ended || amount <= 0) return;
        RecordPeriodProgress("crafted", amount);
        if (IsCraftTool(item)) RecordPeriodProgress("tools-crafted", amount);
        if (IsCraftGear(item) && sources.Add("gear-crafted:" + (int)item)) {
            run.resourceSources.Add("gear-crafted:" + (int)item);
            RecordPeriodProgress("gear-crafted", 1);
        }
        if (item == Item.Dynamite) RecordPeriodProgress("dynamite-crafted", amount);
        if (item == Item.Medkit) RecordPeriodProgress("medkits-crafted", amount);
        if (item == Item.Torche) RecordPeriodProgress("torches", amount);
        if (item == Item.Ladder || item == Item.IronLadder) RecordPeriodProgress("ladders", amount);
        if (exotic) RecordPeriodProgress("exotics", amount);
        MarkChanged(false);
    }

    public static void RecordSale(int proceeds)
    {
        if (!HasRun() || run.ended || proceeds <= 0) return;
        RecordPeriodProgress("sales", proceeds); MarkChanged(false);
    }

    public static void RecordSurfaceHarvest(string metric, string sourceId)
    {
        if (!HasRun() || run.ended || (metric != "trees" && metric != "grass") ||
            string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128) return;
        string key = "harvest:" + metric + ":" + sourceId;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress(metric, 1);
        MarkChanged(false);
    }

    public static void RecordSurfaceBlock(UnityEngine.Vector3Int cell)
    {
        if (!HasRun() || run.ended) return;
        string key = "surface-block:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress("surface-blocks", 1);
        MarkChanged(false);
    }

    public static void RecordArtifact(UnityEngine.Vector3Int cell, string artifactId = null)
    {
        if (!HasRun() || run.ended) return;
        string key = "artifact-mined:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordArtifactSight(cell, artifactId);
        RecordPeriodProgress("artifacts", 1);
        RecordPeriodProgress("artifacts-xxl", 1);
        var day = PeriodState(ChallengePeriod.Daily);
        if (artifactId == "Artifact_" + (day.dailyArtifactType + 1).ToString("D2"))
        {
            RecordPeriodProgress("specific-artifact", 1);
            RecordPeriodProgress("specific-artifact-xxl", 1);
        }
        MarkChanged(false);
    }
    public static void RecordLayerTwoBlock(UnityEngine.Vector3Int cell)
    {
        if (!HasRun() || run.ended) return;
        string key = "l2-block:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress("l2-blocks", 1);
        MarkChanged(false);
    }
    public static void RecordLayerThreeBlock(UnityEngine.Vector3Int cell)
    {
        if (!HasRun() || run.ended) return;
        string key = "l3-block:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress("l3-blocks", 1);
        MarkChanged(false);
    }
    static int[] SelectXxl(int[] selection, long startUtc, int reachedLayer)
    {
        if (reachedLayer < 4) return selection;
        var random = new System.Random(unchecked((int)(startUtc ^ (startUtc >> 32) ^ 0x58584C)));
        if (random.Next(100) >= 5) return selection;
        var result = (int[])selection.Clone();
        var mining = Enumerable.Range(0, result.Length).Where(i => DailyGroups[result[i]] == 0).ToArray();
        var choices = reachedLayer >= 5 ? new[] { 27, 28, 29, 35, 36, 37, 38, 39 } : new[] { 27, 29, 35, 36, 37, 38 };
        result[mining[random.Next(mining.Length)]] = choices[random.Next(choices.Length)];
        return result.OrderBy(i => i).ToArray();
    }

    static int SelectXxlOreType(int[] selection, long startUtc, int reachedLayer)
    {
        var eligible = Enumerable.Range(0, XxlOreItems.Length)
            .Where(i => (OreUnlockLayers[i] < 4 || reachedLayer >= OreUnlockLayers[i]) && !selection.Any(task => PeriodMetrics[task] == XxlOreMetrics[i])).ToArray();
        var random = new System.Random(unchecked((int)(startUtc ^ (startUtc >> 32) ^ 0x4F5245)));
        return eligible[random.Next(eligible.Length)];
    }

    static int SelectXxlGemType(int[] selection, long startUtc, int reachedLayer)
    {
        var eligible = Enumerable.Range(0, GemItems.Length)
            .Where(i => (GemUnlockLayers[i] < 4 || reachedLayer >= GemUnlockLayers[i]) && !selection.Any(task => PeriodMetrics[task] == GemMetrics[i])).ToArray();
        if (eligible.Length == 0) eligible = Enumerable.Range(0, GemItems.Length)
            .Where(i => GemUnlockLayers[i] < 4 || reachedLayer >= GemUnlockLayers[i]).ToArray();
        if (eligible.Length == 0) return 0;
        var random = new System.Random(unchecked((int)(startUtc ^ (startUtc >> 32) ^ 0x47454D)));
        return eligible[random.Next(eligible.Length)];
    }

    static void RecordXxlGem(Item item, int amount)
    {
        var day = PeriodState(ChallengePeriod.Daily);
        if (item == GemItems[day.dailyGemType]) RecordPeriodProgress("specific-gem-xxl", amount);
    }
    static void RecordSpecificXxlOre(Item item, int amount)
    {
        var day = PeriodState(ChallengePeriod.Daily);
        if (item == XxlOreItems[day.dailyOreType]) RecordPeriodProgress("specific-ore-xxl", amount);
    }
    static void ImportReachedMiningLayers()
    {
        // Isolated profiles never inspect user save slots.
        if (writeBlocked || !string.IsNullOrWhiteSpace(TestDirectory)) return;
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) return;
#endif
        int reached = profile.reachedMiningLayer;
        foreach (int slot in GameSaveSystem.OccupiedSlots()) reached = Math.Max(reached, GameSaveSystem.GetHomeLayer(slot));
        if (reached > profile.reachedMiningLayer) { profile.reachedMiningLayer = reached; MarkDirty(); }
    }

    static bool ExplorationEligible(int index, int reachedLayer) => index == 47 || index == 50 ? reachedLayer >= 2 : index != 51 || reachedLayer >= 3;

    public static void RecordRevealedCell(int x, int depth, bool award = true)
    {
        if (!HasRun() || run.ended || depth <= 0 || depth > 100000 || x < 0 || x > 100000) return;
        string key = "revealed:" + x + ":" + depth;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        if (award) { RecordPeriodProgress("revealed-cells", 1); MarkChanged(false); }
        else MarkDirty();
    }

    public static void RecordExplorationPosition(int layer, bool atSurface)
    {
        if (!HasRun() || run.ended || layer < 1 || layer > 100) return;
        var day = PeriodState(ChallengePeriod.Daily);
        int previousMask = day.visitedLayerMask, previousPeak = day.expeditionPeakLayer;
        string previousRun = day.expeditionRunId;
        if (day.dailySelection.Contains(47) && layer <= 5) {
            int bit = 1 << (layer - 1);
            if ((day.visitedLayerMask & bit) == 0) {
                day.visitedLayerMask |= bit;
                RecordPeriodProgress("visited-layers", 1);
            }
        }
        if (day.dailySelection.Contains(51)) {
            if (day.expeditionRunId != run.runId) {
                day.expeditionRunId = null; day.expeditionPeakLayer = 0;
            }
            if (atSurface) {
                int required = day.dailyVariants[51] == 1 ? 5 : 3;
                if (day.expeditionRunId == run.runId && day.expeditionPeakLayer >= required) RecordPeriodProgress("round-trip", 1);
                day.expeditionRunId = run.runId; day.expeditionPeakLayer = 1;
            } else if (day.expeditionRunId == run.runId) day.expeditionPeakLayer = Math.Max(day.expeditionPeakLayer, layer);
        }
        if (previousMask != day.visitedLayerMask || previousPeak != day.expeditionPeakLayer || previousRun != day.expeditionRunId) MarkChanged(false);
    }

    public static void RecordArtifactSight(UnityEngine.Vector3Int cell, string artifactId, bool award = true)
    {
        if (!HasRun() || run.ended || string.IsNullOrEmpty(artifactId)) return;
        string key = "artifact-sight:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        if (award) { RecordArtifactVariety(artifactId); MarkChanged(false); }
        else MarkDirty();
    }
    static void RecordArtifactVariety(string artifactId)
    {
        if (string.IsNullOrEmpty(artifactId) || !artifactId.StartsWith("Artifact_", StringComparison.Ordinal) ||
            !int.TryParse(artifactId.Substring(9), out int type) || type < 1 || type > 10) return;
        var day = PeriodState(ChallengePeriod.Daily);
        if (!day.dailySelection.Contains(50)) return;
        int bit = 1 << (type - 1);
        if ((day.artifactVarietyMask & bit) != 0) return;
        day.artifactVarietyMask |= bit;
        RecordPeriodProgress("artifact-variety", 1);
    }
    public static void RecordReachedMiningLayer(int layer)
    {
        if (!HasRun() || run.ended || layer < 1 || layer > 100 || layer <= profile.reachedMiningLayer) return;
        profile.reachedMiningLayer = layer;
        MarkChanged(false);
    }

    public static void RecordXxlLayerBlock(UnityEngine.Vector3Int cell, int layer)
    {
        if (!HasRun() || run.ended || (layer != 4 && layer != 5)) return;
        string key = "xxl-layer:" + layer + ":" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordPeriodProgress(layer == 4 ? "l4-xxl" : "l5-xxl", 1);
        MarkChanged(false);
    }
    static bool ValidatePeriods(MetaProfile value)
    {
        if (value.periodRewardXp < 0 || value.periodRewardXp > 1000000000000L ||
            value.challengeClockUtc < 0 || value.challengeClockUtc > DateTime.MaxValue.Ticks) return false;
        value.challengePeriods ??= new List<MetaChallengePeriodState>();
        if (value.challengePeriods.Count > 2 || value.challengePeriods.Select(p => p?.period).Distinct().Count() != value.challengePeriods.Count) return false;
        long activeRewards = 0;
        foreach (var state in value.challengePeriods) {
            if (state == null || state.visitedLayerMask < 0 || state.visitedLayerMask > 31 || state.artifactVarietyMask < 0 || state.artifactVarietyMask > 1023 || state.expeditionPeakLayer < 0 || state.expeditionPeakLayer > 100 || (state.expeditionRunId != null && state.expeditionRunId.Length > 160) || state.oreTargetVersion < 0 || state.oreTargetVersion > 2 || state.dailyGemType < 0 || state.dailyGemType >= GemItems.Length || state.dailyOreType < 0 || state.dailyOreType >= XxlOreItems.Length || state.dailyArtifactType < 0 || state.dailyArtifactType >= 10 || state.period < 0 || state.period > 1 || state.startUtc < 0 ||
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
                    activeRewards += paid;
                }
            }
        }
        return activeRewards <= value.periodRewardXp;
    }
}
