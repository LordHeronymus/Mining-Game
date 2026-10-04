using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PanelEditorDefaults
{
    static double nextCheck;
    static PanelEditorDefaults()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.hierarchyChanged += Apply;
        EditorApplication.update += Update;
        EditorSceneManager.sceneSaving += BeforeSave;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) Apply();
        };
    }
    static void Update()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .5;
        Apply();
    }
    static void BeforeSave(Scene scene, string path) => Apply();
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EnsureRoots<SaveSlotPanel>();
        EnsureRoots<NewGamePanel>();
        EnsureRoots<GpsRuntimePanel>();
        foreach (var group in Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsSceneUi(group.gameObject)) continue;
            // Child groups can control opacity independently; their root already hides them.
            if (group.transform.parent && group.transform.parent.GetComponentInParent<CanvasGroup>(true)) continue;
            if (group.alpha == 0) continue;
            group.alpha = 0;
            EditorUtility.SetDirty(group);
            EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        }
    }
    static void EnsureRoots<T>() where T : MonoBehaviour
    {
        foreach (var panel in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsSceneUi(panel.gameObject)) continue;
            panel.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
            if (panel.GetComponent<CanvasGroup>()) continue;
            panel.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        }
    }
    static bool IsSceneUi(GameObject obj) => obj.scene.IsValid() && obj.scene.isLoaded &&
        !EditorUtility.IsPersistent(obj) && obj.GetComponentInParent<Canvas>(true);
}