using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Edit Mode. An F1/GPS gravity change while hanging on a ladder or flying keeps the body weightless and is the value
// restored afterwards (it was overwritten by the value remembered at attach), and F1 "Zum Spawn" lands on the nearest solid
// cell of surface row 0 when the spawn cell (0, 0) is dug away. Builds its own player and small map in a temporary
// additive scene; the open SampleScene, the GPS profile and the test settings are not changed.
public static class BughuntGravityHoldChecks
{
    const string Report = "Temp/BughuntGravityHoldChecks.txt";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static FieldInfo Field(Type type, string name) => type.GetField(name, Any) ?? throw new Exception(type.Name + "." + name + " fehlt");

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear(); Directory.CreateDirectory("Temp");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            var player = new GameObject("Gravity hold check player");
            var body = player.AddComponent<Rigidbody2D>(); body.gravityScale = 2.4f;
            var collider = player.AddComponent<BoxCollider2D>(); collider.size = new Vector2(.6f, 1f);
            var movement = player.AddComponent<PlayerMovement>();
            var ladder = player.AddComponent<PlayerLadder>();
            Physics2D.SyncTransforms();
            // Awake does not run in Edit Mode: wire the cached references like Awake would.
            Field(typeof(PlayerMovement), "rb").SetValue(movement, body);
            Field(typeof(PlayerMovement), "col").SetValue(movement, collider);
            Field(typeof(PlayerMovement), "ladder").SetValue(movement, ladder);
            Field(typeof(PlayerLadder), "body").SetValue(ladder, body);
            Field(typeof(PlayerLadder), "bodyCollider").SetValue(ladder, collider);

            // GpsSettings.SetValue applies "Gravitationsfaktor" (applyTime Immediately) through ApplyField to the player body.
            var applyField = typeof(GpsSettings).GetMethod("ApplyField", Any, null, new[] { typeof(Object), typeof(GpsValue), typeof(bool) }, null)
                ?? throw new Exception("GpsSettings.ApplyField fehlt");
            void ApplyGravity(float value) => applyField.Invoke(null, new object[] { body, GpsCodec.Read("gravityScale", typeof(float), value, _ => null), true });
            ApplyGravity(3f);
            Check(body.gravityScale == 3f, "Ohne Leiter und Flug wirkt eine GPS-Änderung des Gravitationsfaktors weiterhin sofort auf den Körper");
            ApplyGravity(2.4f);

            // Ladder: attach, change gravity while hanging, let go.
            var attach = typeof(PlayerLadder).GetMethod("Attach", Any, null, Type.EmptyTypes, null) ?? throw new Exception("PlayerLadder.Attach fehlt");
            attach.Invoke(ladder, null);
            Check(ladder.IsClimbing && body.gravityScale == 0f, "Einhängen setzt die Schwerkraft auf 0");
            ApplyGravity(4f);
            Check(body.gravityScale == 0f, "F1-Änderung an der Leiter lässt den Körper schwerelos (kein Abrutschen ohne Eingabe)");
            ladder.Detach();
            Check(!ladder.IsClimbing && body.gravityScale == 4f, "Loslassen der Leiter stellt den neuen F1-Wert her, nicht den beim Einhängen gemerkten (" + body.gravityScale + ")");
            attach.Invoke(ladder, null); ladder.Detach();
            Check(body.gravityScale == 4f, "Ohne F1-Änderung stellt die Leiter den Faktor unverändert wieder her");

