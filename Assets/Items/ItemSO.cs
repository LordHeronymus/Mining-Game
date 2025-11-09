using UnityEngine;

public enum Item
{
    coal = 3,
    copper = 0,
    iron = 1,
    silver = 4,
    gold = 2,
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
