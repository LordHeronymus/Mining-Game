using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SurfaceTreeSetup
{
    const string Folder = "Assets/GameObjects/Map/Trees";
    static readonly string[] Names = {
        "Baum_01_Laubbaum_HighRes", "Baum_02_Tanne_HighRes",
        "Baum_03_Birke_HighRes", "Baum_04_Kronenbaum_HighRes"
    };
    static readonly float[] GroundPivots = { .068f, .044f, .082f, .088f };

    [MenuItem("Tools/Map/Install Surface Trees")]
    public static string Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Use Edit Mode.");
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var shop = Object.FindFirstObjectByType<ShopBuilding>();
        if (!map || !shop) throw new System.InvalidOperationException("Map and shop required.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameObjects/Map", "Trees");
        if (!AssetDatabase.IsValidFolder(Folder + "/Sprites")) AssetDatabase.CreateFolder(Folder, "Sprites");
        var sprites = new Sprite[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            string path = Folder + "/Sprites/" + Names[i] + ".png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(path))
            {
                string error = AssetDatabase.MoveAsset("Assets/ZZZ New Assets/" + Names[i] + ".png", path);
                if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException(error);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(.5f, GroundPivots[i]);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        string prefabPath = Folder + "/Tree.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<ChoppableTree>(prefabPath);
        if (!prefab)
        {
            var root = new GameObject("Tree", typeof(SpriteRenderer), typeof(ChoppableTree));
            var visual = root.GetComponent<SpriteRenderer>();
            visual.sharedMaterial = shop.transform.Find("Sprite").GetComponent<SpriteRenderer>().sharedMaterial;
            visual.sortingLayerName = "Default";
            visual.sortingOrder = -1;
            var hitbox = new GameObject("Trunk hitbox", typeof(BoxCollider2D));
            hitbox.transform.SetParent(root.transform, false);
            hitbox.GetComponent<BoxCollider2D>().isTrigger = true;
            var data = new SerializedObject(root.GetComponent<ChoppableTree>());
            data.FindProperty("visual").objectReferenceValue = visual;
            data.FindProperty("trunk").objectReferenceValue = hitbox.GetComponent<BoxCollider2D>();
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            prefab = AssetDatabase.LoadAssetAtPath<ChoppableTree>(prefabPath);
        }
        ConfigureParticles(prefabPath, SplinterMaterial(), LeafMaterial(), SwayMaterial());

        var existing = shop.transform.parent.Find("Surface Trees");
        var owner = existing ? existing.GetComponent<SurfaceTrees>() : null;
        if (!owner)
        {
            var go = existing ? existing.gameObject : new GameObject("Surface Trees");
            if (!existing) { go.transform.SetParent(shop.transform.parent, false); Undo.RegisterCreatedObjectUndo(go, "Create surface trees"); }
            owner = Undo.AddComponent<SurfaceTrees>(go);
        }
        var config = new SerializedObject(owner);
        config.FindProperty("map").objectReferenceValue = map;
        config.FindProperty("prefab").objectReferenceValue = prefab;
        config.FindProperty("wood").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Materials/Wood.asset");
        var variants = config.FindProperty("variants");
        variants.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) variants.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        var migrated = config.FindProperty("placementMigrationDone");
        if (!migrated.boolValue)
        {
            config.FindProperty("treeHeight").floatValue *= 1.5f;
            config.FindProperty("minimumTreeSpacing").floatValue = map.Terrain.layoutGrid.cellSize.x * 4f;
            migrated.boolValue = true;
        }
        var growthMigrated = config.FindProperty("growthSettingsMigrationDone");
        if (!growthMigrated.boolValue)
        {
            int oldMaximum = config.FindProperty("fullyGrownWoodYieldMax").intValue;
            int baseMaximum = config.FindProperty("woodYieldMax").intValue;
            float oldSeconds = config.FindProperty("timeToFullGrowthSeconds").floatValue;
            config.FindProperty("maximumBonusWood").intValue = Mathf.Max(0, oldMaximum - baseMaximum);
            config.FindProperty("maximumSizeBonusPercent").floatValue = 50f;
            config.FindProperty("growthSpeedPercentPerMinute").floatValue = 3000f / Mathf.Max(1f, oldSeconds);
            growthMigrated.boolValue = true;
        }
        config.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
        EditorSceneManager.SaveScene(owner.gameObject.scene);
        return "Randomly distributed surface trees and falling leaf particles installed.";
    }

    static Material SplinterMaterial()
    {
        string texturePath = Folder + "/WoodSplinter.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath))
        {
            const int width = 32, height = 12;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                float taper = Mathf.Clamp01(Mathf.Min(x, width - 1 - x) / 6f);
                if (Mathf.Abs(y - 5.5f) > 2.5f * taper) continue;
                pixels[y * width + x] = y == 5 || y == 6
                    ? new Color32(255, 242, 211, 255)
                    : new Color32(231, 196, 143, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        string path = Folder + "/WoodSplinter.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Material LeafMaterial()
    {
        string texturePath = Folder + "/FallingLeaf.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath))
        {
            const int width = 28, height = 40;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float t = (y + .5f) / height;
                float halfWidth = 12f * Mathf.Sin(Mathf.PI * t) * (.8f + .25f * t);
                float mid = 13.5f + 3f * Mathf.Sin(Mathf.PI * t);
                if (Mathf.Abs(x - mid) > halfWidth) continue;
                pixels[y * width + x] = Mathf.Abs(x - mid) < 1.15f
                    ? new Color32(240, 222, 135, 255)
                    : new Color32(235, 245, 207, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        string path = Folder + "/FallingLeaf.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Material SwayMaterial()
    {
        string path = Folder + "/TreeSwayLit.mat";
        var shader = Shader.Find("Mining Game/Tree Sway Lit");
        if (!shader) throw new System.InvalidOperationException("Tree sway shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
        {
            if (material.shader != shader) material.shader = shader;
            return material;
        }
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void ConfigureParticles(string path, Material splinterMaterial, Material leafMaterial,
        Material swayMaterial)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.GetComponent<SpriteRenderer>().sharedMaterial = swayMaterial;
            var child = root.transform.Find("Wood Splinters");
            if (!child)
            {
                child = new GameObject("Wood Splinters", typeof(ParticleSystem)).transform;
                child.SetParent(root.transform, false);
            }
            var particles = child.GetComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = .75f;
            main.startSpeed = 0;
            main.startSize = .18f;
            main.gravityModifier = 1.1f;
            main.maxParticles = 128;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = splinterMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 2;
            var leafChild = root.transform.Find("Falling Leaves");
            if (!leafChild)
            {
                leafChild = new GameObject("Falling Leaves", typeof(ParticleSystem)).transform;
                leafChild.SetParent(root.transform, false);
            }
            var leaves = leafChild.GetComponent<ParticleSystem>();
            var leafMain = leaves.main;
            leafMain.loop = false;
            leafMain.playOnAwake = false;
            leafMain.duration = 1f;
            leafMain.startLifetime = 2f;
            leafMain.startSpeed = 0;
            leafMain.startSize = .3f;
            leafMain.gravityModifier = .35f;
            leafMain.maxParticles = 256;
            leafMain.simulationSpace = ParticleSystemSimulationSpace.World;
            leafMain.scalingMode = ParticleSystemScalingMode.Shape;
            var leafEmission = leaves.emission;
            leafEmission.enabled = false;
            var leafColor = leaves.colorOverLifetime;
            leafColor.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, .55f),
                    new GradientAlphaKey(.6f, .8f),
                    new GradientAlphaKey(0f, 1f)
                });
            leafColor.color = new ParticleSystem.MinMaxGradient(fade);
            var leafShape = leaves.shape;
            leafShape.enabled = false;
            var noise = leaves.noise;
            noise.enabled = true;
            noise.strength = .75f;
            noise.frequency = .55f;
            noise.scrollSpeed = .35f;
            var rotation = leaves.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            var leafRenderer = leafChild.GetComponent<ParticleSystemRenderer>();
            leafRenderer.sharedMaterial = leafMaterial;
            leafRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            leafRenderer.sortingLayerName = "Default";
            leafRenderer.sortingOrder = 3;
            var data = new SerializedObject(root.GetComponent<ChoppableTree>());
            data.FindProperty("splinters").objectReferenceValue = particles;
            data.FindProperty("leaves").objectReferenceValue = leaves;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
