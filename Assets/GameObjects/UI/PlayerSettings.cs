using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerSettings
{
    const string AmbienceKey = "settings.audio.ambience";
    const string SfxKey = "settings.audio.sfx";
    const string FullscreenKey = "settings.display.fullscreen";
    const string UiScaleKey = "settings.display.uiScale";
    static readonly Dictionary<CanvasScaler, Vector2> References = new();

    public static float Master
    {
        get => GpsSettings.Preferences.masterVolume;
        set
        {
            float volume=Mathf.Clamp01(value); if (Mathf.Approximately(Master,volume)) return;
            var node=GpsSettings.GetValue("preferences","masterVolume");node.number=volume;
            if (GpsSettings.SetValue("preferences",node,out _)) GpsSettings.SavePreference("masterVolume",out _);
        }
    }
    public static float Ambience { get => PlayerPrefs.GetFloat(AmbienceKey, 1f); set { PlayerPrefs.SetFloat(AmbienceKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); } }
    public static float Sfx { get => PlayerPrefs.GetFloat(SfxKey, 1f); set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); } }
    public static bool Fullscreen { get => PlayerPrefs.HasKey(FullscreenKey) ? PlayerPrefs.GetInt(FullscreenKey) != 0 : Screen.fullScreen; set { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); Screen.fullScreen = value; PlayerPrefs.Save(); } }
    public static float UiScale { get => PlayerPrefs.GetFloat(UiScaleKey, 1f); set { PlayerPrefs.SetFloat(UiScaleKey, Mathf.Clamp(value, .8f, 1.2f)); ApplyUiScale(); PlayerPrefs.Save(); } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        ApplyAudio();
        if (PlayerPrefs.HasKey(FullscreenKey)) Screen.fullScreen = Fullscreen;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        ApplyUiScale();
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => ApplyUiScale();
    public static void ApplyAudio() => AudioListener.volume = Master;

    public static void ApplyUiScale()
    {
        foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!scaler || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            if (!References.TryGetValue(scaler, out var reference))
            {
                reference = scaler.referenceResolution;
                References[scaler] = reference;
            }
            scaler.referenceResolution = reference / UiScale;
        }
        var dead = new List<CanvasScaler>();
        foreach (var entry in References) if (!entry.Key) dead.Add(entry.Key);
        foreach (var scaler in dead) References.Remove(scaler);
    }

    public static void RestoreDefaults()
    {
        Master=1; PlayerPrefs.DeleteKey(AmbienceKey); PlayerPrefs.DeleteKey(SfxKey);
        PlayerPrefs.DeleteKey(FullscreenKey); PlayerPrefs.DeleteKey(UiScaleKey);
        PlayerPrefs.Save();
        ApplyAudio(); Screen.fullScreen = true; ApplyUiScale();
        GameBindings.RestoreDefaults();
    }
}
