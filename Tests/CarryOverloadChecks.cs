using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Carrying overload: up to the capacity full speed, up to twice the capacity the speed falls linearly to a third,
// more than twice the capacity cannot be picked up, and even fully loaded the player jumps onto a one-cell step.
// Play Mode (SampleScene, menu may be open): the jump runs on a copy of the player in a separate physics scene;
// the inventory gets a temporary item that is removed again. Result in Temp/CarryOverloadChecks.txt.
public static class CarryOverloadChecks
{
    const string Report = "Temp/CarryOverloadChecks.txt";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (!Application.isPlaying) return "Requires Play Mode.";
        checks.Clear(); Directory.CreateDirectory("Temp");
        try
        {
            Logic();
            Inventory();
            Jumps();
            File.WriteAllText(Report, string.Join("\n", checks)); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            File.WriteAllText(Report, "FAIL " + inner + "\n" + string.Join("\n", checks)); throw;
        }
    }

    static void Logic()
    {
        var prefs = GpsSettings.Preferences;
        Check(Mathf.Approximately(InventoryManager.OverloadMaxMultiplier, 2f), "GPS: maximale Last ist die doppelte Traglast (" + InventoryManager.OverloadMaxMultiplier + ")");
        Check(Mathf.Abs(InventoryManager.OverloadMinSpeed - 1f / 3f) < .001f, "GPS: Tempo bei maximaler Last ist ein Drittel (" + InventoryManager.OverloadMinSpeed + ")");
        Check(prefs.carryMinJumpBlocks >= 1f, "GPS: Mindest-Sprunghöhe mit Last mindestens 1 Block (" + prefs.carryMinJumpBlocks + ")");

        float F(double ratio) => InventoryManager.CalculateMovementWeightFactor(ratio * 30d, 30f, 2f, 1f / 3f);
        Check(Mathf.Approximately(F(0d), 1f) && Mathf.Approximately(F(.5d), 1f) && Mathf.Approximately(F(1d), 1f), "Bis zur Traglast volles Tempo");
        Check(Mathf.Abs(F(1.5d) - 2f / 3f) < .0001f, "Bei 1,5-facher Traglast zwei Drittel Tempo (" + F(1.5d) + ")");
        Check(Mathf.Abs(F(2d) - 1f / 3f) < .0001f, "Bei doppelter Traglast ein Drittel Tempo (" + F(2d) + ")");
        Check(Mathf.Abs(F(2.5d) - 1f / 3f) < .0001f, "Darüber (nur Startressourcen) nicht noch langsamer (" + F(2.5d) + ")");
        float previous = 1f; bool falling = true;
        for (int i = 1; i <= 100; i++) { float value = F(1d + i / 100d); falling &= value < previous; previous = value; }
        Check(falling, "Zwischen 1× und 2× sinkt das Tempo stetig");
        Check(Mathf.Approximately(InventoryManager.CalculateMovementWeightFactor(45d, 30f), F(1.5d)), "Ohne Parameter gelten die GPS-Werte");

        Check(Mathf.Approximately(PlayerMovement.LoadedJumpHeightBlocks(1.5f, 1f, 1.2f), 1.5f), "Ohne Last volle Sprunghöhe");
        Check(Mathf.Approximately(PlayerMovement.LoadedJumpHeightBlocks(1.5f, 1f / 3f, 1.2f), 1.2f), "Mit voller Last nie unter der Mindest-Sprunghöhe");
        Check(Mathf.Approximately(PlayerMovement.LoadedJumpHeightBlocks(.8f, 1f / 3f, 1.2f), .8f), "Eine tiefere Grund-Sprunghöhe wird nicht angehoben");
        Check(Mathf.Approximately(PlayerMovement.LoadedJumpHeightBlocks(3f, .64f, 1.2f), 2.4f), "Mit wenig Last sinkt die Sprunghöhe wie bisher (Wurzel des Tempofaktors)");
    }

    static void Inventory()
    {
        var inventory = InventoryManager.Instance;
        Check(inventory, "InventoryManager vorhanden");
        Check(Mathf.Abs(inventory.MaximumWeight - 2f * inventory.CarryingCapacity) < .001f, "Maximum ist die doppelte Traglast (" + inventory.MaximumWeight + ")");
        bool noWeight = GameplayTestSettings.NoWeight;
        var item = ScriptableObject.CreateInstance<ItemSO>(); item.hideFlags = HideFlags.HideAndDontSave; item.weight = 1f; item.name = "Carry overload probe";
        int added = 0;
        try
        {
            GameplayTestSettings.SetMode(GameplayTestMode.NoWeight, false, out _);
            double free = inventory.MaximumWeight - inventory.TotalWeight;
            int fit = (int)Math.Floor(free + 1e-6);
            if (fit > 0) { Check(inventory.AddTransferred(item, fit), "Bis zur doppelten Traglast lässt sich aufnehmen"); added = fit; }
            Check(!inventory.CanFitWeight(1d), "Über der doppelten Traglast passt nichts mehr hinein");
            Check(!inventory.AddTransferred(item, 1), "Aufnehmen über der doppelten Traglast wird abgelehnt");
            if (inventory.TotalWeight >= inventory.MaximumWeight - 1d)
                Check(inventory.MovementWeightFactor < .37f, "Voll beladen bewegt sich der Spieler mit rund einem Drittel (" + inventory.MovementWeightFactor + ")");
        }
        finally
        {
            if (added > 0) inventory.TryRemove(item, added);
            GameplayTestSettings.SetMode(GameplayTestMode.NoWeight, noWeight, out _);
            Object.DestroyImmediate(item);
        }
    }

    // A copy of the real player (collider, body, base stats) jumps onto a one-cell step at full load.
    static void Jumps()
    {
        var source = Object.FindFirstObjectByType<PlayerMovement>();
        var stats = Object.FindFirstObjectByType<StatsManager>();
        var map = Object.FindFirstObjectByType<MapGenerator>();
        Check(source && stats && map && map.Terrain, "Spieler, Werte und Karte vorhanden");
        float cell = Mathf.Abs((map.Terrain.CellToWorld(Vector3Int.up) - map.Terrain.CellToWorld(Vector3Int.zero)).y);
        float cellWidth = Mathf.Abs((map.Terrain.CellToWorld(Vector3Int.right) - map.Terrain.CellToWorld(Vector3Int.zero)).x);
        var sourceBody = source.GetComponent<Rigidbody2D>(); var sourceCollider = source.GetComponent<Collider2D>();
        Check(sourceBody && sourceCollider, "Spieler hat Körper und Collider");

        var inventory = InventoryManager.Instance;
        bool noWeight = GameplayTestSettings.NoWeight, fly = GameplayTestSettings.FlyMode;
        var item = ScriptableObject.CreateInstance<ItemSO>(); item.hideFlags = HideFlags.HideAndDontSave; item.weight = 1f; item.name = "Carry jump probe";
        int added = 0;
        var scene = SceneManager.CreateScene("Carry overload jump", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var physics = scene.GetPhysicsScene2D();
        var root = new GameObject("Carry jump fixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            GameplayTestSettings.SetMode(GameplayTestMode.NoWeight, false, out _);
            GameplayTestSettings.SetMode(GameplayTestMode.Fly, false, out _);
            int fit = (int)Math.Floor(inventory.MaximumWeight - inventory.TotalWeight + 1e-6);
            if (fit > 0 && inventory.AddTransferred(item, fit)) added = fit;
            float factor = stats.MovementWeightFactor;
            Check(factor < .37f, "Für den Sprungtest voll beladen (Tempofaktor " + factor + ")");
            checks.Add("INFO Sprunghöhe voll beladen ohne Untergrenze " + (stats.JumpHeightBlocks * Mathf.Sqrt(factor)).ToString("0.00") +
                " Blöcke, mit Untergrenze " + PlayerMovement.LoadedJumpHeightBlocks(stats.JumpHeightBlocks, factor, GpsSettings.Preferences.carryMinJumpBlocks).ToString("0.00"));

            Box(root, "Floor", new Vector2(0f, -1f), new Vector2(40f, 2f));
            Box(root, "Step", new Vector2(10f + cellWidth * 2f, cell * .5f), new Vector2(20f + cellWidth * 4f, cell));
            float stepLeft = cellWidth * 2f;

            var actor = new GameObject("Player copy") { layer = 31 };
            actor.SetActive(false);
            actor.transform.SetParent(root.transform);
            actor.transform.localScale = source.transform.lossyScale;
            var body = actor.AddComponent<Rigidbody2D>(); EditorUtility.CopySerialized(sourceBody, body);
            var shape = (Collider2D)actor.AddComponent(sourceCollider.GetType()); EditorUtility.CopySerialized(sourceCollider, shape);
            var movement = actor.AddComponent<PlayerMovement>();
            typeof(PlayerMovement).GetField("stats", Hidden).SetValue(movement, stats);
            typeof(PlayerMovement).GetField("groundLayer", Hidden).SetValue(movement, (LayerMask)(1 << 31));
            actor.SetActive(true); // Awake wires body, collider, map and the no-friction material
            movement.enabled = false;
            body.interpolation = RigidbodyInterpolation2D.None;
            var tick = typeof(PlayerMovement).GetMethod("FixedUpdate", Hidden);
            var inputX = typeof(PlayerMovement).GetField("inputX", Hidden);
            var buffer = typeof(PlayerMovement).GetField("jumpBufferRemaining", Hidden);
            var previousRun = typeof(PlayerMovement).GetField("previousRunVelocity", Hidden);

            // Run up from a few cells away, and stand pressed against the step: both jump and hold right.
            foreach (var (label, start, runUp) in new[] { ("Anlauf", -cellWidth * 3f, true), ("an der Kante stehend", 0f, false) })
            {
                Bounds bounds = shape.bounds;
                float halfWidth = bounds.extents.x, feetOffset = actor.transform.position.y - bounds.min.y;
                body.position = new Vector2(stepLeft - halfWidth - .01f + start, feetOffset + .01f);
                body.linearVelocity = Vector2.zero; previousRun.SetValue(movement, 0f); buffer.SetValue(movement, 0f);
                Physics2D.SyncTransforms();
                inputX.SetValue(movement, runUp ? 1f : 0f);
                bool jumped = false, onTop = false; float highest = float.NegativeInfinity;
                for (int frame = 0; frame < 600 && !onTop; frame++)
                {
                    if (!runUp && frame == 30) inputX.SetValue(movement, 1f);
                    bool atWall = shape.bounds.max.x >= stepLeft - .05f;
                    if (!jumped && atWall && frame >= (runUp ? 0 : 31)) { buffer.SetValue(movement, .12f); jumped = true; }
                    tick.Invoke(movement, null);
                    physics.Simulate(Time.fixedDeltaTime);
                    highest = Mathf.Max(highest, shape.bounds.min.y);
                    onTop = shape.bounds.min.x > stepLeft + .02f && shape.bounds.min.y > cell - .05f && Mathf.Abs(body.linearVelocity.y) < .01f;
                }
                Check(jumped, label + ": Sprung ausgelöst");
                Check(onTop, label + ": voll beladen eine Zelle hoch gesprungen (Fuss höchstens " + (highest / cell).ToString("0.00") + " Zellen)");
            }
        }
        finally
        {
            if (added > 0) inventory.TryRemove(item, added);
            GameplayTestSettings.SetMode(GameplayTestMode.NoWeight, noWeight, out _);
            GameplayTestSettings.SetMode(GameplayTestMode.Fly, fly, out _);
            Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
            Object.DestroyImmediate(item);
        }
    }

    static void Box(GameObject root, string name, Vector2 center, Vector2 size)
    {
        var go = new GameObject(name) { layer = 31 };
        go.transform.SetParent(root.transform);
        go.transform.position = center;
        go.AddComponent<BoxCollider2D>().size = size;
    }
}
