using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LoadingRunChecks
{
    public static object Main()
    {
        LoadingProgress.ResetCalibration();
        var probe = new GameObject("Loading run checks").AddComponent<LoadingRunProbe>();
        UnityEngine.Object.DontDestroyOnLoad(probe.gameObject);
        // Match the restart path: replace the persistent run manager before loading.
        if (StatsManager.Instance) UnityEngine.Object.Destroy(StatsManager.Instance.gameObject);
        LoadingScreen.LoadScene(SceneManager.GetActiveScene().name);
        return "Observing a real scene reload; results in Temp/LoadingRunChecks.txt.";
    }
}

public sealed class LoadingRunProbe : MonoBehaviour
{
    readonly HashSet<int> phases = new HashSet<int>();
    readonly HashSet<int> targetValues = new HashSet<int>();
    readonly HashSet<int> displayedValues = new HashSet<int>();
    float lastTarget, lastDisplayed;
    string failure;
    bool captured;
    void Update()
    {
        if (!LoadingProgress.Active)
        {
            var camera = Camera.main;
            if (phases.Count != 8 || targetValues.Count < 100 || displayedValues.Count < 100)
                failure = "Insufficient phase or subprogress updates.";
            if (Time.timeScale != 1f || GameplayInputBlocker.IsBlocked) failure = "Gameplay remains paused or blocked.";
            if (!camera || !camera.GetComponent<CameraFollow>().enabled || !camera.GetComponent<CameraWorldBorderClamp>().enabled ||
                !GameplayTestSettings.CameraFollowEnabled) failure = "Camera follow is disabled.";
            var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            string prefix = $"LoadingCalibration.v1.{map.mapWidth}x{map.mapHeight}.";
            for (int i = 0; i < 8; i++)
                if (!PlayerPrefs.HasKey(prefix + i) || PlayerPrefs.GetFloat(prefix + i) <= 0f)
                    failure = "Calibration did not save a phase duration.";
            string result = $"passed={failure == null}; phases={phases.Count}; targetValues={targetValues.Count}; displayedValues={displayedValues.Count}; error={failure}";
            File.WriteAllText("Temp/LoadingRunChecks.txt", result);
            Debug.Log(result);
            Destroy(gameObject);
            return;
        }
        phases.Add(LoadingProgress.Stage);
        targetValues.Add(Mathf.RoundToInt(LoadingProgress.Target * 100000));
        var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreen>();
        if (!screen) { failure = "Loading screen is missing."; return; }
        var progress = screen.transform.Find("Progress").GetComponent<Image>();
        float displayed = progress.fillAmount;
        displayedValues.Add(Mathf.RoundToInt(displayed * 100000));
        if (LoadingProgress.Target < lastTarget || displayed < lastDisplayed) failure = "Progress moved backwards.";
        if (displayed >= 1f && !LoadingProgress.Ready) failure = "100 percent appeared before readiness.";
        if (Time.timeScale != 0f || !GameplayInputBlocker.IsBlocked) failure = "Gameplay ran during loading.";
        lastTarget = LoadingProgress.Target; lastDisplayed = displayed;
        if (!captured && LoadingProgress.Stage == 4 && displayed > .15f)
        {
            ScreenCapture.CaptureScreenshot("Temp/LoadingScreen-implemented.png");
            captured = true;
        }
    }
}
