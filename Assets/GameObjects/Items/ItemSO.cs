using UnityEngine;

public enum Item
{
    //next 34

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

    [Header("Stats")]
    public int worth = 0;
    [Min(0f)] public float weight = 1f;

    public bool HasFixedZeroWeight => item == Item.Axe || item == Item.Scythe ||
        item == Item.CopperPickaxe || item == Item.IronPickaxe || item == Item.SteelPickaxe ||
        item == Item.TitaniumPickaxe || item == Item.TungstenPickaxe ||
        item == Item.ObsidianPickaxe || item == Item.MythrilPickaxe || item == Item.DiamondPickaxe;

    public float EffectiveWeight => HasFixedZeroWeight || float.IsNaN(weight) || float.IsInfinity(weight)
        ? 0f : Mathf.Max(0f, weight);

    void OnValidate()
    {
        if (HasFixedZeroWeight) weight = 0f;
        else if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f) weight = 0f;
    }
}
