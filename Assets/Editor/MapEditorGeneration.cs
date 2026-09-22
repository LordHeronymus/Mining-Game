using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MapEditorGeneration
{
    public static int Generate(MapGenerator map)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Die Map kann nur außerhalb des Play-Modus generiert werden.");
        if(!map || !map.gameObject.scene.IsValid() || !map.gameObject.scene.isLoaded)
            throw new InvalidOperationException("Kein Map-Generator in einer geöffneten Szene gefunden.");
        if(!map.registry || map.mapWidth<=0 || map.mapHeight<=0)
            throw new InvalidOperationException("Map-Größe und Blockkatalog prüfen.");

        var terrain=map.Terrain;
        var overlay=map.EnsureOreOverlay();
        var grass=map.EnsureGrassOverlay();
        int usedSeed=map.ChooseGenerationSeed();

        Undo.IncrementCurrentGroup();
        int undoGroup=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Map im Editor generieren");
        Undo.RegisterCompleteObjectUndo(new Object[]{map,terrain,overlay,grass},"Map im Editor generieren");
        var ladders = map.GetComponent<LadderMap>();
        if (ladders && ladders.Tiles) Undo.RegisterCompleteObjectUndo(ladders.Tiles, "Map im Editor generieren");
        try
        {
            map.GenerateMap(usedSeed);
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(overlay);
        EditorUtility.SetDirty(grass);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        SceneView.RepaintAll();
        return usedSeed;
    }
}
