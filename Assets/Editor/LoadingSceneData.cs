using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Runtime worlds are generated/restored after the loading screen is visible.
// Editor previews must not become a second serialized copy of the entire world.
[InitializeOnLoad]
public sealed class LoadingSceneData : IProcessSceneWithReport
{
    const string GameplayScene = "Assets/Scenes/SampleScene.unity";
    public int callbackOrder => 1000;

    static LoadingSceneData() => EditorSceneManager.sceneSaving += BeforeSave;

    static void BeforeSave(Scene scene, string path)
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && path == GameplayScene) Strip(scene);
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report != null && scene.path == GameplayScene) Strip(scene);
    }

    public static void Strip(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var map in root.GetComponentsInChildren<MapGenerator>(true))
        {
            var serialized = new SerializedObject(map);
            serialized.FindProperty("isGenerated").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (map.Terrain.GetUsedTilesCount() == 0 &&
                (!map.OreOverlay || map.OreOverlay.GetUsedTilesCount() == 0) &&
                (!map.ArtifactOverlay || map.ArtifactOverlay.GetUsedTilesCount() == 0)) continue;
            bool enabled = map.enabled;
            var appearance = map.GetComponent<UniformStoneAppearance>();
            bool appearanceEnabled = appearance && appearance.enabled;
            try
            {
                map.enabled = false;
                if (appearance) appearance.enabled = false;
                foreach (var tiles in new[] { map.Terrain, map.OreOverlay, map.ArtifactOverlay })
                {
                    if (!tiles || tiles.GetUsedTilesCount() == 0) continue;
                    tiles.ClearAllTiles();
                    tiles.CompressBounds();
                    EditorUtility.SetDirty(tiles);
                }
                var collider = map.GetComponent<TilemapCollider2D>();
                if (collider) collider.ProcessTilemapChanges();
                var composite = map.GetComponent<UnityEngine.CompositeCollider2D>();
                if (composite) composite.GenerateGeometry();
            }
            finally
            {
                map.enabled = enabled;
                if (appearance) appearance.enabled = appearanceEnabled;
            }
        }
    }
}
