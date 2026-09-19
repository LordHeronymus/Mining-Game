using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

public static class SurfaceWildlifeChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Tick(object target, float dt) => target.GetType().GetMethod("Tick", Private).Invoke(target, new object[] { dt });
    static object Animal(SurfaceCritters owner) => ((IList)typeof(SurfaceCritters).GetField("animals", Private).GetValue(owner))[0];
    static T Get<T>(object animal, string field) => (T)animal.GetType().GetField(field).GetValue(animal);
    static void Set(object animal, string field, object value) => animal.GetType().GetField(field).SetValue(animal, value);

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Run fixture outside Play mode.");
        var source = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var oldCamera = Camera.main; bool enabledCamera = oldCamera && oldCamera.enabled;
        var root = new GameObject("Wildlife test fixture", typeof(Grid)); root.SetActive(false);
        var previous = RenderTexture.active;
        Texture2D capture = null; RenderTexture target = null;
        CritterMesh groundMesh = null;
        try
        {
            root.GetComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0);
            var terrain = Child(root, "Terrain"); terrain.AddComponent<Tilemap>();
            var map = terrain.AddComponent<MapGenerator>(); map.enabled = false; map.registry = source.registry;
            var stone = source.registry.GetById(BlockType.Stone).variants[0];
            for (int x = -60; x < 60; x++) map.Terrain.SetTile(new Vector3Int(x, 0, 0), stone);
            var skyObject = Child(root, "Sky");
            var sky = skyObject.AddComponent<SkyController>(); sky.automaticCycle = false;
            var camera = Child(root, "Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.orthographic = true; camera.orthographicSize = 3; camera.aspect = 2;
            camera.transform.position = new Vector3(0, 1, -10); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.20f, .30f, .20f);
            var player = Child(root, "Player").transform; player.position = new Vector3(0, .5f, 0);
            var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Background/BirdSilhouette.mat");
            var frogs = Child(root, "Frogs").AddComponent<SurfaceCritters>();
            frogs.map = map; frogs.sky = sky; frogs.player = player; frogs.material = bodyMaterial; frogs.maxCount = 1;
            var snails = Child(root, "Snails").AddComponent<SurfaceCritters>();
            snails.map = map; snails.sky = sky; snails.player = player; snails.material = bodyMaterial; snails.maxCount = 1;
            snails.species = SurfaceCritters.Species.Snail; snails.size = .7f; snails.bodyColor = new Color(.62f, .40f, .16f);
            var flies = Child(root, "Fireflies").AddComponent<SurfaceFireflies>();
            flies.map = map; flies.sky = sky; flies.count = 16; flies.cameraMargin = 10;
            flies.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Background/FireflyGlow.mat");
            if (oldCamera) oldCamera.enabled = false;
            root.SetActive(true); map.RestorePreviewMetadata(1, 120, 1);
            // Plain runtime behaviours do not receive the Play lifecycle in an Edit-mode fixture.
            foreach (var behaviour in new MonoBehaviour[] { frogs, snails, flies })
            {
                behaviour.GetType().GetMethod("OnDisable", Private).Invoke(behaviour, null);
                behaviour.GetType().GetMethod("OnEnable", Private).Invoke(behaviour, null);
            }
            Tick(frogs, 0); Tick(snails, 0); Tick(flies, 3);
            Check(frogs.ActiveCount == 1 && snails.ActiveCount == 1, "Initial surface animals missing");
            Check(flies.ActiveCount == 0 && flies.Visibility == 0, "Fireflies visible during day");
            CheckVisibleBudget(frogs, snails, map);
            bool hopped = false, rested = false, crawled = false;
            float lastSnail = Get<float>(Animal(snails), "x");
            for (int i = 0; i < 1200; i++)
            {
                Tick(frogs, .05f); Tick(snails, .05f);
                var frog = Animal(frogs); hopped |= Get<bool>(frog, "jumping");
                rested |= !Get<bool>(frog, "jumping") && Get<float>(frog, "wait") > 3;
                float snail = Get<float>(Animal(snails), "x"); crawled |= Mathf.Abs(snail - lastSnail) > .001f; lastSnail = snail;
                Check(Mathf.Abs(Get<float>(frog, "x") - Get<float>(frog, "home")) <= frogs.roamRadius + .001f, "Frog left roam radius");
            }
            Check(hopped && rested && crawled, "Hop/rest/crawl behavior missing");
            // A single missing surface cell must stop both kinds, even with intact stone below.
            map.Terrain.SetTile(new Vector3Int(2, 0, 0), null); map.Terrain.SetTile(new Vector3Int(2, -1, 0), stone);
            foreach (var group in new[] { frogs, snails })
            {
                var animal = Animal(group); Set(animal, "x", .5f); Set(animal, "home", .5f);
                Set(animal, "direction", 1); Set(animal, "wait", 0f); Set(animal, "travel", 10f);
                Set(animal, "hops", 3); Set(animal, "jumping", false);
                bool turned = false;
                for (int i = 0; i < 160; i++)
                {
                    Tick(group, .05f); animal = Animal(group);
                    Check(Get<float>(animal, "x") < 1f, "Animal crossed a surface gap");
                    turned |= Get<int>(animal, "direction") < 0;
                }
                Check(turned, "Animal did not turn at edge");
            }
            map.Terrain.SetTile(new Vector3Int(2, 0, 0), stone);
            var jumping = Animal(frogs);
            Set(jumping, "x", .5f); Set(jumping, "home", .5f); Set(jumping, "direction", 1);
            Set(jumping, "wait", 0f); Set(jumping, "hops", 3); Set(jumping, "jumping", false);
            Tick(frogs, .01f); Tick(frogs, .1f);
            Check(Get<bool>(jumping, "jumping"), "No test jump");
            map.Terrain.SetTile(new Vector3Int(2, 0, 0), null);
            Tick(frogs, .02f);
            Check(Get<bool>(jumping, "returning"), "Mining during jump did not reverse the frog");
            for (int i = 0; i < 10; i++) Tick(frogs, .05f);
            Check(Mathf.Abs(Get<float>(jumping, "x") - .5f) < .01f, "Frog did not return to safe ground");
            map.Terrain.SetTile(new Vector3Int(2, 0, 0), stone);
            sky.SetNight(true); sky.AdvanceFade(4);
            for (int i = 0; i < 80; i++) Tick(flies, .05f);
            Check(flies.ActiveCount == 16 && flies.Visibility == 1, "Night did not spawn fireflies");
            Check(flies.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount == 64, "Fireflies are not batched quads");
            CheckFireflyCamera(flies, camera, player);
            sky.SetNight(false); sky.AdvanceFade(4); Tick(flies, .5f);
            Check(flies.Visibility < 1, "Dawn did not start fading");
            Tick(flies, 3); Check(flies.ActiveCount == 0 && flies.Visibility == 0, "Fireflies persist into day");
            sky.SetNight(true); sky.AdvanceFade(4); Tick(flies, 3); Tick(flies, 3);
            player.position = new Vector3(0, -40, 0); camera.transform.position = new Vector3(0, -40, -10);
            Tick(frogs, 13); Tick(snails, 13); Tick(flies, 3);
            Check(frogs.ActiveCount == 0 && snails.ActiveCount == 0 && flies.ActiveCount == 0, "Underground population was not released");
            player.position = new Vector3(0, .5f, 0); camera.transform.position = new Vector3(0, 1, -10);
            map.RestorePreviewMetadata(2, 120, 1);
            Tick(frogs, 0); Tick(snails, 0); Tick(flies, 0);
            Check(frogs.ActiveCount == 1 && snails.ActiveCount == 1 && flies.ActiveCount == 16, "Regeneration did not recover population");

            sky.SetNight(false); sky.AdvanceFade(4); Tick(flies, 3);
            foreach (var group in new[] { frogs, snails })
            {
                var animal = Animal(group); float x = group == frogs ? -.8f : .8f;
                Set(animal, "x", x); Set(animal, "home", x); Set(animal, "wait", 10f);
                Set(animal, "jumping", false); Set(animal, "direction", 1); Tick(group, 1);
            }
            groundMesh = new CritterMesh(root.transform, "Preview ground", bodyMaterial, 0);
            groundMesh.Begin(new Vector2(0, .48f), 1, 1, Color.white);
            groundMesh.Ellipse(0, -.12f, 8, .14f, new Color(.20f, .24f, .11f)); groundMesh.Upload();
            groundMesh.renderer.gameObject.layer = 30;
            camera.transform.position = new Vector3(0, .87f, -10); camera.orthographicSize = .9f;
            target = new RenderTexture(1200, 600, 24); camera.targetTexture = target;
            Render(camera, target, ref capture, "SurfaceWildlife-Day.png");
            sky.SetNight(true); sky.AdvanceFade(4); Tick(frogs, .1f); Tick(snails, .1f);
            for (int i = 0; i < 90; i++) Tick(flies, .05f);
            camera.backgroundColor = new Color(.025f, .05f, .085f);
            camera.transform.position = new Vector3(0, 1.6f, -10); camera.orthographicSize = 2;
            Render(camera, target, ref capture, "SurfaceWildlife-Night.png");
            map.RestorePreviewMetadata(3, 120, 1);
            Check(frogs.ActiveCount == 0 && snails.ActiveCount == 0 && flies.ActiveCount == 0, "Old map population retained");
            frogs.enabled = false; snails.enabled = false; flies.enabled = false;
            foreach (var behaviour in new MonoBehaviour[] { frogs, snails, flies })
                behaviour.GetType().GetMethod("OnDisable", Private).Invoke(behaviour, null);
            Check(frogs.GetComponentsInChildren<MeshRenderer>().Length == 0 && flies.GetComponentsInChildren<MeshRenderer>().Length == 0,
                "Disabling left generated meshes behind");
            return "PASS: shared two-animal view budget and entry reservations; hopping/resting/crawling, gap/step safety, mid-jump mining, day/night lifecycle, underground despawn, regeneration, cleanup. Day/night previews rendered.";
        }
        finally
        {
            RenderTexture.active = previous; groundMesh?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            if (target) UnityEngine.Object.DestroyImmediate(target);
            if (capture) UnityEngine.Object.DestroyImmediate(capture);
            if (oldCamera) oldCamera.enabled = enabledCamera;
        }
    }
    static GameObject Child(GameObject root, string name)
    { var go = new GameObject(name); go.layer = 30; go.transform.SetParent(root.transform, false); return go; }
    static void CheckVisibleBudget(SurfaceCritters frogs, SurfaceCritters snails, MapGenerator map)
    {
        var timer = typeof(SurfaceCritters).GetField("nextSpawn", Private);
        frogs.maxCount = snails.maxCount = 3;
        Set(Animal(frogs), "x", 0f); Set(Animal(snails), "x", 2f);
        timer.SetValue(frogs, 0f); timer.SetValue(snails, 0f);
        Tick(frogs, 0); Tick(snails, 0);
        Check(frogs.ActiveCount + snails.ActiveCount == 2, "Separate species bypass shared visibility limit");
        Set(Animal(frogs), "x", 6.2f); // Body still overlaps the right edge.
        timer.SetValue(frogs, 0f); Tick(frogs, 0);
        Check(frogs.ActiveCount == 1, "Partially visible animal did not occupy a slot");
        Set(Animal(frogs), "x", 8f);
        timer.SetValue(frogs, 0f); Tick(frogs, 0);
        Check(frogs.ActiveCount == 2, "Free screen slot did not permit a replacement");
        timer.SetValue(snails, 0f); Tick(snails, 0);
        Check(snails.ActiveCount == 1, "Offscreen entry was not reserved across species");
        // Regeneration starts both species together and must not refill each species to its own cap.
        map.RestorePreviewMetadata(10, 120, 1); Tick(frogs, 0); Tick(snails, 0);
        Check(frogs.ActiveCount == 1 && snails.ActiveCount == 1, "Initial population exceeds common screen budget");
        frogs.maxCount = snails.maxCount = 1;
    }
    static void Render(Camera camera, RenderTexture target, ref Texture2D capture, string filename)
    {
        camera.Render(); RenderTexture.active = target;
        if (!capture) capture = new Texture2D(1200, 600, TextureFormat.RGB24, false);
        capture.ReadPixels(new Rect(0, 0, 1200, 600), 0, 0); capture.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), filename), capture.EncodeToPNG());
    }

    static void CheckFireflyCamera(SurfaceFireflies flies, Camera camera, Transform player)
    {
        var area = typeof(SurfaceFireflies).GetMethod("TryGetSpawnArea", Private);
        var population = typeof(SurfaceFireflies).GetField("flies", Private);
        object[] args = { camera, .5f, 0f, 0f };
        Check((bool)area.Invoke(flies, args), "Surface camera area missing");
        Check(Mathf.Abs((float)args[2] + 16) < .01f && Mathf.Abs((float)args[3] - 16) < .01f,
            "Spawn area does not include ten units beyond both camera borders");
        player.position = new Vector3(-100, -40, 0);
        Tick(flies, 3);
        Check(flies.ActiveCount == 16 && flies.Visibility == 1, "Fireflies still depend on the player's position");
        camera.orthographicSize = 6;
        Check((bool)area.Invoke(flies, args) && Mathf.Abs((float)args[2] + 22) < .01f && Mathf.Abs((float)args[3] - 22) < .01f,
            "Camera zoom does not expand spawn area");
        camera.orthographicSize = 3; camera.transform.position = new Vector3(20, 1, -10);
        Tick(flies, 3); Tick(flies, 3);
        foreach (var fly in (IList)population.GetValue(flies))
        {
            float x = Get<float>(fly, "anchorX");
            Check(x >= 3 && x <= 30, "Camera pan did not replace distant fireflies inside map bounds");
        }
        camera.transform.position = new Vector3(0, 1, -10); player.position = new Vector3(0, .5f, 0);
        Tick(flies, 3); Tick(flies, 3);
    }
}
