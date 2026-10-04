using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class TiefenhallFinalChecks
{
    public static string PreviousSaveDirectory;
    public static object Main()
    {
        PreviousSaveDirectory = GameSaveSystem.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Temp/TiefenhallFinalSaves-" + System.DateTime.UtcNow.Ticks));
        var root = new GameObject("Tiefenhall Final Checks");
        UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<TiefenhallFinalProbe>();
        return "Checking fresh Play with original reload options and player camera. Saves stay in an isolated Temp directory.";
    }
}
public sealed class TiefenhallFinalProbe : MonoBehaviour
{
    IEnumerator Start()
    {
        if (!MainMenuController.IsVisible || LoadingProgress.Active || GameplayInputBlocker.IsBlocked) { Finish("FAIL: Fresh home state"); yield break; }
        var button = FindFirstObjectByType<MainMenuController>().GetComponentsInChildren<Button>().First(x => x.name == "New Game");
        // Buttons are siblings of the controller under ScreenCanvas.
        button.onClick.Invoke();
        yield return null;
        var setup = FindFirstObjectByType<NewGamePanel>();
        setup.GetComponentInChildren<TMPro.TMP_InputField>().text = "Testlauf";
        setup.GetComponentsInChildren<Button>().First(x => x.name == "Start New Game").onClick.Invoke();
        while (RunNavigation.IsTransitioning || LoadingProgress.Active) yield return null;
        var player = FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        bool follows = player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled && FindFirstObjectByType<MapGenerator>().IsGenerated;
        if (!follows) { Finish("FAIL: Fresh gameplay camera"); yield break; }
        yield return null;
        RunNavigation.MainMenu(); while (RunNavigation.IsTransitioning) yield return null;
        Finish("PASS: Fresh home, full new run, active player camera follow and border clamp with original Editor reload settings; returned home with isolated saves.");
    }
    void Finish(string report)
    {
        GameSaveSystem.TestDirectory = TiefenhallFinalChecks.PreviousSaveDirectory;
        File.WriteAllText("Temp/TiefenhallFinalChecks.txt", report); Debug.Log(report); Destroy(gameObject);
    }
}
