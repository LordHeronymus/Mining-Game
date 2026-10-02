using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class TiefenhallSetup
{
    public static void UpdateCatalog()
    {
        const string path = "Assets/Resources/SaveAssetCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<SaveAssetCatalog>(path);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<SaveAssetCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
        catalog.tiles = AssetDatabase.FindAssets("t:TileBase", new[] { "Assets" })
            .Where(guid => !AssetDatabase.GUIDToAssetPath(guid).StartsWith("Assets/_SceneBackups/"))
            .OrderBy(guid => guid).Select(guid => new SaveAssetCatalog.Entry { key = guid,
                tile = AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(guid)) }).Where(x => x.tile).ToArray();
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
    }
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        foreach (string path in new[] { "Assets/Resources/Homescreen/CaveLake.png", "Assets/Resources/Homescreen/MenuAtlas.png" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
        UpdateCatalog();
        EditorSceneManager.SaveOpenScenes();
        var original = SceneManager.GetActiveScene().path;
        const string menuPath = "Assets/Scenes/MainMenu.unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera";
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor = new Color(.015f, .02f, .03f);
        var root = new GameObject("ScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MainMenuController));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        EditorSceneManager.SaveScene(scene, menuPath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(menuPath, true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true) }
            .Concat(EditorBuildSettings.scenes.Where(x => x.path != menuPath && x.path != "Assets/Scenes/SampleScene.unity")).ToArray();
        UnityEditor.PlayerSettings.productName = "Tiefenhall";
        if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
        // Keep normal scene authoring in SampleScene; Play begins at the home screen.
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(menuPath);
        AssetDatabase.SaveAssets();
        Debug.Log("Tiefenhall: MainMenu, Laufzeit-Spielstände und Startszene eingerichtet.");
    }
}
public sealed class TiefenhallSaveBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => TiefenhallSetup.UpdateCatalog();
}
