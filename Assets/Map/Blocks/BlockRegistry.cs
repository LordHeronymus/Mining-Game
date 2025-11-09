using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "BlockRegistry", menuName = "Blocks/Block Registry", order = 1)]
public class BlockRegistry : ScriptableObject
{
    public Block[] blocks; // alle Block-SOs (Earth, Gold, ...)

    // Laufzeit-Indexe (nicht serialisiert)
    [System.NonSerialized] Dictionary<TileBase, Block> _tileToBlock;
    [System.NonSerialized] Dictionary<BlockType, Block> _idToBlock;

    void OnEnable() => BuildIndex();
#if UNITY_EDITOR
    void OnValidate() => BuildIndex();
#endif

    void BuildIndex()
    {
        _tileToBlock = new Dictionary<TileBase, Block>(256);
        _idToBlock = new Dictionary<BlockType, Block>(32);

        if (blocks == null) return;
        foreach (var b in blocks)
        {
            if (!b) continue;
            _idToBlock[b.id] = b;
            if (b.variants != null)
                foreach (var t in b.variants)
                    if (t) _tileToBlock[t] = b; // last-wins bei Duplikaten
        }
    }

    public Block GetById(BlockType id)
        => _idToBlock != null && _idToBlock.TryGetValue(id, out var b) ? b : null;

    public Block FromTile(TileBase tile)
        => tile && _tileToBlock != null && _tileToBlock.TryGetValue(tile, out var b) ? b : null;

    public TileBase GetRandomVariant(Block block)
    {
        if (!block || block.variants == null || block.variants.Length == 0) return null;
        return block.variants[Random.Range(0, block.variants.Length)];
    }

    public int GetPoints(TileBase tile)
        => FromTile(tile)?.points ?? 0;

    public float GetHardness(TileBase tile)
        => FromTile(tile)?.hardness ?? 0f;

    public bool IsSolid(TileBase tile)
        => FromTile(tile)?.isSolid ?? false;
}
