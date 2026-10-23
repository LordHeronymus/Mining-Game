using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class WorldBorderChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void FixedStep(MapWorldBorders borders) => typeof(MapWorldBorders)
        .GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(borders, null);
    static readonly string[] WallNames = { "World Border Left", "World Border Right", "World Border Top", "World Border Bottom" };
    const string Report = "Temp/WorldBorderChecks.txt";
    static void Pass(string label) => File.AppendAllText(Report, "PASS " + label + "\n");

    // Edit Mode: temporary map in an additive scene. Checks the floor wall below the last row and the
    // bottom clamp, because caves and mining can open the last row and the player would fall out of the world.
    public static object Main()
    {
        Check(!EditorApplication.isPlaying, "Edit Mode expected");
        Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("World Border Check Grid", typeof(Grid)); grid.GetComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0f);
            var go = new GameObject("World Border Check Map", typeof(Tilemap), typeof(MapGenerator)); go.transform.SetParent(grid.transform, false);
            var map = go.GetComponent<MapGenerator>(); map.enabled = false; map.mapWidth = 40; map.mapHeight = 30;
            var borders = go.AddComponent<MapWorldBorders>(); borders.sidePaddingCells = 2; borders.topBorderY = 5f;
            borders.RefreshBounds();
            float floor = map.Terrain.CellToWorld(new Vector3Int(0, 1 - map.GeneratedHeight, 0)).y;
            Check(Mathf.Abs(borders.BottomLimit - floor) < .001f, "BottomLimit is not the lower edge of the last map row");
            for (int i = 0; i < 3; i++)
            {
                var wall = go.transform.Find(WallNames[i])?.GetComponent<BoxCollider2D>();
                Check(wall && !wall.isTrigger && wall.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Static, WallNames[i] + " is not a solid static collider");
                Check(wall.gameObject.hideFlags == HideFlags.None, WallNames[i] + " hide flags changed");
            }
            Check(!go.transform.Find("World Border Bottom"), "Edit Mode created a floor wall child that the scene would save");
            Pass("Edit Mode: three saved walls unchanged, no floor wall child in the scene");

            // Runtime creation, triggered explicitly here: never saved, same geometry as in Play Mode.
            var ensureFloor = typeof(MapWorldBorders).GetMethod("EnsureFloorWall", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(ensureFloor != null, "MapWorldBorders.EnsureFloorWall missing");
            ensureFloor.Invoke(borders, null);
            borders.RefreshBounds();
            var bottomWall = go.transform.Find("World Border Bottom")?.GetComponent<BoxCollider2D>();
            Check(bottomWall && !bottomWall.isTrigger && bottomWall.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Static, "World Border Bottom is not a solid static collider");
            Check((bottomWall.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave, "Floor wall is not DontSave");
            Pass("runtime floor wall is a solid static collider with DontSave");
            var leftWall = go.transform.Find("World Border Left").GetComponent<BoxCollider2D>();
            Vector2 center = bottomWall.transform.position, half = bottomWall.size * .5f;
            float sideBottom = leftWall.transform.position.y - leftWall.size.y * .5f;
            Check(Mathf.Abs(center.y + half.y - floor) < .001f, "Floor wall top is not at the lower edge of the last row");
            Check(Mathf.Abs(center.x - half.x - borders.LeftLimit) < .001f && Mathf.Abs(center.x + half.x - borders.RightLimit) < .001f,
                "Floor wall does not span LeftLimit..RightLimit");
            Check(Mathf.Abs(center.y - half.y - sideBottom) < .001f, "Floor wall does not close the side walls");
            Pass("floor wall spans the map width directly below the last row and closes the side walls");

            var player = new GameObject("World Border Check Player", typeof(BoxCollider2D), typeof(Rigidbody2D));
            var collider = player.GetComponent<BoxCollider2D>(); collider.size = new Vector2(.8f, 1.6f);
            var body = player.GetComponent<Rigidbody2D>(); body.gravityScale = 0f;
            var keepInside = typeof(MapWorldBorders).GetMethod("KeepInside", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(keepInside != null, "MapWorldBorders.KeepInside missing");
            void Step(Vector2 position, Vector2 velocity)
            {
                player.transform.position = position; body.linearVelocity = velocity; Physics2D.SyncTransforms();
                keepInside.Invoke(borders, new object[] { player.transform }); Physics2D.SyncTransforms();
            }
            Step(new Vector2(0f, floor - 6f), new Vector2(1.5f, -20f));
            Check(Mathf.Abs(collider.bounds.min.y - floor) < .001f, "Player below the map is not lifted onto the floor");
            Check(body.linearVelocity.y == 0f && Mathf.Approximately(body.linearVelocity.x, 1.5f), "Downward speed below the map is not stopped");
            Pass("player below the map is lifted onto the floor, downward speed reset, sideways speed kept");
            Vector2 inside = new Vector2(0f, floor + 3f);
            Step(inside, new Vector2(0f, -20f));
            Check((Vector2)player.transform.position == inside && body.linearVelocity.y == -20f, "Falling inside the map is changed");
            Pass("falling inside the map stays untouched");
            Step(new Vector2(0f, borders.TopLimit + 4f), new Vector2(0f, 3f));
            Check(collider.bounds.max.y <= borders.TopLimit + .001f && body.linearVelocity.y == 0f, "Top position clamp failed");
            Pass("top clamp still works");

            var camera = new GameObject("World Border Check Camera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 3f;
            var below = borders.ClampCameraCenter(camera, new Vector3(0f, floor - 50f, -10f));
            Check(Mathf.Abs(below.y - (floor - 50f)) < .001f, "Camera is clamped downward");
            Pass("camera stays free downward");
            return "PASS: no saved floor wall in Edit Mode; runtime floor wall (DontSave) and bottom clamp keep the player inside the map; camera free downward.";
        }
        catch (Exception error) { File.AppendAllText(Report, "FAIL " + error.Message + "\n"); throw; }
        finally
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t && t.name == "World Border Bottom") UnityEngine.Object.DestroyImmediate(t.gameObject);
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
    }

    public static string Run()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode required");
        var borders = UnityEngine.Object.FindFirstObjectByType<MapWorldBorders>();
        var map = borders.GetComponent<MapGenerator>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var body = player.GetComponent<Rigidbody2D>();
        var collider = player.GetComponent<Collider2D>();
        var camera = Camera.main;
        var originalPosition = body.position;
        var originalVelocity = body.linearVelocity;
        try
        {
            Check(borders.sidePaddingCells == 12 && Mathf.Approximately(borders.topBorderY, 100f), "Configured defaults differ");
            float cell = map.Terrain.layoutGrid.cellSize.x;
            float expectedLeft = map.Terrain.CellToWorld(new Vector3Int(-map.GeneratedWidth / 2 + 12, 0)).x;
            float expectedRight = map.Terrain.CellToWorld(new Vector3Int(map.GeneratedWidth / 2 - 12, 0)).x;
            Check(Mathf.Abs(borders.LeftLimit - expectedLeft) < .001f && Mathf.Abs(borders.RightLimit - expectedRight) < .001f,
                "Side padding is not twelve cells");
            foreach (var name in WallNames)
            {
                var wall = map.transform.Find(name)?.GetComponent<BoxCollider2D>();
                Check(wall && !wall.isTrigger && wall.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Static, name + " is not a solid static collider");
            }
            body.position = new Vector2(borders.LeftLimit - collider.bounds.extents.x - 4f, 4f);
            body.linearVelocity = new Vector2(-3f, 0f); Physics2D.SyncTransforms(); FixedStep(borders); Physics2D.SyncTransforms();
            Check(collider.bounds.min.x >= borders.LeftLimit - .001f && body.linearVelocity.x == 0f, "Left position clamp failed");
            body.position = new Vector2(borders.RightLimit + collider.bounds.extents.x + 4f, 4f);
            body.linearVelocity = new Vector2(3f, 0f); Physics2D.SyncTransforms(); FixedStep(borders); Physics2D.SyncTransforms();
            Check(collider.bounds.max.x <= borders.RightLimit + .001f && body.linearVelocity.x == 0f, "Right position clamp failed");
            body.position = new Vector2(0f, borders.TopLimit + collider.bounds.extents.y + 4f);
            body.linearVelocity = new Vector2(0f, 3f); Physics2D.SyncTransforms(); FixedStep(borders); Physics2D.SyncTransforms();
            Check(collider.bounds.max.y <= borders.TopLimit + .001f && body.linearVelocity.y == 0f, "Top position clamp failed");
            body.position = new Vector2(0f, borders.BottomLimit - collider.bounds.extents.y - 4f);
            body.linearVelocity = new Vector2(0f, -3f); Physics2D.SyncTransforms(); FixedStep(borders); Physics2D.SyncTransforms();
            Check(collider.bounds.min.y >= borders.BottomLimit - .001f && body.linearVelocity.y == 0f, "Bottom position clamp failed");
            float halfWidth = camera.orthographicSize * camera.aspect;
            var clampedLeft = borders.ClampCameraCenter(camera, new Vector3(borders.LeftLimit - 10f, borders.TopLimit + 10f, -10f));
            var clampedRight = borders.ClampCameraCenter(camera, new Vector3(borders.RightLimit + 10f, 0f, -10f));
            Check(Mathf.Abs(clampedLeft.x - (borders.LeftLimit + halfWidth)) < .001f &&
                Mathf.Abs(clampedRight.x - (borders.RightLimit - halfWidth)) < .001f &&
                Mathf.Abs(clampedLeft.y - (borders.TopLimit - camera.orthographicSize)) < .001f,
                "Camera border clamp failed");
            return "PASS: 12-cell side offsets; four solid invisible walls; left/right/top/bottom position protection; camera locks its sides and top at screen edges.";
        }
        finally
        {
            body.position = originalPosition; body.linearVelocity = originalVelocity; Physics2D.SyncTransforms();
        }
    }
}
