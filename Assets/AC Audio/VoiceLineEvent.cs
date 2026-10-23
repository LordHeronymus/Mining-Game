// Pure data for the voice lines (no UnityEngine outside UNITY_5_3_OR_NEWER, so the logic also compiles in plain .NET).
// Append-only: IDs are stored in GPS/assets (never renumber).
public enum VoiceLineEvent { EnergyLow = 0, EnergyCritical = 1, EnergyRefilled = 2, CarryStage1 = 3, CarryStage2 = 4,
    FallSmall = 5, FallBig = 6, OreSold = 7, BlueprintBought = 8, CraftedAxe = 9, CraftedScythe = 10, CraftedMedkit = 11,
    CraftedTorches = 12, CraftedPickaxe = 13, UpgradeEnergy = 14, UpgradeCarry = 15, NewWorld = 16, SaveLoaded = 17 }

[System.Serializable]
public sealed class VoiceLineEventRule
{
    public VoiceLineEvent trigger;
#if UNITY_5_3_OR_NEWER
    [UnityEngine.Min(0)]
#endif
    public int priority = 50; // higher wins
#if UNITY_5_3_OR_NEWER
    [UnityEngine.Range(0f, 100f)]
#endif
    public float playChancePercent = 100f;
#if UNITY_5_3_OR_NEWER
    [UnityEngine.Min(0f)]
#endif
    public float cooldownSeconds = 30f; // per event, measured from line start
#if UNITY_5_3_OR_NEWER
    [UnityEngine.Min(0f)]
#endif
    public float delaySeconds; // wait after the event before speaking
#if UNITY_5_3_OR_NEWER
    [UnityEngine.Min(0f)]
#endif
    public float maxWaitSeconds = 4f; // request expires at notBefore + maxWait
    public bool ignoreGlobalCooldown; // needs only reactionGapSeconds instead of globalCooldownSeconds

    public VoiceLineEventRule Clone() => (VoiceLineEventRule)MemberwiseClone();

    static VoiceLineEventRule R(VoiceLineEvent e, int priority, float chance, float cooldown, float delay, float maxWait, bool ignoreGlobal) =>
        new VoiceLineEventRule { trigger = e, priority = priority, playChancePercent = chance, cooldownSeconds = cooldown,
            delaySeconds = delay, maxWaitSeconds = maxWait, ignoreGlobalCooldown = ignoreGlobal };

    // Array order = enum order 0..17; returns fresh objects on every call.
    public static VoiceLineEventRule[] Defaults() => new[]
    {
        R(VoiceLineEvent.EnergyLow, 75, 100f, 120f, 0f, 10f, true),
        R(VoiceLineEvent.EnergyCritical, 85, 100f, 60f, .3f, 10f, true),
        R(VoiceLineEvent.EnergyRefilled, 45, 100f, 60f, .9f, 5f, false),
        R(VoiceLineEvent.CarryStage1, 40, 100f, 180f, .3f, 10f, false),
        R(VoiceLineEvent.CarryStage2, 65, 100f, 120f, .3f, 10f, true),
        R(VoiceLineEvent.FallSmall, 90, 50f, 60f, .8f, 2f, true),
        R(VoiceLineEvent.FallBig, 100, 100f, 20f, 1.1f, 2.5f, true),
        R(VoiceLineEvent.OreSold, 35, 50f, 120f, 1f, 4f, false),
        R(VoiceLineEvent.BlueprintBought, 50, 70f, 45f, .7f, 5f, false),
        R(VoiceLineEvent.CraftedAxe, 55, 100f, 0f, .5f, 8f, false),
        R(VoiceLineEvent.CraftedScythe, 55, 100f, 0f, .5f, 8f, false),
        R(VoiceLineEvent.CraftedMedkit, 40, 50f, 180f, .5f, 4f, false),
        R(VoiceLineEvent.CraftedTorches, 40, 40f, 180f, .5f, 4f, false),
        R(VoiceLineEvent.CraftedPickaxe, 60, 100f, 0f, .5f, 8f, true),
        R(VoiceLineEvent.UpgradeEnergy, 60, 100f, 0f, .5f, 8f, true),
        R(VoiceLineEvent.UpgradeCarry, 60, 100f, 0f, .5f, 8f, true),
        R(VoiceLineEvent.NewWorld, 80, 100f, 0f, 1f, 6f, true),
        R(VoiceLineEvent.SaveLoaded, 80, 100f, 0f, 1f, 6f, true),
    };
}

// Plain snapshot, built by VoiceLineSettingsAsset.Config().
public sealed class VoiceLineConfig
{
    public bool enabled = true;
    public double globalCooldownSeconds = 20, minimumGapSeconds = 1.5, reactionGapSeconds = 4;
    public int interruptPriorityGap = 25;
    public System.Func<VoiceLineEvent, VoiceLineEventRule> rule; // never returns null (fallback Defaults())
}
