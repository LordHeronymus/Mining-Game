using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu. Runs in an isolated save/meta profile.
// Workbench filter "Herstellbar": no checkbox, caption centred in the button, active state only as the orange selected frame.
// Screenshots: Temp/WorkbenchCraftable/Aus.png and An.png (zoomed crops of the button); report: Temp/WorkbenchCraftableButtonChecks.txt.
public static class WorkbenchCraftableButtonChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play from MainMenu.";
        EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Show();
        var go = new GameObject("Workbench Craftable Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WorkbenchCraftableButtonProbe>();
        return "Running workbench craftable button checks; result in Temp/WorkbenchCraftableButtonChecks.txt.";
    }
}

public sealed class WorkbenchCraftableButtonProbe : MonoBehaviour
{
    const string Report = "Temp/WorkbenchCraftableButtonChecks.txt", Folder = "Temp/WorkbenchCraftable";
    string save, meta, gps;
    bool clean;
    readonly List<string> lines = new(), failed = new();

    void Check(bool ok, string message) { lines.Add((ok ? "ok   " : "FAIL ") + message); if (!ok) failed.Add(message); }

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory; gps = JsonUtility.ToJson(GpsSettings.Tests);
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/WorkbenchCraftable-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        GpsSettings.Document.tests.testModeDisabled = true;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Report, "");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { failed.Add("ERROR " + error); break; }
            if (!more) break;
            yield return next;
        }
        File.AppendAllLines(Report, lines);
        File.AppendAllText(Report, "\n" + (failed.Count == 0 ? "PASS" : "FAIL") + " " + lines.Count + " checks, " + failed.Count + " failed\n");
        if (failed.Count > 0) File.AppendAllLines(Report, failed.Prepend("Fehler:"));
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning || !MainMenuController.IsVisible) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    static IEnumerator Settle()
    {
        Canvas.ForceUpdateCanvases();
        float end = Time.realtimeSinceStartup + .8f;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Herstellbar-Test");
        float end = Time.realtimeSinceStartup + 180;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        yield return Settle();
        var player = Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform
            && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Kamera folgt dem Spieler im frischen Lauf");

        var workbench = Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        workbench.ShowPanel(true);
        workbench.SetCategory(CraftingRecipe.RecipeCategory.Automatic); workbench.SetSearch(""); workbench.SetFavoritesOnly(false);
        var button = workbench.transform.Find("Layout/Craftable").GetComponent<Button>();
        var frame = button.GetComponent<Image>();
        var feedback = button.GetComponent<GoldButtonFeedback>();

        Check(!button.transform.Find("Checkbox") && !button.GetComponentInChildren<WorkbenchGlyph>(true), "Kein Kästchen und kein Häkchen im Knopf");
        var labels = button.GetComponentsInChildren<TMP_Text>(true).Where(t => !string.IsNullOrWhiteSpace(t.text)).ToArray();
        Check(labels.Length == 1 && labels[0].text == "Herstellbar", "Genau eine Beschriftung \"Herstellbar\"");
        var label = labels.FirstOrDefault();
        if (label)
        {
            Check(label.horizontalAlignment == HorizontalAlignmentOptions.Center, "Text horizontal zentriert ausgerichtet");
            var box = label.rectTransform; var frameRect = frame.rectTransform;
            float left = frameRect.InverseTransformPoint(box.TransformPoint(new Vector3(box.rect.xMin + label.margin.x, 0))).x - frameRect.rect.xMin;
            float right = frameRect.rect.xMax - frameRect.InverseTransformPoint(box.TransformPoint(new Vector3(box.rect.xMax - label.margin.z, 0))).x;
            Check(Mathf.Abs(left - right) <= 1f, $"Textfeld mittig im Knopf (links {left:0.0}, rechts {right:0.0})");
            label.ForceMeshUpdate();
            float inkLeft = float.MaxValue, inkRight = float.MinValue;
            foreach (var c in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible))
            {
                inkLeft = Mathf.Min(inkLeft, frameRect.InverseTransformPoint(box.TransformPoint(c.bottomLeft)).x);
                inkRight = Mathf.Max(inkRight, frameRect.InverseTransformPoint(box.TransformPoint(c.topRight)).x);
            }
            float gapLeft = inkLeft - frameRect.rect.xMin, gapRight = frameRect.rect.xMax - inkRight;
            Check(Mathf.Abs(gapLeft - gapRight) <= 2f, $"Schrift mittig im Knopf (Abstand links {gapLeft:0.0}, rechts {gapRight:0.0})");
        }

        workbench.SetFilter(false); yield return Settle(); yield return new WaitForEndOfFrame();
        var off = Capture(frame, "Aus");
        Check(!feedback.Chosen && Fill(button) < .01f, "Aus: keine orange Markierung");
        workbench.SetFilter(true); yield return Settle(); yield return new WaitForEndOfFrame();
        var on = Capture(frame, "An");
        Check(feedback.Chosen && Fill(button) > .99f, "An: orange Auswahlrahmen sichtbar");
        Check(on - off > .03f, $"An ist deutlich oranger als Aus (Orange-Anteil {off:0.000} → {on:0.000})");
        button.onClick.Invoke(); yield return Settle();
        Check(!workbench.OnlyCraftable && !feedback.Chosen, "Klick schaltet den Filter wieder aus");
        button.onClick.Invoke(); yield return Settle();
        Check(workbench.OnlyCraftable && feedback.Chosen, "Klick schaltet den Filter wieder ein");
        workbench.SetFilter(false);
        workbench.ShowPanel(false);
        yield return null;
    }

    static float Fill(Button button) => button.transform.Find("Gold Fill").GetComponent<Image>().color.a;

    // Saves a zoomed crop of the button and returns its mean orange share (red minus blue).
    float Capture(Image frame, string name)
    {
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            var corners = new Vector3[4]; frame.rectTransform.GetWorldCorners(corners);
            var canvas = frame.canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            float orange = 0; int count = 0;
            for (int y = Mathf.CeilToInt(min.y); y < max.y; y++)
                for (int x = Mathf.CeilToInt(min.x); x < max.x; x++)
                {
                    if (x < 0 || y < 0 || x >= shot.width || y >= shot.height) continue;
                    var c = shot.GetPixel(x, y); orange += Mathf.Max(0, c.r - c.b); count++;
                }
            int pad = 16;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x) - pad, 0, shot.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(min.y) - pad, 0, shot.height - 1);
            int w = Mathf.Clamp(Mathf.CeilToInt(max.x - min.x) + 2 * pad, 1, shot.width - x0), h = Mathf.Clamp(Mathf.CeilToInt(max.y - min.y) + 2 * pad, 1, shot.height - y0);
            int zoom = Mathf.Clamp(Mathf.FloorToInt(900f / w), 1, 4);
            var crop = new Texture2D(w * zoom, h * zoom, TextureFormat.RGB24, false);
            var source = shot.GetPixels(x0, y0, w, h);
            var pixels = new Color[w * zoom * h * zoom];
            for (int py = 0; py < h * zoom; py++)
                for (int px = 0; px < w * zoom; px++) pixels[py * w * zoom + px] = source[py / zoom * w + px / zoom];
            crop.SetPixels(pixels); crop.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", crop.EncodeToPNG());
            Destroy(crop);
            File.WriteAllBytes($"{Folder}/{name}-Ganz.png", shot.EncodeToPNG());
            return count > 0 ? orange / count : 0;
        }
        finally { Destroy(shot); }
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta; GameSaveSystem.TestDirectory = save;
        JsonUtility.FromJsonOverwrite(gps, GpsSettings.Document.tests);
        Object.Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
