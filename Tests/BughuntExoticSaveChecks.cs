using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Edit Mode checks: an open blueprint choice whose recipe is no longer exotic (GPS/F1 "Exotischer Bauplan" off)
// keeps the save loadable. Validation only rejects broken structure; loading drops unknown ids and frees the find
// when no choice is left. Works on plain state objects only; scene, assets and saves are not touched.
public static class BughuntExoticSaveChecks
{
    const string Report = "Temp/BughuntExoticSaveChecks.txt";
    const int Width = 64, Height = 200;
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) { File.AppendAllText(Report, "FAIL " + message + "\n"); throw new Exception(message); }
        checks++; File.AppendAllText(Report, "PASS " + message + "\n");
    }
    static ExoticWorldState Pending(bool claimed, params string[] choices) => new ExoticWorldState
    {
        caches = new[] { new SavedBlueprintCache { cell = new Vector3Int(0, -50, 0), claimed = claimed }, new SavedBlueprintCache { cell = new Vector3Int(4, -95, 0) } },
        boulders = new[] { new SavedBoulder { cell = new Vector3Int(-6, -120, 0), health = MineBoulder.MaximumHealth } },
        pendingCache = 0, pendingChoices = choices
    };
    static bool Accepted(ExoticWorldState state)
    {
        try { ExoticWorldContent.ValidateState(state, Width, Height); return true; }
        catch (InvalidDataException) { return false; }
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks = 0; Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");

        var known = ExoticCatalog.Recipes.FirstOrDefault(recipe => recipe && !string.IsNullOrEmpty(recipe.exoticId));
        Check(known != null, "Mindestens ein exotischer Bauplan im Katalog");
        const string removed = "NoLongerExoticRecipe";
        Check(ExoticCatalog.Find(removed) == null, "Prüf-ID ist kein exotischer Bauplan");

        // Before the fix every unknown id threw, so GameSaveSystem dropped the main file and fell back to the .bak.
        Check(Accepted(Pending(false, known.exoticId, removed)), "Offene Auswahl mit nicht mehr exotischem Bauplan ist ladbar");
        Check(Accepted(Pending(false, removed)), "Offene Auswahl nur aus unbekannten Bauplänen ist ladbar");
        Check(Accepted(Pending(false, known.exoticId)), "Gültige offene Auswahl bleibt ladbar");

        // Structure is still checked.
        Check(!Accepted(Pending(false, known.exoticId, known.exoticId)), "Doppelte Bauplan-ID wird weiterhin abgelehnt");
        Check(!Accepted(Pending(false, known.exoticId, "")), "Leere Bauplan-ID wird weiterhin abgelehnt");
        Check(!Accepted(Pending(false, "a", "b", "c", "d")), "Mehr als drei Angebote werden weiterhin abgelehnt");
        Check(!Accepted(Pending(false)), "Offener Fund ohne Angebot wird weiterhin abgelehnt");
        Check(!Accepted(Pending(true, known.exoticId)), "Offener Fund auf bereits geöffnetem Bauplanfund wird weiterhin abgelehnt");
        var broken = Pending(false, known.exoticId); broken.boulders[0].health = float.NaN;
        Check(!Accepted(broken), "Ungültige Felsbrocken-Gesundheit wird weiterhin abgelehnt");

        // Loading repairs what validation now lets through.
        var repair = typeof(ExoticWorldContent).GetMethod("RepairPending", BindingFlags.Static | BindingFlags.NonPublic);
        Check(repair != null, "Reparatur der offenen Auswahl per Reflection erreichbar");
        var mixed = Pending(false, removed, known.exoticId);
        repair.Invoke(null, new object[] { mixed });
        Check(mixed.pendingCache == 0 && mixed.pendingChoices != null && mixed.pendingChoices.SequenceEqual(new[] { known.exoticId }),
            "Unbekannte Bauplan-ID fällt beim Laden weg, bekannte Angebote bleiben offen");
        var empty = Pending(false, removed);
        repair.Invoke(null, new object[] { empty });
        Check(empty.pendingCache == -1 && empty.pendingChoices == null, "Bleibt kein Angebot übrig, ist der Fund wieder frei für eine neue Auswahl");
        var outside = Pending(false, known.exoticId); outside.pendingCache = 5;
        repair.Invoke(null, new object[] { outside });
        Check(outside.pendingCache == -1 && outside.pendingChoices == null, "Fund ausserhalb der Liste wird wie bisher zurückgesetzt");
        var closed = new ExoticWorldState { caches = Pending(false).caches, pendingChoices = Array.Empty<string>() };
        repair.Invoke(null, new object[] { closed });
        Check(closed.pendingCache == -1, "Ohne offene Auswahl ändert die Reparatur nichts");

        string content = File.ReadAllText("Assets/GameObjects/Map/ExoticWorldContent.cs");
        int begin = content.IndexOf("public void RestoreState(", StringComparison.Ordinal);
        int end = begin < 0 ? -1 : content.IndexOf("BuildVisuals();", begin, StringComparison.Ordinal);
        Check(begin >= 0 && end > begin && content.Substring(begin, end - begin).Contains("RepairPending(state);"),
            "RestoreState repariert die offene Auswahl vor dem Aufbau der Weltobjekte");
        return "PASS: " + checks + " Prüfungen (offene Bauplanauswahl beim Laden); Bericht in " + Report;
    }
}
