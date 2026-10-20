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

// Pause menu "Speichern" writes straight into the active slot (no slot picker) and leaves other slots untouched.
public static class PauseSaveActiveSlotChecks
{
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> passed = new List<string>();
    static string output, oldDirectory, testDirectory;
    static bool background;
    static int frame;
    static double began;

    public static object Main()
    {
        if (!EditorApplication.isPlaying || !MainMenuController.IsVisible || LoadingProgress.Active)
            return "Requires the freshly started homescreen.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/PauseSaveActiveSlotChecks.txt"));
        oldDirectory = GameSaveSystem.TestDirectory;
        testDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/PauseSaveActiveSlot-" + DateTime.UtcNow.Ticks));
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
    static IEnumerator Settled()
    { while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) yield return null; yield return null; }
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
    static IEnumerator Checks()
    {
        StartRun("Other run"); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 1, "First run in slot 1");
        RunNavigation.MainMenu(); yield return Settled();
        StartRun("Active run"); yield return Settled();
        Check(GameSaveSystem.ActiveSlot == 2, "Second run in slot 2");
        CameraCheck();
        byte[] other = File.ReadAllBytes(GameSaveSystem.SlotPath(1));
        StatsManager.Instance.AddMoney(321); int money = StatsManager.Instance.Money;

        var pause = UnityEngine.Object.FindFirstObjectByType<RunPauseMenu>(); pause.Open(); yield return null;
        Button(pause.transform, "Save").onClick.Invoke(); yield return null;
        Check(!SaveSlotPanel.IsOpen && !UnityEngine.Object.FindFirstObjectByType<SaveSlotPanel>(), "Save opens no slot picker");
        yield return Settled();
        Check(GameSaveSystem.GetSummary(2).money == money, "Active slot 2 holds the new money");
        Check(other.SequenceEqual(File.ReadAllBytes(GameSaveSystem.SlotPath(1))), "Slot 1 unchanged");
        Check(!GameSaveSystem.HasSlotData(3) && GameSaveSystem.ActiveSlot == 2, "No extra slot, active slot stays 2");
        var status = pause.GetComponentsInChildren<TextMeshProUGUI>(true).First(x => x.name == "Status");
        Check(RunPauseMenu.IsOpen && status.text == "Gespeichert", "Pause menu stays open and shows 'Gespeichert'");

        // Fallback without a known slot: the picker opens.
        var activeSlot = typeof(GameSaveSystem).GetProperty("ActiveSlot", BindingFlags.Public | BindingFlags.Static);
        activeSlot.SetValue(null, -1);
        Button(pause.transform, "Save").onClick.Invoke(); yield return null;
        Check(SaveSlotPanel.IsOpen, "Without an active slot the slot picker opens");
        UnityEngine.Object.DestroyImmediate(UnityEngine.Object.FindFirstObjectByType<SaveSlotPanel>().gameObject); yield return null;
        activeSlot.SetValue(null, 2);
        pause.Close(); yield return null;
        CameraCheck();
        RunNavigation.MainMenu(); yield return Settled();
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        GameSaveSystem.TestDirectory = oldDirectory; Application.runInBackground = background;
        File.WriteAllText(output, result + "\nIsolated directory: " + testDirectory);
        Debug.Log("PauseSaveActiveSlotChecks: " + result);
    }
}
