using System;
using UnityEngine;

// Daily, weekly and monthly rounds drawn from ChallengeCatalog, new gameplay counters and lifetime milestone stages.
public static partial class MetaProgression
{
    public static ChallengeSettings ChallengeSettingsOverride { get; set; }
    public static TimeZoneInfo ChallengeTimeZoneOverride { get; set; }
    public static ChallengeSettings CurrentChallengeSettings => (ChallengeSettingsOverride ?? new ChallengeSettings()).Sanitized();

    public static void RecordMinedBlock(Vector3Int cell, int layer, bool blasted) { }
    public static void RecordVeinCleared(int item, Vector3Int representative, int size) { }
    public static bool HasMinedOre(Item item, Vector3Int cell) => false;
    public static void RecordPlacedCount(string metric, int count) { }
    public static void SyncPlacedCount(string metric, int count) { }
    public static void RecordReturn(int peakDepth, int peakLayer, bool fullBag) { }
    public static MetaChallengeProgress GetMonthlyProject() => null;
    public static MetaChallengeBonus ChallengeBonus(ChallengePeriod period) => new MetaChallengeBonus();
    public static long ChallengePeriodEnd(ChallengePeriod period) => 0;
}