            // Fly/NoClip: the same state SyncFlyMode enters (flying, gravity remembered, body weightless).
            var flying = Field(typeof(PlayerMovement), "flying");
            var previousGravity = Field(typeof(PlayerMovement), "previousGravity");
            flying.SetValue(movement, true); previousGravity.SetValue(movement, body.gravityScale); body.gravityScale = 0f;
            ApplyGravity(0f);
            Check(body.gravityScale == 0f && (float)previousGravity.GetValue(movement) == 0f, "F1-Wert 0 im Flug wird als wiederherzustellender Wert übernommen");
            ApplyGravity(1.5f);
            Check(body.gravityScale == 0f, "F1-Änderung im Flug lässt den Körper schwerelos (kein Absinken)");
            Check((float)previousGravity.GetValue(movement) == 1.5f, "Flugmodus merkt sich den neuen F1-Wert für das Ausschalten");
            if (!GameplayTestSettings.FlyMode && !GameplayTestSettings.NoClipMode)
            {
                var sync = typeof(PlayerMovement).GetMethod("SyncFlyMode", Any) ?? throw new Exception("PlayerMovement.SyncFlyMode fehlt");
                sync.Invoke(movement, null);
                Check(!(bool)flying.GetValue(movement) && body.gravityScale == 1.5f, "Fly aus stellt den neuen F1-Wert her (" + body.gravityScale + ")");
            }
            else flying.SetValue(movement, false);
            body.gravityScale = 2.4f;

            // Spawn teleport: small map, surface row 0, rock below.
            const int width = 16, height = 6;
            var grid = new GameObject("Gravity hold check grid", typeof(Grid)); grid.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
            var mapObject = new GameObject("Gravity hold check map", typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGenerator));
            mapObject.transform.SetParent(grid.transform, false);
            var map = mapObject.GetComponent<MapGenerator>();
            var tiles = new TileBase[width * height]; for (int i = 0; i < tiles.Length; i++) tiles[i] = tile;
            map.Terrain.SetTilesBlock(new BoundsInt(-width / 2, 1 - height, 0, width, height, 1), tiles);
            var teleportSpawn = typeof(GpsTestActions).GetMethod("TeleportSpawn", Any, null, new[] { typeof(MapGenerator), typeof(PlayerMovement) }, null)
                ?? throw new Exception("GpsTestActions.TeleportSpawn(map, player) fehlt");
            var deep = map.Terrain.GetCellCenterWorld(new Vector3Int(0, -4, 0));
            float surface = map.Terrain.GetCellCenterWorld(Vector3Int.zero).y + 1.1f * .5f;
            void Bury() { body.position = deep; player.transform.position = deep; body.linearVelocity = new Vector2(0, -5f); Physics2D.SyncTransforms(); }
            void ExpectLanding(int x, string label)
            {
                Bury(); teleportSpawn.Invoke(null, new object[] { map, movement });
                float feet = collider.bounds.min.y, target = map.Terrain.GetCellCenterWorld(new Vector3Int(x, 0, 0)).x;
                Check(Mathf.Abs(collider.bounds.center.x - target) < .01f && feet >= surface - .001f && feet < surface + .1f && body.linearVelocity == Vector2.zero,
                    label + " (x " + collider.bounds.center.x.ToString("0.##") + ", Füsse " + feet.ToString("0.###") + ")");
            }

            ExpectLanding(0, "Zum Spawn setzt auf die intakte Spawnzelle (0, 0) wie bisher");
            map.Terrain.SetTile(Vector3Int.zero, null);
            ExpectLanding(-1, "Abgebaute Spawnzelle: Zum Spawn setzt auf die nächste feste Zelle (−1, 0) der Oberflächenreihe");
            map.Terrain.SetTile(new Vector3Int(-1, 0, 0), null); map.Terrain.SetTile(new Vector3Int(1, 0, 0), null); map.Terrain.SetTile(new Vector3Int(-2, 0, 0), null);
            ExpectLanding(2, "Breites Loch an der Oberfläche: Zum Spawn sucht nach aussen bis zur festen Zelle (2, 0)");
            for (int x = -width / 2; x < width / 2; x++) map.Terrain.SetTile(new Vector3Int(x, 0, 0), null);
            Bury(); teleportSpawn.Invoke(null, new object[] { map, movement });
            Check(body.position == (Vector2)deep, "Ganz leere Oberflächenreihe: Zum Spawn bewegt den Spieler nicht");
            map.Terrain.ClearAllTiles();
            Bury(); teleportSpawn.Invoke(null, new object[] { map, movement });
            Check(body.position == (Vector2)deep, "Leere Oberflächenreihe: Zum Spawn tut weiterhin nichts");

            File.WriteAllText(Report, string.Join("\n", checks)); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            File.WriteAllText(Report, "FAIL " + inner + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            Object.DestroyImmediate(tile);
        }
    }
}
