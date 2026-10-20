using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Autosave: timed save into the active slot, no save during pause, "Hauptmenü" saves, no manual save button,
// immediate save for play-mode stop, and no automatic save into a different save directory.
// Ends inside the run with both test directories still set (see output) so the caller can stop play mode and verify the exit save.
public static class AutosaveChecks
{
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> passed = new List<string>();
    static string output, testDirectory;
    static float oldInterval;
    static bool background;
    static int frame;
    static double began;

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Requires the freshly started homescreen.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/AutosaveChecks.txt"));
        testDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Autosave-" + DateTime.UtcNow.Ticks));
        GameSaveSystem.TestDirectory = testDirectory;
        // A fixed meta directory keeps the run alive while the checks switch the save directory.
        MetaProgression.TestDirectory = Path.Combine(testDirectory, "MetaProfile");
        oldInterval = GpsSettings.Preferences.autosaveIntervalMinutes;
        background = Application.runInBackground; Application.runInBackground = true;
        began = EditorApplication.timeSinceStartup; frame = -1; passed.Clear(); routines.Clear();
        routines.Push(Checks()); EditorApplication.update += Tick;
        return output;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - began > 300)
        { Finish("FAIL: Interrupted or timed out", true); return; }
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            while (routines.Count > 0)
            {
                var routine = routines.Peek();
                if (!routine.MoveNext()) { routines.Pop(); continue; }
                if (routine.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish("PASS (" + passed.Count + ")\n" + string.Join("\n", passed), false);
        }
        catch (Exception error) { Finish("FAIL: " + error + "\n" + string.Join("\n", passed), true); }
    }
    static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); passed.Add(label); }
    static Button Button(Transform parent, string name) => parent.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == name);
    static MainMenuController Home => UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
    static IEnumerator Settled()
    { while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) yield return null; yield return null; }
    static IEnumerator Wait(float seconds)
    { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
    static void StartRun(string name)
    {
        Button(Home.transform, "New Game").onClick.Invoke();
        var setup = UnityEngine.Object.FindFirstObjectByType<NewGamePanel>();
        setup.GetComponentInChildren<TMP_InputField>().text = name;
        Button(setup.transform, "Start New Game").onClick.Invoke();
    }
    static void CameraCheck()
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Player camera follows and clamps");
    }
    static int SavedMoney() => GameSaveSystem.IsBusy ? -1 : GameSaveSystem.GetSummary(2)?.money ?? -1;
    static int AddMoney(int amount) { StatsManager.Instance.AddMoney(amount); return StatsManager.Instance.Money; }
    static IEnumerator Checks()
    {
        StartRun("Other run"); yield return Settled();
        RunNavigation.MainMenu(); yield return Settled();
        StartRun("Active run"); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2 && RunAutosave.Instance, "Second run in slot 2 with autosave");
        CameraCheck();
        byte[] other = File.ReadAllBytes(GameSaveSystem.SlotPath(1));

        var pause = UnityEngine.Object.FindFirstObjectByType<RunPauseMenu>();
        var home = Button(pause.transform, "Home");
        Check(!Button(pause.transform, "Save") && home && home.GetComponentInChildren<TextMeshProUGUI>(true).text == "Hauptmenü",
            "Pause menu has no save button, home button reads 'Hauptmenü'");

        GpsSettings.Preferences.autosaveIntervalMinutes = 4f / 60f;
        int money = AddMoney(111);
        double waitStart = EditorApplication.timeSinceStartup;
        while (SavedMoney() != money && EditorApplication.timeSinceStartup - waitStart < 20) yield return null;
        Check(SavedMoney() == money, "Timed autosave writes the active slot");
        yield return Settled();
        var notice = GameObject.Find("Autosave Notice")?.GetComponent<TextMeshProUGUI>();
        Check(notice && notice.alpha > 0, "Autosave shows a short notice");
        Check(other.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))) && !GameSaveSystem.HasSlotData(3), "Other slots untouched");
        Check(Time.timeScale > 0 && !GameplayInputBlocker.IsBlocked, "Game continues after autosave");

        pause.Open(); yield return null;
        AddMoney(5);
        yield return Wait(6);
        Check(SavedMoney() == money, "No autosave while paused");
        pause.Close(); yield return null;
        GpsSettings.Preferences.autosaveIntervalMinutes = 0;
        yield return Wait(6);
        Check(SavedMoney() == money, "Interval 0 disables autosave");

        string active = GameSaveSystem.TestDirectory;
        GameSaveSystem.TestDirectory = active + "-other";
        Check(!GameSaveSystem.CanAutoSave && !GameSaveSystem.SaveActiveRunNow(RunAutosave.Instance) &&
            !Directory.Exists(GameSaveSystem.TestDirectory), "No automatic save into another save directory");
        GameSaveSystem.TestDirectory = active;
        // Regression: a save still running while the directory switches must stay in, and stay bound to, its own directory.
        RunAutosave.Instance.StartCoroutine(GameSaveSystem.Save(2, RunAutosave.Instance, null)); yield return null;
        Check(GameSaveSystem.IsBusy, "In-flight save started");
        GameSaveSystem.TestDirectory = active + "-other";
        while (GameSaveSystem.IsBusy) yield return null;
        Check(!GameSaveSystem.CanAutoSave && !GameSaveSystem.SaveActiveRunNow(RunAutosave.Instance) && !Directory.Exists(GameSaveSystem.TestDirectory),
            "Directory switch during a save never redirects automatic saves");
        GameSaveSystem.TestDirectory = active;
        Check(GameSaveSystem.CanAutoSave, "Back in its own directory the run saves again");

        int nowMoney = AddMoney(3);
        Check(GameSaveSystem.SaveActiveRunNow(RunAutosave.Instance) && SavedMoney() == nowMoney && !GameSaveSystem.IsBusy,
            "Immediate save (play-mode stop) writes synchronously");

        int homeMoney = AddMoney(7);
        pause.Open(); yield return null;
        home.onClick.Invoke(); yield return Settled();
        Check(MainMenuController.IsVisible && SavedMoney() == homeMoney, "'Hauptmenü' saves and returns home");
        Check(other.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))), "Slot 1 still unchanged");

        // Leave a running game for the play-mode stop check done by the caller.
        Button(Home.transform, "Continue").onClick.Invoke(); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2, "Continue reloads slot 2");
        CameraCheck();
        int exitMoney = AddMoney(9);
        passed.Add("EXIT money=" + exitMoney + " slot=2 directory=" + testDirectory);
    }
    static void Finish(string result, bool failed)
    {
        EditorApplication.update -= Tick;
        GpsSettings.Preferences.autosaveIntervalMinutes = oldInterval;
        Application.runInBackground = background;
        // On success TestDirectory stays set until the caller has stopped play mode and checked the exit save.
        if (failed) { GameSaveSystem.TestDirectory = null; MetaProgression.TestDirectory = null; }
        File.WriteAllText(output, result + "\nIsolated directory: " + testDirectory);
    }
}
