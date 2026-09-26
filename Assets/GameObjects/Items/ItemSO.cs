using UnityEngine;

public enum Item
{
    //next 28

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
}
