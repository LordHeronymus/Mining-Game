using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start on the free MainMenu. Uses isolated save/profile directories.
// Captures the in-game pause menu today and two layouts without "Laden" (rearranged at runtime only, no code change).
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
        RectTransform Part(string name) => (RectTransform)layout.Find(name);
        void Place(string name, float y, Vector2? size = null)
        {
            var rect = Part(name); rect.anchoredPosition = new Vector2(0, y);
            if (size.HasValue) rect.sizeDelta = size.Value;
        }
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        yield return Capture("Heute");

        // Variant A: "Laden" gone, everything below moves up one row, the board loses one row (91) and stays centred.
        Part("Load").gameObject.SetActive(false);
        const float shift = -45.5f;
        Place("Board", 90.5f + shift, new Vector2(650, 759));
        Place("Title", 383 + shift);
        Place("Resume", 265 + shift); Place("Progression", 174 + shift); Place("Challenges", 83 + shift);
        Place("Settings", -8 + shift); Place("Home", -99 + shift);
        Place("Status", -209 + shift);
        yield return Capture("Variante-A");

        // Variant B: board height as today, "Hauptmenü" stays at the bottom, separated by the main menu's brass divider.
        Place("Board", 45, new Vector2(650, 850));
        Place("Title", 383);
        Place("Resume", 265); Place("Progression", 174); Place("Challenges", 83); Place("Settings", -8);
        Place("Home", -190); Place("Status", -300);
        Divider(layout, -99);
        yield return Capture("Variante-B");
        File.AppendAllText(Report, "COMPLETE 3 captures\n");
    }

    // Same brass rail with diamond as MainMenuController.BuildHeaderDivider.
    static void Divider(RectTransform layout, float y)
    {
        var divider = HomeUi.Rect("Preview Divider", layout, new Vector2(0, y), new Vector2(436, 22));
        HomeUi.Image("Rail Shadow", divider, new Vector2(0, -2), new Vector2(436, 5)).color = new Color32(29, 12, 3, 220);
        HomeUi.Image("Gold Rail", divider, Vector2.zero, new Vector2(436, 3)).color = new Color32(183, 103, 28, 255);
        HomeUi.Image("Rail Highlight", divider, new Vector2(0, 1), new Vector2(436, 1)).color = new Color32(255, 218, 125, 255);
        var shadow = HomeUi.Image("Diamond Shadow", divider, new Vector2(0, -2), new Vector2(18, 18));
        shadow.color = new Color32(29, 12, 3, 255); shadow.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var border = HomeUi.Image("Diamond Rim", divider, Vector2.zero, new Vector2(16, 16));
        border.color = new Color32(255, 212, 109, 255); border.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var inset = HomeUi.Image("Diamond Inset", divider, Vector2.zero, new Vector2(11, 11));
        inset.color = new Color32(107, 48, 10, 255); inset.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var center = HomeUi.Image("Diamond Gold", divider, Vector2.zero, new Vector2(7, 7));
        center.color = new Color32(232, 160, 52, 255); center.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
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
