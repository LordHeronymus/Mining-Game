using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GrassCarpetChecks
{
    public static string Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain || !map.GrassOverlay) throw new Exception("Map grass missing");
        const int left = 80, right = 84;
        var originals = new TileBase[right - left + 1];
        for (int x = left; x <= right; x++)
        {
            originals[x - left] = map.Terrain.GetTile(new Vector3Int(x, 0, 0));
            if (!originals[x - left]) throw new Exception("Test surface is not intact at " + x);
        }
        var leftEnd = AssetDatabase.LoadAssetAtPath<Tile>(
            "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet/GrassCarpetTile_0.asset");
        var rightEnd = AssetDatabase.LoadAssetAtPath<Tile>(
            "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet/GrassCarpetTile_5.asset");
        var single = AssetDatabase.LoadAssetAtPath<Tile>(
            "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet/GrassCarpetSingleTile.asset");
        if (!leftEnd || !rightEnd || !single) throw new Exception("Grass edge tiles missing");
        if (map.GrassOverlay.GetComponent<TilemapCollider2D>()) throw new Exception("Grass overlay has a collider");
        if (leftEnd.colliderType != Tile.ColliderType.None || rightEnd.colliderType != Tile.ColliderType.None ||
            single.colliderType != Tile.ColliderType.None) throw new Exception("Grass tile has a collider");

        try
        {
            map.Terrain.SetTile(new Vector3Int(82, 0, 0), null);
            if (!Application.isPlaying) map.SyncGrassFromTerrain();
            AssertTile(map, 82, null);
            AssertTile(map, 81, rightEnd);
            AssertTile(map, 83, leftEnd);

            map.Terrain.SetTile(new Vector3Int(80, 0, 0), null);
            if (!Application.isPlaying) map.SyncGrassFromTerrain();
            AssertTile(map, 81, single);

            map.Terrain.SetTile(new Vector3Int(82, 0, 0), originals[2]);
            if (!Application.isPlaying) map.SyncGrassFromTerrain();
            AssertTile(map, 81, leftEnd);
            if (!map.GrassOverlay.GetTile(new Vector3Int(82, 0, 0)))
                throw new Exception("Grass did not regrow at 82");
        }
        finally
        {
            for (int x = left; x <= right; x++)
                map.Terrain.SetTile(new Vector3Int(x, 0, 0), originals[x - left]);
            map.SyncGrassFromTerrain();
        }
        for (int x = left + 1; x < right; x++)
            if (!map.GrassOverlay.GetTile(new Vector3Int(x, 0, 0)))
                throw new Exception("Grass did not recover at " + x);
        return "Hole edges, single tile, restoration, and collider-free grass passed";
    }

    static void AssertTile(MapGenerator map, int x, TileBase expected)
    {
        var actual = map.GrassOverlay.GetTile(new Vector3Int(x, 0, 0));
        if (actual != expected) throw new Exception($"Grass at {x}: expected {expected}, got {actual}");
    }
}
