using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class UpdateFourLayerPreview
{
    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit Mode required.");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.IsGenerated || map.layers == null || map.layers.Length != 4)
            throw new InvalidOperationException("The generated four-layer map is required.");
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, map.GeneratedHeight,
            map.layers, map.oreDensityCurve, map.oreDensityMultiplierPercent,
            map.transitionThickness);
        var edits = new List<TileChangeData>();
        int holes = 0;
        void UpdateRows(int from, int to)
        {
            for (int y = Mathf.Max(0, from); y < Mathf.Min(map.GeneratedHeight, to); y++)
                for (int x = 0; x < map.GeneratedWidth; x++)
                {
                    var cell = new Vector3Int(x - map.GeneratedWidth / 2, -y, 0);
                    if (!map.Terrain.HasTile(cell)) { holes++; continue; }
                    var block = sampler.GetBaseBlock(x, y);
                    TileBase tile;
                    if (block.id == BlockType.Dirt && map.surfaceDirtTile) tile = map.surfaceDirtTile;
                    else if (map.layerOneTile && block == map.layerOneTile.block) tile = map.layerOneTile;
                    else if (map.layerThreeTile && block == map.layerThreeTile.block) tile = map.layerThreeTile;
                    else if (map.uniformTestStone && map.uniformTestTile) tile = map.uniformTestTile;
                    else tile = block.variants[OreVeins.Hash(map.ActiveSeed, x, y, 0x1234u) %
                        (uint)block.variants.Length];
                    if (map.Terrain.GetTile(cell) == tile) continue;
                    edits.Add(new TileChangeData(cell, tile, Color.white, Matrix4x4.identity));
                }
        }
        UpdateRows(15, 45);
        UpdateRows(285, 315);
        UpdateRows(785, 815);
        Undo.RegisterCompleteObjectUndo(map.Terrain, "Update four-layer terrain preview");
        map.Terrain.SetTiles(edits.ToArray(), true);
        map.Terrain.RefreshAllTiles();
        map.GetComponent<DirtSurfaceAppearance>()?.Apply();
        map.GetComponent<UniformStoneAppearance>()?.RefreshAppearance();
        EditorUtility.SetDirty(map.Terrain);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        if (!EditorSceneManager.SaveScene(map.gameObject.scene))
            throw new InvalidOperationException("Could not save the scene.");
        return new { changed = edits.Count, preservedHoles = holes };
    }
}
