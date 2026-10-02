using UnityEngine;

[System.Serializable]
public sealed class PickaxeUpgradeLevel
{
    public ItemSO pickaxe;
    [Min(0.01f)] public float progressPerHitMultiplier = 1f;
    [Min(0.01f)] public float maximumHardness = 1f;
}

[CreateAssetMenu(fileName = "UpgradeSettings", menuName = "Mining Game/Upgrade Settings")]
public sealed class UpgradeSettings : ScriptableObject
{
    [Min(0.01f)] public float[] carryingCapacityLevels = { 30f };
    [Min(1f)] public float[] energyCapacityMultipliers = { 1f };
    public PickaxeUpgradeLevel[] pickaxeLevels = System.Array.Empty<PickaxeUpgradeLevel>();

    public int CarryingCapacityLevelCount => carryingCapacityLevels == null || carryingCapacityLevels.Length == 0
        ? 1 : carryingCapacityLevels.Length;

    public float GetCarryingCapacity(int level)
    {
        if (carryingCapacityLevels == null || carryingCapacityLevels.Length == 0) return 30f;
        float value = carryingCapacityLevels[Mathf.Clamp(level - 1, 0, carryingCapacityLevels.Length - 1)];
        return float.IsNaN(value) || float.IsInfinity(value) ? 30f : Mathf.Max(0.01f, value);
    }

    public int PickaxeLevelCount => pickaxeLevels == null ? 0 : pickaxeLevels.Length;

    public int EnergyCapacityLevelCount => Mathf.Clamp(energyCapacityMultipliers == null
        ? 1 : energyCapacityMultipliers.Length, 1, 8);

    public float GetEnergyCapacityMultiplier(int level)
    {
        if (level <= 1 || energyCapacityMultipliers == null || energyCapacityMultipliers.Length == 0) return 1f;
        float value = energyCapacityMultipliers[Mathf.Clamp(level - 1, 0, EnergyCapacityLevelCount - 1)];
        return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(1f, value);
    }

    public float GetPickaxeProgressMultiplier(int level)
    {
        var entry = GetPickaxeLevel(level);
        return entry == null || float.IsNaN(entry.progressPerHitMultiplier) ||
            float.IsInfinity(entry.progressPerHitMultiplier) ? 1f : Mathf.Max(.01f, entry.progressPerHitMultiplier);
    }

    public float GetPickaxeMaximumHardness(int level)
    {
        var entry = GetPickaxeLevel(level);
        return entry == null || float.IsNaN(entry.maximumHardness) ||
            float.IsInfinity(entry.maximumHardness) ? 1f : Mathf.Max(.01f, entry.maximumHardness);
    }

    PickaxeUpgradeLevel GetPickaxeLevel(int level) => pickaxeLevels != null && pickaxeLevels.Length > 0
        ? pickaxeLevels[Mathf.Clamp(level - 1, 0, pickaxeLevels.Length - 1)] : null;
}
