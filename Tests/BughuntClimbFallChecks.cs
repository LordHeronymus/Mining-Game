using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode. A loaded run hangs on again at a touched ladder (the climb is not saved), a fall after loading counts from the
// loaded height, and the F1 teleport ("Zum Altar") no longer keeps the fall peak from before the jump (lethal landing).
public static class BughuntClimbFallChecks
{
    const string Report = "Temp/BughuntClimbFallChecks.txt";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static FieldInfo Field(Type type, string name) => type.GetField(name, Any) ?? throw new Exception(type.Name + "." + name + " fehlt");
    static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal); if (start < 0) return "";
        var end = new Regex(@"\n    }").Match(source, start); return end.Success ? source.Substring(start, end.Index - start) : "";
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear(); Directory.CreateDirectory("Temp");
        GameObject go = null;
        try
        {
            go = new GameObject("Bughunt climb/fall check") { hideFlags = HideFlags.HideAndDontSave };
            var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 3f;
            var collider = go.AddComponent<BoxCollider2D>(); collider.size = new Vector2(.6f, 1.6f);
            var movement = go.AddComponent<PlayerMovement>();
            var ladder = go.AddComponent<PlayerLadder>();
            Physics2D.SyncTransforms();
            // Awake does not run in Edit Mode: wire the cached references like Awake would.
            Field(typeof(PlayerMovement), "rb").SetValue(movement, body);
            Field(typeof(PlayerMovement), "col").SetValue(movement, collider);
            Field(typeof(PlayerLadder), "body").SetValue(ladder, body);
            Field(typeof(PlayerLadder), "bodyCollider").SetValue(ladder, collider);

            // Fall tracking: TrackLanding(false) keeps the highest feet height of the running fall.
            var grounded = Field(typeof(PlayerMovement), "hasGroundedForFall");
            var tracking = Field(typeof(PlayerMovement), "trackingFall");
            var peak = Field(typeof(PlayerMovement), "fallPeakY");
            var trackLanding = typeof(PlayerMovement).GetMethod("TrackLanding", Any) ?? throw new Exception("PlayerMovement.TrackLanding fehlt");
            void Airborne() => trackLanding.Invoke(movement, new object[] { false });
            const float oldPeak = 1000f; // feet height of a jump at the surface, far above the teleport target
            grounded.SetValue(movement, true); tracking.SetValue(movement, true); peak.SetValue(movement, oldPeak);
            Airborne();
            Check((float)peak.GetValue(movement) == oldPeak, "Ohne Neustart bleibt der alte Gipfel der Fallmessung erhalten (Ausgangslage des Fehlers)");

            var restart = typeof(PlayerMovement).GetMethod("RestartFallTracking", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            Check(restart != null, "PlayerMovement.RestartFallTracking() ist öffentlich");
            restart.Invoke(movement, null);
            Check(!(bool)tracking.GetValue(movement) && (bool)grounded.GetValue(movement),
                "RestartFallTracking verwirft den laufenden Sturz und misst ab der aktuellen Höhe weiter");
            Airborne();
            float feet = collider.bounds.min.y, measured = (float)peak.GetValue(movement);
            Check((bool)tracking.GetValue(movement) && Mathf.Approximately(measured, feet) && measured < oldPeak - 1f,
                "Nach dem Neustart beginnt ein Sturz an der aktuellen Fusshöhe (" + measured + "), nicht am alten Gipfel");

            // Teleport and save restore both restart the fall measurement.
            string teleport = Body(File.ReadAllText("Assets/GameObjects/UI/GpsTestActions.cs"), "static void Teleport(Vector3 position)");
            int moved = teleport.IndexOf("player.transform.position = position;", StringComparison.Ordinal);
            int teleportSync = teleport.IndexOf("Physics2D.SyncTransforms();", StringComparison.Ordinal);
            int teleportFall = teleport.IndexOf("player.RestartFallTracking();", StringComparison.Ordinal);
            Check(moved >= 0 && teleport.Contains("body.position = position;") && teleportSync > moved && teleportFall > teleportSync,
                "F1-Teleport (Zum Altar/Zum Spawn) setzt Körper und Collider auf das Ziel und startet erst danach die Fallmessung neu");
            string restore = Body(File.ReadAllText("Assets/GameObjects/Save/GameSaveSystem.cs"), "public static IEnumerator RestorePending(MapGenerator map)");
            int placed = restore.IndexOf("body.linearVelocity = run.state.playerVelocity;", StringComparison.Ordinal);
            int synced = restore.IndexOf("Physics2D.SyncTransforms();", StringComparison.Ordinal);
            int attach = restore.IndexOf(".AttachIfContact();", StringComparison.Ordinal);
            int fall = restore.IndexOf("player.RestartFallTracking();", StringComparison.Ordinal);
            Check(placed >= 0 && synced > placed && attach > synced && fall > synced,
                "Laden hängt nach dem Setzen und Synchronisieren der Position an der Leiter ein und startet die Fallmessung neu");

            // Ladder: attach keeps the remembered gravity, never attaches twice, and needs a ladder contact.
            Check(!ladder.AttachIfContact() && !ladder.IsClimbing && body.gravityScale == 3f, "Ohne LadderMap/Leiterkontakt hängt AttachIfContact nicht ein");
            var attachMethod = typeof(PlayerLadder).GetMethod("Attach", Any, null, Type.EmptyTypes, null) ?? throw new Exception("PlayerLadder.Attach fehlt");
            body.linearVelocity = new Vector2(1f, -4f);
            attachMethod.Invoke(ladder, null);
            Check(ladder.IsClimbing && body.gravityScale == 0f, "Einhängen setzt die Schwerkraft auf 0");
            Check(!ladder.AttachIfContact() && body.gravityScale == 0f, "Ein zweites Einhängen überschreibt die gemerkte Schwerkraft nicht");
            ladder.Detach();
            Check(!ladder.IsClimbing && body.gravityScale == 3f, "Loslassen stellt die ursprüngliche Schwerkraft wieder her");
            string ladderSource = File.ReadAllText("Assets/GameObjects/Player/PlayerLadder.cs");
            string attachIfContact = Body(ladderSource, "public bool AttachIfContact()");
            Check(attachIfContact.Contains("ladders.FindContact(bodyCollider.bounds") && attachIfContact.Contains("IsFlying") &&
                attachIfContact.Contains("body.linearVelocity = Vector2.zero;"),
                "AttachIfContact prüft den Leiterkontakt, lässt Flugmodus aus und stoppt die geladene Geschwindigkeit");
            Check(Body(ladderSource, "public bool Step(").Contains("Attach();"), "Step nutzt dasselbe Einhängen wie das Laden");

            File.WriteAllText(Report, string.Join("\n", checks)); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            File.WriteAllText(Report, "FAIL " + inner + "\n" + string.Join("\n", checks)); throw;
        }
        finally { if (go) Object.DestroyImmediate(go); }
    }
}
