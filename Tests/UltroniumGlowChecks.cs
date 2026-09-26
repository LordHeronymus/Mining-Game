#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class UltroniumGlowChecks
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object Main()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        const string shaderPath = "Assets/GameObjects/Map/OreOverlayLit.shader";
        AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        Check(shader && !ShaderUtil.ShaderHasError(shader), "Ultronium glow shader failed to compile.");
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(
            "Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
        var ultronium = registry.GetById(BlockType.UltroniumOre);
        var gold = registry.GetById(BlockType.GoldOre);
        Check(ultronium && gold, "Required ores are missing.");

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var existingLights = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var lightStates = new bool[existingLights.Length];
        var target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32);
        var oldTarget = RenderTexture.active;
        Texture2D pixels = null;
        Material material = null;
        try
        {
            for (int i = 0; i < existingLights.Length; i++)
            {
                lightStates[i] = existingLights[i].enabled;
                existingLights[i].enabled = false;
            }
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Ultronium Glow Grid", typeof(Grid));
            grid.GetComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0);
            var root = new GameObject("Ultronium Glow Map", typeof(Tilemap),
                typeof(TilemapRenderer), typeof(MapGenerator));
            root.transform.SetParent(grid.transform, false);
            root.layer = 31;
            var map = root.GetComponent<MapGenerator>();
            map.enabled = false;
            map.registry = registry;
            map.EnsureOreOverlay();
            map.Terrain.GetComponent<TilemapRenderer>().enabled = false;
            var overlay = map.OreOverlay;
            overlay.gameObject.layer = 31;
            overlay.SetTile(Vector3Int.zero, gold.richOre[0]);
            overlay.SetTile(Vector3Int.right, ultronium.richOre[0]);
            material = new Material(shader);
            overlay.GetComponent<TilemapRenderer>().sharedMaterial = material;

            var darkness = new GameObject("Zero Intensity Light", typeof(Light2D)).GetComponent<Light2D>();
            darkness.gameObject.layer = 31;
            darkness.lightType = Light2D.LightType.Global;
            darkness.intensity = 0f;

            var camera = new GameObject("Ultronium Glow Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(.5f, .25f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = .32f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            camera.allowHDR = false;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(512, 256, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 512, 256), 0, 0);
            pixels.Apply();
            float goldEnergy = 0f, ultroniumEnergy = 0f;
            var colors = pixels.GetPixels();
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 512; x++)
                {
                    var color = colors[y * 512 + x];
                    float energy = color.r + color.g + color.b;
                    if (x < 256) goldEnergy += energy; else ultroniumEnergy += energy;
                }
            Check(goldEnergy < 1f, "Ordinary ore became self-lit.");
            Check(ultroniumEnergy > 100f, "Ultronium has no visible emission in darkness.");

            var sparkle = root.AddComponent<OreSparkles>();
            sparkle.sparkleMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/GameObjects/Map/OreSparkle.mat");
            typeof(OreSparkles).GetMethod("OnEnable", Private).Invoke(sparkle, null);
            typeof(OreSparkles).GetMethod("EmitCell", Private).Invoke(sparkle,
                new object[] { Vector3Int.right, ultronium });
            var system = (ParticleSystem)typeof(OreSparkles).GetField("particles", Private).GetValue(sparkle);
            var particleBuffer = new ParticleSystem.Particle[4];
            int count = system.GetParticles(particleBuffer);
            Check(count == 1 && particleBuffer[0].velocity.y > 0f,
                "Ultronium did not emit a rising particle.");
            Check(particleBuffer[0].startSize < sparkle.size * .5f,
                "Ultronium particle is not smaller than a normal ore sparkle.");
            return new { passed = true, spriteGeometryUnchanged = true, shaderCompiled = true,
                goldDarkEnergy = goldEnergy, ultroniumDarkEnergy = ultroniumEnergy,
                risingParticle = true };
        }
        finally
        {
            RenderTexture.active = oldTarget;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            for (int i = 0; i < existingLights.Length; i++)
                if (existingLights[i]) existingLights[i].enabled = lightStates[i];
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
            if (material) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
#endif
