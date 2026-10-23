using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Money labels count softly (MoneyDisplay): a change starts at the shown value and reaches the new amount after
// F1 → UI → Geldanzeige → Zähldauer, also downwards; 0 s jumps; run start and loaded saves jump; several labels show the same.
// Runs in Edit Mode on a temporary StatsManager and labels; the elapsed time is set through MoneyDisplay.startTime.
// The GPS value is restored. Result in Temp/MoneyDisplayChecks.txt.
public static class MoneyDisplayChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var previousStats = StatsManager.Instance; var previousAudio = AudioManager.Instance;
        float previousSeconds = GpsSettings.Preferences.moneyCountSeconds;
        var startTime = typeof(MoneyDisplay).GetField("startTime", BindingFlags.Static | BindingFlags.NonPublic);
        var host = new GameObject("MoneyDisplayChecks") { hideFlags = HideFlags.HideAndDontSave };
        var labelA = new GameObject("Money A", typeof(RectTransform), typeof(TextMeshProUGUI)) { hideFlags = HideFlags.HideAndDontSave };
        var labelB = new GameObject("Money B", typeof(RectTransform), typeof(TextMeshProUGUI)) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Check(startTime != null, "MoneyDisplay.startTime gefunden");
            void Elapsed(float seconds) => startTime.SetValue(null, Time.unscaledTime - seconds);
            AudioManager.Instance = null;
            Check(Mathf.Approximately(new GpsPreferences().moneyCountSeconds, .6f), "Standard-Zähldauer 0,6 s");
            GpsSettings.Preferences.moneyCountSeconds = 1f;
            var stats = host.AddComponent<StatsManager>();
            StatsManager.Instance = stats;
            // Set directly: AddMoney skips withdrawals in the test mode "Unendlich Geld".
            var money = typeof(StatsManager).GetProperty("Money");
            void Add(int amount) => money.SetValue(stats, stats.Money + amount);
            Add(1000 - stats.Money);
            MoneyDisplay.Snap(stats);
            var a = MoneyDisplay.Bind(labelA.GetComponent<TMP_Text>()); var b = MoneyDisplay.Bind(labelB.GetComponent<TMP_Text>());
            Check(a && b && labelA.GetComponent<MoneyDisplay>() == a && MoneyDisplay.Bind(labelA.GetComponent<TMP_Text>()) == a,
                "Bind hängt genau eine Anzeige an");
            Check(MoneyDisplay.Shown == 1000 && labelA.GetComponent<TMP_Text>().text == ShopMoneyFormatter.Format(1000), "Startwert ohne Zählen");

            Add(1000);
            Check(MoneyDisplay.Shown == 1000 && MoneyDisplay.IsCounting, "Zugang beginnt beim angezeigten Wert");
            Elapsed(.5f);
            long half = MoneyDisplay.Shown;
            Check(half > 1500 && half < 2000, "Nach der halben Zeit zwischen alt und neu, mit Abbremsen (" + half + ")");
            a.Refresh(); b.Refresh();
            Check(labelA.GetComponent<TMP_Text>().text == labelB.GetComponent<TMP_Text>().text &&
                labelA.GetComponent<TMP_Text>().text == ShopMoneyFormatter.Format(half), "Alle Anzeigen zeigen denselben Wert");
            Elapsed(1.01f);
            Check(MoneyDisplay.Shown == 2000 && !MoneyDisplay.IsCounting, "Nach der Zähldauer genau der neue Wert");

            // A new change during a count starts from the value shown now, not from the old target.
            Add(-1500);
            Check(MoneyDisplay.Shown == 2000, "Abgang beginnt beim angezeigten Wert");
            Elapsed(.5f);
            long down = MoneyDisplay.Shown;
            Check(down < 2000 && down > 500, "Abgang zählt herunter (" + down + ")");
            Add(100);
            Check(MoneyDisplay.Shown == down, "Änderung mitten im Zählen springt nicht");
            Elapsed(1.01f);
            Check(MoneyDisplay.Shown == 600, "Endwert nach Unterbrechung");

            GpsSettings.Preferences.moneyCountSeconds = 0f;
            Add(400);
            Check(MoneyDisplay.Shown == 1000, "Zähldauer 0 s springt sofort");
            GpsSettings.Preferences.moneyCountSeconds = float.NaN;
            Check(Mathf.Approximately(MoneyDisplay.CountSeconds, .6f), "Ungültige Zähldauer fällt auf 0,6 s zurück");
            GpsSettings.Preferences.moneyCountSeconds = 99f;
            Check(Mathf.Approximately(MoneyDisplay.CountSeconds, MoneyDisplay.MaxCountSeconds), "Zähldauer höchstens 5 s");

            GpsSettings.Preferences.moneyCountSeconds = 1f;
            Add(5000);
            MoneyDisplay.Finish();
            Check(MoneyDisplay.Shown == 6000 && labelA.GetComponent<TMP_Text>().text == ShopMoneyFormatter.Format(6000),
                "Finish beendet das Zählen und aktualisiert die Anzeigen");

            // Loading a save shows its amount at once.
            stats.RestoreRunState(new SavedStats { money = 42 }, null);
            Check(MoneyDisplay.Shown == 42 && !MoneyDisplay.IsCounting, "Geladener Spielstand zeigt den Betrag sofort");
            var other = new GameObject("MoneyDisplayChecks Other") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<StatsManager>();
            try
            {
                StatsManager.Instance = other;
                Check(MoneyDisplay.Shown == other.Money && !MoneyDisplay.IsCounting, "Neuer StatsManager zeigt seinen Betrag sofort");
            }
            finally { Object.DestroyImmediate(other.gameObject); }

            string report = string.Join("\n", checks);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/MoneyDisplayChecks.txt", report);
            return checks.Count + " Checks bestanden\n" + report;
        }
        finally
        {
            GpsSettings.Preferences.moneyCountSeconds = previousSeconds;
            StatsManager.Instance = previousStats; AudioManager.Instance = previousAudio;
            Object.DestroyImmediate(labelA); Object.DestroyImmediate(labelB); Object.DestroyImmediate(host);
            MoneyDisplay.Snap(previousStats);
        }
    }
}
