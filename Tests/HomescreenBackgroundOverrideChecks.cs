using System;
using System.Linq;
using UnityEngine;

public static class HomescreenBackgroundOverrideChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Run in MainMenu Play Mode");
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        var visual = UnityEngine.Object.FindFirstObjectByType<HomeCaveVisual>();
        if (!menu || !visual) throw new Exception("Main menu required");
        var data = GpsSettings.Tests;
        var original = data.homescreenBackground;
        bool disabled = data.testModeDisabled;
        int slot = menu.SelectedSaveSlot;
        int automatic = slot > 0 ? GameSaveSystem.GetHomeLayer(slot) : 1;
        int checks = 0;
        var dirtyField = typeof(GpsSettings).GetField("dirty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool dirty = (bool)dirtyField.GetValue(null);
        Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); checks++; };
        try {
            check(new GameplayTestSettingsData().homescreenBackground == HomescreenBackgroundMode.Auto, "Default Auto");
            check(JsonUtility.FromJson<GameplayTestSettingsData>("{\"version\":9}").homescreenBackground == HomescreenBackgroundMode.Auto, "Old profile Auto");
            check(GpsSchema.Sections.Any(s => s.tab == GpsSchema.TestTab && s.fields.Any(f => f.name == "homescreenBackground")), "Test dropdown registered");
            data.testModeDisabled = false;
            foreach (var mode in Enum.GetValues(typeof(HomescreenBackgroundMode)).Cast<HomescreenBackgroundMode>()) {
                data.homescreenBackground = mode;
                GpsSettings.NotifyConfigurationChanged();
                int expected = mode == HomescreenBackgroundMode.Auto ? automatic : (int)mode;
                check(visual.Layer == expected, "Visual " + mode);
                check(LoadingAudio.HomeLayer == expected, "Ambience " + mode);
                check(visual.WaterEffectsEnabled == (expected == 3), "Water " + mode);
                check(menu.SelectedSaveSlot == slot, "Save unchanged " + mode);
                var roundtrip = JsonUtility.FromJson<GameplayTestSettingsData>(JsonUtility.ToJson(data));
                check(roundtrip.homescreenBackground == mode, "Serialization " + mode);
            }
            data.homescreenBackground = HomescreenBackgroundMode.L6;
            data.testModeDisabled = true;
            GpsSettings.NotifyConfigurationChanged();
            check(visual.Layer == automatic, "Disabled test mode uses Auto");
            data.testModeDisabled = false;
            data.homescreenBackground = (HomescreenBackgroundMode)99;
            check(GameplayTestSettings.ResolveHomescreenLayer(automatic) == automatic, "Invalid falls back");
            var invalid = GpsCodec.Read("homescreenBackground", typeof(HomescreenBackgroundMode), data.homescreenBackground, GpsSettings.Profile.Key);
            check(!GpsCodec.Validate(invalid, out _), "Reject invalid enum");
            data.homescreenBackground = HomescreenBackgroundMode.Auto;
            GpsSettings.NotifyConfigurationChanged();
            GpsRuntimePanel.Open();
            var panel = UnityEngine.Object.FindFirstObjectByType<GpsRuntimePanel>();
            var expanded = (System.Collections.Generic.HashSet<string>)typeof(GpsRuntimePanel).GetField("expanded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(panel);
            expanded.Add("Testeinstellungen/Entwicklung");
            panel.SelectTab(GpsSchema.TestTab);
            var dropdown = panel.GetComponentsInChildren<TMPro.TMP_Dropdown>().Single(d => d.options.Select(o => o.text).SequenceEqual(Enum.GetNames(typeof(HomescreenBackgroundMode))));
            check(dropdown.options.Count == 7, "Actual dropdown Auto/L1-L6");
            for (int i = 1; i <= 6; i++) {
                dropdown.value = i;
                check(visual.Layer == i && LoadingAudio.HomeLayer == i, "Actual dropdown change " + i);
            }
            dropdown.value = 0;
            check(visual.Layer == automatic, "Actual dropdown restores Auto");
            System.IO.File.WriteAllText("Temp/HomescreenBackgroundOverrideChecks.txt", checks + " checks passed; original test values restored in finally.");
            return new { checks, automatic, slot };
        } finally {
            data.homescreenBackground = original;
            data.testModeDisabled = disabled;
            dirtyField.SetValue(null, dirty);
            GpsRuntimePanel.Close();
            GpsSettings.NotifyConfigurationChanged();
        }
    }
}
