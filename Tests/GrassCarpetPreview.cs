using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GrassCarpetPreview
{
    public static string Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var camera = Camera.main;
        if (!map || !camera) throw new Exception("Map or camera missing");
        const int left = 45, right = 48;
        var oldTiles = new TileBase[right - left + 1, 3];
        var oldOre = new TileBase[right - left + 1, 3];
        for (int x = left; x <= right; x++)
            for (int depth = 0; depth < 3; depth++)
            {
                var cell = new Vector3Int(x, -depth, 0);
                oldTiles[x - left, depth] = map.Terrain.GetTile(cell);
                if (map.OreOverlay) oldOre[x - left, depth] = map.OreOverlay.GetTile(cell);
            }
        var oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize;
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        RenderTexture target = null;
        Texture2D output = null;
        var hidden = new List<SpriteRenderer>();
        var priorVisibility = new List<bool>();
        try
        {
            foreach (var tree in UnityEngine.Object.FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None))
                HideSprites(tree.gameObject, hidden, priorVisibility);
            foreach (var plant in UnityEngine.Object.FindObjectsByType<SurfaceTallGrass>(FindObjectsSortMode.None))
                HideSprites(plant.gameObject, hidden, priorVisibility);
            for (int x = left; x <= right; x++)
                for (int depth = 0; depth < 3; depth++)
                    map.Terrain.SetTile(new Vector3Int(x, -depth, 0), null);
            map.SyncGrassFromTerrain();
            float worldX = map.Terrain.GetCellCenterWorld(new Vector3Int(46, 0, 0)).x;
            camera.transform.position = new Vector3(worldX, -0.15f, oldPosition.z);
            camera.orthographicSize = 2.75f;
            target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            output = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            output.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            output.Apply();
            const string path = "Assets/Design/GrassCarpet-implemented.png";
            System.IO.File.WriteAllBytes(path, output.EncodeToPNG());
            return path;
        }
        finally
        {
            for (int x = left; x <= right; x++)
                for (int depth = 0; depth < 3; depth++)
                {
                    var cell = new Vector3Int(x, -depth, 0);
                    map.Terrain.SetTile(cell, oldTiles[x - left, depth]);
                    if (map.OreOverlay) map.OreOverlay.SetTile(cell, oldOre[x - left, depth]);
                }
            map.SyncGrassFromTerrain();
            camera.transform.position = oldPosition;
            camera.orthographicSize = oldSize;
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            if (target) UnityEngine.Object.DestroyImmediate(target);
            if (output) UnityEngine.Object.DestroyImmediate(output);
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i]) hidden[i].enabled = priorVisibility[i];
        }
    }

    static void HideSprites(GameObject root, List<SpriteRenderer> hidden, List<bool> visibility)
    {
        foreach (var sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            hidden.Add(sprite);
            visibility.Add(sprite.enabled);
            sprite.enabled = false;
        }
    }
}
