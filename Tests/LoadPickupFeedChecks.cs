using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Play Mode from the homescreen (isolated save directory): a new game still announces its starting items in the
// item feed, but loading a saved run does not, so the pickup bling no longer plays on load.
public static class LoadPickupFeedChecks
{
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> passed = new List<string>();
    static string output, oldDirectory, testDirectory;
    static bool background;
    static int frame;
    static double began;
    static readonly BindingFlags member = BindingFlags.NonPublic | BindingFlags.Instance;

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Requires the freshly started homescreen.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadPickupFeedChecks.txt"));
        oldDirectory = GameSaveSystem.TestDirectory;
        testDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadPickupFeed-" + DateTime.UtcNow.Ticks));
        GameSaveSystem.TestDirectory = testDirectory;
        background = Application.runInBackground; Application.runInBackground = true;
        began = EditorApplication.timeSinceStartup; frame = -1; passed.Clear(); routines.Clear();
        routines.Push(Checks()); EditorApplication.update += Tick;
        return output;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - began > 240)
        { Finish("FAIL: Interrupted or timed out"); return; }
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
            Finish("PASS (" + passed.Count + ")\n" + string.Join("\n", passed));
        }
        catch (Exception error) { Finish("FAIL: " + error + "\n" + string.Join("\n", passed)); }
    }
    static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); passed.Add(label); }
    static Button Button(Transform parent, string name) => parent.GetComponentsInChildren<Button>(true).First(x => x.name == name);
    static MainMenuController Home => UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
    static void RefreshHome() => typeof(MainMenuController).GetMethod("Refresh", member).Invoke(Home, null);

    // Pickups the feed has shown or still queues; the bling plays with each one it shows.
    static int fed;
    static int FeedCount()
    {
        var feed = ItemFeed.Instance;
        if (!feed) return 0;
        return ((ICollection)typeof(ItemFeed).GetField("entries", member).GetValue(feed)).Count +
            ((ICollection)typeof(ItemFeed).GetField("pending", member).GetValue(feed)).Count;
    }
    // Waits for the run to settle and then as long again as the feed needs for two queued pickups.
    static IEnumerator WatchUntilSettled()
    {
        fed = 0;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)
        { fed = Mathf.Max(fed, FeedCount()); yield return null; }
        float until = Time.unscaledTime + 1f;
        while (Time.unscaledTime < until) { fed = Mathf.Max(fed, FeedCount()); yield return null; }
    }
    static IEnumerator Checks()
    {
        var resources = StartingResourcesSettings.Load();
        Check(resources.items.Count(x => x.amount > 0 && StartingResourcesSettings.Resolve(x.itemId)) > 0,
            "Starting resources contain items");
        RefreshHome();
        Button(Home.transform, "New Game").onClick.Invoke(); yield return null;
        var setup = UnityEngine.Object.FindFirstObjectByType<NewGamePanel>();
        setup.GetComponentInChildren<TMPro.TMP_InputField>().text = "Pickup feed test";
        Button(setup.transform, "Start New Game").onClick.Invoke();
        yield return WatchUntilSettled();
        Check(GameSaveSystem.ActiveSlot == 1 && GameSaveSystem.GetSummary(1) != null, "New game creates slot 1");
        Check(fed > 0, "New game announces its starting items in the feed");
        RunNavigation.MainMenu(); yield return WatchUntilSettled();
        Check(RunNavigation.LoadGame(1, out _), "Saved run can be loaded");
        yield return WatchUntilSettled();
        Check(GameSaveSystem.ActiveSlot == 1 && InventoryManager.Instance &&
            resources.items.All(x => InventoryManager.Instance.GetCount(StartingResourcesSettings.Resolve(x.itemId)) == x.amount),
            "Loaded run keeps its saved inventory");
        Check(fed == 0, "Loading a saved run shows no pickup in the feed (no bling)");
        RunNavigation.MainMenu(); yield return WatchUntilSettled();
        GameSaveSystem.TestDirectory = oldDirectory; RefreshHome();
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick; GameSaveSystem.TestDirectory = oldDirectory;
        Application.runInBackground = background;
        File.WriteAllText(output, result + "\nIsolated directory: " + testDirectory);
    }
}
