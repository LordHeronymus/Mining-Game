using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LoadingTransitionChecks
{
    public static object Main()
    {
        if (!MainMenuController.IsVisible || LoadingProgress.Active || RunNavigation.IsTransitioning) return "Requires the running main menu.";
        var root = new GameObject("Loading transition checks");
        root.AddComponent<LoadingTransitionProbe>();
        return "Temp/LoadingTransitionChecks.txt";
    }
}
public sealed class LoadingTransitionProbe : MonoBehaviour
{
    readonly List<string> checks = new();
    string oldDirectory, directory;
    bool oldBackground;
    IEnumerator Start()
    {
        oldDirectory = GameSaveSystem.TestDirectory;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        directory = Path.GetFullPath("Temp/LoadingTransitionChecks-" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory); GameSaveSystem.TestDirectory = directory;
        var steps = Run();
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception error) { Finish("FAIL: " + error); yield break; }
            if (!more) break;
            yield return steps.Current;
        }
        Finish("PASS: " + checks.Count + " transition/error checks\n" + string.Join("\n", checks));
    }
    void Check(bool value, string label)
    { if (!value) throw new Exception(label); checks.Add(label); }
    IEnumerator Run()
    {
        Check(!RunNavigation.LoadGame(11, out _) && !RunNavigation.IsTransitioning, "Invalid slot rejected without transition");
        var home = FindFirstObjectByType<MainMenuController>();
        var scene = SceneManager.GetActiveScene();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            SaveSlotPanel panel = null;
            if (attempt == 1)
            {
                File.WriteAllText(Path.Combine(directory, "slot-1.thsave"), "broken save");
                panel = SaveSlotPanel.Show(home.transform, false);
            }
            float timeScale = Time.timeScale;
            bool blocked = GameplayInputBlocker.IsBlocked;
            Check(RunNavigation.LoadGame(1, out _), "Load accepted before deferred validation " + attempt);
            float end = Time.realtimeSinceStartup + 10;
            while (RunNavigation.IsTransitioning)
            {
                if (Time.realtimeSinceStartup > end) throw new Exception("Error-return timeout");
                yield return null;
            }
            Check(SceneManager.GetActiveScene() == scene && FindFirstObjectByType<MainMenuController>() == home,
                "Original scene and controller preserved " + attempt);
            Check(!LoadingScreen.TransitionActive && !LoadingProgress.Active, "Black curtain removed on failure " + attempt);
            Check(Time.timeScale == timeScale && GameplayInputBlocker.IsBlocked == blocked, "Original pause/input ownership restored " + attempt);
            object owner = panel ? (object)panel : home;
            var field = owner.GetType().GetField("status", BindingFlags.Instance | BindingFlags.NonPublic);
            var status = (TMPro.TextMeshProUGUI)field.GetValue(owner);
            Check(status && !string.IsNullOrEmpty(status.text), "Failure visible in original view " + attempt);
            if (panel)
            {
                panel.GetComponentsInChildren<UnityEngine.UI.Button>().First(x => x.name == "Back").onClick.Invoke();
                yield return null;
            }
        }
    }
    void Finish(string result)
    {
        GameSaveSystem.TestDirectory = oldDirectory; Application.runInBackground = oldBackground;
        File.WriteAllText("Temp/LoadingTransitionChecks.txt", result); Destroy(gameObject);
    }
}
