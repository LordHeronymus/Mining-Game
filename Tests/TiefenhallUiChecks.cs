using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TiefenhallUiChecks
{
    public static object Main()
    {
        var root = new GameObject("Tiefenhall UI Checks");
        UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<TiefenhallUiProbe>();
        return "Checking manual save clicks, overwrite confirmation, cancellation and paused input. Results: Temp/TiefenhallUiChecks.txt";
    }
}
public sealed class TiefenhallUiProbe : MonoBehaviour
{
    int passed;
    Button FindButton(Transform parent, string name) => parent.GetComponentsInChildren<Button>().First(x => x.name == name && x.GetComponent<HomeButtonFeedback>());
    void Click(Button button) => ExecuteEvents.Execute(button.gameObject,
        new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
    IEnumerator Start()
    {
        var run = Checks();
        while (true)
        {
            bool next; object yielded;
            try { next = run.MoveNext(); yielded = next ? run.Current : null; }
            catch (Exception error) { Finish("FAIL: " + error); yield break; }
            if (!next) break; yield return yielded;
        }
        Finish("PASS (" + passed + ") - Real pointer-click handlers save a slot, confirm/cancel overwrite, preserve pause gates and cancel loading.");
    }
    IEnumerator Checks()
    {
        GameSaveSystem.TestDirectory = Directory.GetDirectories(Path.GetFullPath("Temp"), "TiefenhallSaveChecks-*").OrderBy(x => x).Last();
        Check(RunNavigation.LoadGame(GameSaveSystem.MostRecentSlot(), out _), "Isolated saved run loads");
        while (RunNavigation.IsTransitioning || LoadingProgress.Active) yield return null;
        var pause = FindFirstObjectByType<RunPauseMenu>(); pause.Open();
        Click(FindButton(pause.transform, "Save")); yield return null;
        var slots = FindFirstObjectByType<SaveSlotPanel>(); Check(slots && SaveSlotPanel.IsOpen, "Save opens slots");
        Click(FindButton(slots.transform, "Slot 2"));
        while (GameSaveSystem.IsBusy) yield return null;
        Check(GameSaveSystem.GetSummary(2) != null, "Pointer click writes slot 2");
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked && SaveSlotPanel.IsOpen, "Save retains modal pause");
        long date = GameSaveSystem.GetSummary(2).savedUtc;
        Click(FindButton(slots.transform, "Slot 2")); yield return null;
        Check(slots.transform.Find("Confirmation"), "Overwrite asks confirmation");
        Click(FindButton(slots.transform, "Cancel")); yield return null;
        Check(GameSaveSystem.GetSummary(2).savedUtc == date && !slots.transform.Find("Confirmation"), "Cancel retains save");
        Click(FindButton(slots.transform, "Slot 2")); yield return null;
        Click(FindButton(slots.transform, "Confirm"));
        while (GameSaveSystem.IsBusy) yield return null;
        Check(GameSaveSystem.GetSummary(2).savedUtc > date && File.Exists(GameSaveSystem.SlotPath(2) + ".bak"), "Confirmed overwrite keeps backup");
        Click(FindButton(slots.transform, "Back")); yield return null;
        Check(!SaveSlotPanel.IsOpen && RunPauseMenu.IsOpen && Time.timeScale == 0, "Back restores paused menu");
        Click(FindButton(pause.transform, "Load")); yield return null;
        slots = FindFirstObjectByType<SaveSlotPanel>();
        Click(FindButton(slots.transform, "Slot 2")); yield return null;
        Click(FindButton(slots.transform, "Start Save")); yield return null;
        Click(FindButton(slots.transform, "Cancel")); yield return null;
        Check(!RunNavigation.IsTransitioning && !LoadingProgress.Active && SaveSlotPanel.IsOpen, "Cancel load leaves current run intact");
        Click(FindButton(slots.transform, "Back")); yield return null;
        RunNavigation.MainMenu(); while (RunNavigation.IsTransitioning) yield return null;
        GameSaveSystem.TestDirectory = null;
        // Rebuild the home controller against the normal, untouched save directory.
        SceneManager.LoadScene("MainMenu"); yield return null;
    }
    void Finish(string result)
    {
        File.WriteAllText("Temp/TiefenhallUiChecks.txt", result); Debug.Log(result);
        GameSaveSystem.TestDirectory = null; Destroy(gameObject);
    }
}
