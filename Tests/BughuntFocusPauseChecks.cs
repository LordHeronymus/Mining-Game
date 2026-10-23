using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A build that loses the window focus during a run opens the pause menu (energy no longer drains to Game Over in the
// background), also with inventory, shop, storage or workbench open (input blocked, time running). A loss during
// loading, a blocking save or a time-stopping panel stays pending and pauses afterwards. The editor keeps running unfocused.
// Under such a pause the keys belong to the menu (one Esc closes only the pause, Tab/I/B/H do nothing behind it), opening
// it releases a selected shop button and its buttons navigate only among themselves. Uses a temporary pause menu and a
// temporary EventSystem that is current only during the check.
public static class BughuntFocusPauseChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static bool Calls(MethodInfo caller, MethodBase callee)
    {
        var il = caller?.GetMethodBody()?.GetILAsByteArray();
        if (il == null || callee == null) return false;
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

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(RunPauseMenu).Assembly.GetType("RunFocusPause");
        var pending = type?.GetField("focusLost", Static);
        object savedPending = pending?.GetValue(null);
        float savedScale = Time.timeScale;
        bool savedListener = AudioListener.pause;
        int savedConsumed = RunPauseMenu.InputConsumedFrame;
        var consumedSetter = typeof(RunPauseMenu).GetProperty("InputConsumedFrame", Static)?.GetSetMethod(true);
        GameObject blockerObject = null, menuObject = null, eventsObject = null, card = null;
        CanvasGroup blocker = null;
        RunPauseMenu menu = null;
        EventSystem events = null;
        List<EventSystem> eventSystems = null;
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

            // R3-focus-panels-0: the pause lies over an open window. While it is open and in the frame its Esc closed it, the
            // keys belong to the menu: no close key (Shop, Lager, Werkbank via CloseDown), no Tab/I/B and no medkit key behind it.
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            pending.SetValue(null, false);
            GameplayInputBlocker.SetBlocked(blocker, false);
            var owns = typeof(RunPauseMenu).GetProperty("OwnsInput", Static);
            Check(owns != null && owns.PropertyType == typeof(bool) && consumedSetter != null, "RunPauseMenu.OwnsInput vorhanden");
            bool Owns() => (bool)owns.GetValue(null);
            consumedSetter.Invoke(null, new object[] { Time.frameCount - 1 });
            Check(!Owns(), "Ohne Pausemenü wirken die Fenstertasten wie bisher");
            RunPauseMenu.ConsumeInput();
            Check(Owns(), "Im Bild, in dem Esc die Pause schliesst, schliesst dasselbe Esc kein Fenster dahinter");
            consumedSetter.Invoke(null, new object[] { Time.frameCount - 1 });
            var ownsGetter = owns.GetGetMethod();
            Check(Calls(typeof(GameBindings).GetMethod("CloseDown", Static), ownsGetter), "Schliessen-Taste (Shop, Lager, Werkbank) wirkt unter der Pause nicht");
            Check(Calls(typeof(InventoryUI).GetMethod("Update", Instance), ownsGetter), "Inventar: Tab/I und Einstellungen-Taste wirken unter der Pause nicht");
            Check(Calls(typeof(WorkbenchPanel).GetMethod("Update", Instance), ownsGetter), "Werkbank: Werkbank-Taste wirkt unter der Pause nicht");
            Check(Calls(typeof(CompactHud).GetMethod("Update", Instance), ownsGetter), "Medkit-Taste wirkt unter der Pause auch bei offenem Inventar nicht");

            // R3-focus-panels-1 on a temporary pause menu (Awake does not run in Edit Mode) and a temporary current EventSystem.
            menuObject = new GameObject("BughuntFocusPause Menu", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            menu = menuObject.AddComponent<RunPauseMenu>();
            typeof(RunPauseMenu).GetMethod("Awake", Instance).Invoke(menu, null);
            var buttons = menuObject.GetComponentsInChildren<Button>();
            Check(buttons.Length >= 5 && buttons.All(button => button.navigation.mode == Navigation.Mode.Explicit &&
                      !button.navigation.selectOnLeft && !button.navigation.selectOnRight &&
                      buttons.Any(other => other == button.navigation.selectOnUp) && buttons.Any(other => other == button.navigation.selectOnDown)),
                "Pauseknöpfe navigieren nur untereinander (keine automatische Navigation zu Fenstern hinter dem Scrim)");
            var visited = new HashSet<Selectable>();
            Selectable step = buttons[0];
            for (int i = 0; i < buttons.Length; i++) { visited.Add(step); step = step.navigation.selectOnDown; }
            Check(visited.Count == buttons.Length && step == buttons[0], "Pfeil runter erreicht jeden Pauseknopf und kehrt zum ersten zurück");
            eventSystems = typeof(EventSystem).GetField("m_EventSystems", Static)?.GetValue(null) as List<EventSystem>;
            Check(eventSystems != null, "EventSystem-Liste erreichbar");
            // Inactive, so no EventSystem callbacks run; put first in the list it is EventSystem.current for this check only.
            eventsObject = new GameObject("BughuntFocusPause EventSystem") { hideFlags = HideFlags.HideAndDontSave };
            eventsObject.SetActive(false);
            events = eventsObject.AddComponent<EventSystem>();
            eventSystems.Insert(0, events);
            Check(EventSystem.current == events, "Temporäres EventSystem ist aktuell");
            card = new GameObject("BughuntFocusPause Shop Buy Button") { hideFlags = HideFlags.HideAndDontSave };
            card.SetActive(false);
            events.SetSelectedGameObject(card);
            Check(events.currentSelectedGameObject == card, "Angeklickter Shop-Kaufknopf ist die EventSystem-Auswahl");
            Time.timeScale = 1;
            menu.Open();
            Check(RunPauseMenu.IsOpen && Owns(), "Offenes Pausemenü besitzt die Tasten");
            Check(events.currentSelectedGameObject == null, "Öffnen der Pause hebt die Auswahl auf: Pfeile/Enter kaufen nicht im Shop dahinter");
            menu.Close();
            Check(!RunPauseMenu.IsOpen && !Owns() && Time.timeScale == 1, "Nach dem Schliessen (ohne Esc) besitzt das Pausemenü die Tasten nicht mehr");

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
            if (eventSystems != null && events) eventSystems.Remove(events);
            if (menu)
            {
                GameplayInputBlocker.SetBlocked(menu, false); GamePause.Set(menu, false);
                typeof(RunPauseMenu).GetProperty("IsOpen").GetSetMethod(true).Invoke(null, new object[] { false });
            }
            if (menuObject) UnityEngine.Object.DestroyImmediate(menuObject);
            if (eventsObject) UnityEngine.Object.DestroyImmediate(eventsObject);
            if (card) UnityEngine.Object.DestroyImmediate(card);
            consumedSetter?.Invoke(null, new object[] { savedConsumed });
            AudioListener.pause = savedListener;
            if (blocker) GameplayInputBlocker.SetBlocked(blocker, false);
            if (blockerObject) UnityEngine.Object.DestroyImmediate(blockerObject);
            if (savedPending != null) pending.SetValue(null, savedPending);
            Time.timeScale = savedScale;
        }
    }
}
