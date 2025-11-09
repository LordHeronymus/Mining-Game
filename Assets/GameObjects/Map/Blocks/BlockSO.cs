using UnityEngine;
using UnityEngine.Tilemaps;

public enum BlockType
{
    Stone = 0, 
    IronOre = 1,
    CopperOre = 2,
    SilverOre = 4,
    GoldOre = 3,
    Empty
}

[CreateAssetMenu(fileName = "New BlockType", menuName = "Blocks/BlockType", order = 0)]
public class Block : ScriptableObject
{
    [Header("Basic Info")]
    public BlockType id;                
    public string displayName;          
    public ItemSO itemDrop = null;
    public SoundType digSound;
    public SoundType breakSound;

    [Header("Stats")]
    public float hardness = 1f;
    public int points = 0;
    public bool isSolid = true;         

    [Header("Variants")]
    [Tooltip("Alle möglichen Tiles, aus denen zufällig gewählt wird.")]
    public TileBase[] variants;         

    [Header("Generation")]
    public AnimationCurve rarityCurve;  
    public bool spawnWithNoise = false;
    [Range(0.001f, 1f)] public float noiseScale = 0.05f;  
    public int noiseSeedOffset = 0;                       
}
