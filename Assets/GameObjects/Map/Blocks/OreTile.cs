using UnityEngine;
using UnityEngine.Tilemaps;

public enum OreRichness { Small, Medium, Rich }

// The persistent overlay carries gameplay identity independently of the stone below it.
[CreateAssetMenu(fileName = "Ore Overlay", menuName = "Blocks/Ore Overlay")]
public sealed class OreTile : Tile
{
    public Block block;
    public OreRichness richness;

    public static int DropCount(OreRichness richness, float roll)
    {
        switch (richness)
        {
            case OreRichness.Small: return 1;
            case OreRichness.Medium: return roll < .5f ? 2 : 1;
            case OreRichness.Rich: return 2;
            default: throw new System.ArgumentOutOfRangeException(nameof(richness));
        }
    }

    // Expected drop count in half-item units: 1, 1.5 and 2 items.
    public static int ExpectedDropHalfUnits(OreRichness richness)
    {
        switch (richness)
        {
            case OreRichness.Small: return 2;
            case OreRichness.Medium: return 3;
            case OreRichness.Rich: return 4;
            default: throw new System.ArgumentOutOfRangeException(nameof(richness));
        }
    }
}
