using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupMapLayers
{
    const string Root = "Assets/GameObjects/Map/Blocks";

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) throw new InvalidOperationException("Map or registry missing.");
        if (map.layers != null && map.layers.Length > 0) return "Layer settings already exist; preserved.";
        var stone = map.registry.GetById(BlockType.Stone);
        var stone2 = CreateStone(stone, 2, BlockType.StoneLayer2);
        var stone3 = CreateStone(stone, 3, BlockType.StoneLayer3);
        Undo.RecordObject(map.registry, "Register layer stones");
        var blocks = new List<Block>(map.registry.blocks);
        if (!blocks.Contains(stone2)) blocks.Add(stone2);
        if (!blocks.Contains(stone3)) blocks.Add(stone3);
        map.registry.blocks = blocks.ToArray();
        EditorUtility.SetDirty(map.registry);
        typeof(BlockRegistry).GetMethod("BuildIndex", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(map.registry, null);

        var first = new[] { BlockType.CopperOre, BlockType.Coal, BlockType.IronOre, BlockType.SilverOre };
        var second = first.Concat(new[] { BlockType.GoldOre, BlockType.PlatinumOre }).ToArray();
        Undo.RecordObject(map, "Configure map layers");
        map.layers = new[] {
            new MapLayer { name = "Layer 1", startDepth = 0, stone = stone, ores = first },
            new MapLayer { name = "Layer 2", startDepth = 300, stone = stone2, ores = second },
            new MapLayer { name = "Layer 3", startDepth = 800, stone = stone3,
                ores = second.Concat(new[] { BlockType.DiamondOre }).ToArray() }
        };
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return map.layers.Select(layer => new { layer.name, layer.startDepth, stone = layer.stone.name,
            ores = layer.ores.Select(ore => ore.ToString()).ToArray() }).ToArray();
    }

    static Block CreateStone(Block source, int number, BlockType id)
    {
        string folder = Root + "/LayerStones";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "LayerStones");
        string path = folder + "/Stone_Layer" + number + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Block>(path);
        if (existing) return existing;
        var block = UnityEngine.Object.Instantiate(source);
        block.name = "Stone_Layer" + number;
        block.id = id;
        block.displayName = "Stein Layer " + number;
        block.spawnWithNoise = false;
        block.variants = new TileBase[source.variants.Length];
        for (int i = 0; i < block.variants.Length; i++)
        {
            // Independent Tile assets preserve each stone's gameplay identity and save-game references.
            string tilePath = folder + "/Stone_Layer" + number + "_" + (i + 1).ToString("00") + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tilePath);
            if (!tile)
            {
                tile = UnityEngine.Object.Instantiate(source.variants[i]);
                tile.name = "Stone_Layer" + number + "_" + (i + 1).ToString("00");
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            block.variants[i] = tile;
        }
        AssetDatabase.CreateAsset(block, path);
        return block;
    }
}
