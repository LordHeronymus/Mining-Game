using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;

public static class MiningCrackChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static string Main()
    {
        var root = new GameObject("Mining crack checks", typeof(Grid));
        root.transform.position = new Vector3(10000, 10000, 0);
        root.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
        var target = new RenderTexture(1200, 440, 24);
        var oldTarget = RenderTexture.active;
        var pixels = new Texture2D(1200, 440, TextureFormat.RGBA32, false);
        var tile = ScriptableObject.CreateInstance<Tile>();
        MiningCrackVisual[] visuals = new MiningCrackVisual[5];
        TileMiner miner = null;
        Sprite stoneSprite = null;
        Material stoneMaterial = null;
        try
        {
            GameObject Child(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.layer = 31; return go;
            }
            var terrain = Child("Terrain").AddComponent<Tilemap>();
            var tr = terrain.gameObject.AddComponent<TilemapRenderer>();
            stoneMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
            tr.sharedMaterial = stoneMaterial;
            Check(!UnityEditor.ShaderUtil.ShaderHasError(Resources.Load<Material>("Mining/MiningCracksLit").shader), "Growth shader failed compilation");
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Map/StoneTest/WarmStone.png");
            stoneSprite = Sprite.Create(source, new Rect(0, 0, source.width, source.height), Vector2.one * .5f, source.width / 1.1f);
            tile.sprite = stoneSprite; tile.colliderType = Tile.ColliderType.None;
            for (int i = 0; i < 5; i++) terrain.SetTile(new Vector3Int(i * 2, 0, 0), tile);
            var light = Child("Light").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global; light.intensity = 1;
            float[] amounts = { .1f, .3f, .5f, .7f, .9f };
            for (int i = 0; i < 5; i++)
            {
                visuals[i] = new MiningCrackVisual(terrain);
                visuals[i].Show(new Vector3Int(i * 2, 0, 0), amounts[i]);
                var r = visuals[i].Renderer;
                Check(r && r.enabled && r.sprite.name.EndsWith((i + 1).ToString()), "Wrong damage stage");
                Check(r.color.a == 1f, "Black crack cores must stay opaque");
                Check(r.sprite.rect.x == 512 && r.sprite.rect.y == 0, "Stages must share the same geometry");
                var properties = new MaterialPropertyBlock(); r.GetPropertyBlock(properties);
                Check(Mathf.Abs(properties.GetFloat("_CrackGrowth") - amounts[i]) < .001f, "Growth does not track damage");
                Check(Mathf.Abs(r.bounds.size.x - 1.1f * .86f) < .001f, "Cracks not scaled to grid");
                Check(r.GetComponent<Collider2D>() == null, "Cracks add a collider");
                Check(r.sortingOrder > tr.sortingOrder + 1, "Cracks hidden under ore");
            }
            var camera = Child("Camera").AddComponent<Camera>();
            camera.transform.localPosition = new Vector3(4.95f, .55f, -10);
            camera.orthographic = true; camera.orthographicSize = 2.1f;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.14f, .16f, .18f); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1200, 440), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Design/MiningCracks");
            File.WriteAllBytes("Design/MiningCracks/MiningCracks-implemented.png", pixels.EncodeToPNG());
            // Compare rendered alpha at identical coordinates: no existing branch may disappear.
            tr.enabled = false;
            for (int i = 1; i < 5; i++) visuals[i].Hide(new Vector3Int(i * 2, 0, 0));
            camera.transform.localPosition = new Vector3(.55f, .55f, -10);
            camera.orthographicSize = .65f; camera.backgroundColor = Color.clear;
            Color32[] previous = null;
            long previousCoverage = 0;
            foreach (float amount in amounts)
            {
                visuals[0].Show(Vector3Int.zero, amount);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,1200,440),0,0); pixels.Apply();
                var current = pixels.GetPixels32(); long coverage = 0;
                for (int p = 0; p < current.Length; p++)
                {
                    coverage += current[p].a;
                    if (previous != null) Check(current[p].a + 2 >= previous[p].a, "Existing crack pixel disappeared");
                }
                Check(coverage > previousCoverage, "Crack stage failed to grow");
                previous = current; previousCoverage = coverage;
            }
            visuals[0].Show(new Vector3Int(2, 0, 0), .5f);
            Check(Vector3.Distance(visuals[0].Renderer.transform.position, terrain.GetCellCenterWorld(new Vector3Int(2,0,0))) < .001f, "Target switch failed");
            visuals[0].Show(Vector3Int.zero, 0); Check(!visuals[0].Renderer.enabled, "Zero progress visible");
            visuals[0].Show(Vector3Int.zero, 1); Check(!visuals[0].Renderer.enabled, "Finished progress visible");
            visuals[0].Show(Vector3Int.one, .5f); Check(!visuals[0].Renderer.enabled, "Empty cell visible");
            var goMiner = Child("Miner"); goMiner.SetActive(false);
            goMiner.AddComponent<BoxCollider2D>();
            var stats = goMiner.AddComponent<StatsManager>();
            miner = goMiner.AddComponent<TileMiner>();
            typeof(TileMiner).GetField("tilemap", Private).SetValue(miner, terrain);
            typeof(TileMiner).GetField("stats", Private).SetValue(miner, stats);
            typeof(TileMiner).GetMethod("ShowMiningCracks", Private).Invoke(miner, new object[] { Vector3Int.zero, .5f });
            var owned = (MiningCrackVisual)typeof(TileMiner).GetField("miningCracks", Private).GetValue(miner);
            var progress = (System.Collections.Generic.Dictionary<Vector3Int, float>)typeof(TileMiner).GetField("progress", Private).GetValue(miner);
            progress[Vector3Int.zero] = .5f;
            Check(owned.Renderer.enabled, "Miner did not show cracks");
            var audio = AudioManager.Instance;
            AudioManager.Instance = null;
            try
            {
                Check(miner.CompleteMining(Vector3Int.zero), "CompleteMining failed");
                Check(!owned.Renderer.enabled && !terrain.HasTile(Vector3Int.zero), "Completion left cracks or terrain");
            }
            finally { AudioManager.Instance = audio; }
            typeof(TileMiner).GetMethod("ShowMiningCracks", Private).Invoke(miner, new object[] { new Vector3Int(2,0,0), .5f });
            typeof(TileMiner).GetMethod("OnDisable", Private).Invoke(miner, null);
            Check(owned.Renderer.enabled, "Disable cleared stored damage");
            typeof(TileMiner).GetMethod("ShowMiningCracks", Private).Invoke(miner, new object[] { new Vector3Int(2,0,0), .5f });
            progress[new Vector3Int(2,0,0)] = .5f;
            var persistedRenderer = owned.Renderer;
            typeof(TileMiner).GetMethod("Update", Private).Invoke(miner, null);
            Check(persistedRenderer.enabled, "Stored damage did not remain visible");
            var strikeCell = new Vector3Int(8, 0, 0);
            var targetTime = (float)typeof(TileMiner).GetMethod("GetTargetMineTime", Private).Invoke(miner, new object[] { strikeCell });
            float strikeInterval = Mathf.Min(.01f, targetTime * .1f);
            var previousHit = TileMiner.OnBlockHit;
            var previousAudio = AudioManager.Instance;
            TileMiner.OnBlockHit = null; AudioManager.Instance = null;
            try
            {
                typeof(TileMiner).GetMethod("ApplyMiningHit", Private).Invoke(miner, new object[] { strikeCell, strikeInterval });
                float first = progress[strikeCell];
                Check(Mathf.Abs(first - strikeInterval / targetTime) < .0001f, "First hit damage is wrong");
                typeof(TileMiner).GetMethod("Update", Private).Invoke(miner, null);
                Check(Mathf.Abs(progress[strikeCell] - first) < .0001f, "Progress changed between hits");
                typeof(TileMiner).GetMethod("ApplyMiningHit", Private).Invoke(miner, new object[] { strikeCell, strikeInterval });
                Check(Mathf.Abs(progress[strikeCell] - first * 2f) < .0001f, "Second hit damage is wrong");
            }
            finally { TileMiner.OnBlockHit = previousHit; AudioManager.Instance = previousAudio; }
            return "PASS: five growing crack stages, persistent damage, hit-only progress, no frame damage, completion and rendering. Play mode=" + Application.isPlaying;
        }
        finally
        {
            if (miner) ((MiningCrackVisual)typeof(TileMiner).GetField("miningCracks", Private).GetValue(miner))?.Dispose();
            foreach (var v in visuals) v?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(tile);
            if (stoneSprite) UnityEngine.Object.DestroyImmediate(stoneSprite);
            if (stoneMaterial) UnityEngine.Object.DestroyImmediate(stoneMaterial);
            RenderTexture.active = oldTarget;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
