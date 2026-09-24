using System;
using System.Reflection;
using UnityEngine;

public static class WorldBorderChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void FixedStep(MapWorldBorders borders) => typeof(MapWorldBorders)
        .GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(borders, null);

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
            foreach (var name in new[] { "World Border Left", "World Border Right", "World Border Top" })
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
            float halfWidth = camera.orthographicSize * camera.aspect;
            var clampedLeft = borders.ClampCameraCenter(camera, new Vector3(borders.LeftLimit - 10f, borders.TopLimit + 10f, -10f));
            var clampedRight = borders.ClampCameraCenter(camera, new Vector3(borders.RightLimit + 10f, 0f, -10f));
            Check(Mathf.Abs(clampedLeft.x - (borders.LeftLimit + halfWidth)) < .001f &&
                Mathf.Abs(clampedRight.x - (borders.RightLimit - halfWidth)) < .001f &&
                Mathf.Abs(clampedLeft.y - (borders.TopLimit - camera.orthographicSize)) < .001f,
                "Camera border clamp failed");
            return "PASS: 12-cell side offsets; three solid invisible walls; left/right/top position protection; camera locks its sides and top at screen edges.";
        }
        finally
        {
            body.position = originalPosition; body.linearVelocity = originalVelocity; Physics2D.SyncTransforms();
        }
    }
}
