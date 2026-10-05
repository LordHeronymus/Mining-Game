using UnityEngine;

public enum Item
{
    //next 54; serialized values must never be reordered.

    Coal = 3,
    Copper = 0,
    Iron = 1,
    Silver = 4,
    Gold = 2,
    Platinum = 8,

    Torche = 5,
    Dynamite = 6,
    Ladder = 7,
    Wood = 9,
    BridgePart = 10,
    Rope = 11,
    Nails = 12,
    PlantFiber = 13,
    CopperPickaxe = 14,
    IronPickaxe = 15,
    SteelPickaxe = 16,
    TitaniumPickaxe = 17,
    TungstenPickaxe = 18,
    ObsidianPickaxe = 19,
    MythrilPickaxe = 20,
    DiamondPickaxe = 21,
    Scythe = 22,
    Axe = 23,
    Ultronium = 24,
    Medkit = 25,
    Fabric = 26,
    HealingHerbs = 27,
    Titanium = 28,
    Tungsten = 29,
    Steel = 30,
    Obsidian = 31,
    OrangeGarnet = 32,
    Diamond = 33,
    Mythril = 34,
    Backpack = 35,
    Emerald = 36,
    LoadBelt = 37,
    ReinforcedBackpack = 38,
    HeavyDutyBoots = 39,
    Ruby = 40,
    SpringGreaves = 41,
    LoadFrame = 42,
    Exoskeleton = 43,
    CrystalPendant = 44,
    CopperEnergyBracelet = 45,
    EnergyStorageVial = 46,
    RuneBelt = 47,
    CrystalHeart = 48,
    CrystalHarness = 49,
    TravelMonolith = 50,
    IronLadder = 51,
    LavaLamp = 52,
    PercussionHammer = 53,
}

public enum ItemCategory
{
    Ore = 0, 
    Tool = 1, 
    Consumable = 2, 
    Misc = 3,
    Powerup = 4,
}

[CreateAssetMenu(fileName = "Item", menuName = "Item")]
public class ItemSO : ScriptableObject
{
    public Item item;
    public ItemCategory category;
    public string displayName;
    public Sprite icon;
    public Color themeColor = new Color(1f, .86f, .58f);
    public Vector2 shopIconScale = new Vector2(.85f, .85f);
    public Vector2 shopIconOffset;
    public bool shopIconFlipX, shopIconFlipY;
    public Vector2 iconScale = Vector2.one;
    public Vector2 iconOffset;
    public bool iconFlipX, iconFlipY;
    public int iconLayoutVersion;

    [Header("Stats")]
    public int worth = 0;
    [Min(0f)] public float weight = 1f;

    [Header("Powerup effect")]
    [Min(0)] public int carryingCapacityUpgradeLevel;
    [Min(0)] public int energyCapacityUpgradeLevel;

    public bool HasFixedZeroWeight => item == Item.Axe || item == Item.Scythe ||
        item == Item.CopperPickaxe || item == Item.IronPickaxe || item == Item.SteelPickaxe ||
        item == Item.TitaniumPickaxe || item == Item.TungstenPickaxe ||
        item == Item.ObsidianPickaxe || item == Item.MythrilPickaxe || item == Item.DiamondPickaxe || item == Item.PercussionHammer;

    public float EffectiveWeight => HasFixedZeroWeight || float.IsNaN(weight) || float.IsInfinity(weight)
        ? 0f : Mathf.Max(0f, weight);

    void OnValidate()
    {
        if (HasFixedZeroWeight) weight = 0f;
        else if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f) weight = 0f;
    }
}
