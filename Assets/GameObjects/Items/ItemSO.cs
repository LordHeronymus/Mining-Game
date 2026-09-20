using UnityEngine;

public enum Item
{
    //next 14

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
}

public enum ItemCategory
{
    Ore = 0, 
    Tool = 1, 
    Consumable = 2, 
    Misc = 3,
}

[CreateAssetMenu(fileName = "Item", menuName = "Item")]
public class ItemSO : ScriptableObject
{
    public Item item;
    public ItemCategory category;
    public string displayName;
    public Sprite icon;

    [Header("Stats")]
    public int worth = 0;
}
