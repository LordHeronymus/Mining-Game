using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Edit Mode. Carry weight in the HUD: text "12,5 / 40 kg" with the inventory's carry colours, a plaque below the
// vitals that follows the setting "Traglast im HUD" (saved, on by default), and the switch on the display page.
// Uses temporary objects only; the PlayerPrefs key is restored. Result in Temp/CarryWeightHudChecks.txt.
public static class CarryWeightHudChecks
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Key = "settings.display.showCarryWeight";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static void Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, Hidden);
        if (info == null) throw new Exception(target.GetType().Name + "." + method + " fehlt");
        info.Invoke(target, null);
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        bool hadKey = PlayerPrefs.HasKey(Key);
        int oldValue = PlayerPrefs.GetInt(Key, 1);
        var canvas = new GameObject("Carry Weight Probe Canvas", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // Text and colour.
            Check(CompactHud.CarryWeightText(12.5, 40f, false) == "12,5 / 40 kg", "Anzeige 12,5 / 40 kg");
            Check(CompactHud.CarryWeightText(0, 30f, false) == "0 / 30 kg", "Leeres Inventar 0 / 30 kg");
            Check(CompactHud.CarryWeightText(1234.25, 270f, false) == "1.234,25 / 270 kg", "Tausendertrennzeichen und zwei Nachkommastellen");
            Check(CompactHud.CarryWeightText(12.5, 40f, true) == "12,5 kg", "Testmodus ohne Gewicht zeigt nur das Gewicht");
            Check(CompactHud.CarryWeightText(-.001, 40f, false) == "0 / 40 kg", "Rundungsreste unter 0 werden als 0 gezeigt");
            Color cream = CompactHud.CarryWeightColor(40, 40f, false), yellow = CompactHud.CarryWeightColor(40.5, 40f, false),
                red = CompactHud.CarryWeightColor(80.5, 40f, false);
            Check(cream != yellow && yellow != red && cream != red, "Drei Farben: normal, überladen, stark überladen");
            Check(CompactHud.CarryWeightColor(80, 40f, false) == yellow && CompactHud.CarryWeightColor(200, 40f, true) == cream,
                "Stufen wie im Inventar (bis doppelte Traglast gelb), Testmodus bleibt normal");

            // Setting: saved, on by default.
            PlayerPrefs.DeleteKey(Key);
            Check(PlayerSettings.ShowCarryWeight, "Standard: Traglast im HUD eingeschaltet");
            PlayerSettings.ShowCarryWeight = false;
            Check(!PlayerSettings.ShowCarryWeight && PlayerPrefs.GetInt(Key, 1) == 0, "Aus wird gespeichert");
            PlayerSettings.ShowCarryWeight = true;
            Check(PlayerSettings.ShowCarryWeight && PlayerPrefs.GetInt(Key, 0) == 1, "Ein wird gespeichert");

            // HUD plaque below the vitals, follows the setting, never catches clicks.
            var hudObject = new GameObject("CompactHud", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            hudObject.transform.SetParent(canvas.transform, false);
            var hud = hudObject.AddComponent<CompactHud>();
            Invoke(hud, "Build");
            var plaque = (RectTransform)hud.transform.Find("Status Strip/Carry Weight");
            Check(plaque, "Plakette \"Carry Weight\" im Status-Streifen links oben");
            var energy = (RectTransform)hud.transform.Find("Status Strip/Energy Fill");
            Check(-plaque.anchoredPosition.y >= -energy.anchoredPosition.y + energy.sizeDelta.y, "Plakette liegt unter dem Energiebalken");
            Check(plaque.Find("Carry Frame").GetComponent<Image>() && plaque.Find("Carry Value").GetComponent<TMP_Text>(), "Holzrahmen und Wert vorhanden");
            Check(plaque.Find("Carry Value").GetComponent<TMP_Text>().text.EndsWith(" kg"), "Wert wird beim Aufbau gesetzt");
            foreach (var graphic in plaque.GetComponentsInChildren<Graphic>(true))
                Check(!graphic.raycastTarget, "Kein Klickfänger: " + graphic.name);
            Check(plaque.gameObject.activeSelf, "Eingeschaltet: Plakette sichtbar");
            PlayerPrefs.SetInt(Key, 0); hud.RefreshCarryWeightVisibility();
            Check(!plaque.gameObject.activeSelf, "Ausgeschaltet: Plakette verborgen");
            PlayerPrefs.SetInt(Key, 1); hud.RefreshCarryWeightVisibility();
            Check(plaque.gameObject.activeSelf, "Wieder eingeschaltet: Plakette sichtbar");

            // Switch on the display page of the settings.
            var settingsObject = new GameObject("SettingsPanel", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            settingsObject.transform.SetParent(canvas.transform, false);
            var settings = settingsObject.AddComponent<SettingsPanel>();
            Invoke(settings, "Awake");
            var button = settings.transform.Find("Layout/Anzeige/Traglast im HUD")?.GetComponent<Button>();
            Check(button, "Einstellungen → Anzeige: Schalter \"Traglast im HUD\"");
            var caption = button.GetComponentInChildren<TextMeshProUGUI>();
            Check(caption.text == "Ein", "Schalter zeigt Ein");
            button.onClick.Invoke();
            Check(!PlayerSettings.ShowCarryWeight && caption.text == "Aus", "Klick schaltet aus");
            button.onClick.Invoke();
            Check(PlayerSettings.ShowCarryWeight && caption.text == "Ein", "Zweiter Klick schaltet wieder ein");
            var page = (RectTransform)settings.transform.Find("Layout/Anzeige");
            var row = (RectTransform)button.transform;
            Check(-row.anchoredPosition.y + row.sizeDelta.y <= page.sizeDelta.y, "Vierte Zeile passt auf die Anzeige-Seite");

            File.WriteAllText("Temp/CarryWeightHudChecks.txt", string.Join("\n", checks) + "\n");
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } wrapped) ex = wrapped.InnerException;
            File.WriteAllText("Temp/CarryWeightHudChecks.txt", string.Join("\n", checks) + "\nFAIL " + ex + "\n");
            throw;
        }
        finally
        {
            Object.DestroyImmediate(canvas);
            if (hadKey) PlayerPrefs.SetInt(Key, oldValue); else PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
