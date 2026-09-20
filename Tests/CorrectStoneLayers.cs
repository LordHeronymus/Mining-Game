using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class CorrectStoneLayers
{
    const string Root = "Assets/GameObjects/Map/Blocks";

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "NEEDS_EDIT_MODE";

        var second = AssetDatabase.LoadAssetAtPath<Block>(Root + "/LayerStones/Stone_Layer2.asset");
        var third = AssetDatabase.LoadAssetAtPath<Block>(Root + "/LayerStones/Stone_Layer3.asset");
        if (!second || !third) throw new Exception("Missing layer stone assets");

        string[] normal = {
            "Stein_01_Ruhig", "Stein_02_Ein_Riss", "Stein_03_Geschichtet",
            "Stein_04_Zwei_Risse", "Stein_05_Riss_Quer", "Stein_06_Riss_Steil"
        };
        string[] deep = {
            "TS1_01_Ruhig", "TS1_02_Verdichtet", "TS1_03_Geschichtet", "TS1_04_Feiner_Riss"
        };

        MoveFolder(Root + "/Sprites/DeepStone/Layer3", Root + "/Sprites/DeepStone/Layer4");
        MoveFolder(Root + "/Sprites/DeepStone/Layer2", Root + "/Sprites/DeepStone/Layer3");

        for (int i = 0; i < 6; i++)
        {
            SetSprite(second, i, Root + "/Sprites/" + normal[i] + ".png");
            SetSprite(third, i, Root + "/Sprites/DeepStone/Layer3/" + deep[i % deep.Length] + ".png");
        }
        second.displayName = "Stein";
        third.displayName = "Tiefstein 1";
        EditorUtility.SetDirty(second);
        EditorUtility.SetDirty(third);

        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
        if (!material) throw new Exception("Missing terrain material");
        material.SetTexture("_DeepStoneTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Sprites/" + normal[0] + ".png"));
        EditorUtility.SetDirty(material);

        var fourth = PrepareFourthLayer(third);

        AssetDatabase.SaveAssets();
        int scenes = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (map.layers == null || map.layers.Length < 3 || map.layers[1].stone != second || map.layers[2].stone != third) continue;
            var appearance = map.GetComponent<DirtSurfaceAppearance>();
            if (appearance) appearance.Apply();
            map.Terrain.RefreshAllTiles();
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if (!EditorSceneManager.SaveScene(map.gameObject.scene)) throw new Exception("Could not save scene");
            scenes++;
        }
        return new { success = true, layer2 = second.displayName, layer3 = third.displayName,
            preparedLayer4 = fourth.displayName, scenes };
    }

    public static object Verify()
    {
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Root + "/BlockRegistry.asset");
        var first = registry.GetById(BlockType.Stone);
        var second = registry.GetById(BlockType.StoneLayer2);
        var third = registry.GetById(BlockType.StoneLayer3);
        var fourth = registry.GetById(BlockType.StoneLayer4);
        if (!first || !second || !third || !fourth) throw new Exception("Missing stone block");
        CheckVariants(second, "/Sprites/Stein_", registry);
        CheckVariants(third, "/Sprites/DeepStone/Layer3/TS1_", registry);
        CheckVariants(fourth, "/Sprites/DeepStone/Layer4/TS2_", registry);
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
        if (!map || map.layers == null || map.layers.Length < 3 ||
            map.layers[0].stone != first || map.layers[1].stone != second || map.layers[2].stone != third)
            throw new Exception("Scene layer order is incorrect");
        var sampler = new MapGenerationSampler(registry, map.seed, map.mapHeight, map.layers,
            map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness);
        if (sampler.GetStone(0) != first || sampler.GetStone(300) != second || sampler.GetStone(800) != third)
            throw new Exception("Generated stone layers are incorrect");
        return new { passed = true, activeLayers = map.layers.Length, preparedLayer4 = fourth.displayName };
    }

    static void CheckVariants(Block block, string expectedPathPart, BlockRegistry registry)
    {
        if (block.variants == null || block.variants.Length != 6) throw new Exception("Incorrect variant count for " + block.name);
        foreach (var tile in block.variants)
        {
            var serialized = new SerializedObject(tile);
            var sprite = serialized.FindProperty("m_DefaultSprite")?.objectReferenceValue as Sprite;
            if (!sprite || !AssetDatabase.GetAssetPath(sprite).Contains(expectedPathPart) || registry.FromTile(tile) != block)
                throw new Exception("Incorrect sprite or registry mapping for " + tile.name);
        }
    }

    static Block PrepareFourthLayer(Block template)
    {
        string path = Root + "/LayerStones/Stone_Layer4.asset";
        var fourth = AssetDatabase.LoadAssetAtPath<Block>(path);
        if (!fourth)
        {
            fourth = UnityEngine.Object.Instantiate(template);
            fourth.name = "Stone_Layer4";
            AssetDatabase.CreateAsset(fourth, path);
        }
        string[] names = { "TS2_01_Ruhig", "TS2_02_Verdichtet", "TS2_03_Geschichtet", "TS2_04_Feiner_Riss" };
        var variants = new TileBase[6];
        for (int i = 0; i < variants.Length; i++)
        {
            string tilePath = Root + "/LayerStones/Stone_Layer4_" + (i + 1).ToString("00") + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tilePath);
            if (!tile)
            {
                tile = UnityEngine.Object.Instantiate(template.variants[i]);
                tile.name = "Stone_Layer4_" + (i + 1).ToString("00");
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            variants[i] = tile;
        }
        fourth.id = BlockType.StoneLayer4;
        fourth.displayName = "Tiefstein 2";
        fourth.variants = variants;
        for (int i = 0; i < variants.Length; i++)
            SetSprite(fourth, i, Root + "/Sprites/DeepStone/Layer4/" + names[i % names.Length] + ".png");
        EditorUtility.SetDirty(fourth);

        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Root + "/BlockRegistry.asset");
        if (!registry) throw new Exception("Missing block registry");
        if (Array.IndexOf(registry.blocks, fourth) < 0)
        {
            var blocks = new Block[registry.blocks.Length + 1];
            Array.Copy(registry.blocks, blocks, registry.blocks.Length);
            blocks[blocks.Length - 1] = fourth;
            registry.blocks = blocks;
            EditorUtility.SetDirty(registry);
        }
        typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(registry, null);
        return fourth;
    }

    static void SetSprite(Block block, int index, string spritePath)
    {
        if (block.variants == null || index >= block.variants.Length || !block.variants[index])
            throw new Exception("Missing tile variant for " + block.name + " at " + index);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (!sprite) throw new Exception("Missing sprite: " + spritePath);
        var tile = block.variants[index];
        var serialized = new SerializedObject(tile);
        var property = serialized.FindProperty("m_DefaultSprite");
        if (property == null) throw new Exception("Tile has no default sprite: " + tile.name);
        property.objectReferenceValue = sprite;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tile);
    }

    static void MoveFolder(string source, string target)
    {
        if (AssetDatabase.IsValidFolder(target)) return;
        if (!AssetDatabase.IsValidFolder(source)) throw new Exception("Missing sprite folder: " + source);
        string error = AssetDatabase.MoveAsset(source, target);
        if (!string.IsNullOrEmpty(error)) throw new Exception(error);
    }
}
