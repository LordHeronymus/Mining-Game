using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MigrateFourLayers
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || map.layers == null || map.layers.Length != 3 ||
            map.layers[0].startDepth != 0 || map.layers[1].startDepth != 300 ||
            map.layers[2].startDepth != 800)
            throw new InvalidOperationException("Expected the original three-layer map.");

        var dirt = map.registry.GetById(BlockType.Dirt);
        const string folder = "Assets/AB Sprites/Parralax BG/";
        var dirtBackground = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Untergrund_01_Erdschicht_Seamless_XY_6144x4096.png");
        var layer2Background = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Untergrund_02_Geschichteter_Schiefer_Seamless_XY_6144x4096.png");
        var layer3Background = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Untergrund_03_Schiefer_Seamless_XY_6144x4096.png");
        if (!dirt || !dirtBackground || !layer2Background || !layer3Background)
            throw new InvalidOperationException("Surface stone or underground backgrounds are missing.");

        Undo.RecordObject(map, "Create four map layers");
        var old = map.layers;
        map.layers = new[]
        {
            new MapLayer { name = "Layer 1", startDepth = 0, transitionWidth = 0,
                stone = dirt, backgroundSprite = dirtBackground,
                ores = (BlockType[])old[0].ores.Clone() },
            new MapLayer { name = "Layer 2", startDepth = 30, transitionWidth = 15,
                stone = old[0].stone,
                backgroundSprite = dirtBackground,
                ores = (BlockType[])old[0].ores.Clone() },
            new MapLayer { name = "Layer 3", startDepth = 300, transitionWidth = 15,
                stone = old[1].stone,
                backgroundSprite = layer2Background,
                ores = (BlockType[])old[1].ores.Clone() },
            new MapLayer { name = "Layer 4", startDepth = 800, transitionWidth = 15,
                stone = old[2].stone,
                backgroundSprite = layer3Background,
                ores = (BlockType[])old[2].ores.Clone() }
        };
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        if (!EditorSceneManager.SaveScene(map.gameObject.scene))
            throw new InvalidOperationException("Could not save the scene.");
        return new { layerCount = map.layers.Length, starts = map.layers.Select(layer => layer.startDepth).ToArray() };
    }
}
