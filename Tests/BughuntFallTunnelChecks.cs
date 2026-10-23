using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Edit Mode. Long falls no longer sink through the hollow outline floor (the player body uses continuous collision), and
// F1 "Map generieren" moves the player out of the new rock onto the spawn cell with a fresh fall measurement.
// Builds its own player and small map in a temporary additive scene; the open SampleScene is not changed.
public static class BughuntFallTunnelChecks
{
    const string Report = "Temp/BughuntFallTunnelChecks.txt";
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
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var tile = ScriptableObject.CreateInstance<Tile>();
        PhysicsMaterial2D noFriction = null;
        try
        {
            // R2-physics-0: the player body runs with continuous collision once PlayerMovement.Awake has run.
            var player = new GameObject("Fall tunnel check player");
            var body = player.AddComponent<Rigidbody2D>(); body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            var collider = player.AddComponent<BoxCollider2D>(); collider.size = new Vector2(.6f, 1f);
            var movement = player.AddComponent<PlayerMovement>();
            player.AddComponent<PlayerLadder>();
            Check(body.collisionDetectionMode == CollisionDetectionMode2D.Discrete, "Ausgangslage: Rigidbody2D steht wie in der Szene auf Discrete");
            var awake = typeof(PlayerMovement).GetMethod("Awake", Any) ?? throw new Exception("PlayerMovement.Awake fehlt");
            awake.Invoke(movement, null);
            noFriction = (PhysicsMaterial2D)Field(typeof(PlayerMovement), "noFrictionMaterial").GetValue(movement);
            Check(body.collisionDetectionMode == CollisionDetectionMode2D.Continuous,
                "PlayerMovement.Awake stellt den Spielerkörper auf kontinuierliche Kollision (kein Durchsinken bei langen Stürzen)");
            Check(collider.sharedMaterial == noFriction && noFriction && noFriction.friction == 0f, "Awake setzt weiterhin das reibungsfreie Material");

            // R2-physics-1: small map, spawn cell (0, 0) on the surface row, solid rock below.
            const int width = 16, height = 24;
            var grid = new GameObject("Fall tunnel check grid", typeof(Grid)); grid.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
            var mapObject = new GameObject("Fall tunnel check map", typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGenerator));
            mapObject.transform.SetParent(grid.transform, false);
            var map = mapObject.GetComponent<MapGenerator>();
            var tiles = new TileBase[width * height]; for (int i = 0; i < tiles.Length; i++) tiles[i] = tile;
            map.Terrain.SetTilesBlock(new BoundsInt(-width / 2, 1 - height, 0, width, height, 1), tiles);
            Check(map.Terrain.HasTile(Vector3Int.zero) && map.Terrain.HasTile(new Vector3Int(0, -10, 0)) && !map.Terrain.HasTile(Vector3Int.up),
                "Prüfkarte aufgebaut (Spawnzelle an der Oberfläche, Fels darunter)");

            // The player stood in a dug tunnel of the old map; that spot is rock now, and the old fall state is still active.
            var stuck = map.Terrain.GetCellCenterWorld(new Vector3Int(2, -10, 0));
            body.position = stuck; player.transform.position = stuck; body.linearVelocity = new Vector2(3f, -20f);
            Physics2D.SyncTransforms();
            var grounded = Field(typeof(PlayerMovement), "hasGroundedForFall");
            var tracking = Field(typeof(PlayerMovement), "trackingFall");
            var peak = Field(typeof(PlayerMovement), "fallPeakY");
            grounded.SetValue(movement, true); tracking.SetValue(movement, true); peak.SetValue(movement, 1000f);
            Check(map.Terrain.HasTile(map.Terrain.WorldToCell(collider.bounds.center)), "Ausgangslage: Spieler steckt im neuen Gestein");

            var teleportSpawn = typeof(GpsTestActions).GetMethod("TeleportSpawn", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(MapGenerator), typeof(PlayerMovement) }, null);
            Check(teleportSpawn != null, "GpsTestActions.TeleportSpawn(map, player) vorhanden");
            teleportSpawn.Invoke(null, new object[] { map, movement });

            var spawn = map.Terrain.GetCellCenterWorld(Vector3Int.zero);
            float surface = spawn.y + 1.1f * .5f, feet = collider.bounds.min.y;
            Check(Mathf.Abs(collider.bounds.center.x - spawn.x) < .01f && feet >= surface - .001f && feet < surface + .1f,
                "Spieler steht danach auf der Spawnzelle (Füsse " + feet.ToString("0.###") + ", Oberfläche " + surface.ToString("0.###") + ")");
            Check(!map.Terrain.HasTile(map.Terrain.WorldToCell(collider.bounds.center)) &&
                !map.Terrain.HasTile(map.Terrain.WorldToCell(new Vector3(collider.bounds.center.x, feet + .02f))),
                "Spielercollider liegt in freiem Raum, nicht mehr im Gestein");
            Check(body.linearVelocity == Vector2.zero && body.position.y > stuck.y, "Körper ist versetzt und ohne Restgeschwindigkeit");
            Check(!(bool)tracking.GetValue(movement) && (bool)grounded.GetValue(movement),
                "Fallmessung ist neu gestartet: der alte Sturz zählt nicht, ein neuer misst ab der Spawnhöhe");

            // F1 "Map generieren" calls that spawn teleport after the new map and its exotic content exist.
            string generate = Body(File.ReadAllText("Assets/GameObjects/UI/GpsRuntimePanel.cs"), "void GenerateMap()");
            int built = generate.IndexOf("map.GenerateMap();", StringComparison.Ordinal);
            int exotic = generate.IndexOf("ExoticWorldContent.Ensure(map).Regenerate();", StringComparison.Ordinal);
            int moved = generate.IndexOf("GpsTestActions.TeleportSpawn(map,", StringComparison.Ordinal);
            Check(built >= 0 && exotic > built && moved > exotic, "F1 'Map generieren' setzt den Spieler nach dem Neuaufbau auf den Spawn der neuen Karte");

            File.WriteAllText(Report, string.Join("\n", checks)); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            File.WriteAllText(Report, "FAIL " + inner + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            if (noFriction) Object.DestroyImmediate(noFriction);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            Object.DestroyImmediate(tile);
        }
    }
}
