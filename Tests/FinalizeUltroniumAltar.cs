using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class FinalizeUltroniumAltar
{
    public static string Run()
    {
        if(EditorApplication.isPlaying) throw new Exception("Edit mode required");
        if(EditorUtility.scriptCompilationFailed) throw new Exception("Compilation failed");
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>(); var altar=map.AltarChamber;
        altar.ResetChargeForNewRun(); altar.RebuildVisuals();
        EditorUtility.SetDirty(altar); EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(map.gameObject.scene);
        var view=SceneView.lastActiveSceneView;
        if(view) view.LookAt(altar.AltarPosition+Vector3.up*altar.CellSize*3.5f,Quaternion.identity,15,true);
        return "Saved SampleScene, altar charge 0, location "+altar.Layout.origin+", compilation successful.";
    }
}
