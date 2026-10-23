using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// A build that loses the window focus during a run opens the pause menu (energy no longer drains to Game Over in the
// background), also with inventory, shop, storage or workbench open (input blocked, time running). A loss during
// loading, a blocking save or a time-stopping panel stays pending and pauses afterwards. The editor keeps running unfocused.
public static class BughuntFocusPauseChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(RunPauseMenu).Assembly.GetType("RunFocusPause");
        var pending = type?.GetField("focusLost", Static);
        object savedPending = pending?.GetValue(null);
        float savedScale = Time.timeScale;
        GameObject blockerObject = null;
        CanvasGroup blocker = null;
        try
        {
            Check(type != null, "RunFocusPause vorhanden");
            Check(pending != null && pending.FieldType == typeof(bool), "Vorgemerkter Fokusverlust (focusLost) vorhanden");
            var decide = type.GetMethod("PausesOnFocusLoss", Static);
            var now = type.GetMethod("PausesNow", Static);
            var poll = type.GetMethod("Poll", Static);
            var handler = type.GetMethod("FocusChanged", Static);
            Check(decide != null && now != null && poll != null && handler != null, "PausesOnFocusLoss, PausesNow, Poll und FocusChanged vorhanden");
            bool Pauses(bool focusLost = true, bool hasMenu = true, bool menuOpen = false, bool timeStopped = false, bool loading = false,
                bool runEnded = false, bool saving = false) =>
                (bool)decide.Invoke(null, new object[] { focusLost, hasMenu, menuOpen, timeStopped, loading, runEnded, saving });

            Check(Pauses(), "Fokusverlust im laufenden Run öffnet das Pausemenü");
            Check(!Pauses(focusLost: false), "Ohne Fokusverlust (bzw. nach Rückkehr des Fokus) öffnet sich nichts");
            Check(!Pauses(hasMenu: false), "Ohne Pausemenü (Hauptmenü-Szene) passiert nichts");
            Check(!Pauses(menuOpen: true), "Bereits offenes Pausemenü bleibt unverändert");
            Check(!Pauses(timeStopped: true), "Panel mit stehender Zeit (Einstellungen, Karte, F1, Meta, SaveSlot) bleibt unverändert");
            Check(!Pauses(loading: true), "Während Laden oder Szenenwechsel öffnet sich kein Pausemenü");
            Check(!Pauses(runEnded: true), "Über Game Over, Sieg, Auswertung oder Altar-Siegsequenz öffnet sich kein Pausemenü");
            Check(!Pauses(saving: true), "Während eines blockierenden Speicherns öffnet sich kein Pausemenü");

            // The focus event only marks the loss; returning focus clears it without resuming anything.
            handler.Invoke(null, new object[] { false });
            Check((bool)pending.GetValue(null), "Fokusverlust wird vorgemerkt");
            handler.Invoke(null, new object[] { true });
            Check(!(bool)pending.GetValue(null), "Rückkehr des Fokus löscht die Vormerkung");

            // Real state of the open edit-mode scene: no run flag may be set, otherwise the following cases say nothing.
            Check(!RunPauseMenu.IsOpen && !GamePause.IsPaused && !MainMenuController.IsVisible && !LoadingProgress.Active &&
                  !RunNavigation.IsTransitioning && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen && !ExpeditionResultPanel.IsOpen &&
                  !UltroniumAltarChamber.VictorySequenceActive && !GameSaveSystem.IsBusy, "Ausgangslage: kein Menü, Laden, Endscreen oder Speichern aktiv");
            pending.SetValue(null, true);
            Time.timeScale = 1;
            Check((bool)now.Invoke(null, new object[] { true }), "Laufende Zeit ohne Panel pausiert");
            blockerObject = new GameObject("BughuntFocusPauseBlocker") { hideFlags = HideFlags.HideAndDontSave };
            blocker = blockerObject.AddComponent<CanvasGroup>();
            GameplayInputBlocker.SetBlocked(blocker, true);
            Check(GameplayInputBlocker.IsBlocked && (bool)now.Invoke(null, new object[] { true }),
                "Eingabesperre bei laufender Zeit (Inventar, Shop, Lager, Werkbank) pausiert trotzdem");
            Time.timeScale = 0;
            Check(!(bool)now.Invoke(null, new object[] { true }), "Eingabesperre mit stehender Zeit pausiert nicht zusätzlich");
            Time.timeScale = 1;
            Check(!(bool)now.Invoke(null, new object[] { false }), "Ohne Pausemenü pausiert nichts");

            // Without a menu (loading, menu not created yet) the poll keeps the loss pending for a later frame.
            poll.Invoke(null, new object[] { null });
            Check((bool)pending.GetValue(null) && !RunPauseMenu.IsOpen && Time.timeScale == 1,
                "Poll ohne Pausemenü ändert nichts und behält den Fokusverlust für später (z. B. nach dem Laden)");
            Check(Pauses(loading: true) == false && Pauses(), "Vorgemerkter Fokusverlust pausiert, sobald das Laden endet");

            // Only builds subscribe; the editor (test bench plays unfocused) keeps running.
            Check(type.GetMethod("Register", Static) == null, "Im Editor wird Application.focusChanged nicht abonniert");
            var field = typeof(Application).GetField("focusChanged", Static);
            if (field?.GetValue(null) is Delegate subscribed)
                Check(subscribed.GetInvocationList().All(entry => entry.Method.DeclaringType != type), "Kein Editor-Abo auf Application.focusChanged");
            string source = File.ReadAllText("Assets/GameObjects/UI/RunFocusPause.cs").Replace("\r", "");
            int guard = source.IndexOf("#if !UNITY_EDITOR", StringComparison.Ordinal), end = source.IndexOf("#endif", StringComparison.Ordinal);
            int unsubscribe = source.IndexOf("Application.focusChanged -= FocusChanged", StringComparison.Ordinal);
            int subscribe = source.IndexOf("Application.focusChanged += FocusChanged", StringComparison.Ordinal);
            Check(guard >= 0 && guard < unsubscribe && unsubscribe < subscribe && subscribe < end,
                "Build-Abo steht unter #if !UNITY_EDITOR und meldet sich vor += zuerst ab (Domain-Reload aus)");
            Check(File.ReadAllText("Assets/GameObjects/UI/RunPauseMenu.cs").Contains("RunFocusPause.Poll(this)"),
                "RunPauseMenu prüft den vorgemerkten Fokusverlust jedes Frame");
            Check(File.ReadAllText("ProjectSettings/ProjectSettings.asset").Contains("runInBackground: 1"), "runInBackground bleibt eingeschaltet");

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntFocusPauseChecks.txt", report); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            var error = ex is TargetInvocationException { InnerException: not null } wrapped ? wrapped.InnerException : ex;
            File.WriteAllText("Temp/BughuntFocusPauseChecks.txt", "FAIL " + error + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            if (blocker) GameplayInputBlocker.SetBlocked(blocker, false);
            if (blockerObject) UnityEngine.Object.DestroyImmediate(blockerObject);
            if (savedPending != null) pending.SetValue(null, savedPending);
            Time.timeScale = savedScale;
        }
    }
}
