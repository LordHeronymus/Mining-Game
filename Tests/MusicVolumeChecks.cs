using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MusicVolumeDragChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play required");
        var passed = new List<string>();
        void Check(bool valid, string label) { if (!valid) throw new Exception(label); passed.Add(label); }
        var fields = typeof(GpsSettings).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => new[] { "profile", "document", "savedJson", "dirty", "<Warning>k__BackingField" }.Contains(f.Name)).ToArray();
        var original = fields.ToDictionary(f => f, f => f.GetValue(null));
        string committedSession = SessionState.GetString("GPS.Committed", "");
        var profile = Object.Instantiate(GpsSettings.Profile);
        string path = "Assets/MusicVolumeTest-" + Guid.NewGuid().ToString("N") + ".asset";
        var panel = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        var document = JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(GpsSettings.Document));
        float originalAudio = AudioListener.volume;
        Action savedHandler = null, changedHandler = null;
        try
        {
            AssetDatabase.CreateAsset(profile, path);
            GpsSettings.UseProfile(profile, document);
            panel.Open();
            var buttons = panel.GetComponentsInChildren<Button>(true);
            Button Find(string name) => buttons.First(b => b.name == name);
            Find("Tab Audio").onClick.Invoke();
            var slider = panel.GetComponentsInChildren<Slider>().First(s => s.name == "Musik");
            var pending = slider.GetComponent<SettingsSliderCommit>();
            int saves = 0, changes = 0;
            savedHandler = () => saves++; changedHandler = () => changes++;
            GpsSettings.Saved += savedHandler; GpsSettings.Changed += changedHandler;
            string committed = GpsSettings.CommittedJson;
            string beforeFile = File.ReadAllText(path);
            float committedAlpha = document.preferences.panelElementAlpha;
            document.preferences.panelElementAlpha = committedAlpha > .5f ? .3f : .8f;
            var rect = slider.GetComponent<RectTransform>();
            PointerEventData Pointer(float value) => new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(11 + 428 * value, -22, 0)))
            };
            var watch = Stopwatch.StartNew();
            ExecuteEvents.Execute(slider.gameObject, Pointer(.05f), ExecuteEvents.pointerDownHandler);
            for (int i = 1; i <= 120; i++) slider.OnDrag(Pointer(.05f + .9f * i / 120));
            watch.Stop();
            Check(Mathf.Approximately(AudioListener.volume, originalAudio) && Mathf.Abs(PlayerSettings.Music - .95f) < .001f, "120 pointer drag updates apply volume immediately");
            Check(saves == 0 && changes == 0, "Dragging emits no GPS save or broad change events");
            Check(GpsSettings.CommittedJson == committed && File.ReadAllText(path) == beforeFile, "Dragging leaves committed configuration and disk unchanged");
            Check(slider.transform.parent.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "95 %"), "Displayed percentage follows dragging");
            ExecuteEvents.Execute(slider.gameObject, Pointer(.95f), ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(slider.gameObject, Pointer(.95f), ExecuteEvents.endDragHandler);
            Check(saves == 1, "Release and end-drag persist once");
            Check(Mathf.Abs(JsonUtility.FromJson<GpsDocument>(profile.documentJson).preferences.musicVolume - .95f) < .001f, "Final volume is stored in the GPS profile");
            Check(JsonUtility.FromJson<GpsDocument>(profile.documentJson).preferences.panelElementAlpha == committedAlpha && GpsSettings.HasUnsavedChanges,
                "Unrelated pending GPS preference remains unsaved");
            pending.Commit(); Check(saves == 1, "Repeated commit does not save again");
            slider.value = .42f; Find("Tab Anzeige").onClick.Invoke();
            Check(saves == 2 && Mathf.Abs(JsonUtility.FromJson<GpsDocument>(profile.documentJson).preferences.musicVolume - .42f) < .001f,
                "Changing tabs commits pending volume");
            Find("Tab Audio").onClick.Invoke(); slider.value = .63f;
            Find("Fortsetzen").onClick.Invoke();
            Check(saves == 3 && !panel.IsOpen && Mathf.Abs(JsonUtility.FromJson<GpsDocument>(profile.documentJson).preferences.musicVolume - .63f) < .001f,
                "Closing panel commits pending volume");
            panel.Open(); slider.value = .28f;
            pending.OnDeselect(new BaseEventData(EventSystem.current));
            Check(saves == 4, "Focus loss commits keyboard/programmatic changes");
            var save = pending.Save; pending.Save = () => false; slider.value = .35f;
            pending.Commit(); pending.Save = save; pending.Commit();
            Check(saves == 5, "Failed commit retains pending value for retry");
            Find("Fortsetzen").onClick.Invoke();
            File.WriteAllLines("Temp/MusicVolumeDragChecks.txt", passed.Concat(new[] { "120 drag updates: " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms total", "PASS" }));
            return new { passed = passed.Count, dragUpdates = 120, milliseconds = watch.Elapsed.TotalMilliseconds, saveEventsDuringDrag = 0 };
        }
        catch (Exception e) { File.WriteAllLines("Temp/MusicVolumeDragChecks.txt", passed.Concat(new[] { "FAIL: " + e })); throw; }
        finally
        {
            if (panel && panel.IsOpen) panel.GetComponentsInChildren<Button>(true).First(b => b.name == "Fortsetzen").onClick.Invoke();
            if (savedHandler != null) GpsSettings.Saved -= savedHandler;
            if (changedHandler != null) GpsSettings.Changed -= changedHandler;
            foreach (var entry in original) entry.Key.SetValue(null, entry.Value);
            SessionState.SetString("GPS.Committed", committedSession);
            AudioListener.volume = originalAudio;
            AssetDatabase.DeleteAsset(path);
        }
    }
}

