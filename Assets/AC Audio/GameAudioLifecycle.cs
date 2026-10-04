using UnityEngine;

public static class GameAudioLifecycle
{
    public static bool IsStopping { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Initialize()
    {
        IsStopping = false;
        Application.quitting -= StopAll;
        Application.quitting += StopAll;
    }

    public static void StopAll()
    {
        IsStopping = true;
        foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            source.volume = 0f;
            source.Stop();
        }
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterEditor()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= PlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    static void PlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) StopAll();
        else if (state == UnityEditor.PlayModeStateChange.ExitingEditMode ||
                 state == UnityEditor.PlayModeStateChange.EnteredEditMode) IsStopping = false;
    }
#endif
}
