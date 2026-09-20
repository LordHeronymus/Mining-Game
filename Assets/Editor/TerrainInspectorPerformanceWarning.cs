using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainInspectorPerformanceWarning
{
    const string Message = "Unity-Editor: Die ausgewählte Terrain-Tilemap bremst den Inspector beim Abbau. Wähle ein anderes Objekt.";
    static bool shownForSelection;

    static TerrainInspectorPerformanceWarning()
    {
        Selection.selectionChanged += CheckSelection;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                shownForSelection = false;
                EditorApplication.delayCall += CheckSelection;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                shownForSelection = false;
        };
    }

    static void CheckSelection()
    {
        if (!EditorApplication.isPlaying)
        {
            shownForSelection = false;
            return;
        }

        var selected = Selection.activeGameObject;
        bool terrainSelected = selected && selected.GetComponent<MapGenerator>() && selected.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        if (!terrainSelected)
        {
            shownForSelection = false;
            return;
        }
        if (shownForSelection) return;
        shownForSelection = true;

        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (window.GetType().Name == "GameView")
            {
                window.ShowNotification(new GUIContent(Message), 6);
                return;
            }
        Debug.LogWarning(Message);
    }
}
