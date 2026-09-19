using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class MinerPlayerChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static float Value(MinerPlayerVisual visual, string field) => (float)typeof(MinerPlayerVisual).GetField(field, Private).GetValue(visual);
    static void Animate(MinerPlayerVisual visual, float dt, Vector2 velocity, bool grounded, bool flying, bool mining, Vector2 target)
        => typeof(MinerPlayerVisual).GetMethod("Animate", Private).Invoke(visual, new object[] { dt, velocity, grounded, flying, mining, target });
    static bool Different(Vector3[] a, Vector3[] b)
    {
        if (a.Length != b.Length) return true;
        for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > .00001f) return true;
        return false;
    }

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Run fixture in Edit mode.");
        var installed = UnityEngine.Object.FindFirstObjectByType<MinerPlayerVisual>();
        Check(installed, "Miner not installed");
        Check(installed.legacySprite && !installed.legacySprite.enabled, "Ball is still visible");
        var collider = installed.GetComponent<CapsuleCollider2D>();
        Check(collider && collider.bounds.size.x < .5f && collider.bounds.size.y < 1, "Miner cannot fit existing passages");
        Check(!installed.GetComponent<CircleCollider2D>(), "Old rolling collider remains");
        Check((installed.GetComponent<Rigidbody2D>().constraints & RigidbodyConstraints2D.FreezeRotation) != 0, "Miner still rolls");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Miner test fixture"); root.layer = 30; SceneManager.MoveGameObjectToScene(root, scene);
        var target = new RenderTexture(1400, 650, 24); var previous = RenderTexture.active; Texture2D capture = null;
        try
        {
            var figures = new MinerPlayerVisual[4];
            for (int i = 0; i < figures.Length; i++)
            {
                var go = new GameObject("Pose " + i); go.layer = 30; go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(-1.8f + i * 1.2f, .47f, 0);
                var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic; rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                var capsule = go.AddComponent<CapsuleCollider2D>(); capsule.size = new Vector2(.46f, .94f);
                var visual = go.AddComponent<MinerPlayerVisual>(); visual.material = installed.material; visual.height = installed.height; visual.Refresh();
                figures[i] = visual;
            }
            Physics2D.SyncTransforms();
            var walking = figures[1];
            Animate(walking, .1f, new Vector2(3, 0), true, false, false, Vector2.right);
            var mesh = walking.GetComponentInChildren<MeshFilter>().sharedMesh; var firstStep = mesh.vertices;
            Animate(walking, .1f, new Vector2(3, 0), true, false, false, Vector2.right);
            Check(Different(firstStep, mesh.vertices), "Walking limbs do not animate");
            Animate(walking, .1f, new Vector2(-3, 0), true, false, false, Vector2.left);
            Check((int)typeof(MinerPlayerVisual).GetField("facing", Private).GetValue(walking) == -1, "Walking does not change facing");
            Animate(walking, .1f, new Vector2(3, 0), true, false, false, Vector2.right);
            Animate(figures[2], .2f, new Vector2(2, 4), false, false, false, Vector2.right);
            Check(Value(figures[2], "airborne") > .99f && Value(figures[2], "walking") < .01f, "Jump did not tuck legs");
            Animate(figures[2], .2f, new Vector2(0, 2), false, true, false, Vector2.right);
            Check(Value(figures[2], "airborne") > .99f, "Fly mode pose missing");
            var mining = figures[3];
            Vector2 aim = (Vector2)mining.transform.position + new Vector2(1, -.6f);
            Animate(mining, .1f, Vector2.zero, true, false, true, aim);
            var pickBefore = mining.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            Animate(mining, .13f, Vector2.zero, true, false, true, aim);
            Check(Value(mining, "miningWeight") > .99f && Different(pickBefore, mining.GetComponentInChildren<MeshFilter>().sharedMesh.vertices), "Pickaxe does not swing");
            var idleBefore = figures[0].GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            Animate(figures[0], .4f, Vector2.zero, true, false, false, Vector2.right);
            Check(Different(idleBefore, figures[0].GetComponentInChildren<MeshFilter>().sharedMesh.vertices), "Idle breathing missing");
            foreach (var visual in figures)
                foreach (var vertex in visual.GetComponentInChildren<MeshFilter>().sharedMesh.vertices)
                    Check(!float.IsNaN(vertex.x) && !float.IsInfinity(vertex.y), "Invalid mesh vertex");

            var cameraGo = new GameObject("Preview camera", typeof(Camera)); cameraGo.transform.SetParent(root.transform, false);
            var camera = cameraGo.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.3f;
            camera.transform.position = new Vector3(.1f, .62f, -10); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .060f, .09f);
            camera.scene = scene;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(1400, 650, TextureFormat.RGB24, false); capture.ReadPixels(new Rect(0, 0, 1400, 650), 0, 0); capture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MinerPlayer-Poses.png"), capture.EncodeToPNG());

            // Isolated physics proves the new capsule can traverse a two-tile-high passage.
            foreach (var visual in figures) visual.gameObject.SetActive(false);
            var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.transform.SetParent(root.transform, false);
            floor.layer = 3;
            floor.transform.position = new Vector3(0, -.1f, 0); floor.GetComponent<BoxCollider2D>().size = new Vector2(8, .2f);
            var roof = new GameObject("Roof", typeof(BoxCollider2D)); roof.transform.SetParent(root.transform, false);
            roof.layer = 3;
            roof.transform.position = new Vector3(0, 1.1f, 0); roof.GetComponent<BoxCollider2D>().size = new Vector2(8, .2f);
            var testBody = new GameObject("Physics miner", typeof(Rigidbody2D), typeof(CapsuleCollider2D)); testBody.transform.SetParent(root.transform, false);
            testBody.layer = 6;
            testBody.transform.position = new Vector3(0, .48f, 0);
            var body = testBody.GetComponent<Rigidbody2D>(); body.gravityScale = 3; body.constraints = RigidbodyConstraints2D.FreezeRotation;
            testBody.GetComponent<CapsuleCollider2D>().size = new Vector2(.46f, .94f);
            Physics2D.SyncTransforms(); var physics = scene.GetPhysicsScene2D();
            Check(physics != Physics2D.defaultPhysicsScene, "Fixture requires isolated physics");
            for (int i = 0; i < 30; i++) { body.linearVelocity = new Vector2(3, body.linearVelocity.y); physics.Simulate(.02f); }
            Check(body.position.x > 1.5f && body.position.y > .4f && body.position.y < .55f && Mathf.Abs(body.rotation) < .01f,
                "Capsule cannot traverse the passage upright: position=" + body.position + ", rotation=" + body.rotation + ", scene=" + testBody.scene.name);
            var miner = testBody.AddComponent<TileMiner>();
            typeof(TileMiner).GetField("mining", Private).SetValue(miner, true);
            typeof(TileMiner).GetMethod("Update", Private).Invoke(miner, null);
            Check(!miner.IsMining, "Mining animation remains active without a valid target");
            return "PASS: installed capsule/hidden ball/upright body, walk and facing, idle breathing, jump/fly pose, mining swing/reset, finite geometry, passage physics. Pose preview rendered.";
        }
        finally
        {
            RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(target);
            if (capture) UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
