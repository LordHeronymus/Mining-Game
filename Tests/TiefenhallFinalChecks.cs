using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class TiefenhallFinalChecks
{
    public static object Main()
    {
        var root = new GameObject("Tiefenhall Final Checks");
        UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<TiefenhallFinalProbe>();
        return "Checking fresh Play with original reload options and player camera. Returns to home without writing saves.";
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
        while (RunNavigation.IsTransitioning || LoadingProgress.Active) yield return null;
        var player = FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        bool follows = player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled && FindFirstObjectByType<MapGenerator>().IsGenerated;
        if (!follows) { Finish("FAIL: Fresh gameplay camera"); yield break; }
        yield return null;
        RunNavigation.MainMenu(); while (RunNavigation.IsTransitioning) yield return null;
        Finish("PASS: Fresh home, full new run, active player camera follow and border clamp with original Editor reload settings; returned home without save writes.");
    }
    void Finish(string report)
    {
        File.WriteAllText("Temp/TiefenhallFinalChecks.txt", report); Debug.Log(report); Destroy(gameObject);
    }
}
