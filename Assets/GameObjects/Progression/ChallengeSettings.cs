using System;

// GPS section "Herausforderungen". New values apply from the next day/week/month change, because XP,
// counts and amounts are stored in a round when it is drawn. Attributes are fully qualified so this
// file stays free of UnityEngine code and the catalog can be simulated outside Unity.
[Serializable] public sealed class ChallengePeriodSettings
{
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("XP leicht")] public int xpEasy;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("XP mittel")] public int xpMedium;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("XP schwer")] public int xpHard;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("XP XXL")] public int xpXxl;
    [UnityEngine.Range(0, 10), UnityEngine.InspectorName("Anzahl leicht")] public int countEasy;
    [UnityEngine.Range(0, 10), UnityEngine.InspectorName("Anzahl mittel")] public int countMedium;
    [UnityEngine.Range(0, 10), UnityEngine.InspectorName("Anzahl schwer")] public int countHard;
    [UnityEngine.Range(0f, 100f), UnityEngine.InspectorName("XXL-Chance (%)")] public float xxlChancePercent;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("Bonus alle erledigt (XP)")] public int completionBonusXp;

    public int Xp(int tier) => tier switch { 0 => xpEasy, 1 => xpMedium, 2 => xpHard, 3 => xpXxl, _ => 0 };
    public int Count(int tier) => tier switch { 0 => countEasy, 1 => countMedium, 2 => countHard, _ => 0 };
}

