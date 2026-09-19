using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class MinerGaitChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static float Get(MinerPlayerVisual v, string name) => (float)typeof(MinerPlayerVisual).GetField(name, Private).GetValue(v);
    static void Animate(MinerPlayerVisual v, float dt, float speed, bool grounded = true, bool flying = false)
        => typeof(MinerPlayerVisual).GetMethod("Animate", Private).Invoke(v,
            new object[] { dt, new Vector2(speed, 0), grounded, flying, false, (Vector2)v.transform.position + Vector2.right });

    static void CheckTrajectories()
    {
        for (int blend = 0; blend <= 20; blend++)
        {
            float run = blend / 20f, stride = Mathf.Lerp(.34f, .44f, run), duty = Mathf.Lerp(.62f, .30f, run);
            float lift = Mathf.Lerp(.10f, .19f, run);
            for (int i = 0; i < 1000; i++)
            {
                float phase = i / 1000f;
                float bob = (.5f - .5f * Mathf.Cos((phase - Mathf.Lerp(.06f, .18f, run)) * Mathf.PI * 4)) * Mathf.Lerp(.012f, .027f, run);
                for (int leg = 0; leg < 2; leg++)
                {
                    var foot = MinerGait.Sample(phase + leg * .5f, stride, duty, lift);
                    var hip = new Vector2(leg == 0 ? .035f : -.035f, Mathf.Lerp(.50f, .43f, run) + bob);
                    Check(Vector2.Distance(hip, foot.ankle) < .47f, "Leg overextends");
                    var knee = MinerGait.Knee(hip, foot.ankle, .24f, .23f);
                    Check(Mathf.Abs(Vector2.Distance(hip, knee) - .24f) < .0001f, "Thigh length changes");
                    Check(Mathf.Abs(Vector2.Distance(knee, foot.ankle) - .23f) < .0001f, "Shin length changes");
                    float a = foot.angle * Mathf.Deg2Rad;
                    float sole = foot.ankle.y - .081f * Mathf.Cos(a) + Mathf.Min(-.056f * Mathf.Sin(a), .123f * Mathf.Sin(a)) - .015f;
                    Check(sole >= -.0001f, "Boot passes through floor");
                    if (foot.planted) Check(Mathf.Abs(foot.ankle.y - .10f) < .00001f, "Stance foot bounces");
                }
            }
            const float eps = .00001f;
            foreach (float boundary in new[] { duty, 1f })
            {
                var before = MinerGait.Sample(boundary - eps, stride, duty, lift);
                var at = MinerGait.Sample(boundary, stride, duty, lift);
                var after = MinerGait.Sample(boundary + eps, stride, duty, lift);
                Check(Vector2.Distance(before.ankle, after.ankle) < .0001f, "Foot position jumps at contact");
                Vector2 v0 = (at.ankle - before.ankle) / eps, v1 = (after.ankle - at.ankle) / eps;
                Check(Vector2.Distance(v0, v1) < .03f, "Foot velocity jumps at contact");
            }
            float sample = duty * .3f, worldScale = 1.06f / 1.29f, speed = Mathf.Lerp(1.3f, 7, run);
            float cycleRate = speed * duty / (stride * worldScale), dt = .0001f;
            float x0 = MinerGait.Sample(sample, stride, duty, lift).ankle.x * worldScale;
            float x1 = MinerGait.Sample(sample + dt * cycleRate, stride, duty, lift).ankle.x * worldScale + speed * dt;
            Check(Mathf.Abs(x1 - x0) < .000001f, "Planted foot slides in world space");
        }
    }

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Run in Edit mode.");
        CheckTrajectories();
        var installed = UnityEngine.Object.FindFirstObjectByType<MinerPlayerVisual>();
        Check(installed && installed.material, "Installed miner missing");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Gait check"); root.layer = 30; SceneManager.MoveGameObjectToScene(root, scene);
        var target = new RenderTexture(1200, 480, 24);
        var capture = new Texture2D(1200, 480, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        CritterMesh ground = null;
        string folder = Path.GetFullPath("Temp/MinerGaitPreview"); Directory.CreateDirectory(folder);
        try
        {
            var figures = new MinerPlayerVisual[3];
            float[] speeds = { 1.3f, 3f, 7f };
            for (int i = 0; i < figures.Length; i++)
            {
                var go = new GameObject("Gait " + i); go.layer = 30; go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(-1.5f + i * 1.5f, .47f);
                var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<CapsuleCollider2D>().size = new Vector2(.46f, .94f);
                var v = go.AddComponent<MinerPlayerVisual>(); v.material = installed.material; v.height = installed.height; v.Refresh();
                figures[i] = v;
                for (int step = 0; step < 60; step++) Animate(v, 1f / 60, speeds[i]);
            }
            Physics2D.SyncTransforms();
            var cameraGo = new GameObject("Gait camera", typeof(Camera)); cameraGo.transform.SetParent(root.transform, false);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = .95f; camera.transform.position = new Vector3(.05f, .48f, -10);
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .060f, .09f); camera.targetTexture = target;
            ground = new CritterMesh(root.transform, "Floor markers", installed.material, 29);
            for (int frame = 0; frame < 90; frame++)
            {
                ground.Clear();
                for (int i = 0; i < figures.Length; i++)
                {
                    float center = -1.5f + i * 1.5f;
                    ground.Begin(new Vector2(center, 0), 1, 1, Color.white);
                    ground.Stroke(-.69f, -.02f, .69f, -.02f, .012f, new Color(.28f, .36f, .42f));
                    for (int mark = -3; mark <= 3; mark++)
                    {
                        float x = Mathf.Repeat(mark * .20f - speeds[i] * (frame + 1) / 60, 1.4f) - .7f;
                        ground.Stroke(x, -.065f, x + .07f, -.065f, .010f, new Color(.18f, .25f, .30f));
                    }
                    Animate(figures[i], 1f / 60, speeds[i]);
                }
                ground.Upload(); camera.Render(); RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 1200, 480), 0, 0); capture.Apply();
                File.WriteAllBytes(Path.Combine(folder, "frame-" + frame.ToString("D3") + ".png"), capture.EncodeToPNG());
            }
            var figure = figures[0];
            float phaseBefore = Get(figure, "walkPhase");
            Animate(figure, .2f, 5, false);
            Check(Get(figure, "walkPhase") == phaseBefore && Get(figure, "walking") < .01f, "Legs keep running in air");
            Animate(figure, .2f, 5, false, true);
            Check(Get(figure, "walkPhase") == phaseBefore, "Fly mode advances gait");
            Animate(figure, .2f, 0);
            Check(Get(figure, "walkPhase") == phaseBefore && Get(figure, "walking") < .01f, "Stopped gait does not settle");
            Animate(figure, .2f, -3);
            Check((int)typeof(MinerPlayerVisual).GetField("facing", Private).GetValue(figure) == -1, "Left facing missing");
            return "PASS: 42000 leg poses, fixed limb lengths, sole clearance, smooth contacts, no stance sliding, idle/jump/fly/facing; 90 frames rendered at 1.3/3/7 units/s: " + folder;
        }
        finally
        {
            RenderTexture.active = previous; ground?.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
