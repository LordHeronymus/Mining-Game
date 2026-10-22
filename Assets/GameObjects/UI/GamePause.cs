using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Full game pause for the pause menu and the settings: the owners stop time themselves (timeScale 0),
// this adds the audio. AudioListener.pause halts every world source in place and resumes it where it stopped;
// UI sources opt out with ignoreListenerPause. Loops scheduled on the DSP clock listen to Changed and
// move their schedule by PausedDspSeconds, because that clock keeps running during the pause.
public static class GamePause
{
    static readonly HashSet<Behaviour> Owners = new();
    static double pausedAtDsp;

    public static bool IsPaused { get; private set; }
    // Length of the last completed pause on the DSP clock.
    public static double PausedDspSeconds { get; private set; }
    public static event Action<bool> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        Owners.Clear(); IsPaused = false; PausedDspSeconds = 0; Changed = null;
        AudioListener.pause = false;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

    public static void Set(Behaviour owner, bool paused)
    {
        if (!owner) { Refresh(); return; }
        if (paused) Owners.Add(owner);
        else Owners.Remove(owner);
        Refresh();
    }

    static void Refresh()
    {
        Owners.RemoveWhere(owner => !owner);
        bool paused = Owners.Count > 0 && Application.isPlaying;
        if (paused == IsPaused) return;
        IsPaused = paused;
        if (paused) pausedAtDsp = AudioSettings.dspTime;
        else PausedDspSeconds = Math.Max(0, AudioSettings.dspTime - pausedAtDsp);
        AudioListener.pause = paused;
        Changed?.Invoke(paused);
    }
}
