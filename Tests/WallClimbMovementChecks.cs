using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WallClimbMovementChecks
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        var appearance = UnityEngine.Object.FindFirstObjectByType<UniformStoneAppearance>();
        var stats = UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        if (!appearance || !stats) throw new Exception("Terrain and player stats are required.");
        var map = appearance.GetComponent<MapGenerator>();
        var shapes = new TerrainCollisionShape(appearance, map.Terrain);
        var scene = SceneManager.CreateScene("Wall climb check", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var physics = scene.GetPhysicsScene2D();
        var root = new GameObject("Wall climb fixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        float previousSpeed = stats.MoveSpeed;
        bool previousFly = GameplayTestSettings.FlyMode;
        try
        {
            stats.MoveSpeed = 2f;
            GameplayTestSettings.SetMode(GameplayTestMode.Fly, false, out _);
            var floor = new GameObject("Floor");
            floor.layer = 31;
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0f, -.5f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f);

            var actor = new GameObject("Player");
            actor.layer = 31;
            actor.transform.SetParent(root.transform);
            var shape = actor.AddComponent<BoxCollider2D>();
            shape.size = new Vector2(.5f, 1f);
            var body = actor.AddComponent<Rigidbody2D>();
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            var movement = actor.AddComponent<PlayerMovement>();
            movement.enabled = false;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PlayerMovement).GetField("stats", flags).SetValue(movement, stats);
            typeof(PlayerMovement).GetField("groundLayer", flags).SetValue(movement, (LayerMask)(1 << 31));
            var tick = typeof(PlayerMovement).GetMethod("FixedUpdate", flags);

            foreach (int sign in new[] { 1, -1 })
            {
                var wall = new GameObject("Three-tile wall");
                wall.layer = 31;
                wall.transform.SetParent(root.transform);
                wall.transform.position = new Vector3(sign > 0 ? .65f : -1.75f, 0f, 0f);
                var wallBody = wall.AddComponent<Rigidbody2D>();
                wallBody.bodyType = RigidbodyType2D.Static;
                wall.AddComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
                var collider = wall.AddComponent<PolygonCollider2D>();
                collider.compositeOperation = Collider2D.CompositeOperation.Merge;
                collider.pathCount = 3;
                for (int row = 0; row < 3; row++)
                {
                    var outline = shapes.Outline(sign > 0 ? 1 : 2, row * 3, false);
                    var path = new Vector2[outline.Length];
                    for (int i = 0; i < path.Length; i++)
                        path[i] = new Vector2(outline[i].x * 1.1f,
                            (row + outline[i].y) * 1.1f);
                    collider.SetPath(row, path);
                }

                body.position = new Vector2(0f, .5f);
                body.linearVelocity = Vector2.zero;
                typeof(PlayerMovement).GetField("inputX", flags).SetValue(movement, (float)sign);
                typeof(PlayerMovement).GetField("previousRunVelocity", flags).SetValue(movement, 0f);
                Physics2D.SyncTransforms();
                float highest = body.position.y;
                for (int frame = 0; frame < 140; frame++)
                {
                    tick.Invoke(movement, null);
                    physics.Simulate(Time.fixedDeltaTime);
                    highest = Mathf.Max(highest, body.position.y);
                }
                if (highest > .58f)
                    throw new Exception($"Player climbed a tall wall: direction={sign}, height={highest}.");
                UnityEngine.Object.DestroyImmediate(wall);
            }
            return "PASS: no climbing against tall terrain walls from either direction over 140 physics steps.";
        }
        finally
        {
            stats.MoveSpeed = previousSpeed;
            GameplayTestSettings.SetMode(GameplayTestMode.Fly, previousFly, out _);
            shapes.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
