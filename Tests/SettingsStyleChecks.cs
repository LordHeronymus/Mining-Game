using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SettingsStyleChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play required");
        var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        view.Show(); view.Focus();
        var root = new GameObject("Settings Style Checks");
        Object.DontDestroyOnLoad(root); root.AddComponent<SettingsStyleProbe>();
        return "Started runtime settings checks";
    }
}
public sealed class SettingsStyleProbe : MonoBehaviour
{
    readonly List<string> passed = new();
    string previousDirectory;
    const string Report = "Temp/SettingsStyleChecks.txt";
    bool restored;
    void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        passed.Add(label); File.WriteAllLines(Report, passed);
    }
    Button Find(SettingsPanel panel, string name) => panel.GetComponentsInChildren<Button>(true).First(b => b.name == name);
    IEnumerator Start()
    {
        previousDirectory = GameSaveSystem.TestDirectory;
        var directory = Path.GetFullPath("Temp/SettingsStyleSaves-" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);
        File.Copy("Temp/SelectionEdit-639266992421592508/slot-3.thsave", Path.Combine(directory, "slot-3.thsave"));
        GameSaveSystem.TestDirectory = directory;
        var routines = new Stack<IEnumerator>(); routines.Push(Run());
        while (routines.Count > 0)
        {
            object current = null; bool more = false; Exception failure = null;
            try { more = routines.Peek().MoveNext(); if (more) current = routines.Peek().Current; }
            catch (Exception e) { failure = e; }
            if (failure != null) { File.AppendAllText(Report, "\nFAIL: " + failure); Cleanup(); yield break; }
            if (!more) { routines.Pop(); continue; }
            if (current is IEnumerator nested) routines.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "\nPASS"); Cleanup();
    }
    IEnumerator Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        yield return new WaitForSecondsRealtime(.5f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot("Temp/Settings-v2-" + name + ".png");
        yield return null; yield return null;
    }
    IEnumerator Run()
    {
        var panel = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        Check(panel && panel.transform.Find("Layout/Navigation Divider"), "Fresh runtime uses sidebar layout");
        panel.Open(); yield return null;
        Check(panel.IsOpen && Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Settings pause time and block gameplay");
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
        {
            var image = button.GetComponent<Image>(); var feedback = button.GetComponent<HomeButtonFeedback>();
            Check(image.sprite == HomeUi.Sprite("SaveButton") && feedback.normalPart == "SaveButton" && feedback.activePart == "SaveActive" && !feedback.animateScale,
                button.name + " uses actual homescreen sprites and stationary feedback");
            var text = button.GetComponentInChildren<TMP_Text>(); text.ForceMeshUpdate();
            Check(!text.isTextTruncated && text.fontStyle == FontStyles.Normal, button.name + " text fits wooden interior");
        }
        var scroll = panel.GetComponentInChildren<ScrollRect>();
        Check(scroll && scroll.content.rect.height > scroll.viewport.rect.height, "Complete binding list remains scrollable");
        scroll.verticalNormalizedPosition = 0; yield return null;
        var last = Find(panel, "Bind Map 0").GetComponent<RectTransform>();
        var lastCenter = scroll.viewport.InverseTransformPoint(last.TransformPoint(last.rect.center));
        Check(scroll.viewport.rect.Contains(lastCenter), "Last binding is reachable by scrolling");
        scroll.verticalNormalizedPosition = 1; yield return null;
        var tab = Find(panel, "Tab Tastenbelegung"); var tabRect = tab.GetComponent<RectTransform>();
        var before = tabRect.anchoredPosition;
        tab.GetComponent<HomeButtonFeedback>().OnPointerEnter(new PointerEventData(EventSystem.current));
        yield return new WaitForSecondsRealtime(.4f);
        Check(tabRect.anchoredPosition == before && tabRect.localScale == Vector3.one, "Hover preserves button size and position while paused");
        tab.GetComponent<HomeButtonFeedback>().OnPointerExit(new PointerEventData(EventSystem.current));
        yield return Capture("Keys");
        Find(panel, "Bind MoveLeft 0").onClick.Invoke(); yield return null;
        Check(Find(panel, "Bind MoveLeft 0").GetComponentInChildren<TMP_Text>().text == "…", "Binding button enters key capture");
        Find(panel, "Fortsetzen").onClick.Invoke(); yield return null;
        Check(!panel.IsOpen && Time.timeScale == 1 && !GameplayInputBlocker.IsBlocked, "Continue closes capture and releases pause");
        panel.Open(); Find(panel, "Tab Audio").onClick.Invoke(); yield return null;
        Check(panel.transform.Find("Layout/Audio").gameObject.activeSelf && !scroll.gameObject.activeInHierarchy, "Audio navigation selects only audio page");
        var audio = panel.GetComponentsInChildren<Slider>();
        Check(audio.Length == 4, "All four audio sliders are present");
        Check(audio.All(s => Mathf.Approximately(s.handleRect.rect.height, 32) && s.handleRect.GetComponent<Image>().sprite == HomeUi.Sprite("SaveHandle")), "Slider grips retain correct size and original brass artwork");
        var ambience = audio.First(s => s.name == "Ambiente");
        const string preference = "settings.audio.ambience";
        bool hadPreference = PlayerPrefs.HasKey(preference); float old = PlayerSettings.Ambience;
        try
        {
            var sliderRect = (RectTransform)ambience.transform;
            var click = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector3(11 + 428 * .37f, -22, 0))) };
            ambience.OnPointerDown(click); yield return null;
            Check(Mathf.Abs(PlayerSettings.Ambience - .37f) < .001f && ambience.transform.parent.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "37 %"), "Audio slider updates actual preference and percentage");
        }
        finally { ambience.value = old; if (!hadPreference) PlayerPrefs.DeleteKey(preference); PlayerPrefs.Save(); }
        yield return Capture("Audio");
        Find(panel, "Tab Anzeige").onClick.Invoke(); yield return null;
        Check(panel.transform.Find("Layout/Anzeige").gameObject.activeSelf && Find(panel, "Vollbild").isActiveAndEnabled && panel.GetComponentsInChildren<Slider>().Length == 1, "Display navigation shows fullscreen and UI scale");
        yield return Capture("Display");
        var rect = panel.GetComponent<RectTransform>(); var oldMin = rect.anchorMin; var oldMax = rect.anchorMax; var oldSize = rect.sizeDelta;
        try
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1024, 768), new Vector2(2560, 1080) })
            {
                rect.sizeDelta = size; Canvas.ForceUpdateCanvases(); yield return null;
                var layout = (RectTransform)panel.transform.Find("Layout");
                Check(layout.rect.width * layout.localScale.x <= size.x && layout.rect.height * layout.localScale.y <= size.y && Mathf.Approximately(layout.localScale.x, layout.localScale.y), "Panel fits without distortion at " + size);
            }
        }
        finally { rect.anchorMin = oldMin; rect.anchorMax = oldMax; rect.sizeDelta = oldSize; }
        Find(panel, "Fortsetzen").onClick.Invoke(); yield return null;
        Check(RunNavigation.LoadGame(3, out string error), "Load isolated gameplay world: " + error);
        double deadline = Time.realtimeSinceStartupAsDouble + 180;
        while ((RunNavigation.IsTransitioning || LoadingProgress.Active) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        Check(!RunNavigation.IsTransitioning && !LoadingProgress.Active, "Isolated gameplay loading completes");
        var player = Object.FindFirstObjectByType<PlayerMovement>(); var camera = Camera.main;
        Check(player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target == player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Fresh gameplay has active player camera follow and world clamp");
        var pause = Object.FindFirstObjectByType<RunPauseMenu>(); pause.Open(); yield return null;
        panel = Object.FindFirstObjectByType<SettingsPanel>(); panel.Open(); yield return null;
        Find(panel, "Tab Tastenbelegung").onClick.Invoke(); yield return null;
        yield return Capture("Gameplay");
        Find(panel, "Fortsetzen").onClick.Invoke(); yield return null;
        Check(RunPauseMenu.IsOpen && Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Closing settings preserves underlying pause menu");
        pause.Close(); yield return null;
        Check(Time.timeScale == 1 && !GameplayInputBlocker.IsBlocked, "Closing pause menu resumes gameplay");
        RunNavigation.MainMenu(); while (RunNavigation.IsTransitioning) yield return null;
        Check(MainMenuController.IsVisible, "Returned to homescreen");
    }
    void Cleanup() { GameSaveSystem.TestDirectory = previousDirectory; restored = true; Object.Destroy(gameObject); }
    void OnDestroy() { if (!restored) GameSaveSystem.TestDirectory = previousDirectory; }
}

