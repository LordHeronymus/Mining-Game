using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GrassHangV2Checks
{
    public static string Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain || !map.GrassOverlay ||
            !map.GrassHangLeftOverlay || !map.GrassHangRightOverlay)
            throw new Exception("Grass or hanging overlays missing");

        const string folder = "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet/";
        var left = AssetDatabase.LoadAssetAtPath<Tile>(folder + "GrassEdgeDropLeftTile.asset");
        var right = AssetDatabase.LoadAssetAtPath<Tile>(folder + "GrassEdgeDropRightTile.asset");
        if (!left || !right || !left.sprite || !right.sprite)
            throw new Exception("Hanging grass tiles missing");
        if (left.colliderType != Tile.ColliderType.None || right.colliderType != Tile.ColliderType.None ||
            map.GrassHangLeftOverlay.GetComponent<TilemapCollider2D>() ||
            map.GrassHangRightOverlay.GetComponent<TilemapCollider2D>())
            throw new Exception("Hanging grass must have no collision");
        if (Mathf.Abs(left.transform.m13 - .30f) > .001f ||
            Mathf.Abs(right.transform.m13 - .30f) > .001f)
            throw new Exception("Hanging grass must be rooted at the surface lip");
        if (left.transform.m00 <= 0f || right.transform.m00 >= 0f)
            throw new Exception("Grass roots face the hole instead of the turf");
        var leftBounds = left.sprite.bounds;
        var rightBounds = right.sprite.bounds;
        if (left.transform.MultiplyPoint3x4(leftBounds.min).x < -.371f ||
            left.transform.MultiplyPoint3x4(leftBounds.max).x > .001f ||
            right.transform.MultiplyPoint3x4(rightBounds.max).x < -.001f ||
            right.transform.MultiplyPoint3x4(rightBounds.min).x > .371f)
            throw new Exception("Hanging grass reaches too far into the hole");

        var baseRenderer = map.GrassOverlay.GetComponent<TilemapRenderer>();
        var leftRenderer = map.GrassHangLeftOverlay.GetComponent<TilemapRenderer>();
        var rightRenderer = map.GrassHangRightOverlay.GetComponent<TilemapRenderer>();
        if (baseRenderer.sortingLayerID != leftRenderer.sortingLayerID ||
            baseRenderer.sortingLayerID != rightRenderer.sortingLayerID ||
            leftRenderer.sortingOrder >= baseRenderer.sortingOrder ||
            rightRenderer.sortingOrder >= baseRenderer.sortingOrder)
            throw new Exception("Hanging grass must render behind the carpet");

        const int start = 80, end = 84;
        var old = new TileBase[end - start + 1];
        for (int x = start; x <= end; x++)
        {
            old[x - start] = map.Terrain.GetTile(new Vector3Int(x, 0, 0));
            if (!old[x - start]) throw new Exception("Expected intact surface at " + x);
        }
        try
        {
            SetSurface(map, 82, null);
            Assert(map, 81, null, right);
            Assert(map, 82, null, null);
            Assert(map, 83, left, null);

            SetSurface(map, 80, null);
            Assert(map, 81, left, right);

            SetSurface(map, 82, old[2]);
            Assert(map, 81, left, null);
            Assert(map, 82, null, null);
        }
        finally
        {
            for (int x = start; x <= end; x++)
                map.Terrain.SetTile(new Vector3Int(x, 0, 0), old[x - start]);
            map.SyncGrassFromTerrain();
        }
        for (int x = start + 1; x < end; x++) Assert(map, x, null, null);
        return "V2 hanging grass follows both hole edges, isolated cells, regrowth, and has no collider";
    }

    static void SetSurface(MapGenerator map, int x, TileBase tile)
    {
        map.Terrain.SetTile(new Vector3Int(x, 0, 0), tile);
        if (!Application.isPlaying) map.SyncGrassFromTerrain();
    }

    static void Assert(MapGenerator map, int x, TileBase expectedLeft, TileBase expectedRight)
    {
        var cell = new Vector3Int(x, 0, 0);
        var left = map.GrassHangLeftOverlay.GetTile(cell);
        var right = map.GrassHangRightOverlay.GetTile(cell);
        if (left != expectedLeft || right != expectedRight)
            throw new Exception($"Hanging grass at {x}: {left}/{right}, expected {expectedLeft}/{expectedRight}");
    }
}
