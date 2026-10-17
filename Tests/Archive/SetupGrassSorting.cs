using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupGrassSorting
{
    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "NEEDS_EDIT_MODE";
        EnsureSortingLayer();
        int id = SortingLayer.NameToID("Grass");
        int lights = 0, maps = 0;
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!light.gameObject.scene.IsValid()) continue;
            var serialized = new SerializedObject(light);
            var layers = serialized.FindProperty("m_ApplyToSortingLayers");
            if (layers == null || !layers.isArray) throw new Exception("Light sorting layers unavailable: " + light.name);
            bool included = false;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).intValue == id) included = true;
            if (!included)
            {
                layers.InsertArrayElementAtIndex(layers.arraySize);
                layers.GetArrayElementAtIndex(layers.arraySize - 1).intValue = id;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(light);
            }
            lights++;
        }
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!map.gameObject.scene.IsValid()) continue;
            var overlay = map.EnsureGrassOverlay();
            var renderer = overlay.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();
            if (renderer.sortingLayerID != id) throw new Exception("Grass sorting layer was not assigned");
            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if (!EditorSceneManager.SaveScene(map.gameObject.scene)) throw new Exception("Could not save map scene");
            maps++;
        }
        AssetDatabase.SaveAssets();
        return new { success = true, sortingLayer = "Grass", lights, maps };
    }

    static void EnsureSortingLayer()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = settings.FindProperty("m_SortingLayers");
        int beforeUi = layers.arraySize;
        for (int i = 0; i < layers.arraySize; i++)
        {
            var item = layers.GetArrayElementAtIndex(i);
            string name = item.FindPropertyRelative("name").stringValue;
            if (name == "Grass")
            {
                var existingId = item.FindPropertyRelative("uniqueID");
                if (existingId.intValue != 0) return;
                existingId.intValue = NewId();
                settings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return;
            }
            if (name == "UI") { beforeUi = i; break; }
        }
        layers.InsertArrayElementAtIndex(beforeUi);
        var entry = layers.GetArrayElementAtIndex(beforeUi);
        entry.FindPropertyRelative("name").stringValue = "Grass";
        entry.FindPropertyRelative("uniqueID").intValue = NewId();
        entry.FindPropertyRelative("locked").boolValue = false;
        settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static int NewId()
    {
        int id;
        do id = Guid.NewGuid().GetHashCode() & int.MaxValue;
        while (id == 0 || SortingLayer.layers.Any(layer => layer.id == id));
        return id;
    }
}
