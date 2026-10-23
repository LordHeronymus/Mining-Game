using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Bug hunt 23.10.2026: run end. Energy death must not reopen game over over the result screen (softlock),
// energy is frozen during the altar climax, "Weiter" leaves when no committed run can be presented,
// no warning heartbeat on the result screen, F1 keeps time stopped behind a test-action game over,
// and a medkit that cannot heal (regeneration 0) is not consumed.
public static class BughuntEndScreenChecks
{
    const string ReportPath = "Temp/BughuntEndScreenChecks.txt";
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    static bool IsOpen(Type type) => (bool)type.GetProperty("IsOpen", Any).GetValue(null);
    static void SetOpen(Type type, bool open) => type.GetProperty("IsOpen", Any).GetSetMethod(true).Invoke(null, new object[] { open });
    static void SetField(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);
    static object GetField(object target, string name) => target.GetType().GetField(name, Any).GetValue(target);
    static object Call(Type type, string name, params object[] args)
    {
        var method = type.GetMethod(name, Any);
        if (method == null) throw new Exception(type.Name + "." + name + " fehlt");
        return method.Invoke(null, args);
    }
    static bool Static(Type type, string name)
    {
        var property = type.GetProperty(name, Any);
        if (property == null) throw new Exception(type.Name + "." + name + " fehlt");
        return (bool)property.GetValue(null);
    }
    static void CloseAll() { foreach (var type in EndScreens) SetOpen(type, false); }
    static readonly Type[] EndScreens = { typeof(GameOverPanel), typeof(GameVictoryPanel), typeof(ExpeditionResultPanel) };

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var openStates = new bool[EndScreens.Length];
        for (int i = 0; i < EndScreens.Length; i++) openStates[i] = IsOpen(EndScreens[i]);
        var altarField = typeof(UltroniumAltarChamber).GetField("completingAltar", Any);
        var previousAltar = altarField.GetValue(null);
        var inventoryProperty = typeof(InventoryManager).GetProperty("Instance", Any);
        var previousInventory = inventoryProperty.GetValue(null);
        float previousScale = Time.timeScale;
        bool previousListenerPause = AudioListener.pause;
        string previousMetaDirectory = MetaProgression.TestDirectory, previousSaveDirectory = GameSaveSystem.TestDirectory;
        string directory = Path.GetFullPath("Temp/BughuntEndScreenChecks-" + DateTime.UtcNow.Ticks);
        var objects = new List<Object>();
        GameObject Probe(string name)
        {
            // Inactive: no Awake/OnEnable, not even for ExecuteAlways components.
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false); objects.Add(go); return go;
        }
        try
        {
            Directory.CreateDirectory(directory);
            GameSaveSystem.TestDirectory = directory;
            MetaProgression.TestDirectory = Path.Combine(directory, "MetaProfile");
            Check(!LoadingProgress.Active && !RunNavigation.IsTransitioning, "Ausgangslage: kein Laden, kein Szenenwechsel");
            CloseAll(); Time.timeScale = 1f;

            // Energy death softlock: game over must not reopen over the result (or victory) screen.
            var gameOver = Probe("Bughunt GameOver Probe").AddComponent<GameOverPanel>();
            Check(Static(typeof(GameOverPanel), "CanShow"), "Game Over darf ohne offenen Endscreen erscheinen");
            Check((bool)Call(typeof(EnergyManager), "EndsRun", 0f) && !(bool)Call(typeof(EnergyManager), "EndsRun", .5f),
                "Energie 0 beendet den Run, Restenergie nicht");
            SetOpen(typeof(ExpeditionResultPanel), true);
            Check(!Static(typeof(GameOverPanel), "CanShow") && !(bool)Call(typeof(EnergyManager), "EndsRun", 0f),
                "Energie 0 hinter dem Ergebnis-Panel öffnet kein neues Game Over");
            gameOver.Show();
            Check(!GameOverPanel.IsOpen && ExpeditionResultPanel.IsOpen && Time.timeScale == 1f,
                "GameOverPanel.Show bei offenem Ergebnis-Panel bleibt wirkungslos (Ergebnis bleibt bedienbar)");
            CloseAll(); SetOpen(typeof(GameVictoryPanel), true);
            gameOver.Show();
            Check(!GameOverPanel.IsOpen && !(bool)Call(typeof(EnergyManager), "EndsRun", 0f), "Kein Game Over über dem Sieg-Screen");
            CloseAll(); SetOpen(typeof(GameOverPanel), true);
            gameOver.Show();
            Check(GameOverPanel.IsOpen && Time.timeScale == 1f, "Offenes Game Over wird nicht erneut geöffnet und verbucht");
            CloseAll();

            // Altar climax: energy neither drains nor ends the run while the victory sequence runs.
            var altar = Probe("Bughunt Altar Probe").AddComponent<UltroniumAltarChamber>();
            SetField(altar, "completing", true); altarField.SetValue(null, altar);
            Check(UltroniumAltarChamber.VictorySequenceActive, "Siegsequenz simuliert");
            Check(!Static(typeof(EnergyManager), "DrainsEnergy"), "Energie sinkt während der Siegsequenz nicht");
            Check(!(bool)Call(typeof(EnergyManager), "EndsRun", 0f), "Energie 0 löst während der Siegsequenz kein Game Over aus");
            Check(!(bool)Call(typeof(StatsManager), "LowHealthHeartbeatActive", 10f, .1f), "Kein Herzschlag während der Siegsequenz");
            altarField.SetValue(null, null);
            Check(Static(typeof(EnergyManager), "DrainsEnergy") == !GameplayTestSettings.NoEnergyConsume,
                "Nach der Sequenz gilt wieder der normale Verbrauch");

            // Warning heartbeat stops on the expedition result.
            Check((bool)Call(typeof(StatsManager), "LowHealthHeartbeatActive", 10f, .1f), "Herzschlag bei 10 % Gesundheit im Spiel");
            Check(!(bool)Call(typeof(StatsManager), "LowHealthHeartbeatActive", 50f, .5f), "Kein Herzschlag bei 50 % Gesundheit");
            SetOpen(typeof(ExpeditionResultPanel), true);
            Check(!(bool)Call(typeof(StatsManager), "LowHealthHeartbeatActive", 10f, .1f), "Kein Herzschlag auf dem Expeditionsergebnis");
            CloseAll();

            // "Weiter" leaves directly when the run could not be committed (test mode victory, read-only profile).
            Check((bool)Call(typeof(ExpeditionResultPanel), "LeavesWithoutResult", new object[] { null }), "Ohne Meta-Run führt Weiter direkt ins Hauptmenü");
            Check((bool)Call(typeof(ExpeditionResultPanel), "LeavesWithoutResult", new MetaRunState { ended = false }),
                "Nicht verbuchter Run (Testmodus-Sieg) führt Weiter direkt ins Hauptmenü");
            Check(!(bool)Call(typeof(ExpeditionResultPanel), "LeavesWithoutResult", new MetaRunState { ended = true }),
                "Verbuchter Run zeigt das Ergebnis statt direkt zu verlassen");
            Check(Call(typeof(ExpeditionResultPanel), "ShowOrLeave", Probe("Bughunt Result Parent").transform) == null && !RunNavigation.IsTransitioning,
                "Ausserhalb des Play Mode startet Weiter keinen Szenenwechsel");

            // F1: closing after a lethal test action keeps time stopped behind the game over.
            var gps = Probe("Bughunt Gps Probe").AddComponent<GpsRuntimePanel>();
            var endPause = typeof(GpsRuntimePanel).GetMethod("EndPause", Any);
            SetField(gps, "pausing", true); SetField(gps, "previousScale", 1f); Time.timeScale = 0f;
            SetOpen(typeof(GameOverPanel), true);
            endPause.Invoke(gps, null);
            Check(Time.timeScale == 0f && !(bool)GetField(gps, "pausing"), "F1 schliessen lässt die Zeit hinter Game Over angehalten");
            CloseAll(); SetOpen(typeof(ExpeditionResultPanel), true);
            SetField(gps, "pausing", true); endPause.Invoke(gps, null);
            Check(Time.timeScale == 0f, "F1 schliessen lässt die Zeit hinter dem Ergebnis angehalten");
            CloseAll();
            SetField(gps, "pausing", true); endPause.Invoke(gps, null);
            Check(Time.timeScale == 1f, "F1 schliessen setzt die Zeit im laufenden Spiel zurück");

            // Medkit with regeneration 0 cannot heal and stays in the inventory.
            var host = Probe("Bughunt Vitals Probe");
            var inventory = host.AddComponent<InventoryManager>();
            inventoryProperty.GetSetMethod(true).Invoke(null, new object[] { inventory });
            var stats = host.AddComponent<StatsManager>();
            stats.initialHealth = 100f;
            typeof(StatsManager).GetField("<Health>k__BackingField", Any).SetValue(stats, 50f);
            var medkit = ScriptableObject.CreateInstance<ItemSO>();
            medkit.hideFlags = HideFlags.HideAndDontSave; medkit.name = "Bughunt Medkit"; medkit.item = Item.Medkit;
            medkit.category = ItemCategory.Consumable; medkit.displayName = "Probe-Medkit"; objects.Add(medkit);
            Check(inventory.AddStartingItem(medkit, 1) && inventory.GetCount(medkit) == 1, "Probe-Medkit im Inventar");
            stats.healthRegenPerSecond = 0f;
            Check(!stats.TryUseMedkit(medkit) && inventory.GetCount(medkit) == 1 && !stats.IsMedkitActive,
                "Regeneration 0: Medkit wird nicht verbraucht");
            stats.healthRegenPerSecond = 2f;
            Check(stats.TryUseMedkit(medkit) && inventory.GetCount(medkit) == 0, "Mit Regeneration heilt das Medkit wie bisher");

            string report = string.Join("\n", checks);
            File.WriteAllText(ReportPath, report); return checks[checks.Count - 1];
        }
        catch (Exception ex)
        {
            var error = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;
            File.WriteAllText(ReportPath, "FAIL " + error.Message + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            for (int i = 0; i < EndScreens.Length; i++) SetOpen(EndScreens[i], openStates[i]);
            altarField.SetValue(null, previousAltar);
            inventoryProperty.GetSetMethod(true).Invoke(null, new[] { previousInventory });
            Time.timeScale = previousScale;
            AudioListener.pause = previousListenerPause;
            foreach (var obj in objects) if (obj) Object.DestroyImmediate(obj);
            MetaProgression.TestDirectory = previousMetaDirectory;
            GameSaveSystem.TestDirectory = previousSaveDirectory;
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
