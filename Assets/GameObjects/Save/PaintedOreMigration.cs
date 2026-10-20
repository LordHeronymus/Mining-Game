using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Saves store the exact ore overlay tiles. Worlds created before an ore switched to painted single
// blocks (Docs/Gemalte-Erze.md) still hold its old vein tiles (_01.._07). On load every such tile is
// replaced by a current variant of the same ore and size, picked with the generator's own hash, so a
// migrated world shows the tiles a fresh world with this seed would have. The next save keeps them.
static class PaintedOreMigration
{
    // Ore layer cells are stored row by row from bounds.yMin; generation cells are (x - width / 2, -y).
    internal static int Apply(TileBase[] tiles, TileFlags[] flags, Dictionary<int, (Color color, Matrix4x4 matrix)> custom,
        BoundsInt bounds, int seed, int width)
    {
        Dictionary<OreTile, OreTile[]> replacements = null;
        int changed = 0, w = Mathf.Max(1, bounds.size.x);
        for (int i = 0; i < tiles.Length; i++)
        {
            if (!(tiles[i] is OreTile old)) continue;
            replacements ??= new Dictionary<OreTile, OreTile[]>();
            if (!replacements.TryGetValue(old, out var variants)) replacements.Add(old, variants = CurrentVariants(old));
            if (variants == null) continue;
            int x = bounds.xMin + i % w + width / 2, y = -(bounds.yMin + i / w);
            var tile = variants[OreVeins.Hash(seed, x, y, 0x5678u) % (uint)variants.Length];
            // Same cell state as a generated tile: the tile's own flags, colour marker and transform.
            tiles[i] = tile; flags[i] = tile.flags;
            if (tile.color != Color.white || tile.transform != Matrix4x4.identity) custom[i] = (tile.color, tile.transform);
            else custom.Remove(i);
            changed++;
        }
        if (changed > 0) Debug.Log("Tiefenhall: " + changed + " alte Erz-Kacheln auf die aktuellen gemalten Erze umgestellt.");
        return changed;
    }

    // Null keeps the tile: it is already a current variant of its ore, or its block has no overlay groups.
    static OreTile[] CurrentVariants(OreTile old)
    {
        var block = old.block;
        if (!block || !block.HasOreOverlays) return null;
        if (Array.IndexOf(block.smallOre, old) >= 0 || Array.IndexOf(block.mediumOre, old) >= 0 ||
            Array.IndexOf(block.richOre, old) >= 0) return null;
        var variants = block.GetOreVariants(old.richness);
        return Array.TrueForAll(variants, x => x) ? variants : null;
    }
}
