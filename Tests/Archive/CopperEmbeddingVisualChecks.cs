using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Standalone Pipeline run_script fixture. No saved scene, material or user data is changed.
public static class CopperEmbeddingVisualChecks
{
    const string Output = "Design/OreVeins/CopperEmbedding/Implemented";
    const int Width = 1120, Height = 960, CellPixels = 80;
    static readonly List<string> passed = new();
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); passed.Add(message); }

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        passed.Clear(); Directory.CreateDirectory(Output); Directory.CreateDirectory("Temp");
        var previous = SceneManager.GetActiveScene();
        var sourceScene = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool openedSource = !sourceScene.IsValid() || !sourceScene.isLoaded;
        if (openedSource) sourceScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        var source = sourceScene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MapGenerator>(true)).First();
        var appearance = source.GetComponent<UniformStoneAppearance>();
        var previousLights = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var lightStates = previousLights.Select(x => x.enabled).ToArray();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var materials = new List<Material>();
        var generated = new List<Object>();
        var oldTarget = RenderTexture.active;
        Action<ScriptableRenderContext, Camera> overrideArt = null;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            foreach (var light in previousLights) light.enabled = false;
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Copper embedding test grid", typeof(Grid));
            grid.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
            var ground = new GameObject("Copper embedding test terrain", typeof(Tilemap), typeof(TilemapRenderer));
            ground.transform.SetParent(grid.transform, false); ground.layer = 31;
            var terrain = ground.GetComponent<Tilemap>();
            terrain.orientation = Tilemap.Orientation.Custom;
            terrain.orientationMatrix = Matrix4x4.Scale(new Vector3(2.2f, 2.2f, 1));
            var map = ground.AddComponent<MapGenerator>(); map.enabled = false;
            map.registry = source.registry; map.seed = 74123; map.oreScale = source.oreScale;
            map.mapWidth = 14; map.mapHeight = 12;
            var overlay = map.EnsureOreOverlay(); overlay.gameObject.layer = 31;
            var oreAppearance = ground.GetComponent<OreOverlayAppearance>();
            var oreMaterial = new Material(source.GetComponent<OreOverlayAppearance>().OverlayMaterial);
            materials.Add(oreMaterial); oreAppearance.OverlayMaterial = oreMaterial;
            var stoneMaterial = new Material(Shader.Find("Mining Game/Dirt Terrain Lit")); materials.Add(stoneMaterial);
            terrain.GetComponent<TilemapRenderer>().sharedMaterial = stoneMaterial;
            overlay.GetComponent<TilemapRenderer>().sortingOrder = 1;
            var mask = new Texture2D(14, 12, TextureFormat.RGBA32, false, true)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            generated.Add(mask);
            var brown = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            brown.SetPixels(Enumerable.Repeat(new Color(.36f, .26f, .17f, 1), 4).ToArray()); brown.Apply();
            generated.Add(brown);
            var colors = new Color[14 * 12];
            var props = new MaterialPropertyBlock();
            props.SetVector("_UniformStone", new Vector4(1.1f, 3, 0, 0));
            props.SetVector("_TestBounds", new Vector4(0, 0, 14, 12));
            props.SetTexture("_TestOccupancy", mask);
            for (int y = 0; y < 12; y++) for (int x = 0; x < 14; x++)
                terrain.SetTile(new Vector3Int(x, y, 0), source.uniformTestTile);
            var lamp = new GameObject("Copper embedding test lamp", typeof(Light2D)).GetComponent<Light2D>();
            lamp.gameObject.layer = 31;
            lamp.lightType = Light2D.LightType.Global; lamp.intensity = 1;
            var camera = new GameObject("Copper embedding test camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(7.7f, 6.6f, -10);
            camera.orthographic = true; camera.orthographicSize = 6.6f;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); generated.Add(rt);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false); generated.Add(pixels);
            camera.targetTexture = rt;
            float recess = 1;
            bool controlled = false, silhouette = false;
            void SetRock(int layer)
            {
                for (int y = 0; y < 12; y++) for (int x = 0; x < 14; x++)
                    colors[y * 14 + x] = new Color(1, layer == 0 ? 1 : 0, layer == 1 ? 1 : 0, layer == 3 ? .5f : layer == 4 ? 1 : 0);
                mask.SetPixels(colors); mask.Apply();
                props.SetTexture("_TestStoneTex", controlled ? brown : appearance.texture);
                props.SetTexture("_SurfaceDirtTex", controlled ? brown : appearance.dirtTexture);
                props.SetTexture("_LayerOneTex", controlled ? brown : appearance.layerOneTexture);
                props.SetTexture("_LayerThreeTex", controlled ? brown : appearance.layerThreeTexture);
                props.SetTexture("_LayerFourTex", controlled ? brown : appearance.layerFourTexture);
                terrain.GetComponent<TilemapRenderer>().SetPropertyBlock(props);
                oreAppearance.ApplyTerrain(props);
            }
            overrideArt = (context, view) =>
            {
                if (view != camera) return;
                var p = new MaterialPropertyBlock(); overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(p);
                p.SetFloat("_CopperRecessDepth", recess); p.SetFloat("_CopperCoverEnabled", 1);
                if (controlled || silhouette) p.SetTexture("_CopperCarpet", Texture2D.whiteTexture);
                // The same property block survives camera renders. Explicitly
                // restore every host texture when leaving silhouette mode.
                foreach (string property in new[] { "_TestStoneTex", "_SurfaceDirtTex", "_LayerOneTex", "_LayerThreeTex", "_LayerFourTex" })
                    p.SetTexture(property, silhouette ? Texture2D.blackTexture : props.GetTexture(property));
                overlay.GetComponent<TilemapRenderer>().SetPropertyBlock(p);
                if (oreAppearance.CopperSurface) { p.SetFloat("_CopperSurfacePass", 1); oreAppearance.CopperSurface.SetPropertyBlock(p); }
            };
            RenderPipelineManager.beginCameraRendering += overrideArt;
            Color32[] Render(string name)
            {
                terrain.RefreshAllTiles(); overlay.RefreshAllTiles();
                oreAppearance.PrepareVeins(camera); camera.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            void Deposit(Block block)
            {
                overlay.ClearAllTiles();
                for (int y = 3; y <= 8; y++) for (int x = 4; x <= 9; x++)
                    overlay.SetTile(new Vector3Int(x, y, 0), block.richOre[0]);
            }
            var copper = map.registry.GetById(BlockType.CopperOre);
            Check(oreMaterial.HasProperty("_CopperRecessDepth"), "Recess comparison control is available on the production shader");
            Deposit(copper); controlled = true; SetRock(1);
            recess = 0; var before = Render("Controlled-before");
            silhouette = true; terrain.GetComponent<TilemapRenderer>().enabled = false;
            var maskPixels = Render("Controlled-silhouette");
            silhouette = false; terrain.GetComponent<TilemapRenderer>().enabled = true;
            recess = 1; var after = Render("Controlled-recessed");
            double interiorReference = 0; int referenceCount = 0;
            for (int y = 5 * CellPixels; y < 7 * CellPixels; y++) for (int x = 6 * CellPixels; x < 8 * CellPixels; x++)
            { interiorReference += after[y * Width + x].r; referenceCount++; }
            interiorReference /= referenceCount;
            int walls = 0, shadows = 0, highlights = 0, changed = 0;
            double upperLeft = 0, lowerRight = 0; int upperLeftCount = 0, lowerRightCount = 0;
            bool Occupied(int x, int y) => maskPixels[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)].r > 26;
            for (int y = 24; y < Height - 24; y++) for (int x = 24; x < Width - 24; x++)
            {
                int n = y * Width + x; var a = before[n]; var b = after[n];
                if (!a.Equals(b)) changed++;
                bool inside = Occupied(x, y);
                int gx = (Occupied(x + 24, y) ? 1 : 0) - (Occupied(x - 24, y) ? 1 : 0);
                int gy = (Occupied(x, y + 24) ? 1 : 0) - (Occupied(x, y - 24) ? 1 : 0);
                bool nearEdge = gx != 0 || gy != 0;
                if (inside && b.r > b.b * 1.15f && b.g > b.b * 1.08f && a.b - b.b > 12) walls++;
                // White copper remains achromatic under the flat white lamp.
                // A darker achromatic band is therefore a shadow, not brown earth.
                bool copperShadow = inside && nearEdge && Math.Max(b.r, Math.Max(b.g, b.b)) - Math.Min(b.r, Math.Min(b.g, b.b)) < 5
                    && b.r > 15 && a.r - b.r > 8;
                if (copperShadow) shadows++;
                if (!inside && nearEdge && b.r - a.r > 5) highlights++;
                if (inside && nearEdge && Math.Max(b.r, Math.Max(b.g, b.b)) - Math.Min(b.r, Math.Min(b.g, b.b)) < 5 && b.r > 15)
                {
                    // Assess the visible shadow against the unchanged white
                    // interior, independently of shadows already present before.
                    double darkness = Math.Max(0, interiorReference - b.r) / 255.0;
                    if (gx > 0 || gy < 0) { upperLeft += darkness; upperLeftCount++; }
                    if (gx < 0 || gy > 0) { lowerRight += darkness; lowerRightCount++; }
                }
            }
            Check(changed > 1500, "Recess creates a visible boundary change: " + changed + " pixels");
            Check(walls > 120, "Brown sidewall is visible inside the opening: " + walls + " pixels");
            Check(shadows > 120, "Separate narrow shadow remains on white copper: " + shadows + " pixels");
            Check(highlights > 60, "Upper earth lip receives a visible light edge: " + highlights + " pixels");
            double upperMean = upperLeft / Math.Max(1, upperLeftCount), lowerMean = lowerRight / Math.Max(1, lowerRightCount);
            Check(upperLeftCount > 50 && lowerRightCount > 50 && upperMean > lowerMean + .025,
                "Copper shadow is directional, strongest below upper/left earth: " + upperMean + " vs " + lowerMean);
            bool stableInterior = true, stableEarth = true, cores = true;
            for (int y = 5 * CellPixels; y < 7 * CellPixels; y++) for (int x = 6 * CellPixels; x < 8 * CellPixels; x++)
                stableInterior &= before[y * Width + x].Equals(after[y * Width + x]);
            for (int y = 0; y < Height; y += 4) for (int x = 0; x < 3 * CellPixels; x += 4)
                stableEarth &= before[y * Width + x].Equals(after[y * Width + x]);
            foreach (var cell in overlay.cellBounds.allPositionsWithin)
            {
                if (!overlay.HasTile(cell)) continue;
                for (int dy = -12; dy <= 12; dy++) for (int dx = -12; dx <= 12; dx++)
                {
                    float px = (dx + .5f) / CellPixels, py = (dy + .5f) / CellPixels;
                    if (px * px + py * py > .15f * .15f) continue;
                    var c = after[(cell.y * CellPixels + 40 + dy) * Width + cell.x * CellPixels + 40 + dx];
                    cores &= c.r > 15 && Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b)) < 5;
                }
            }
            Check(stableInterior, "Deep copper interior retains the exact original material colour");
            Check(stableEarth, "Earth outside the deposit retains the exact original material colour");
            Check(cores, "Entire 30%-diameter circle in every copper cell stays copper, without brown sidewall cover");
            controlled = false;
            for (int layer = 0; layer < 5; layer++)
            {
                SetRock(layer); recess = 0; var old = Render("Copper-L" + (layer + 1) + "-before");
                recess = 1; var current = Render("Copper-L" + (layer + 1) + "-recessed");
                int changes = current.Where((c, n) => !c.Equals(old[n])).Count();
                Check(layer < 2 ? changes > 1000 : changes == 0,
                    "Layer " + (layer + 1) + " recess scope: " + changes + " changed pixels");
            }
            SetRock(1); Deposit(map.registry.GetById(BlockType.DiamondOre));
            recess = 0; var diamondBefore = Render("Diamond-before");
            recess = 1; var diamondAfter = Render("Diamond-after");
            Check(diamondBefore.SequenceEqual(diamondAfter), "Other ores are unaffected by copper recess depth");
            Deposit(copper);
            lamp.intensity = 0; var dark = Render("Copper-dark");
            Check(dark.All(c => c.r < 2 && c.g < 2 && c.b < 2), "Recessed copper and earth wall remain dark without scene light");
            lamp.intensity = 1; lamp.color = Color.blue; var blue = Render("Copper-blue-light");
            Check(blue.All(c => c.r < 2 && c.g < 2), "Copper, sidewall and lip follow the real scene light colour");
            lamp.color = Color.white; overlay.ClearAllTiles();
            for (int y = 1; y < 11; y++) for (int x = 1; x < 13; x++)
                if (Mathf.Abs(y - (x * .52f + 2)) < 1.65f || (x > 5 && Mathf.Abs(y - (-x * .7f + 12)) < 1.1f))
                    overlay.SetTile(new Vector3Int(x, y, 0), copper.richOre[0]);
            recess = 0; Render("Copper-large-before");
            recess = 1; Render("Copper-large-recessed");
            Check(!ShaderUtil.ShaderHasError(oreMaterial.shader), "Embedding shader compiled and rendered successfully");
            File.WriteAllLines("Temp/CopperEmbeddingVisualChecks.txt", passed);
            return "PASS " + passed.Count + " checks. Images: " + Output;
        }
        finally
        {
            if (overrideArt != null) RenderPipelineManager.beginCameraRendering -= overrideArt;
            foreach (var view in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true))) view.targetTexture = null;
            RenderTexture.active = oldTarget;
            EditorSceneManager.CloseScene(scene, true);
            if (openedSource) EditorSceneManager.CloseScene(sourceScene, true);
            SceneManager.SetActiveScene(previous);
            for (int i = 0; i < previousLights.Length; i++) if (previousLights[i]) previousLights[i].enabled = lightStates[i];
            foreach (var obj in generated) if (obj) Object.DestroyImmediate(obj);
            foreach (var mat in materials) if (mat) Object.DestroyImmediate(mat);
            ShaderUtil.allowAsyncCompilation = previousAsync;
        }
    }
}
