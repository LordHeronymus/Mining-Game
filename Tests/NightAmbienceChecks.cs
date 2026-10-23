using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks for the night ambience from 23.10.2026: at night the surface plays the cricket loop instead of the
// meadow loop with birds, the clip is in the GPS clip catalog and the day/night fade time comes from GPS.
// Edit Mode, runs on a hidden probe object. The sound itself is accepted by the user in the game.
public static class NightAmbienceChecks
{
    const string Report = "Temp/NightAmbienceChecks.txt";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + label + "\n");
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "");
        var root = new GameObject("Night ambience probe") { hideFlags = HideFlags.HideAndDontSave };
        var skyObject = new GameObject("Night sky probe") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var night = Resources.Load<AudioClip>(SurfaceAmbience.NightClipResource);
            Check(night, "Night clip loads from Resources/" + SurfaceAmbience.NightClipResource);
            Check(Mathf.Abs(night.length - 9.5f) < .05f, "Night clip is the 9.5 s cricket loop (" + night.length + " s)");
            Check(GpsAudio.Tuning(night) != null, "Night clip is in the GPS clip catalog");
            var meadow = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AC Audio/Ambience/MeadowAmbience.wav");
            Check(meadow, "Meadow clip found");

            root.transform.position = new Vector3(0f, 500f, 0f); // above any terrain: full surface gain
            var ambience = root.AddComponent<SurfaceAmbience>();
            GpsSettings.ApplyComponent(ambience);
            Check(Mathf.Approximately(ambience.nightFadeSeconds, 8f), "GPS day/night fade is 8 s (" + ambience.nightFadeSeconds + ")");
            typeof(SurfaceAmbience).GetField("clip", Private).SetValue(ambience, meadow);
            Invoke(ambience, "Awake");

            skyObject.AddComponent<ParallaxLayer>();
            var sky = skyObject.AddComponent<SkyController>();
            typeof(SurfaceAmbience).GetField("sky", Private).SetValue(ambience, sky);

            sky.SetNight(true); Invoke(sky, "Update");
            Invoke(ambience, "Update");
            Check(Mathf.Approximately(ambience.NightMix, 1f), "Night: mix starts fully on night (" + ambience.NightMix + ")");
            Check(Running(ambience, "night") && !Running(ambience, "day"), "Night: only the cricket loop runs, the meadow (birds) is stopped");
            Check(LoopClip(ambience, "night") == night, "Night loop plays the cricket clip");

            ambience.nightFadeSeconds = 0f;
            sky.SetNight(false); Invoke(sky, "Update");
            Invoke(ambience, "Update");
            Check(Mathf.Approximately(ambience.NightMix, 0f), "Day: mix back to day (" + ambience.NightMix + ")");
            Check(Running(ambience, "day") && !Running(ambience, "night"), "Day: only the meadow loop runs");
            Check(LoopClip(ambience, "day") == meadow, "Day loop plays the meadow clip");

            Invoke(ambience, "OnDisable");
            Check(!Running(ambience, "day") && !Running(ambience, "night"), "Disable stops both loops");
            File.AppendAllText(Report, "PASS all checks\n");
            return new { night = night.name, length = night.length };
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error.Message + "\n");
            throw;
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(skyObject);
        }
    }

    static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private | BindingFlags.Public).Invoke(target, null);

    static object Loop(SurfaceAmbience ambience, string name) => typeof(SurfaceAmbience).GetField(name, Private).GetValue(ambience);
    static bool Running(SurfaceAmbience ambience, string name) { var loop = Loop(ambience, name); return (bool)loop.GetType().GetField("running").GetValue(loop); }
    static AudioClip LoopClip(SurfaceAmbience ambience, string name) { var loop = Loop(ambience, name); return (AudioClip)loop.GetType().GetField("clip").GetValue(loop); }
}
