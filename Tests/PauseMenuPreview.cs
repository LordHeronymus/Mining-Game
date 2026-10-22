using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Requires a fresh Play start on the free MainMenu. Uses isolated save/profile directories.
// Captures the in-game pause menu (5 buttons, no "Laden"), once plain, once with the "Speichern …" status line
// and once with a long error message, and checks that the status line stays inside the board above its frame.
// Output: Temp/PauseMenuPreview/*.png, report Temp/PauseMenuPreview.txt
public static class PauseMenuPreview
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play start on the free MainMenu.";
        var go = new GameObject("Pause Menu Preview");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PauseMenuPreviewProbe>();
        return "Started, see Temp/PauseMenuPreview.txt";
    }
}

public sealed class PauseMenuPreviewProbe : MonoBehaviour
{
    const string Report = "Temp/PauseMenuPreview.txt", Output = "Temp/PauseMenuPreview";
    string save, meta;
    bool clean;
    int checks;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/PauseMenuPreview-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Report, "");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { File.AppendAllText(Report, "FAILED " + error + "\n"); break; }
            if (!more) break;
            yield return next;
        }
        var pause = Object.FindFirstObjectByType<RunPauseMenu>();
        if (pause && RunPauseMenu.IsOpen) pause.Close();
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Pause-Preview");
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        float settle = Time.realtimeSinceStartup + 2;
        while (Time.realtimeSinceStartup < settle) yield return null;

        var pause = Object.FindFirstObjectByType<RunPauseMenu>();
        pause.Open(); yield return null;
        var layout = (RectTransform)typeof(RunPauseMenu).GetField("layout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pause);
        var status = (TMPro.TextMeshProUGUI)typeof(RunPauseMenu).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pause);
        Check(!layout.Find("Load"), "No \"Laden\" button in the pause menu");
        Check(layout.Find("Resume") && layout.Find("Progression") && layout.Find("Challenges") && layout.Find("Settings") && layout.Find("Home"),
            "Fortsetzen, Fortschritt, Herausforderungen, Einstellungen, Hauptmenü present");
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        yield return Capture("Final");

        var board = (RectTransform)layout.Find("Board");
        var home = (RectTransform)layout.Find("Home");
        float frame = 40; // the brass frame along the board's bottom edge
        foreach (var (name, text) in new[] { ("Final-Speichern", "Speichern …"),
            ("Final-Fehler", "Speichern fehlgeschlagen: Der Spielstand konnte nicht geschrieben werden.") })
        {
            status.text = text; status.ForceMeshUpdate();
            yield return Capture(name);
            var bounds = status.textBounds;
            float top = status.rectTransform.anchoredPosition.y + bounds.max.y, bottom = status.rectTransform.anchoredPosition.y + bounds.min.y;
            float boardBottom = board.anchoredPosition.y - board.sizeDelta.y * .5f, homeBottom = home.anchoredPosition.y - home.sizeDelta.y * .5f;
            Check(top < homeBottom && bottom > boardBottom + frame && bounds.size.x <= status.rectTransform.sizeDelta.x + 1,
                name + ": status text between \"Hauptmenü\" and frame (text " + bottom.ToString("0") + ".." + top.ToString("0") +
                ", button bottom " + homeBottom.ToString("0") + ", board bottom " + boardBottom.ToString("0") + ", size " + status.fontSize.ToString("0") + ")");
        }
        status.text = "";

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var cam = Camera.main;
        Check(player && cam.GetComponent<CameraFollow>().enabled && cam.GetComponent<CameraFollow>().target == player.transform &&
            cam.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Camera follows player");
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
    }

    IEnumerator Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        yield return null; yield return null;
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        File.AppendAllText(Report, "Captured " + name + " " + texture.width + "x" + texture.height + "\n");
        Object.Destroy(texture);
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta;
        GameSaveSystem.TestDirectory = save;
        Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
