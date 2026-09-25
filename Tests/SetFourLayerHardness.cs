using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetFourLayerHardness
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || map.layers == null || map.layers.Length != 4)
            throw new InvalidOperationException("Four layers are required.");
        Undo.RecordObject(map, "Initialize layer hardness");
        foreach (var layer in map.layers)
            layer.stoneHardness = layer.stone ? Mathf.Max(.01f, layer.stone.hardness) : 1f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        if (!EditorSceneManager.SaveScene(map.gameObject.scene))
            throw new InvalidOperationException("Could not save the scene.");
        return new { hardness = Array.ConvertAll(map.layers, layer => layer.stoneHardness) };
    }
}
