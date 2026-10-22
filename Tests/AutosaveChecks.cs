using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Autosave: timed save into the active slot while the game keeps running, consistent world when tiles change during
// the save, other saves supersede a running autosave, no save during pause, "Hauptmenü" saves, no manual save button,
// immediate save for play-mode stop, and no automatic save into a different save directory.
// Ends inside the run with both test directories still set (see output) so the caller can stop play mode and verify the exit save.
public static class AutosaveChecks
{
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> passed = new List<string>();
    static string output, testDirectory;
    static float oldInterval;
    static bool background;
    static int frame, failedSaves;
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
        began = EditorApplication.timeSinceStartup; frame = -1; failedSaves = 0; passed.Clear(); routines.Clear();
        Application.logMessageReceived += Logged;
        routines.Push(Checks()); EditorApplication.update += Tick;
        return output;
    }
    static void Logged(string message, string stack, LogType type)
    { if (message.Contains("Speichern fehlgeschlagen")) failedSaves++; }
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
    static Tilemap Terrain => UnityEngine.Object.FindFirstObjectByType<MapGenerator>().Terrain;
    static Vector3Int TileInRow(Tilemap tiles, int y)
    {
        var bounds = tiles.cellBounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++) { var cell = new Vector3Int(x, y, 0); if (tiles.HasTile(cell)) return cell; }
        throw new Exception("No tile in row " + y);
    }
    // The autosave relies on every tile change being reported while it happens (GameSaveSystem.RunSnapshot).
    static void CallbackCheck()
    {
        int reported = 0;
        Action<Tilemap, NativeArray<Vector3Int>> probe = (source, positions) => { if (source && source.name == "Callback Probe") reported += positions.Length; };
        var grid = new GameObject("Callback Probe Grid", typeof(Grid));
        var child = new GameObject("Callback Probe"); child.transform.SetParent(grid.transform);
        var tiles = child.AddComponent<Tilemap>(); var cell = Vector3Int.zero;
        var tile = Terrain.GetTile(TileInRow(Terrain, Terrain.cellBounds.yMin + 1));
        Tilemap.tilemapPositionsChanged += probe;
        try
        {
            tiles.SetTile(cell, tile); bool set = reported > 0; reported = 0;
            tiles.SetTile(cell, null); bool cleared = reported > 0; reported = 0;
            tiles.SetTile(cell, tile); reported = 0;
            tiles.SetTileFlags(cell, TileFlags.None); bool flags = reported > 0; reported = 0;
            tiles.SetColor(cell, Color.red); bool color = reported > 0; reported = 0;
            tiles.SetTransformMatrix(cell, Matrix4x4.Scale(new Vector3(2, 2, 1))); bool matrix = reported > 0;
            Check(set && cleared, "Tile changes are reported synchronously");
            passed.Add($"Info: synchronously reported SetTileFlags {flags}, SetColor {color}, SetTransformMatrix {matrix}");
        }
        finally { Tilemap.tilemapPositionsChanged -= probe; UnityEngine.Object.Destroy(grid); }
    }
    static IEnumerator Checks()
    {
        StartRun("Other run"); yield return Settled();
        RunNavigation.MainMenu(); yield return Settled();
        StartRun("Active run"); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2 && RunAutosave.Instance, "Second run in slot 2 with autosave");
        CameraCheck();
        CallbackCheck();
        byte[] other = File.ReadAllBytes(GameSaveSystem.SlotPath(1));

        var pause = UnityEngine.Object.FindFirstObjectByType<RunPauseMenu>();
        var home = Button(pause.transform, "Home");
        Check(!Button(pause.transform, "Save") && home && home.GetComponentInChildren<TextMeshProUGUI>(true).text == "Hauptmenü",
            "Pause menu has no save button, home button reads 'Hauptmenü'");

        GpsSettings.Preferences.autosaveIntervalMinutes = 4f / 60f;
        int money = AddMoney(111);
        double waitStart = EditorApplication.timeSinceStartup;
        bool started = false, halted = false;
        while (SavedMoney() != money && EditorApplication.timeSinceStartup - waitStart < 30)
        {
            if (GameSaveSystem.IsAutoSaving)
            { started = true; halted |= Time.timeScale == 0 || GameplayInputBlocker.IsBlocked || GameSaveSystem.IsBusy; }
            yield return null;
        }
        Check(SavedMoney() == money, "Timed autosave writes the active slot");
        Check(started && !halted, "Autosave keeps the game running (no pause, no input block, not busy)");
        // The file is complete a frame before the autosave coroutine reports it and the notice fades in.
        var notice = GameObject.Find("Autosave Notice")?.GetComponent<TextMeshProUGUI>();
        waitStart = EditorApplication.timeSinceStartup;
        while (notice && notice.alpha <= 0 && EditorApplication.timeSinceStartup - waitStart < 1) yield return null;
        Check(notice && notice.alpha > 0, "Autosave shows a short notice");
        // The capture took longer than the 4 s test interval; the finished autosave still restarts the interval.
        Check(!GameSaveSystem.IsAutoSaving, "No second autosave right after the first");
        Check(other.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))) && !GameSaveSystem.HasSlotData(3), "Other slots untouched");
        Check(Time.timeScale > 0 && !GameplayInputBlocker.IsBlocked, "Game continues after autosave");

        pause.Open(); yield return null;
        AddMoney(5);
        yield return Wait(6);
        Check(SavedMoney() == money, "No autosave while paused");
        pause.Close(); yield return null;
        GpsSettings.Preferences.autosaveIntervalMinutes = 0;
        while (GameSaveSystem.IsAutoSaving) yield return null;
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
        // An autosave running while the directory switches gives up instead of writing anywhere.
        bool switched = true;
        var routine = GameSaveSystem.Autosave(RunAutosave.Instance, (ok, _) => switched = ok);
        routine.MoveNext(); yield return null;
        GameSaveSystem.TestDirectory = active + "-other";
        while (routine.MoveNext()) yield return null;
        Check(!switched && !Directory.Exists(GameSaveSystem.TestDirectory), "Autosave gives up after a directory switch");
        GameSaveSystem.TestDirectory = active;

        int nowMoney = AddMoney(3);
        Check(GameSaveSystem.SaveActiveRunNow(RunAutosave.Instance) && SavedMoney() == nowMoney && !GameSaveSystem.IsBusy,
            "Immediate save (play-mode stop) writes synchronously");
        RunAutosave.Instance.StartCoroutine(GameSaveSystem.Autosave(RunAutosave.Instance, null)); yield return null;
        int duringMoney = AddMoney(4);
        Check(GameSaveSystem.IsAutoSaving && GameSaveSystem.SaveActiveRunNow(RunAutosave.Instance) && SavedMoney() == duringMoney &&
            !GameSaveSystem.IsAutoSaving, "Immediate save during an autosave supersedes it");
        yield return null;

        // Rows are captured from the bottom: the second row is captured within the first steps, the top rows only later.
        // Removing a tile in each and adding money in between must all reach the save of the final frame.
        var terrain = Terrain; var bounds = terrain.cellBounds;
        Vector3Int early = TileInRow(terrain, bounds.yMin + 1), late = TileInRow(terrain, bounds.yMax - 2);
        bool autosaved = false;
        routine = GameSaveSystem.Autosave(RunAutosave.Instance, (ok, _) => autosaved = ok);
        for (int i = 0; i < 3 && routine.MoveNext(); i++) yield return null;
        Check(GameSaveSystem.IsAutoSaving && !GameSaveSystem.IsBusy && Time.timeScale > 0, "Autosave captures while the game runs");
        terrain.SetTile(early, null); terrain.SetTile(late, null);
        int tileMoney = AddMoney(13);
        while (routine.MoveNext()) yield return null;
        Check(autosaved && SavedMoney() == tileMoney, "Autosave with tile changes completes");
        RunNavigation.MainMenu(); yield return Settled();
        Button(Home.transform, "Continue").onClick.Invoke(); yield return Settled();
        terrain = Terrain;
        Check(GameSaveSystem.ActiveSlot == 2 && !terrain.HasTile(early) && !terrain.HasTile(late) && StatsManager.Instance.Money == tileMoney,
            "Reloaded world matches the autosave (row changed after its capture included)");
        CameraCheck();

        pause = UnityEngine.Object.FindFirstObjectByType<RunPauseMenu>(); home = Button(pause.transform, "Home");
        int homeMoney = AddMoney(7);
        RunAutosave.Instance.StartCoroutine(GameSaveSystem.Autosave(RunAutosave.Instance, null)); yield return null;
        Check(GameSaveSystem.IsAutoSaving, "Autosave running before 'Hauptmenü'");
        pause.Open(); yield return null;
        home.onClick.Invoke(); yield return Settled();
        Check(MainMenuController.IsVisible && SavedMoney() == homeMoney && !GameSaveSystem.IsAutoSaving,
            "'Hauptmenü' during an autosave saves and returns home");
        Check(other.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))), "Slot 1 still unchanged");
        Check(failedSaves == 0, "No failed save logged");

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
        Application.logMessageReceived -= Logged;
        GpsSettings.Preferences.autosaveIntervalMinutes = oldInterval;
        Application.runInBackground = background;
        // On success TestDirectory stays set until the caller has stopped play mode and checked the exit save.
        if (failed) { GameSaveSystem.TestDirectory = null; MetaProgression.TestDirectory = null; }
        File.WriteAllText(output, result + "\nIsolated directory: " + testDirectory);
    }
}
