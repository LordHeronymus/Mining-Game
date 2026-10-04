using UnityEditor;
using UnityEditor.SceneManagement;

// The Play start scene is an Editor-session setting, so restore it after reloads.
[InitializeOnLoad]
public static class TiefenhallPlayStart
{
    const string MenuPath = "Assets/Scenes/MainMenu.unity";

    static TiefenhallPlayStart()
    {
        EditorApplication.delayCall += Configure;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) Configure();
        };
    }

    static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath);
        if (menu && EditorSceneManager.playModeStartScene != menu)
            EditorSceneManager.playModeStartScene = menu;
    }
}
