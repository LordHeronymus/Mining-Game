using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

public static class GrassSurfaceChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
        Check(map && map.GrassOverlay, "Grass overlay missing from the scene");
        var grass = map.GrassOverlay;
        var source = map.GetComponent<TilemapRenderer>();
        var target = grass.GetComponent<TilemapRenderer>();
        var sortingLayers = SortingLayer.layers;
        int terrainIndex = Array.FindIndex(sortingLayers, layer => layer.id == source.sortingLayerID);
        int grassIndex = Array.FindIndex(sortingLayers, layer => layer.id == target.sortingLayerID);
        int uiIndex = Array.FindIndex(sortingLayers, layer => layer.name == "UI");
        Check(source && target && target.sharedMaterial == source.sharedMaterial &&
            target.sortingLayerName == "Grass" && grassIndex > terrainIndex && grassIndex < uiIndex,
            "Grass sorting layer is not in front of actors and behind UI");
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!light.gameObject.scene.IsValid()) continue;
            var affected = new SerializedObject(light).FindProperty("m_ApplyToSortingLayers");
            bool included = false;
            for (int i = 0; i < affected.arraySize; i++)
                if (affected.GetArrayElementAtIndex(i).intValue == target.sortingLayerID) included = true;
            Check(included, "Grass is missing from a scene light: " + light.name);
        }
        Check(!grass.GetComponent<TilemapCollider2D>(), "Grass must not have a collider");
        Check(Mathf.Abs(grass.transform.localPosition.y - map.grassYOffset) < .0001f,
            "Grass Y offset was not applied");

        var used = new HashSet<TileBase>();
        int count = 0;
        int left = -map.GeneratedWidth / 2;
        for (int x = 0; x < map.GeneratedWidth; x++)
        {
            var cell = new Vector3Int(left + x, 0, 0);
            var tile = grass.GetTile(cell);
            Check(!tile == !map.Terrain.HasTile(cell), "Grass and top row do not match");
            if (tile) { count++; used.Add(tile); }
        }
        foreach (var cell in grass.cellBounds.allPositionsWithin)
            if (grass.HasTile(cell)) Check(cell.y == 0, "Grass exists below the top row");
        Check(used.Count == 4, "Not all four grass variants are used");
        foreach (var tile in used)
        {
            var sprite = new SerializedObject(tile).FindProperty("m_Sprite").objectReferenceValue as Sprite;
            Check(sprite, "Grass tile sprite missing");
            string path = AssetDatabase.GetAssetPath(sprite);
            Check(path.Contains("/Blocks/Sprites/Grass/"), "Grass sprite is not organized in the project");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(importer != null && importer.mipmapEnabled &&
                importer.textureCompression == TextureImporterCompression.Uncompressed &&
                importer.alphaSource == TextureImporterAlphaSource.FromInput &&
                Mathf.Abs(sprite.bounds.size.x - .5f) < .001f,
                "Grass sprite import settings are incorrect");
        }
        return new { passed = true, topGrassCells = count, variants = used.Count };
    }
}
