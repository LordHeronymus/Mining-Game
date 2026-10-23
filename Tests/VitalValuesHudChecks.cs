using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode. Health and energy values right of the gem rows ("80 / 100"): same size, outline and shadow as the
// carry weight line, red below 25 %, pulsing with the low-health heartbeat and the low-energy beep.
// Uses temporary objects only. Result in Temp/VitalValuesHudChecks.txt.
public static class VitalValuesHudChecks
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static object Invoke(object target, string method, params object[] args)
    {
        var info = target.GetType().GetMethod(method, Hidden);
        if (info == null) throw new Exception(target.GetType().Name + "." + method + " fehlt");
        return info.Invoke(target, args);
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var canvas = new GameObject("Vital Values Probe Canvas", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // Colour: cream from 25 %, red below.
            Color cream = CompactHud.VitalValueColor(1f), red = CompactHud.VitalValueColor(.24f);
            Check(cream != red && red.r > .95f && red.g < .45f, "Unter 25 % rot");
            Check(CompactHud.VitalValueColor(.25f) == cream && CompactHud.VitalValueColor(.5f) == cream, "Ab 25 % normal");
            Check(CompactHud.VitalValueColor(0f) == red, "Bei 0 rot");
            Check(CompactHud.LowVitalFraction == StatsManager.LowHealthHeartbeatThresholdFraction, "Grenze wie der Herzschlag (25 %)");

            // Pulse: 1 at rest, larger at the peak, clamped.
            Check(Mathf.Approximately(CompactHud.PulseScale(0f), 1f), "Ruhe: normale Grösse");
            Check(CompactHud.PulseScale(1f) > 1.1f && CompactHud.PulseScale(1f) < 1.3f, "Puls: leicht grösser");
            Check(Mathf.Approximately(CompactHud.PulseScale(5f), CompactHud.PulseScale(1f)), "Puls begrenzt");

            // Layout: values right of the chains, same look as the carry weight.
            var hudObject = new GameObject("CompactHud", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            hudObject.transform.SetParent(canvas.transform, false);
            var hud = hudObject.AddComponent<CompactHud>();
            Invoke(hud, "Build");
            var carry = hud.transform.Find("Status Strip/Carry Weight/Carry Value").GetComponent<TMP_Text>();
            foreach (string name in new[] { "Health", "Energy" })
            {
                var fill = (RectTransform)hud.transform.Find("Status Strip/" + name + " Fill");
                var value = hud.transform.Find("Status Strip/" + name + " Value").GetComponent<TMP_Text>();
                var rect = value.rectTransform;
                Check(value.gameObject.activeSelf, name + ": Wert sichtbar");
                Check(rect.anchoredPosition.x >= fill.anchoredPosition.x + fill.sizeDelta.x, name + ": rechts neben der Kugelreihe");
                Check(Mathf.Abs(rect.anchoredPosition.y - (fill.anchoredPosition.y - fill.sizeDelta.y / 2f)) < 1f, name + ": auf der Höhe der Kugeln");
                Check(rect.pivot == new Vector2(0, .5f), name + ": Puls wächst nach rechts");
                Check(value.alignment == TextAlignmentOptions.MidlineLeft, name + ": links ausgerichtet");
                Check(Mathf.Approximately(value.fontSize, carry.fontSize) && !value.enableAutoSizing, name + ": gleiche Schriftgrösse wie die Traglast");
                Check(value.font == carry.font && value.fontSharedMaterial == carry.fontSharedMaterial, name + ": gleiche Schrift, Kontur und Schatten");
                Check(!value.raycastTarget, name + ": kein Klickfänger");
            }

            // Energy pulse starts on each new beep, not on the first observation.
            Invoke(hud, "TrackEnergyBeep", 3);
            Check(Field<float>(hud, "energyBeepPulseTime") == 0f, "Erster Stand löst keinen Puls aus");
            Invoke(hud, "TrackEnergyBeep", 3);
            Check(Field<float>(hud, "energyBeepPulseTime") == 0f, "Ohne Piepsen kein Puls");
            Invoke(hud, "TrackEnergyBeep", 4);
            Check(Field<float>(hud, "energyBeepPulseTime") > 0f, "Neues Piepsen startet den Puls");
            Check(typeof(EnergyManager).GetProperty("LowEnergyBeepCount") != null, "EnergyManager zählt die Piepser");

            File.WriteAllText("Temp/VitalValuesHudChecks.txt", string.Join("\n", checks) + "\n");
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } wrapped) ex = wrapped.InnerException;
            File.WriteAllText("Temp/VitalValuesHudChecks.txt", string.Join("\n", checks) + "\nFAIL " + ex + "\n");
            throw;
        }
        finally
        {
            Object.DestroyImmediate(canvas);
        }
    }
}