[Serializable] public sealed class ChallengeSettings
{
    [UnityEngine.InspectorName("Täglich")] public ChallengePeriodSettings daily = Defaults(ChallengePeriod.Daily);
    [UnityEngine.InspectorName("Wöchentlich")] public ChallengePeriodSettings weekly = Defaults(ChallengePeriod.Weekly);
    [UnityEngine.InspectorName("Monatlich")] public ChallengePeriodSettings monthly = Defaults(ChallengePeriod.Monthly);
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("Monatsprojekt Etappe 1 (XP)")] public int projectStage1Xp = 5000;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("Monatsprojekt Etappe 2 (XP)")] public int projectStage2Xp = 10000;
    [UnityEngine.Range(0, 1000000), UnityEngine.InspectorName("Monatsprojekt Etappe 3 (XP)")] public int projectStage3Xp = 20000;
    [UnityEngine.Range(0, 7), UnityEngine.InspectorName("Fleiss: Tage pro Woche (0 = aus)")] public int diligenceDays = 5;
    [UnityEngine.Range(1, 10), UnityEngine.InspectorName("Fleiss: Tagesaufgaben pro Tag")] public int diligenceDailyTasks = 3;
    [UnityEngine.Range(1, 5), UnityEngine.InspectorName("XXL ab Schicht")] public int xxlMinLayer = 3;
    [UnityEngine.Range(0f, 500f), UnityEngine.InspectorName("Mengenwachstum pro Schicht (%)")] public float growthPercentPerLayer = 10f;
    [UnityEngine.Range(0f, 500f), UnityEngine.InspectorName("Mengenwachstum pro 10 Level (%)")] public float growthPercentPer10Levels = 2f;
    [UnityEngine.Range(0f, 500f), UnityEngine.InspectorName("Max. Mengenwachstum (%)")] public float maxGrowthPercent = 60f;
    [UnityEngine.Range(1, 1200), UnityEngine.InspectorName("Rückkehr: Mindesttiefe (Blöcke)")] public int returnMinDepth = 30;
    [UnityEngine.Range(1, 1200), UnityEngine.InspectorName("Volle Taschen: Mindesttiefe (Blöcke)")] public int fullBagMinDepth = 10;
    [UnityEngine.Range(10f, 300f), UnityEngine.InspectorName("Volle Taschen: Ladung (% Tragkraft)")] public float fullBagPercent = 90f;
    [UnityEngine.Range(0f, 300f), UnityEngine.InspectorName("Volle Taschen: Zuwachs (% Tragkraft)")] public float fullBagGainPercent = 50f;

    public ChallengePeriodSettings Period(ChallengePeriod period) => period switch {
        ChallengePeriod.Daily => daily, ChallengePeriod.Weekly => weekly, ChallengePeriod.Monthly => monthly, _ => null };
    public int ProjectStageXp(int stage) => stage switch { 0 => projectStage1Xp, 1 => projectStage2Xp, 2 => projectStage3Xp, _ => 0 };

    public static ChallengePeriodSettings Defaults(ChallengePeriod period) => period switch {
        ChallengePeriod.Daily => new ChallengePeriodSettings { xpEasy = 300, xpMedium = 600, xpHard = 1200, xpXxl = 3000,
            countEasy = 3, countMedium = 3, countHard = 1, xxlChancePercent = 5f, completionBonusXp = 1000 },
        ChallengePeriod.Weekly => new ChallengePeriodSettings { xpEasy = 1500, xpMedium = 3000, xpHard = 5000, xpXxl = 10000,
            countEasy = 3, countMedium = 3, countHard = 2, xxlChancePercent = 5f, completionBonusXp = 5000 },
        ChallengePeriod.Monthly => new ChallengePeriodSettings { xpEasy = 5000, xpMedium = 10000, xpHard = 20000, xpXxl = 0,
            countEasy = 2, countMedium = 2, countHard = 2, xxlChancePercent = 0f, completionBonusXp = 15000 },
        _ => new ChallengePeriodSettings()
    };

    // GPS bounds only guard the editors; code and tests may assign anything, so every consumer reads Sanitized().
    public ChallengeSettings Sanitized()
    {
        var fallback = new ChallengeSettings();
        return new ChallengeSettings {
            daily = Clean(daily, fallback.daily), weekly = Clean(weekly, fallback.weekly), monthly = Clean(monthly, fallback.monthly),
            projectStage1Xp = Clamp(projectStage1Xp, 0, 1000000), projectStage2Xp = Clamp(projectStage2Xp, 0, 1000000),
            projectStage3Xp = Clamp(projectStage3Xp, 0, 1000000), diligenceDays = Clamp(diligenceDays, 0, 7),
            diligenceDailyTasks = Clamp(diligenceDailyTasks, 1, 10), xxlMinLayer = Clamp(xxlMinLayer, 1, 5),
            growthPercentPerLayer = Finite(growthPercentPerLayer, fallback.growthPercentPerLayer, 0f, 500f),
            growthPercentPer10Levels = Finite(growthPercentPer10Levels, fallback.growthPercentPer10Levels, 0f, 500f),
            maxGrowthPercent = Finite(maxGrowthPercent, fallback.maxGrowthPercent, 0f, 500f),
            returnMinDepth = Clamp(returnMinDepth, 1, 1200), fullBagMinDepth = Clamp(fullBagMinDepth, 1, 1200),
            fullBagPercent = Finite(fullBagPercent, fallback.fullBagPercent, 10f, 300f),
            fullBagGainPercent = Finite(fullBagGainPercent, fallback.fullBagGainPercent, 0f, 300f)
        };
    }
    static ChallengePeriodSettings Clean(ChallengePeriodSettings value, ChallengePeriodSettings fallback) => value == null ? fallback : new ChallengePeriodSettings {
        xpEasy = Clamp(value.xpEasy, 0, 1000000), xpMedium = Clamp(value.xpMedium, 0, 1000000),
        xpHard = Clamp(value.xpHard, 0, 1000000), xpXxl = Clamp(value.xpXxl, 0, 1000000),
        countEasy = Clamp(value.countEasy, 0, 10), countMedium = Clamp(value.countMedium, 0, 10), countHard = Clamp(value.countHard, 0, 10),
        xxlChancePercent = Finite(value.xxlChancePercent, fallback.xxlChancePercent, 0f, 100f),
        completionBonusXp = Clamp(value.completionBonusXp, 0, 1000000)
    };
    static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
    static float Finite(float value, float fallback, float minimum, float maximum)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Min(maximum, Math.Max(minimum, value));
}
