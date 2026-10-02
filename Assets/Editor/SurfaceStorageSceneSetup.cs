using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfaceStorageSceneSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string SpritePath = "Assets/Resources/SurfaceStorage/StorageHut.png";
    const string PrefabPath = "Assets/GameObjects/Map/Storage/SurfaceStorage.prefab";

    static SurfaceStorageSceneSetup()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += InstallIfNeeded;
        EditorApplication.delayCall += InstallIfNeeded;
    }

    static void InstallIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != ScenePath ||
            UnityEngine.Object.FindFirstObjectByType<SurfaceStorageBuilding>()) return;
        Install();
    }

    [MenuItem("Tools/Storage/Install Storage Hut")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var shop = UnityEngine.Object.FindFirstObjectByType<ShopBuilding>();
        if (!shop) throw new InvalidOperationException("Shop required for surface placement.");
        var existing = UnityEngine.Object.FindFirstObjectByType<SurfaceStorageBuilding>();
        if (existing) return;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (!sprite) throw new InvalidOperationException("Storage hut sprite import failed.");
        var storage = SurfaceStorageBuilding.CreateNearShop(shop, sprite);
        var root = storage.gameObject;
        Undo.RegisterCreatedObjectUndo(root, "Create storage hut");

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
    }
}
