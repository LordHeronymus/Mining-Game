using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks for live F1/GPS player values: walking speed and mining reach follow a change during the run,
// and a lowered initial health caps the current health.
// Runs in Edit Mode on a temporary StatsManager and PlayerBaseStats; scene, assets and the GPS profile stay untouched.
public static class BughuntStatsLiveChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var previousStats = StatsManager.Instance; var previousAudio = AudioManager.Instance;
        var baseStats = ScriptableObject.CreateInstance<PlayerBaseStats>(); baseStats.hideFlags = HideFlags.HideAndDontSave;
        var foreign = ScriptableObject.CreateInstance<PlayerBaseStats>(); foreign.hideFlags = HideFlags.HideAndDontSave;
        var host = new GameObject("BughuntStatsLiveChecks") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            AudioManager.Instance = null;
            var stats = host.AddComponent<StatsManager>();
            var baseField = typeof(StatsManager).GetField("baseStats", Hidden);
            Check(baseField != null, "StatsManager.baseStats gefunden");
            baseField.SetValue(stats, baseStats);
            StatsManager.Instance = stats;
            var apply = typeof(GpsSettings).GetMethod("ApplyField", BindingFlags.Static | BindingFlags.NonPublic);
            Check(apply != null, "GpsSettings.ApplyField gefunden");
            void Apply(Object target, string name, float value, bool effects = true) =>
                apply.Invoke(null, new object[] { target, GpsCodec.Read(name, typeof(float), value, _ => ""), effects });

            // R3-gps-live-0: F1 walking speed and reach reach the running StatsManager without a run reset.
            baseStats.moveSpeed = 5f; baseStats.reach = 2.5f;
            stats.MoveSpeed = 5f; stats.Reach = 2.5f;
            stats.SetHealthForDebug(stats.MaxHealth * .4f); float health = stats.Health;
            Apply(baseStats, "moveSpeed", 10f);
            Check(Mathf.Approximately(baseStats.moveSpeed, 10f), "GPS schreibt die Laufgeschwindigkeit ins Basiswerte-Asset");
            Check(Mathf.Approximately(stats.MoveSpeed, 10f), "Laufgeschwindigkeit wirkt sofort im laufenden Run (" + stats.MoveSpeed + ")");
            Apply(baseStats, "reach", 6f);
            Check(Mathf.Approximately(stats.Reach, 6f), "Abbau-Reichweite wirkt sofort im laufenden Run (" + stats.Reach + ")");
            Check(Mathf.Approximately(stats.Health, health), "Run wird dabei nicht zurückgesetzt (Gesundheit " + stats.Health + ")");
            stats.MoveSpeed = 2f;
            Apply(baseStats, "jumpHeightBlocks", 2f);
            Check(Mathf.Approximately(stats.MoveSpeed, 2f), "Andere Basiswerte überschreiben eine direkt gesetzte Laufgeschwindigkeit nicht");
            Apply(foreign, "moveSpeed", 20f);
            Check(Mathf.Approximately(stats.MoveSpeed, 2f), "Fremdes Basiswerte-Asset ändert den StatsManager nicht");
            Apply(baseStats, "moveSpeed", 7f, false);
            Check(Mathf.Approximately(stats.MoveSpeed, 2f), "Profil laden ohne Nebenwirkungen (Awake vor ResetRun) bleibt unverändert");

            // R3-gps-live-5: lowering the initial health caps the current health in the next frame, like the energy.
            var update = typeof(StatsManager).GetMethod("Update", Hidden);
            Check(update != null, "StatsManager.Update gefunden");
            stats.healthRegenPerSecond = 0f; stats.initialHealth = 100f; stats.SetHealthForDebug(stats.MaxHealth);
            Check(Mathf.Approximately(stats.Health, stats.MaxHealth), "Gesundheit voll (" + stats.Health + ")");
            float reportedHealth = -1f, reportedMax = -1f;
            stats.OnHealthChanged += (value, max) => { reportedHealth = value; reportedMax = max; };
            stats.initialHealth = 20f;
            update.Invoke(stats, null);
            Check(Mathf.Approximately(stats.Health, stats.MaxHealth) && stats.Health < 100f * MetaProgression.CurrentLoadout.healthMultiplier,
                "Gesenkte Initiale Gesundheit begrenzt die aktuelle Gesundheit (" + stats.Health + " / " + stats.MaxHealth + ")");
            Check(Mathf.Approximately(reportedHealth, stats.Health) && Mathf.Approximately(reportedMax, stats.MaxHealth), "HUD wird über OnHealthChanged informiert");
            float capped = stats.Health;
            stats.initialHealth = 100f;
            update.Invoke(stats, null);
            Check(Mathf.Approximately(stats.Health, capped), "Höheres Maximum hebt die Gesundheit nicht an (" + stats.Health + ")");

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntStatsLiveChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntStatsLiveChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            StatsManager.Instance = previousStats; AudioManager.Instance = previousAudio;
            Object.DestroyImmediate(host); Object.DestroyImmediate(baseStats); Object.DestroyImmediate(foreign);
        }
    }
}
