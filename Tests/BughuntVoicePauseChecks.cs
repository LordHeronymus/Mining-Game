using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks that a regular voice line which takes over after an interrupt fade during the full pause (pause menu,
// settings, F1) is held like any other line instead of playing through the pause as a forced F1 test line.
public static class BughuntVoicePauseChecks
{
    const string Report = "Temp/BughuntVoicePauseChecks.txt";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var created = new List<Object>();
        var pausedProperty = typeof(GamePause).GetProperty(nameof(GamePause.IsPaused));
        bool wasPaused = GamePause.IsPaused;
        float timeScale = Time.timeScale;
        var randomState = UnityEngine.Random.state;
        try
        {
            var settings = ScriptableObject.CreateInstance<VoiceLineSettingsAsset>(); settings.hideFlags = HideFlags.HideAndDontSave; created.Add(settings);
            var library = ScriptableObject.CreateInstance<VoiceLineLibraryAsset>(); library.hideFlags = HideFlags.HideAndDontSave; created.Add(library);
            var clip = AudioClip.Create("Bughunt voice pause", 4410, 1, 44100, false); clip.hideFlags = HideFlags.HideAndDontSave; created.Add(clip);
            var take = new VoiceLineTake { clip = clip, gain = 1f };
            var line = new VoiceLine { id = "bughunt" }; line.takes.Add(take);
            var set = new VoiceLineSet { trigger = VoiceLineEvent.FallBig }; set.lines.Add(line);
            library.sets.Add(set);
            Check(library.Take(VoiceLineEvent.FallBig, 0, 0) == take && clip.loadState == AudioDataLoadState.Loaded, "Testbibliothek mit geladenem Clip");

            var host = new GameObject("Bughunt voice pause checks") { hideFlags = HideFlags.HideAndDontSave }; created.Add(host);
            var player = host.AddComponent<VoiceLinePlayer>();
            var source = host.AddComponent<AudioSource>(); source.playOnAwake = false; source.ignoreListenerPause = false;
            void Set(string name, object value) => typeof(VoiceLinePlayer).GetField(name, Hidden).SetValue(player, value);
            T Get<T>(string name) => (T)typeof(VoiceLinePlayer).GetField(name, Hidden).GetValue(player);
            Set("settings", settings); Set("library", library); Set("source", source);
            Set("director", new VoiceLineDirector(1) { lines = e => 1, takes = (e, l) => 1 });
            var step = typeof(VoiceLinePlayer).GetMethod("StepPlayback", Hidden);
            Check(step != null && pausedProperty?.GetSetMethod(true) != null, "StepPlayback und GamePause.IsPaused per Reflection erreichbar");
            double now = Time.realtimeSinceStartupAsDouble;

            void Interrupt(bool forced)
            {
                // An old line is fading out; the new one (FallBig) waits in nextPlay; the full pause is open.
                source.Stop(); source.ignoreListenerPause = false;
                Set("activeTake", take); Set("activeClip", clip); Set("loading", false); Set("paused", false); Set("testLine", false);
                Set("fading", true); Set("fadeFrom", 1f); Set("fadeStart", now - 10d); Set("fade", 1f);
                Set("nextPlay", new VoiceLinePlay { trigger = VoiceLineEvent.FallBig, priority = 100, interrupt = true, forced = forced });
                Time.timeScale = 0f; pausedProperty.GetSetMethod(true).Invoke(null, new object[] { true });
                step.Invoke(player, new object[] { now });
            }

            Interrupt(false);
            Check(Get<VoiceLineTake>("activeTake") == take && !Get<bool>("fading"), "Ausblenden endet und startet den unterbrechenden Satz");
            Check(!Get<bool>("testLine") && !source.ignoreListenerPause, "Normaler Satz nach Unterbrechung in der Pause wird nicht als Testsatz behandelt");
            step.Invoke(player, new object[] { now + .02d });
            Check(Get<bool>("paused"), "Normaler Satz wird in der vollen Pause angehalten");

            Interrupt(true);
            Check(Get<bool>("testLine") && source.ignoreListenerPause, "Erzwungener F1-Testsatz spielt weiterhin durch die Pause");
            step.Invoke(player, new object[] { now + .02d });
            Check(!Get<bool>("paused"), "Erzwungener F1-Testsatz wird nicht angehalten");
            source.Stop();

            string report = string.Join("\n", checks) + "\nPASS " + checks.Count + "\n";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, report); return "PASS " + checks.Count;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, "FAIL " + ex + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            pausedProperty?.GetSetMethod(true)?.Invoke(null, new object[] { wasPaused });
            Time.timeScale = timeScale;
            UnityEngine.Random.state = randomState;
            foreach (var obj in created) if (obj) Object.DestroyImmediate(obj);
        }
    }
}
