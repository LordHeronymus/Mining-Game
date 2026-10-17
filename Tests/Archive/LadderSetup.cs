using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LadderSetup
{
    const string Root = "Assets/GameObjects/Map/Ladders/";

    [MenuItem("Tools/Map/Install Ladders")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Edit Mode required.");
        AssetDatabase.ImportAsset(Root + "LadderSegment.png");
        var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "LadderSegment.png");
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = height * 2;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(.5f, .5f);
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var tile = AssetDatabase.LoadAssetAtPath<Tile>(Root + "LadderSegment.asset");
        if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, Root + "LadderSegment.asset"); }
        tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "LadderSegment.png");
        tile.colliderType = Tile.ColliderType.None;
        tile.color = Color.white;
        tile.transform = Matrix4x4.identity;
        EditorUtility.SetDirty(tile);
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "LadderLit.mat");
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
            AssetDatabase.CreateAsset(material, Root + "LadderLit.mat");
        }
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var ladderMap = map.GetComponent<LadderMap>();
        if (!ladderMap) ladderMap = Undo.AddComponent<LadderMap>(map.gameObject);
        ladderMap.segment = tile;
        ladderMap.ladderMaterial = material;
        ladderMap.ladderItem = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Tools/Ladder.asset");
        ladderMap.EnsureTiles();
        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        var player = movement.GetComponent<PlayerLadder>();
        if (!player) player = Undo.AddComponent<PlayerLadder>(movement.gameObject);
        player.ladders = ladderMap;
        player.stats = Object.FindFirstObjectByType<StatsManager>();
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(ladderMap);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
    }
}
