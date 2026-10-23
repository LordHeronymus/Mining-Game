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
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu. Runs in an isolated save/meta profile.
// Measures every button caption in the main views: glyph ink stays inside the visible gold rim (also for the worst
// case Ä/g at the label's font size), the gap above the cap line equals the gap below the baseline, and centred captions
// have equal gaps left and right of their visible ink inside the label box (margins included).
// Screenshots: Temp/ButtonText/Nachher-<View>.png (+ -Zoom crops); report: Temp/ButtonTextCenterChecks.txt.
// (The "Vorher" images of 22.10.2026 came from the first commit of this script, before ButtonTextCentering existed.)
public static class ButtonTextCenterChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play from MainMenu.";
        var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        view.Show();
        var go = new GameObject("Button Text Center Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ButtonTextCenterProbe>();
        return "Running button caption checks; result in Temp/ButtonTextCenterChecks.txt.";
    }
}

public sealed class ButtonTextCenterProbe : MonoBehaviour
{
    const string Report = "Temp/ButtonTextCenterChecks.txt";
    const float BalanceTolerance = 1.5f, InkTolerance = .5f, SideTolerance = 1f;
    static readonly string[] Ascenders = { "Ä", "Ö", "Ü", "É", "f", "l" }, Descenders = { "g", "j", "p", "q", "y" };
    string save, meta, gps, phase;
    bool clean;
    int labels, failures;
    readonly List<string> lines = new();
    readonly List<string> failed = new();

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory; gps = JsonUtility.ToJson(GpsSettings.Tests);
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/ButtonTextCenter-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        GpsSettings.Document.tests.testModeDisabled = true;
        phase = "Nachher";
        Directory.CreateDirectory("Temp/ButtonText");
        File.WriteAllText(Report, "Phase " + phase + "\n");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { File.AppendAllText(Report, "ERROR " + error + "\n"); break; }
            if (!more) break;
            yield return next;
        }
        WriteEntries();
        File.AppendAllLines(Report, lines);
        File.AppendAllText(Report, "\n" + (failures == 0 ? "PASS" : "FAIL") + " " + labels + " captions, " + failures + " outside tolerance\n");
        if (failed.Count > 0) File.AppendAllLines(Report, failed.Prepend("Abweichungen:"));
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning || !MainMenuController.IsVisible) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    IEnumerator Ready()
    {
        float end = Time.realtimeSinceStartup + 180;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        yield return Settle();
    }

    static IEnumerator Settle()
    {
        Canvas.ForceUpdateCanvases();
        float end = Time.realtimeSinceStartup + .7f;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    static IEnumerator Unblocked()
    {
        float end = Time.realtimeSinceStartup + 5;
        while (GameplayInputBlocker.IsBlocked && Time.realtimeSinceStartup < end) yield return null;
        yield return null;
    }

    // The GPS offset moves a caption live by the given share of its font size.
    IEnumerator OffsetFollowsGps(WorkbenchPanel workbench)
    {
        var craft = Find(workbench, "Craft").GetComponentInChildren<TMP_Text>();
        var centering = craft.GetComponent<ButtonTextCentering>();
        var prefs = GpsSettings.Preferences;
        float old = prefs.buttonTextOffset;
        bool ok;
        try
        {
            craft.ForceMeshUpdate(); float before = centering.AppliedShift;
            prefs.buttonTextOffset = old + .05f; GpsSettings.NotifyConfigurationChanged();
            craft.ForceMeshUpdate(); float after = centering.AppliedShift;
            ok = Mathf.Abs(after - before - .05f * craft.fontSize) < .2f;
            lines.Add($"GPS-Versatz +5 % verschiebt \"{craft.text}\" um {after - before:0.00} (erwartet {.05f * craft.fontSize:0.00}): {(ok ? "ok" : "FEHLER")}");
        }
        finally { prefs.buttonTextOffset = old; GpsSettings.NotifyConfigurationChanged(); }
        if (!ok) { failures++; failed.Add("GPS-Versatz wirkt nicht"); }
        yield return null;
    }

    static Button Find(Component root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

    IEnumerator Run()
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        var canvas = GameObject.Find("ScreenCanvas").transform;
        yield return Settle();
        yield return View("Hauptmenue", canvas, "Settings", "Progression");

        Find(menu, "New Game").onClick.Invoke(); yield return Settle();
        var newGame = Object.FindFirstObjectByType<NewGamePanel>();
        yield return View("NeuesSpiel", newGame.transform, "Start New Game");
        Object.Destroy(newGame.gameObject); yield return null;

        var slots = SaveSlotPanel.Show(canvas, false); yield return Settle();
        yield return View("Spielstaende", slots.transform, "Start Save", "Back");
        Object.Destroy(slots.gameObject); yield return null;

        var progression = MetaProgressionPanel.Show(canvas); yield return Settle();
        yield return View("Fortschritt", progression.transform, "Overview Back");
        progression.Close(); yield return null;

        var settings = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        settings.Open(); yield return Settle();
        yield return View("Einstellungen", settings.transform, "Fortsetzen", "Tab Tastenbelegung");
        typeof(SettingsPanel).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(settings, null);
        yield return null;

        RunNavigation.NewGame("Knopftext-Test"); yield return Ready();
        var player = Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        bool follow = player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform
            && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled;
        lines.Add("Camera follows player in fresh run: " + follow);
        if (!follow) failed.Add("Camera follow inactive");

        foreach (var sign in Object.FindObjectsByType<EntrySign>(FindObjectsSortMode.None))
            Measure("Schild", sign.Button, sign.Button.GetComponent<Image>(), sign.Label);

        var pause = Object.FindFirstObjectByType<RunPauseMenu>();
        pause.Open(); yield return Settle();
        yield return View("Pausemenue", pause.transform, "Resume", "Challenges");
        var challenges = Find(pause, "Challenges");
        if (challenges) { challenges.onClick.Invoke(); yield return Settle(); yield return View("Herausforderungen", pause.transform, "Challenges Back"); }
        var challengePanel = pause.GetComponentInChildren<ChallengePanel>();
        if (challengePanel) { challengePanel.Close(); yield return null; }
        pause.Close(); yield return null;

        var inventory = InventoryManager.Instance;
        var workbench = Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var nails = workbench.recipes.First(r => r && r.name == "Nails");
        if (nails.TryGetCosts(out var costs)) foreach (var cost in costs) inventory.AddStartingItem(cost.Key, cost.Value * 10);
        workbench.ShowPanel(true);
        workbench.SetCategory(CraftingRecipe.RecipeCategory.Automatic); workbench.SetSearch(""); workbench.SetFavoritesOnly(false); workbench.SetFilter(false);
        workbench.SelectRecipe(nails);
        workbench.SetQuantity(Mathf.Max(1, 4 / Mathf.Max(1, nails.outputAmount)));
        yield return Settle();
        yield return View("Werkbank", workbench.transform, "Craft", "Category_Tools");
        if (phase == "Nachher") yield return OffsetFollowsGps(workbench);
        workbench.ShowPanel(false); yield return Unblocked();

        var storage = SurfaceStorageBuilding.Active.FirstOrDefault();
        if (storage)
        {
            var storagePanel = StoragePanel.Ensure();
            storagePanel.Show(storage); yield return Settle();
            yield return View("Lager", storagePanel.transform, "Deposit Ores", "Close");
            storagePanel.Hide(); yield return Unblocked();
        }

        var shop = Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        if (shop)
        {
            shop.ShowPanel(true); yield return Settle();
            yield return View("Shop", shop.transform, "Sell Selected");
            shop.ShowPanel(false); yield return Unblocked();
        }

        var inventoryUi = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUi)
        {
            inventoryUi.ShowPanel(); yield return Settle();
            yield return View("Inventar", inventoryUi.transform);
            inventoryUi.HidePanel(); yield return null;
        }
    }

    IEnumerator View(string name, Transform root, params string[] zoom)
    {
        foreach (var button in root.GetComponentsInChildren<Button>(false))
        {
            if (!button.isActiveAndEnabled || button.GetComponentsInParent<CanvasGroup>().Any(g => g.alpha < .05f)) continue;
            var frame = button.targetGraphic as Image;
            if (!frame) frame = button.GetComponent<Image>();
            if (!frame) continue;
            foreach (var label in Captions(button, frame)) Measure(name, button, frame, label);
        }
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            File.WriteAllBytes($"Temp/ButtonText/{phase}-{name}.png", shot.EncodeToPNG());
            foreach (string target in zoom)
            {
                var button = Find(root, target);
                if (button && button.isActiveAndEnabled) Crop(shot, (RectTransform)button.transform, $"Temp/ButtonText/{phase}-{name}-Zoom-{target}.png");
            }
        }
        finally { Destroy(shot); }
    }

    // A caption is a vertically centred label whose box covers the middle of the button frame.
    static IEnumerable<TMP_Text> Captions(Button button, Image frame)
    {
        var rect = frame.rectTransform.rect;
        foreach (var label in button.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!label.isActiveAndEnabled || string.IsNullOrWhiteSpace(label.text)) continue;
            var vertical = label.verticalAlignment;
            if (vertical != VerticalAlignmentOptions.Middle && vertical != VerticalAlignmentOptions.Geometry && vertical != VerticalAlignmentOptions.Capline) continue;
            var box = label.rectTransform; var local = box.rect;
            float top = frame.rectTransform.InverseTransformPoint(box.TransformPoint(new Vector3(0, local.yMax))).y;
            float bottom = frame.rectTransform.InverseTransformPoint(box.TransformPoint(new Vector3(0, local.yMin))).y;
            if (top - bottom < rect.height * .4f || Mathf.Abs((top + bottom) / 2 - rect.center.y) > rect.height * .2f) continue;
            yield return label;
        }
    }

    void Measure(string view, Button button, Image frame, TMP_Text label)
    {
        if (!frame || !label || !label.isActiveAndEnabled || string.IsNullOrWhiteSpace(label.text)) return;
        label.ForceMeshUpdate();
        var info = label.textInfo;
        var frameRect = frame.rectTransform;
        var inner = ButtonTextCentering.InnerFrame(frame);
        float inkTop = float.MinValue, inkBottom = float.MaxValue, inkLeft = float.MaxValue, inkRight = float.MinValue;
        float firstBase = float.NaN, lastBase = float.NaN, scale = 0;
        bool letters = false;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || c.textElement == null) continue;
            var m = c.textElement.glyph.metrics;
            float top = c.baseLine + m.horizontalBearingY * c.scale, bottom = top - m.height * c.scale;
            float left = c.origin + m.horizontalBearingX * c.scale, right = left + m.width * c.scale;
            Vector2 a = ToFrame(label, frameRect, new Vector2(left, bottom)), b = ToFrame(label, frameRect, new Vector2(right, top));
            inkTop = Mathf.Max(inkTop, b.y); inkBottom = Mathf.Min(inkBottom, a.y);
            inkLeft = Mathf.Min(inkLeft, a.x); inkRight = Mathf.Max(inkRight, b.x);
            if (float.IsNaN(firstBase)) { firstBase = ToFrame(label, frameRect, new Vector2(0, c.baseLine)).y; scale = c.scale; }
            lastBase = ToFrame(label, frameRect, new Vector2(0, c.baseLine)).y;
            letters |= char.IsLetterOrDigit(c.character);
        }
        if (float.IsNaN(firstBase)) return;
        labels++;
        float unit = frameRect.InverseTransformVector(label.rectTransform.TransformVector(Vector3.up)).y;
        float cap = label.font.faceInfo.capLine * scale * unit;
        float topGap = inner.yMax - (firstBase + cap), bottomGap = lastBase - inner.yMin;
        float balance = letters ? topGap - bottomGap : (inner.yMax - inkTop) - (inkBottom - inner.yMin);
        float worstTop = firstBase + Extreme(label.font, Ascenders, true) * scale * unit;
        float worstBottom = lastBase + Extreme(label.font, Descenders, false) * scale * unit;
        bool visibleFrame = frame.color.a > .05f && frame.sprite;
        bool inkInside = inkTop <= inner.yMax + InkTolerance && inkBottom >= inner.yMin - InkTolerance;
        bool sidesInside = inkLeft >= inner.xMin - InkTolerance && inkRight <= inner.xMax + InkTolerance;
        bool worstInside = worstTop <= inner.yMax + InkTolerance && worstBottom >= inner.yMin - InkTolerance;
        bool centred = Mathf.Abs(balance) <= BalanceTolerance;
        // Horizontal: ink gaps to the label box minus margins (where TMP centres the line), measured in frame units.
        var box = label.rectTransform.rect; var margin = label.margin;
        float boxLeft = ToFrame(label, frameRect, new Vector2(box.xMin + margin.x, box.center.y)).x;
        float boxRight = ToFrame(label, frameRect, new Vector2(box.xMax - margin.z, box.center.y)).x;
        bool centreAligned = label.horizontalAlignment == HorizontalAlignmentOptions.Center;
        float sideBalance = (inkLeft - boxLeft) - (boxRight - inkRight);
        bool sideCentred = !centreAligned || phase == "Vorher" || Mathf.Abs(sideBalance) <= SideTolerance;
        bool component = label.GetComponent<ButtonTextCentering>();
        bool ok = !visibleFrame || (inkInside && sidesInside && worstInside && centred && sideCentred && (phase == "Vorher" || component));
        string metrics = $"{label.fontSize:0.#}pt | Rahmen innen {inner.height:0.#} | oben {topGap:0.0} unten {bottomGap:0.0} Differenz {balance:+0.0;-0.0;0.0}"
            + (centreAligned ? $" | links/rechts {sideBalance:+0.0;-0.0;0.0}" : "")
            + $" | Tinte {(inkInside ? "innen" : "RAGT HERAUS")}{(sidesInside ? "" : " (seitlich)")} | Ä/g {(worstInside ? "passt" : "RAGT HERAUS")}"
            + (visibleFrame ? "" : " | Rahmen unsichtbar") + (phase == "Nachher" ? component ? " | zentriert" : " | OHNE Zentrierung" : "");
        entries.Add((view, $"{button.name} \"{label.text}\"", metrics, ok));
        if (!ok) failures++;
    }

    readonly List<(string view, string caption, string metrics, bool ok)> entries = new();

    // Buttons of one kind (e.g. 30 key bindings) share their metrics; one line per view and metric set.
    void WriteEntries()
    {
        foreach (var group in entries.GroupBy(e => (e.view, e.metrics)))
        {
            var captions = group.Select(e => e.caption).ToList();
            string line = $"{group.Key.view} | {group.Key.metrics} | {captions.Count}× {string.Join(", ", captions.Take(4))}{(captions.Count > 4 ? ", …" : "")}";
            lines.Add(line);
            if (!group.First().ok) failed.Add(line);
        }
    }

    static Vector2 ToFrame(TMP_Text label, RectTransform frame, Vector2 local) => frame.InverseTransformPoint(label.rectTransform.TransformPoint(local));

    // Highest glyph top (or lowest glyph bottom) of the probe characters in font units at scale 1.
    static float Extreme(TMP_FontAsset font, string[] characters, bool top)
    {
        float result = 0;
        foreach (string s in characters)
        {
            if (!font.HasCharacter(s[0], false, true) || !font.characterLookupTable.TryGetValue(s[0], out var character)) continue;
            var m = character.glyph.metrics;
            result = top ? Mathf.Max(result, m.horizontalBearingY) : Mathf.Min(result, m.horizontalBearingY - m.height);
        }
        return result;
    }

    static void Crop(Texture2D shot, RectTransform target, string path)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        float padX = (max.x - min.x) * .12f + 12, padY = (max.y - min.y) * .35f + 12;
        int x = Mathf.Clamp(Mathf.FloorToInt(min.x - padX), 0, shot.width - 1), y = Mathf.Clamp(Mathf.FloorToInt(min.y - padY), 0, shot.height - 1);
        int w = Mathf.Clamp(Mathf.CeilToInt(max.x - min.x + 2 * padX), 1, shot.width - x), h = Mathf.Clamp(Mathf.CeilToInt(max.y - min.y + 2 * padY), 1, shot.height - y);
        int zoom = Mathf.Clamp(Mathf.FloorToInt(900f / w), 1, 4);
        var crop = new Texture2D(w * zoom, h * zoom, TextureFormat.RGB24, false);
        var source = shot.GetPixels(x, y, w, h);
        var pixels = new Color[w * zoom * h * zoom];
        for (int py = 0; py < h * zoom; py++)
            for (int px = 0; px < w * zoom; px++) pixels[py * w * zoom + px] = source[py / zoom * w + px / zoom];
        crop.SetPixels(pixels); crop.Apply();
        File.WriteAllBytes(path, crop.EncodeToPNG());
        Destroy(crop);
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
