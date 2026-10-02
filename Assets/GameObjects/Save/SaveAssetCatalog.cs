using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Stable asset keys work in players, where AssetDatabase and GlobalObjectId do not exist.
public sealed class SaveAssetCatalog : ScriptableObject
{
    [Serializable] public struct Entry { public string key; public TileBase tile; }
    public Entry[] tiles = Array.Empty<Entry>();
    Dictionary<string, TileBase> byKey;
    Dictionary<TileBase, string> byTile;
    void Initialize()
    {
        if (byKey != null) return;
        byKey = new(); byTile = new();
        foreach (var entry in tiles)
            if (entry.tile && !string.IsNullOrEmpty(entry.key))
            { byKey[entry.key] = entry.tile; byTile[entry.tile] = entry.key; }
    }
    public string Key(TileBase tile)
    {
        Initialize();
        if (!tile || !byTile.TryGetValue(tile, out string key))
            throw new System.IO.InvalidDataException("Tile fehlt im Spielstand-Katalog: " + (tile ? tile.name : "null"));
        return key;
    }
    public TileBase Resolve(string key)
    {
        Initialize();
        if (!byKey.TryGetValue(key, out var tile) || !tile)
            throw new System.IO.InvalidDataException("Ein benötigtes Welt-Asset fehlt.");
        return tile;
    }
}
