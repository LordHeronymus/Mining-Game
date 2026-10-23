// CI: local-only
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Side niche one cell up, barely taller than the player: jumping and holding towards it must end standing inside.
// Built from the real terrain outlines (several shape variants, both directions, early and late input) in a local physics scene.
public static class LedgeAssistChecks
{
    const float Cell = 1.1f;
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Requires Play Mode (game scene with terrain).");
        var appearance = UnityEngine.Object.FindFirstObjectByType<UniformStoneAppearance>();
        var stats = UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (!appearance || !stats || !player) throw new Exception("Terrain, player and player stats are required.");
        var map = appearance.GetComponent<MapGenerator>();
        float cell = Mathf.Abs((map.Terrain.CellToWorld(Vector3Int.up) - map.Terrain.CellToWorld(Vector3Int.zero)).y);
        if (Mathf.Abs(cell - Cell) > .001f) throw new Exception("Unexpected cell size " + cell);
        var playerSize = (Vector2)player.GetComponent<Collider2D>().bounds.size;
        float gravityScale = player.GetComponent<Rigidbody2D>().gravityScale;
        var shapes = new TerrainCollisionShape(appearance, map.Terrain);
        if (!shapes.Ready) throw new Exception("Terrain collision shapes not ready.");
        var scene = SceneManager.CreateScene("Ledge assist check", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var physics = scene.GetPhysicsScene2D();
        var root = new GameObject("Ledge assist fixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        var report = new List<string>();
        try
        {
            var body = CreateActor(root, stats, playerSize, gravityScale, out var movement);
            int passed = 0, skipped = 0, baselinePassed = 0, total = 0;
            var failures = new List<string>();
            foreach (int sign in new[] { 1, -1 })
            foreach (bool tall in new[] { false, true })
            for (int variant = 0; variant < 6; variant++)
            {
                var terrain = BuildNiche(root, shapes, sign, tall, new Vector2Int(variant * 7, variant * 3));
                Physics2D.SyncTransforms();
                if (!NicheFits(physics, sign, playerSize, out float nicheFloor)) { skipped++; UnityEngine.Object.DestroyImmediate(terrain); continue; }
                foreach (int lateInput in new[] { 0, 18 })
                {
                    total++;
                    if (Run(movement, body, physics, sign, lateInput, true, nicheFloor, out string detail)) passed++;
                    else failures.Add($"dir={sign} tall={tall} variant={variant} late={lateInput}: {detail}");
                    if (!tall && Run(movement, body, physics, sign, lateInput, false, nicheFloor, out _)) baselinePassed++;
                }
                UnityEngine.Object.DestroyImmediate(terrain);
            }
            if (failures.Count > 0) throw new Exception($"Niche not entered in {failures.Count}/{total} cases:\n" + string.Join("\n", failures));
            if (total < 24) throw new Exception($"Too few niche cases fit the player ({total}, skipped {skipped}).");
            report.Add($"PASS niche entry: {passed}/{total} cases (skipped {skipped} variants too low for the player); without assist {baselinePassed} of {total / 2} low-niche cases");

            report.Add(CheckFlatFall(root, movement, body, physics));
            return string.Join("\n", report);
        }
        finally
        {
            shapes.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    static Rigidbody2D CreateActor(GameObject root, StatsManager stats, Vector2 size, float gravityScale, out PlayerMovement movement)
    {
        var actor = new GameObject("Player");
        actor.layer = 30;
        actor.transform.SetParent(root.transform);
        actor.AddComponent<BoxCollider2D>().size = size;
        var body = actor.AddComponent<Rigidbody2D>();
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        movement = actor.AddComponent<PlayerMovement>();
        movement.enabled = false;
        typeof(PlayerMovement).GetField("stats", Flags).SetValue(movement, stats);
        typeof(PlayerMovement).GetField("groundLayer", Flags).SetValue(movement, (LayerMask)(1 << 31));
        body.gravityScale = gravityScale;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        return body;
    }

    // Shaft column 0 (rows 0-4), niche cells one row up beside it, two deep; tall adds a second niche row.
    static GameObject BuildNiche(GameObject root, TerrainCollisionShape shapes, int sign, bool tall, Vector2Int variantOffset)
    {
        var open = new HashSet<Vector2Int>();
        for (int y = 0; y <= 4; y++) open.Add(new Vector2Int(0, y));
        for (int x = 1; x <= 2; x++) { open.Add(new Vector2Int(sign * x, 1)); if (tall) open.Add(new Vector2Int(sign * x, 2)); }
        var terrain = new GameObject("Terrain");
        terrain.layer = 31;
        terrain.transform.SetParent(root.transform);
        var rb = terrain.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        terrain.AddComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
        var collider = terrain.AddComponent<PolygonCollider2D>();
        collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        var paths = new List<Vector2[]>();
        for (int x = -4; x <= 4; x++)
        for (int y = -2; y <= 6; y++)
        {
            var c = new Vector2Int(x, y);
            if (open.Contains(c)) continue;
            int bits = 0;
            for (int i = 0; i < 8; i++)
            {
                var n = TerrainCollisionShape.NeighbourOffsets[i];
                if (open.Contains(c + new Vector2Int(n.x, n.y))) bits |= 1 << i;
            }
            Vector2[] outline = bits == 0
                ? new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }
                : shapes.Outline(bits, TerrainCollisionShape.Variant(new Vector3Int(x + variantOffset.x, y + variantOffset.y, 0)), false);
            var path = new Vector2[outline.Length];
            for (int i = 0; i < path.Length; i++) path[i] = new Vector2((x + outline[i].x) * Cell, (y + outline[i].y) * Cell);
            paths.Add(path);
        }
        collider.pathCount = paths.Count;
        for (int i = 0; i < paths.Count; i++) collider.SetPath(i, paths[i]);
        return terrain;
    }

    static bool NicheFits(PhysicsScene2D physics, int sign, Vector2 size, out float floor)
    {
        float x = sign > 0 ? 1.6f * Cell : -.6f * Cell;
        floor = 0f;
        var hit = physics.Raycast(new Vector2(x, 1.5f * Cell), Vector2.down, Cell, 1 << 31);
        if (!hit.collider) return false;
        floor = hit.point.y;
        return !physics.OverlapBox(new Vector2(x, floor + size.y * .5f + .03f), size - Vector2.one * .02f, 0f, 1 << 31);
    }

    static void Reset(PlayerMovement movement, Rigidbody2D body, Vector2 position)
    {
        typeof(PlayerMovement).GetField("inputX", Flags).SetValue(movement, 0f);
        typeof(PlayerMovement).GetField("previousRunVelocity", Flags).SetValue(movement, 0f);
        typeof(PlayerMovement).GetField("ledgeAssistRemaining", Flags).SetValue(movement, 0f);
        typeof(PlayerMovement).GetField("jumpBufferRemaining", Flags).SetValue(movement, 0f);
        body.position = position;
        body.linearVelocity = Vector2.zero;
        body.transform.position = position;
        Physics2D.SyncTransforms();
    }

    static bool Run(PlayerMovement movement, Rigidbody2D body, PhysicsScene2D physics, int sign, int lateInput, bool assist, float nicheFloor, out string detail)
    {
        var tick = typeof(PlayerMovement).GetMethod("FixedUpdate", Flags);
        movement.ledgeAssist = assist;
        try
        {
            Reset(movement, body, new Vector2(.55f * Cell, .6f));
            for (int i = 0; i < 25; i++) { tick.Invoke(movement, null); physics.Simulate(Time.fixedDeltaTime); }
            if (!movement.IsOnGround) { detail = "player not standing at start"; return false; }
            // Pressed against the wall below the niche before the jump, like in the game.
            typeof(PlayerMovement).GetField("inputX", Flags).SetValue(movement, (float)sign);
            for (int i = 0; i < 20; i++) { tick.Invoke(movement, null); physics.Simulate(Time.fixedDeltaTime); }
            if (lateInput > 0) typeof(PlayerMovement).GetField("inputX", Flags).SetValue(movement, 0f);
            typeof(PlayerMovement).GetField("jumpBufferRemaining", Flags).SetValue(movement, .12f);
            float highest = body.position.y;
            bool assisted = false;
            for (int frame = 0; frame < 150; frame++)
            {
                if (frame == lateInput) typeof(PlayerMovement).GetField("inputX", Flags).SetValue(movement, (float)sign);
                tick.Invoke(movement, null);
                physics.Simulate(Time.fixedDeltaTime);
                assisted |= movement.IsLedgeAssisting;
                highest = Mathf.Max(highest, body.position.y);
            }
            var box = body.GetComponent<Collider2D>().bounds;
            float edge = sign > 0 ? Cell : 0f;
            bool inside = (body.position.x - edge) * sign > .3f;
            bool onFloor = movement.IsOnGround && Mathf.Abs(box.min.y - nicheFloor) < .08f;
            detail = $"x={body.position.x:0.00} feet={box.min.y:0.00} floor={nicheFloor:0.00} highest={highest:0.00} grounded={movement.IsOnGround} assisted={assisted}";
            return inside && onFloor;
        }
        finally { movement.ledgeAssist = true; }
    }

    // Falling next to flat ground while holding a direction must stay a plain fall (same path with and without assist).
    static string CheckFlatFall(GameObject root, PlayerMovement movement, Rigidbody2D body, PhysicsScene2D physics)
    {
        var floor = new GameObject("Flat floor");
        floor.layer = 31;
        floor.transform.SetParent(root.transform);
        floor.transform.position = new Vector3(0f, -.5f, 0f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f);
        Physics2D.SyncTransforms();
        var tick = typeof(PlayerMovement).GetMethod("FixedUpdate", Flags);
        var paths = new List<Vector2>[2];
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                movement.ledgeAssist = pass == 0;
                paths[pass] = new List<Vector2>();
                Reset(movement, body, new Vector2(0f, 3f));
                typeof(PlayerMovement).GetField("inputX", Flags).SetValue(movement, 1f);
                for (int frame = 0; frame < 90; frame++)
                {
                    tick.Invoke(movement, null);
                    physics.Simulate(Time.fixedDeltaTime);
                    if (movement.IsLedgeAssisting) throw new Exception("Ledge assist started over flat ground at frame " + frame);
                    paths[pass].Add(body.position);
                }
            }
            for (int i = 0; i < paths[0].Count; i++)
                if ((paths[0][i] - paths[1][i]).sqrMagnitude > 1e-8f) throw new Exception($"Flat fall differs with assist at frame {i}: {paths[0][i]} vs {paths[1][i]}");
            return "PASS flat fall: identical path with and without assist, no assist started";
        }
        finally
        {
            movement.ledgeAssist = true;
            UnityEngine.Object.DestroyImmediate(floor);
        }
    }
}
