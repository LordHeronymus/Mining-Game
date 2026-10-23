using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Grass that converts into a healing herb at runtime must look like a spawned or loaded herb:
// herb size multiplier instead of the grass size, and a reach glow that the next TileMiner frame restores.
public static class BughuntGrassConversionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Get(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); checks.Add(label); }
    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f;

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var root = new GameObject("Grass conversion probe", typeof(Grid)) { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var texture = new Texture2D(8, 16) { hideFlags = HideFlags.HideAndDontSave };
        var grassSprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), new Vector2(.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
        var herbSprite = Sprite.Create(texture, new Rect(0, 0, 8, 12), new Vector2(.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
        grassSprite.hideFlags = herbSprite.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var terrainObject = new GameObject("Terrain") { hideFlags = HideFlags.HideAndDontSave };
            terrainObject.transform.SetParent(root.transform, false);
            terrainObject.AddComponent<Tilemap>();
            var map = terrainObject.AddComponent<MapGenerator>();
            // Keep the map and its ExecuteAlways appearance helpers inert on the probe terrain.
            map.enabled = false;
            terrainObject.GetComponent<OreOverlayAppearance>().enabled = false;
            terrainObject.GetComponent<DirtSurfaceAppearance>().enabled = false;
            var grass = terrainObject.AddComponent<SurfaceTallGrass>();
            root.SetActive(true);

            Set(grass, "map", map);
            Set(grass, "variants", new[] { grassSprite });
            Set(grass, "medicinalHerbVariants", new[] { herbSprite });
            Set(grass, "healingHerbRarityPercent", 30f);
            Set(grass, "healingHerbConversionFactor", .5f);
            Set(grass, "healingHerbConversionLimitPercent", 100f);
            Set(grass, "healingHerbSpawnAreaSize", 0f);
            Set(grass, "grassSizeMultiplier", .75f);
            Set(grass, "healingHerbSizeMultiplier", 1.2f);
            Set(grass, "respawnRandom", new System.Random(7));
            ((HashSet<int>)Get(grass, "healingHerbSites")).Clear();

            const int cell = 5;
            Call(grass, "Spawn", cell, 0f);
            var patches = (Dictionary<int, TallGrassPatch>)Get(grass, "patches");
            var patch = patches[cell];
            var renderer = patch.GetComponent<SpriteRenderer>();
            Check(!patch.IsHealingHerb && renderer.sprite == grassSprite, "Fixture spawns plain grass");
            Vector3 grassScale = patch.transform.localScale;
            float grassBottom = renderer.bounds.min.y;
            patch.SetReachGlow(1);
            Check(patch.ReachGlowLevel == 1, "Grass in reach glows before conversion");

            Call(grass, "ConvertOneGrass");
            Check(patches[cell] == patch && patch.IsHealingHerb && renderer.sprite == herbSprite, "Conversion turns the patch into a healing herb");
            Vector3 convertedScale = patch.transform.localScale;
            Check(Mathf.Abs(renderer.bounds.min.y - grassBottom) < 1e-4f, "Converted herb keeps its bottom on the ground");

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Check(patch.ReachGlowLevel == 0 && block.GetFloat("_ReachGlow") == 0f, "Conversion resets glow level and property block together");
            patch.SetReachGlow(1);
            renderer.GetPropertyBlock(block);
            Check(patch.ReachGlowLevel == 1 && block.GetFloat("_ReachGlow") == 1f, "Next reach update restores the glow on the converted herb");

            // Reference: the same cell spawned (or loaded) directly as a healing herb.
            Call(grass, "Clear");
            Call(grass, "Spawn", cell, 0f);
            var spawnedHerb = patches[cell];
            Check(spawnedHerb.IsHealingHerb, "Reference spawn is a healing herb");
            Vector3 herbScale = spawnedHerb.transform.localScale;
            Check(!Near(herbScale, grassScale), "Fixture size multipliers differ for grass and herb");
            Check(Near(convertedScale, herbScale), "Converted herb has the spawned herb size (" + convertedScale + " vs " + herbScale + ")");

            File.WriteAllText("Temp/BughuntGrassConversionChecks.txt", string.Join("\n", checks.ConvertAll(label => "PASS " + label)) + "\n");
            return "PASS " + checks.Count + " grass conversion checks";
        }
        catch (Exception exception)
        {
            var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
            File.WriteAllText("Temp/BughuntGrassConversionChecks.txt", string.Join("\n", checks.ConvertAll(label => "PASS " + label)) +
                "\n" + (inner.Message.StartsWith("FAIL ") ? inner.Message : "FAIL " + inner) + "\n");
            throw;
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(grassSprite);
            Object.DestroyImmediate(herbSprite);
            Object.DestroyImmediate(texture);
        }
    }
}
