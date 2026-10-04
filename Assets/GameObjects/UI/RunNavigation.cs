using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RunNavigation : MonoBehaviour
{
    static RunNavigation instance;
    public static bool IsTransitioning { get; private set; }
    public const string GameScene = "SampleScene", MenuScene = "MainMenu";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { instance = null; IsTransitioning = false; }
    static RunNavigation Host
    {
        get
        {
            if (!instance) { var root = new GameObject("RunNavigation"); DontDestroyOnLoad(root); instance = root.AddComponent<RunNavigation>(); }
            return instance;
        }
    }
    public static void NewGame()
        => NewGame(null);
    public static void NewGame(string name)
    {
        if (IsTransitioning || GameSaveSystem.IsBusy) return;
        if (!GameSaveSystem.BeginNewRun(name)) return;
        IsTransitioning = true; Host.StartCoroutine(Transition(GameScene, true, true));
    }
    public static bool LoadGame(int slot, out string error)
    {
        error = null;
        if (IsTransitioning || GameSaveSystem.IsBusy) return false;
        if (slot < 1 || slot > GameSaveSystem.MaxSlots) { error = "Spielstand konnte nicht geladen werden."; return false; }
        IsTransitioning = true; Host.StartCoroutine(LoadValidated(slot)); return true;
    }
    static IEnumerator LoadValidated(int slot)
    {
        float previousScale = Time.timeScale;
        yield return LoadingScreen.FadeToBlack();
        if (!GameSaveSystem.PrepareLoad(slot, out string error))
        {
            yield return LoadingScreen.FadeFromBlack();
            Time.timeScale = previousScale;
            IsTransitioning = false;
            var panel = FindFirstObjectByType<SaveSlotPanel>();
            if (panel) panel.ShowLoadError(error);
            else FindFirstObjectByType<MainMenuController>()?.ShowStatus(error);
            yield break;
        }
        yield return Transition(GameScene, true, black: true);
    }
    public static void MainMenu()
    {
        if (IsTransitioning || GameSaveSystem.IsBusy) return;
        GameSaveSystem.CancelPendingLoad(); IsTransitioning = true; Host.StartCoroutine(Transition(MenuScene, false));
        GameSaveSystem.LeaveRun();
    }
    static IEnumerator Transition(string scene, bool loading, bool newRun = false, bool black = false)
    {
        if (loading)
        {
            if (!black) yield return LoadingScreen.FadeToBlack();
            yield return LoadingScreen.Prepare();
            yield return LoadingScreen.PrepareTransitionAudio();
        }
        var roots = new HashSet<GameObject>();
        if (StatsManager.Instance) roots.Add(StatsManager.Instance.gameObject);
        if (InventoryManager.Instance) roots.Add(InventoryManager.Instance.gameObject);
        if (AudioManager.Instance) roots.Add(AudioManager.Instance.gameObject);
        foreach (var root in roots) { root.SetActive(false); Destroy(root); }
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        yield return null;
        Time.timeScale = 1;
        if (loading)
        {
            LoadingScreen.LoadScene(scene);
            while (LoadingProgress.Active) yield return null;
            if (newRun && GameSaveSystem.InitialSaveError != null)
            {
                string error = GameSaveSystem.InitialSaveError;
                GameSaveSystem.LeaveRun();
                yield return Transition(MenuScene, false);
                FindFirstObjectByType<MainMenuController>()?.ShowStatus(error);
            }
        }
        else
        {
            LoadingAudio.Complete();
            var operation = SceneManager.LoadSceneAsync(scene); while (!operation.isDone) yield return null;
        }
        IsTransitioning = false;
    }
    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public static void EnsurePlayerCamera(Transform player)
    {
        var camera = Camera.main; if (!camera || !player) return;
        var follow = camera.GetComponent<CameraFollow>();
        if (follow)
        {
            follow.enabled = true; follow.target = player;
            camera.transform.position = player.position + follow.offset + Vector3.up * follow.GetDepthYOffset(player.position.y);
        }
        var clamp = camera.GetComponent<CameraWorldBorderClamp>(); if (clamp) clamp.enabled = true;
    }
}
