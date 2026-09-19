using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SurfaceBackgroundSetup
{
    const string Folder = "Assets/AB Sprites/Parralax BG";
    static readonly string[] Files = {
        "Parallax_01_Himmel_6144x2046.png", "Parallax_02_Berge_6144x2046.png",
        "Berge_Ebene_02_Vordergrund_6144x2046.png", "Parallax_04_Huegel_Baeume_V1_6144x2046.png"
    };

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
            layer.Refresh();
        }
        controller.CollectLayers();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
    }

    public static Sprite ImportPanorama(string path)
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
        importer.wrapModeV = TextureWrapMode.Clamp;
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


