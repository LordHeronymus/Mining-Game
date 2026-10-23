using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Pause menu "Hauptmenü" and "Fortsetzen": no save into another save directory, a normal save still running when play
// mode stops or the application quits is finished instead of lost, a failed save lets a second click leave without
// saving, and "Fortsetzen" keeps gameplay input blocked for the rest of its frame (Space is Submit and Jump).
public static class BughuntPauseSaveChecks
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static FieldInfo SaveField(string name) => typeof(GameSaveSystem).GetField(name, Any) ?? throw new Exception("GameSaveSystem." + name + " fehlt");
    static FieldInfo MenuField(string name) => typeof(RunPauseMenu).GetField(name, Any) ?? throw new Exception("RunPauseMenu." + name + " fehlt");
    static MethodInfo MenuMethod(string name) => typeof(RunPauseMenu).GetMethod(name, Any) ?? throw new Exception("RunPauseMenu." + name + " fehlt");
    static void SetBusy(bool busy) => typeof(GameSaveSystem).GetProperty("IsBusy").GetSetMethod(true).Invoke(null, new object[] { busy });

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        string previousDirectory = GameSaveSystem.TestDirectory;
        string root = Path.GetFullPath("Temp/BughuntPauseSave-" + DateTime.UtcNow.Ticks);
        string own = Path.Combine(root, "Own"), other = Path.Combine(root, "Other");
        var activeDirectory = SaveField("activeDirectory");
        object previousActive = activeDirectory.GetValue(null);
        float previousScale = Time.timeScale, previousPlayed = GameSaveSystem.PlayedSeconds;
        bool previousBusy = GameSaveSystem.IsBusy, previousListener = AudioListener.pause;
        GameObject host = null;
        RunPauseMenu menu = null;
        try
        {
            Directory.CreateDirectory(own);
            GameSaveSystem.TestDirectory = own;

            // A-save-core-1: "Hauptmenü" saves only into the directory the run was started or loaded in.
            var isActive = typeof(GameSaveSystem).GetProperty("IsActiveDirectory", Any);
            Check(isActive != null, "GameSaveSystem.IsActiveDirectory vorhanden");
            activeDirectory.SetValue(null, own);
            Check((bool)isActive.GetValue(null), "Lauf im eigenen Speicherverzeichnis gilt als aktiv");
            GameSaveSystem.TestDirectory = other;
            Check(!(bool)isActive.GetValue(null), "Nach einem TestDirectory-Wechsel gilt das Verzeichnis als fremd");
            GameSaveSystem.TestDirectory = own;
            var decide = MenuMethod("SavesOnHome");
            bool Saves(bool canSave, bool ownDirectory, bool failedBefore) => (bool)decide.Invoke(null, new object[] { canSave, ownDirectory, failedBefore });
            Check(Saves(true, true, false), "'Hauptmenü' speichert einen speicherbaren Lauf im eigenen Verzeichnis");
            Check(!Saves(true, false, false), "'Hauptmenü' speichert nie in ein fremdes Speicherverzeichnis (verlässt ohne Speichern)");
            Check(!Saves(false, true, false), "Nicht erfassbarer Lauf verlässt wie bisher ohne Speichern");
            Check(!Saves(true, true, true), "Nach einem Schreibfehler verlässt ein zweiter Klick ohne Speichern");
            string menuSource = File.ReadAllText("Assets/GameObjects/UI/RunPauseMenu.cs");
            Check(menuSource.Contains("SavesOnHome(GameSaveSystem.CanSave, GameSaveSystem.IsActiveDirectory, saveFailed)"),
                "SaveAndHome entscheidet über SavesOnHome mit Verzeichnisvergleich und Fehlerzustand");
            Check(!Directory.Exists(other), "Fremdes Verzeichnis bleibt leer");

            // A-save-core-5: play-mode stop / quit during a normal save finishes that save synchronously.
            var normalSave = SaveField("normalSave");
            var normalSaved = SaveField("normalSaved");
            var finishing = SaveField("finishingOnExit");
            int steps = 0; bool suppressed = true;
            IEnumerator Running()
            {
                for (int i = 0; i < 3; i++) { steps++; suppressed &= (bool)finishing.GetValue(null); yield return null; }
                steps++; suppressed &= (bool)finishing.GetValue(null);
                SetBusy(false); normalSaved.SetValue(null, true);
            }
            SetBusy(true); normalSave.SetValue(null, Running());
            bool exitSaved = GameSaveSystem.SaveActiveRunNow(null);
            Check(steps == 4 && exitSaved, "Beenden während des 'Hauptmenü'-Speicherns führt dieses Speichern synchron zu Ende");
            Check(suppressed && !(bool)finishing.GetValue(null), "Rückruf des Pausemenüs wird beim Beenden unterdrückt (keine Navigation), danach wieder frei");
            Check(!GameSaveSystem.IsBusy && normalSave.GetValue(null) == null, "Kein hängendes Speichern nach dem Beenden");
            string saveSource = File.ReadAllText("Assets/GameObjects/Save/GameSaveSystem.cs");
            Check(saveSource.Contains("if (!finishingOnExit) complete?.Invoke("), "SaveRun meldet beim Beenden nicht an den Aufrufer zurück");
            bool reported = false;
            var routine = GameSaveSystem.Save(1, null, (ok, _) => reported = true);
            Check(normalSave.GetValue(null) == routine, "Normales Speichern wird für das Beenden gemerkt");
            while (routine.MoveNext()) { }
            Check(reported && !GameSaveSystem.IsBusy, "Ohne Beenden meldet ein (hier nicht mögliches) Speichern wie bisher zurück");
            normalSave.SetValue(null, null);
            Check(!GameSaveSystem.SaveActiveRunNow(null), "Ohne laufendes Speichern und ohne Lauf speichert das Beenden nichts");

            // A-save-ui-flow-3 and D-pause-settings-8 on a temporary pause menu (Awake does not run in Edit Mode).
            Check(!GameplayInputBlocker.IsBlocked, "Keine Eingabesperre vor dem Menütest");
            host = new GameObject("BughuntPauseSave Menu") { hideFlags = HideFlags.HideAndDontSave };
            var group = host.AddComponent<CanvasGroup>();
            var label = new GameObject("BughuntPauseSave Status") { hideFlags = HideFlags.HideAndDontSave };
            label.transform.SetParent(host.transform, false);
            var status = label.AddComponent<TMPro.TextMeshProUGUI>();
            menu = host.AddComponent<RunPauseMenu>();
            MenuField("group").SetValue(menu, group);
            MenuField("status").SetValue(menu, status);
            var saveFailed = MenuField("saveFailed");
            MenuMethod("SaveDone").Invoke(menu, new object[] { false, "Spielstand konnte nicht gespeichert werden." });
            Check((bool)saveFailed.GetValue(menu) && status.text.Contains("ohne Speichern"),
                "Schreibfehler: Statuszeile bietet den zweiten Klick ohne Speichern an");
            menu.Open();
            Check(!(bool)saveFailed.GetValue(menu) && status.text == "" && RunPauseMenu.IsOpen && GameplayInputBlocker.IsBlocked,
                "Neues Öffnen des Pausemenüs speichert beim ersten Klick wieder");
            MenuMethod("Resume").Invoke(menu, null);
            Check(!RunPauseMenu.IsOpen && Time.timeScale == previousScale && GameplayInputBlocker.IsBlocked,
                "'Fortsetzen' schliesst das Menü, die Spieleingabe bleibt im selben Bild gesperrt (Leertaste springt nicht)");
            var resumed = MenuField("resumedFrame");
            Check((int)resumed.GetValue(menu) == Time.frameCount, "Sperre gilt genau bis zum nächsten Bild");
            resumed.SetValue(menu, Time.frameCount - 1);
            MenuMethod("Update").Invoke(menu, null);
            Check(!GameplayInputBlocker.IsBlocked && (int)resumed.GetValue(menu) == -1, "Im nächsten Bild ist die Spieleingabe wieder frei");
            menu.Open(); menu.Close();
            Check(!RunPauseMenu.IsOpen && !GameplayInputBlocker.IsBlocked, "Schliessen per Esc/Code gibt die Eingabe wie bisher sofort frei");

            File.WriteAllText("Temp/BughuntPauseSaveChecks.txt", string.Join("\n", checks));
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            File.WriteAllText("Temp/BughuntPauseSaveChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks));
            throw;
        }
        finally
        {
            if (menu)
            {
                GameplayInputBlocker.SetBlocked(menu, false); GamePause.Set(menu, false);
                typeof(RunPauseMenu).GetProperty("IsOpen").GetSetMethod(true).Invoke(null, new object[] { false });
            }
            if (host) Object.DestroyImmediate(host);
            typeof(GameSaveSystem).GetField("normalSave", Any)?.SetValue(null, null);
            typeof(GameSaveSystem).GetField("finishingOnExit", Any)?.SetValue(null, false);
            SetBusy(previousBusy);
            activeDirectory.SetValue(null, previousActive);
            GameSaveSystem.TestDirectory = previousDirectory;
            GameSaveSystem.PlayedSeconds = previousPlayed;
            Time.timeScale = previousScale; AudioListener.pause = previousListener;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
        }
    }
}
