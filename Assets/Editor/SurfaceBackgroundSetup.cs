using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfaceBackgroundSetup
{
    const string Folder = "Assets/AB Sprites/Parralax BG";
    const string UndergroundFile = Folder + "/Untergrund_01_Erdschicht_Seamless_XY_6144x4096.png";
    const string UndergroundLayer2File = Folder + "/Untergrund_02_Geschichteter_Schiefer_Seamless_XY_6144x4096.png";
    const string UndergroundLayer3File = Folder + "/Untergrund_03_Schiefer_Seamless_XY_6144x4096.png";
    static readonly string[] Files = {
        "Parallax_01_Himmel_6144x2046.png", "Parallax_02_Berge_6144x2046.png",
        "Berge_Ebene_02_Vordergrund_6144x2046.png", "Parallax_04_Huegel_Erde_Seamless_6144x4096.png"
    };

    static SurfaceBackgroundSetup()
    {
        EditorApplication.update += BindUndergroundInOpenScenes;
        EditorSceneManager.sceneOpened += (scene, mode) => BindUnderground(scene);
    }

    static void BindUndergroundInOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (BindUnderground(SceneManager.GetSceneAt(i)))
            {
                EditorApplication.update -= BindUndergroundInOpenScenes;
                return;
            }
    }

    static bool BindUnderground(Scene scene)
    {
        if (!scene.isLoaded || scene.path != "Assets/Scenes/SampleScene.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode) return false;
        Sprite underground = AssetDatabase.LoadAssetAtPath<Sprite>(UndergroundFile);
        Sprite undergroundLayer2 = AssetDatabase.LoadAssetAtPath<Sprite>(UndergroundLayer2File);
        Sprite undergroundLayer3 = AssetDatabase.LoadAssetAtPath<Sprite>(UndergroundLayer3File);
        if (!underground || !undergroundLayer2 || !undergroundLayer3) return false;
        bool found = false;
        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (ParallaxLayer layer in root.GetComponentsInChildren<ParallaxLayer>(true))
        {
            if (layer.name == "NearHills") found = true;
            if (layer.name != "NearHills" ||
                (layer.undergroundTile && layer.undergroundLayer2Tile &&
                 layer.undergroundLayer3Tile && layer.ignoreDepthFade)) continue;
            if (!layer.undergroundTile) layer.undergroundTile = underground;
            if (!layer.undergroundLayer2Tile) layer.undergroundLayer2Tile = undergroundLayer2;
            if (!layer.undergroundLayer3Tile) layer.undergroundLayer3Tile = undergroundLayer3;
            layer.ignoreDepthFade = true;
            EditorUtility.SetDirty(layer);
            EditorSceneManager.MarkSceneDirty(scene);
            layer.Refresh();
            changed = true;
        }
        if (changed) EditorSceneManager.SaveScene(scene);
        return found;
    }

    [MenuItem("Mining Game/Background/Create Surface Background")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Create the background outside Play Mode.");
        var scene = SceneManager.GetActiveScene();
        var existing = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<SurfaceBackgroundController>(true)).FirstOrDefault();
        if (existing) { Selection.activeGameObject = existing.gameObject; return; }

        foreach (string file in Files)
            if (!(AssetImporter.GetAtPath(Folder + "/" + file) is TextureImporter))
                throw new InvalidOperationException("Missing panorama: " + file);

        var sprites = Files.Select(file => ImportPanorama(Folder + "/" + file)).ToArray();
        var underground = ImportPanorama(UndergroundFile, true);
        var undergroundLayer2 = ImportPanorama(UndergroundLayer2File, true);
        var undergroundLayer3 = ImportPanorama(UndergroundLayer3File, true);

        EnsureSortingLayer();
        string materialPath = Folder + "/ParallaxUnlit.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader) throw new InvalidOperationException("URP 2D unlit sprite shader is missing.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var root = new GameObject("SurfaceBackground");
        Undo.RegisterCreatedObjectUndo(root, "Create surface background");
        root.transform.position = new Vector3(0f, 0f, 5f);
        var controller = Undo.AddComponent<SurfaceBackgroundController>(root);
        controller.targetCamera = Camera.main;
        controller.CaptureCameraReference();
        controller.fadeWithDepth = true;

        string[] names = { "Sky", "Mountains", "WoodedCliffs", "NearHills" };
        float[] factors = { 0f, 0.08f, 0.25f, 0.5f };
        float[] heights = { 40f, 20f, 20f, 16f };
        float[] offsets = { 0f, 6f, 5f, 6f };
        int[] orders = { 0, 10, 20, 30 };
        for (int i = 0; i < names.Length; i++)
        {
            var child = new GameObject(names[i]);
            Undo.RegisterCreatedObjectUndo(child, "Create background layer");
            child.transform.SetParent(root.transform, false);
            var layer = Undo.AddComponent<ParallaxLayer>(child);
            layer.segments = new[] { sprites[i] };
            layer.height = heights[i];
            layer.verticalOffset = offsets[i];
            layer.horizontalParallax = factors[i];
            layer.verticalParallax = factors[i];
            layer.sortingOrder = orders[i];
            layer.material = material;
            layer.extendBottomToCamera = i == 0;
            if (i == 3)
            {
                layer.undergroundTile = underground;
                layer.undergroundLayer2Tile = undergroundLayer2;
                layer.undergroundLayer3Tile = undergroundLayer3;
                layer.ignoreDepthFade = true;
            }
            layer.Refresh();
        }
        controller.CollectLayers();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
    }

    public static Sprite ImportPanorama(string path, bool repeatVertically = false)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.maxTextureSize = 8192;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = repeatVertically ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureSortingLayer()
    {
        if (SortingLayer.layers.Any(layer => layer.name == "Background")) return;
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = settings.FindProperty("m_SortingLayers");
        layers.InsertArrayElementAtIndex(0);
        var entry = layers.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("name").stringValue = "Background";
        int id;
        do { id = Guid.NewGuid().GetHashCode(); } while (id == 0 || SortingLayer.layers.Any(layer => layer.id == id));
        entry.FindPropertyRelative("uniqueID").intValue = id;
        entry.FindPropertyRelative("locked").boolValue = false;
        settings.ApplyModifiedProperties();
    }
}


