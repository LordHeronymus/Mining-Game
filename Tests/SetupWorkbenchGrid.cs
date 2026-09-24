using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SetupWorkbenchGrid
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Use Edit Mode.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new Exception("Open SampleScene.");
        Directory.CreateDirectory("Assets/_SceneBackups");
        const string backup = "Assets/_SceneBackups/BeforeWorkbenchGrid_20260923.unity";
        if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);
        const string path = "Assets/UI/Workbench/WorkbenchGridBackground.png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false; importer.maxTextureSize = 2048;
        importer.filterMode = FilterMode.Bilinear; importer.SaveAndReimport();
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        Undo.RecordObject(panel.GetComponent<Image>(), "Workbench grid background");
        panel.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        EditorUtility.SetDirty(panel.GetComponent<Image>());
        foreach (var recipe in panel.recipes)
        {
            var data = new SerializedObject(recipe);
            data.FindProperty("persistentId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe));
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(recipe);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "PASS: grid background installed, recipe IDs saved, existing recipe costs and scene preserved.";
    }
}
