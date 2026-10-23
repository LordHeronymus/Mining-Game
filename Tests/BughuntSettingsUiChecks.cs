using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

// Checks for the settings fixes: key capture never binds a stray click (and "Einstellungen" never a mouse
// button, also not from old PlayerPrefs), and opening the settings clears the selected menu button behind them.
public static class BughuntSettingsUiChecks
{
    const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static string Key(GameAction action, int slot) => "settings.binding." + action + "." + slot;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var loaded = typeof(GameBindings).GetField("loaded", StaticMembers);
        if (loaded == null) throw new MissingFieldException("GameBindings.loaded");
        string[] keys = { Key(GameAction.Settings, 0), Key(GameAction.Settings, 1), Key(GameAction.Mine, 1) };
        var backup = keys.ToDictionary(key => key, key => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null);
        void Reload() => loaded.SetValue(null, false);
        try
        {
            // A click during capture cancels unless it lands on the capturing button; "Einstellungen" never takes a mouse button.
            var cancels = typeof(SettingsPanel).GetMethod("CancelsCapture", StaticMembers);
            Check(cancels != null, "SettingsPanel.CancelsCapture vorhanden");
            bool Cancels(GameAction action, KeyCode key, bool over) => (bool)cancels.Invoke(null, new object[] { action, key, over });
            Check(Cancels(GameAction.Jump, KeyCode.Mouse0, false), "Linksklick neben dem Knopf bricht die Aufnahme ab");
            Check(Cancels(GameAction.Settings, KeyCode.Mouse0, true), "Einstellungen: Klick bricht die Aufnahme immer ab");
            Check(Cancels(GameAction.Settings, KeyCode.Mouse4, true), "Einstellungen: auch Zusatzmaustasten brechen ab");
            Check(!Cancels(GameAction.Mine, KeyCode.Mouse0, true), "Abbauen: Linksklick auf den Knopf wird belegt");
            Check(!Cancels(GameAction.Remove, KeyCode.Mouse1, true), "Entfernen: Rechtsklick auf den Knopf wird belegt");
            Check(!Cancels(GameAction.Settings, KeyCode.F10, false) && !Cancels(GameAction.Jump, KeyCode.Space, false),
                "Tastaturtasten werden unabhängig vom Zeiger belegt");

            // Set refuses a mouse button for "Einstellungen" and keeps mouse bindings for other actions.
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            Reload();
            GameBindings.Set(GameAction.Settings, 1, KeyCode.Mouse0);
            Check(GameBindings.Get(GameAction.Settings, 1) == KeyCode.None && !PlayerPrefs.HasKey(Key(GameAction.Settings, 1)),
                "Set(Einstellungen, Linksklick) wird abgelehnt");
            GameBindings.Set(GameAction.Settings, 0, KeyCode.Mouse1);
            Check(GameBindings.Get(GameAction.Settings, 0) == KeyCode.Escape, "Set(Einstellungen, Rechtsklick) wird abgelehnt");
            GameBindings.Set(GameAction.Settings, 1, KeyCode.F10);
            Check(GameBindings.Get(GameAction.Settings, 1) == KeyCode.F10, "Einstellungen auf F10 bleibt erlaubt");
            GameBindings.Set(GameAction.Mine, 1, KeyCode.Mouse1);
            Check(GameBindings.Get(GameAction.Mine, 1) == KeyCode.Mouse1, "Abbauen auf Rechtsklick bleibt erlaubt");

            // Bindings stored by the old capture fall back to the defaults when loaded.
            PlayerPrefs.SetString(Key(GameAction.Settings, 0), "Escape");
            PlayerPrefs.SetString(Key(GameAction.Settings, 1), "Mouse0");
            Reload();
            Check(GameBindings.Get(GameAction.Settings, 0) == KeyCode.Escape && GameBindings.Get(GameAction.Settings, 1) == KeyCode.None,
                "Gespeichertes Einstellungen=Linksklick fällt auf den Standard zurück");
            PlayerPrefs.SetString(Key(GameAction.Settings, 0), "None");
            PlayerPrefs.SetString(Key(GameAction.Settings, 1), "Mouse0");
            Reload();
            Check(GameBindings.Get(GameAction.Settings, 0) == KeyCode.Escape && GameBindings.Get(GameAction.Settings, 1) == KeyCode.None,
                "Einstellungen bleiben nach dem Zurücksetzen erreichbar (Esc)");
            PlayerPrefs.SetString(Key(GameAction.Settings, 0), "Mouse2");
            PlayerPrefs.SetString(Key(GameAction.Settings, 1), "F10");
            Reload();
            Check(GameBindings.Get(GameAction.Settings, 0) == KeyCode.Escape && GameBindings.Get(GameAction.Settings, 1) == KeyCode.F10,
                "Gültige zweite Taste bleibt beim Zurücksetzen erhalten");
            PlayerPrefs.SetString(Key(GameAction.Mine, 1), "Mouse1");
            Reload();
            Check(GameBindings.Get(GameAction.Mine, 1) == KeyCode.Mouse1, "Gespeichertes Abbauen=Rechtsklick bleibt erhalten");

            // Opening the settings clears the selected menu button, so arrows/Enter cannot trigger the hidden menu.
            // Open needs Play Mode, so the check reads the compiled calls of Open and ClearSelection.
            var clear = typeof(SettingsPanel).GetMethod("ClearSelection", StaticMembers);
            Check(clear != null, "SettingsPanel.ClearSelection vorhanden");
            var select = typeof(EventSystem).GetMethod("SetSelectedGameObject", new[] { typeof(GameObject) });
            var current = typeof(EventSystem).GetProperty("current", StaticMembers)?.GetGetMethod();
            Check(select != null && current != null && Calls(clear, select) && Calls(clear, current),
                "ClearSelection hebt die Auswahl von EventSystem.current auf");
            var open = typeof(SettingsPanel).GetMethod("Open", BindingFlags.Instance | BindingFlags.Public);
            Check(open != null && Calls(open, clear), "SettingsPanel.Open hebt die Auswahl beim Öffnen auf");
            var close = typeof(SettingsPanel).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(close != null && Calls(close, clear), "SettingsPanel.Close hebt die Auswahl weiterhin auf");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntSettingsUiChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } inner) ex = inner.InnerException;
            File.WriteAllText("Temp/BughuntSettingsUiChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks)); throw ex;
        }
        finally
        {
            foreach (var entry in backup)
                if (entry.Value == null) PlayerPrefs.DeleteKey(entry.Key); else PlayerPrefs.SetString(entry.Key, entry.Value);
            PlayerPrefs.Save();
            Reload();
        }
    }

    // True when the IL of caller calls callee (call/callvirt followed by a method token).
    static bool Calls(MethodInfo caller, MethodBase callee)
    {
        var il = caller.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return false;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F) continue;
            try
            {
                var target = caller.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                if (target != null && target.Module == callee.Module && target.MetadataToken == callee.MetadataToken) return true;
            }
            catch (Exception) { } // operand bytes that are no method token
        }
        return false;
    }
}
