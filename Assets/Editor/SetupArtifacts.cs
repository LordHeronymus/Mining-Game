using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupArtifacts
{
    const string Sheet = "Assets/Design/ArtifactsConcept-10.png";
    const string Folder = "Assets/GameObjects/Map/Artifacts";
    static readonly string[] Names =
    {
        "Sonnenscheibe", "Alter Kompass", "Jademaske", "Bernsteinskarabäus", "Astrolabium",
        "Antike Vase", "Siegelring", "Fossile Spirale", "Kupfertafel", "Kristall-Sanduhr"
    };
    static readonly int[] Cash = { 200, 400, 750, 1500, 3000, 5000, 8000, 13000, 25000, 50000 };
    static readonly float[] Chance = { .20f, .12f, .08f, .10f, .07f, .06f, .045f, .035f, .025f, .01f };
    static readonly int[] FirstLayer = { 0, 0, 0, 1, 1, 1, 2, 2, 3, 3 };

    [MenuItem("Tools/Mining Game/Artefakte einrichten")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Artefakte nur im Edit Mode einrichten.");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.gameObject.scene.IsValid())
            throw new InvalidOperationException("SampleScene mit MapGenerator öffnen.");

        var importer = AssetImporter.GetAtPath(Sheet) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Artefakt-Sheet fehlt: " + Sheet);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 792f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        var slices = new SpriteMetaData[Names.Length];
        for (int i = 0; i < slices.Length; i++)
        {
            int column = i % 5;
            int row = i / 5;
            int left = 1983 * column / 5;
            int right = 1983 * (column + 1) / 5;
            int bottom = row == 0 ? 396 : 0;
            int top = row == 0 ? 793 : 396;
            slices[i] = new SpriteMetaData
            {
                name = "Artifact_" + (i + 1).ToString("00"),
                rect = new Rect(left, bottom, right - left, top - bottom),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(.5f, .5f)
            };
        }
        importer.spritesheet = slices;
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToDictionary(s => s.name);
        var settings = new ArtifactDistributionSetting[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            string path = Folder + "/Artifact_" + (i + 1).ToString("00") + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<ArtifactTile>(path);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<ArtifactTile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.displayName = Names[i];
            tile.sprite = sprites["Artifact_" + (i + 1).ToString("00")];
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            settings[i] = new ArtifactDistributionSetting
            {
                tile = tile,
                layerIndices = Enumerable.Range(FirstLayer[i], Math.Max(0, map.layers.Length - FirstLayer[i])).ToArray(),
                chancePercent = Chance[i],
                cash = Cash[i]
            };
        }
        Undo.RecordObject(map, "Artefakte konfigurieren");
        if (map.artifactSettings == null || map.artifactSettings.Length != Names.Length ||
            map.artifactSettings.Any(entry => entry == null || !entry.tile))
            map.artifactSettings = settings;
        map.EnsureArtifactOverlay();
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        MapEditorGeneration.Generate(map);
        EditorSceneManager.SaveScene(map.gameObject.scene);
    }
}
