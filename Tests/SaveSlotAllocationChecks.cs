using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SaveSlotAllocationChecks
{
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> passed = new List<string>();
    static string output, oldDirectory, testDirectory;
    static bool background;
    static int frame;
    static double began;
    static readonly BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Requires the freshly started homescreen.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SaveSlotAllocationChecks.txt"));
        oldDirectory = GameSaveSystem.TestDirectory;
        testDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SaveSlotAllocation-" + DateTime.UtcNow.Ticks));
        GameSaveSystem.TestDirectory = testDirectory;
        background = Application.runInBackground; Application.runInBackground = true;
        began = EditorApplication.timeSinceStartup; frame = -1; passed.Clear(); routines.Clear();
        routines.Push(Checks()); EditorApplication.update += Tick;
        return output;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - began > 360)
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
    static void RefreshHome() => typeof(MainMenuController).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Home, null);
    static IEnumerator Settled()
    { while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) yield return null; yield return null; }
    static void CameraCheck()
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Player camera follows and clamps");
    }
    static void CloneSave(string source, int slot)
    {
        object[] arguments = { source, null, null };
        typeof(GameSaveSystem).GetMethod("ReadEnvelope", hidden).Invoke(null, arguments);
        var summary = (GameSaveSystem.Summary)arguments[1]; summary.slot = slot;
        typeof(GameSaveSystem).GetMethod("WriteEnvelopeAtomic", hidden).Invoke(null,
            new object[] { GameSaveSystem.SlotPath(slot), JsonUtility.ToJson(summary), arguments[2], true });
    }
    static void StartConfiguredRun()
    {
        var setup = UnityEngine.Object.FindFirstObjectByType<NewGamePanel>();
        setup.GetComponentInChildren<TMPro.TMP_InputField>().text = "Slot allocation test";
        Button(setup.transform, "Start New Game").onClick.Invoke();
    }
    static IEnumerator Checks()
    {
        RefreshHome();
        Check(GameSaveSystem.NextFreeSlot() == 1 && !Button(Home.transform, "Continue").interactable, "Empty directory starts at slot 1");
        Button(Home.transform, "Save Games").onClick.Invoke(); yield return null;
        var panel = UnityEngine.Object.FindFirstObjectByType<SaveSlotPanel>();
        Check(panel.GetComponentsInChildren<Button>(true).Count(x => x.name.StartsWith("Slot ")) == 10 &&
            !panel.GetComponentsInChildren<TextMeshProUGUI>(true).Any(x => x.text.Contains("Automatische Sicherung")), "Ten slots and no automatic-backup field");
        Button(panel.transform, "Back").onClick.Invoke(); yield return null;
        Button(Home.transform, "New Game").onClick.Invoke(); yield return null; StartConfiguredRun(); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 1 && GameSaveSystem.GetSummary(1) != null && !GameSaveSystem.HasSlotData(0), "New Game creates real slot 1 before gameplay release");
        CameraCheck();
        byte[] first = File.ReadAllBytes(GameSaveSystem.SlotPath(1));
        RunNavigation.MainMenu(); yield return Settled();
        Check(Button(Home.transform, "Continue").interactable, "Continue immediately available after a new game");
        Button(Home.transform, "New Game").onClick.Invoke(); yield return null; StartConfiguredRun(); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2 && GameSaveSystem.GetSummary(2) != null &&
            first.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))), "Second new game uses slot 2 and preserves slot 1");
        StatsManager.Instance.AddMoney(123); int money = StatsManager.Instance.Money;
        var pause = UnityEngine.Object.FindFirstObjectByType<RunPauseMenu>(); pause.Open();
        Button(pause.transform, "Home").onClick.Invoke(); yield return Settled();
        Check(GameSaveSystem.GetSummary(2).money == money && File.Exists(GameSaveSystem.SlotPath(2) + ".bak") &&
            first.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))) && !GameSaveSystem.HasSlotData(0), "Save and Home updates current slot, preserving other saves");
        Button(Home.transform, "Continue").onClick.Invoke(); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2 && StatsManager.Instance.Money == money, "Continue reloads the saved run and its slot");
        CameraCheck(); RunNavigation.MainMenu(); yield return Settled();

        for (int slot = 3; slot <= GameSaveSystem.MaxSlots; slot++) CloneSave(GameSaveSystem.SlotPath(1), slot);
        File.Copy(GameSaveSystem.SlotPath(3), GameSaveSystem.SlotPath(3) + ".bak"); RefreshHome();
        Check(GameSaveSystem.NextFreeSlot() == -1 && !Button(Home.transform, "New Game").interactable, "Ten occupied slots disable New Game");
        RunNavigation.NewGame(); yield return null;
        Check(MainMenuController.IsVisible && !RunNavigation.IsTransitioning && !GameSaveSystem.HasSlotData(11), "Direct NewGame cannot exceed the cap");
        Button(Home.transform, "Save Games").onClick.Invoke(); yield return null;
        panel = UnityEngine.Object.FindFirstObjectByType<SaveSlotPanel>();
        var scroll = panel.GetComponentInChildren<ScrollRect>(); scroll.verticalNormalizedPosition = 0; yield return null;
        Canvas.ForceUpdateCanvases();
        var viewport = scroll.viewport; var row10 = Button(panel.transform, "Slot 10").GetComponent<RectTransform>();
        float rowY = viewport.InverseTransformPoint(row10.position).y;
        Check(Mathf.Abs(rowY) < viewport.rect.height / 2 && Button(panel.transform, "Slot 10").interactable, "Last slot is reachable by scrolling");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SaveSlots-ten.png")));
        Button(panel.transform, "Slot 3").onClick.Invoke();
        if (!panel.transform.Find("Edit Save Panel")) { Button(panel.transform, "Edit Save").onClick.Invoke(); yield return null; }
        Button(panel.transform, "Delete Save").onClick.Invoke(); yield return null;
        Check(panel.transform.Find("Confirmation"), "Deleting requires confirmation");
        Button(panel.transform, "Cancel").onClick.Invoke(); yield return null;
        Check(GameSaveSystem.HasSlotData(3), "Cancel preserves the slot");
        Button(panel.transform, "Slot 3").onClick.Invoke();
        if (!panel.transform.Find("Edit Save Panel")) { Button(panel.transform, "Edit Save").onClick.Invoke(); yield return null; }
        Button(panel.transform, "Delete Save").onClick.Invoke(); yield return null;
        Button(panel.transform, "Confirm").onClick.Invoke(); yield return null;
        Check(!GameSaveSystem.HasSlotData(3) && GameSaveSystem.NextFreeSlot() == 3 && Button(Home.transform, "New Game").interactable,
            "Confirmed deletion removes current/recovery files and reopens slot 3");

        CloneSave(GameSaveSystem.SlotPath(1), 0);
        GameSaveSystem.MigrateLegacyAutomaticSave();
        Check(GameSaveSystem.GetSummary(3) != null && File.Exists(GameSaveSystem.SlotPath(0)) && File.Exists(GameSaveSystem.SlotPath(0) + ".migrated"),
            "Legacy automatic save is safely copied into a normal free slot");
        Check(GameSaveSystem.PrepareLoad(3, out _), "Migrated save payload passes load validation");
        GameSaveSystem.CancelPendingLoad(); GameSaveSystem.LeaveRun();
        Check(GameSaveSystem.DeleteSlot(3, out _), "Fixture migration slot can be freed");
        Button(panel.transform, "Back").onClick.Invoke(); yield return null;
        RefreshHome(); Button(Home.transform, "New Game").onClick.Invoke(); yield return null; StartConfiguredRun(); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 3 && GameSaveSystem.GetSummary(3) != null && GameSaveSystem.NextFreeSlot() == -1 &&
            first.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))), "New game reuses the lowest free slot without overwriting other runs");
        CameraCheck();
        Check(RunNavigation.LoadGame(1, out _), "Earlier run can still be loaded"); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 1 && StatsManager.Instance.Money == GameSaveSystem.GetSummary(1).money, "Slot ownership follows a loaded earlier run");
        CameraCheck(); RunNavigation.MainMenu(); yield return Settled();
        GameSaveSystem.TestDirectory = oldDirectory; RefreshHome();
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick; GameSaveSystem.TestDirectory = oldDirectory;
        Application.runInBackground = background;
        File.WriteAllText(output, result + "\nIsolated directory: " + testDirectory);
    }
}
