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
public static class CopperShallowWallChecks
{
    const string Output = "Assets/Design/OreVeins/CopperEarthWall/ShallowReference/Implemented";
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
            float recess = 2;
            float wallHeight = .34f;
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
                p.SetFloat("_CopperWallHeight", wallHeight);
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
            Check(oreMaterial.HasProperty("_CopperRecessDepth") && oreMaterial.HasProperty("_CopperWallHeight"),
                "Earth-wall depth and height comparison controls are available on the production shader");
            using (var defaultMaterial = new DisposableMaterial(oreMaterial.shader))
                Check(Mathf.Abs(defaultMaterial.Value.GetFloat("_CopperRecessDepth") - 2) < .001f &&
                    Mathf.Abs(defaultMaterial.Value.GetFloat("_CopperWallHeight") - .34f) < .001f,
                    "Shallow .34-cell earth wall is the production shader default");
            Deposit(copper); controlled = false; SetRock(1);
            wallHeight = .71f; Render("Copper-initial-tall");
            wallHeight = .34f; Render("Copper-initial-shallow");
            controlled = true; SetRock(1);
            wallHeight = .71f; var before = Render("Controlled-tall-wall");
            silhouette = true; terrain.GetComponent<TilemapRenderer>().enabled = false;
            recess = 0;
            var maskPixels = Render("Controlled-silhouette");
            silhouette = false; terrain.GetComponent<TilemapRenderer>().enabled = true;
            recess = 2; wallHeight = .34f; var after = Render("Controlled-shallow-wall");
            double interiorReference = 0; int referenceCount = 0;
            for (int y = 5 * CellPixels; y < 7 * CellPixels; y++) for (int x = 6 * CellPixels; x < 8 * CellPixels; x++)
            { interiorReference += after[y * Width + x].r; referenceCount++; }
            interiorReference /= referenceCount;
            double hostReference = 0; int hostCount = 0;
            for (int y = 0; y < Height; y += 4) for (int x = 0; x < 3 * CellPixels; x += 4)
            { hostReference += after[y * Width + x].r; hostCount++; }
            hostReference /= hostCount;
            int tallWalls = 0, shallowWalls = 0, shadows = 0, highlights = 0, changed = 0;
            double upperLeft = 0, lowerRight = 0; int upperLeftCount = 0, lowerRightCount = 0;
            bool Occupied(int x, int y) => maskPixels[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)].r > 26;
            bool EarthColoured(Color32 c) => c.r > c.b * 1.15f && c.g > c.b * 1.08f;
            for (int y = 40; y < Height - 40; y++) for (int x = 40; x < Width - 40; x++)
            {
                int n = y * Width + x; var a = before[n]; var b = after[n];
                if (!a.Equals(b)) changed++;
                bool inside = Occupied(x, y);
                int gx = (Occupied(x + 40, y) ? 1 : 0) - (Occupied(x - 40, y) ? 1 : 0);
                int gy = (Occupied(x, y + 40) ? 1 : 0) - (Occupied(x, y - 40) ? 1 : 0);
                bool nearEdge = gx != 0 || gy != 0;
                const int areaNearEdgeRadius = 80;
                bool areaNearEdge = Occupied(x + areaNearEdgeRadius, y) != Occupied(x - areaNearEdgeRadius, y)
                    || Occupied(x, y + areaNearEdgeRadius) != Occupied(x, y - areaNearEdgeRadius);
                // The raised opening can place part of the earth wall above
                // the copper footprint. Count its darker brown face on the
                // otherwise flat host, using the same image rule in both views.
                if (EarthColoured(a) && (inside || (areaNearEdge && a.r < hostReference - 12))) tallWalls++;
                if (EarthColoured(b) && (inside || (areaNearEdge && b.r < hostReference - 12))) shallowWalls++;
                // White copper remains achromatic under the flat white lamp.
                // A darker achromatic band is therefore a shadow, not brown earth.
                bool copperShadow = inside && nearEdge && Math.Max(b.r, Math.Max(b.g, b.b)) - Math.Min(b.r, Math.Min(b.g, b.b)) < 5
                    && b.r > 15 && interiorReference - b.r > 10;
                if (copperShadow) shadows++;
                if (!inside && nearEdge && b.r - hostReference > 5) highlights++;
                if (inside && nearEdge && Math.Max(b.r, Math.Max(b.g, b.b)) - Math.Min(b.r, Math.Min(b.g, b.b)) < 5 && b.r > 15)
                {
                    // Assess the visible shadow against the unchanged white
                    // interior, independently of shadows already present before.
                    double darkness = Math.Max(0, interiorReference - b.r) / 255.0;
                    if (gx > 0 || gy < 0) { upperLeft += darkness; upperLeftCount++; }
                    if (gx < 0 || gy > 0) { lowerRight += darkness; lowerRightCount++; }
                }
            }
            Check(changed > 1500, "Shallower wall creates a visible boundary change: " + changed + " pixels");
            Check(shallowWalls > 120 && shallowWalls < tallWalls * .70 && tallWalls - shallowWalls > 250,
                "Brown earth wall visibly reduces in area: " + tallWalls + " to " + shallowWalls + " pixels");
            Check(shadows > 120, "Separate recessed shadow remains on white copper: " + shadows + " pixels");
            Check(highlights > 60, "Upper earth lip receives a visible light edge: " + highlights + " pixels");
            double upperMean = upperLeft / Math.Max(1, upperLeftCount), lowerMean = lowerRight / Math.Max(1, lowerRightCount);
            Check(upperLeftCount > 50 && lowerRightCount > 50 && upperMean > lowerMean + .025,
                "Copper shadow is directional, strongest below upper/left earth: " + upperMean + " vs " + lowerMean);
            bool stableInterior = true, stableEarth = true;
            for (int y = 5 * CellPixels; y < 7 * CellPixels; y++) for (int x = 6 * CellPixels; x < 8 * CellPixels; x++)
                stableInterior &= before[y * Width + x].Equals(after[y * Width + x]);
            for (int y = 0; y < Height; y += 4) for (int x = 0; x < 3 * CellPixels; x += 4)
                stableEarth &= before[y * Width + x].Equals(after[y * Width + x]);
            bool AllCoresCopper(Color32[] image)
            {
                foreach (var cell in overlay.cellBounds.allPositionsWithin)
                {
                    if (!overlay.HasTile(cell)) continue;
                    for (int dy = -12; dy <= 12; dy++) for (int dx = -12; dx <= 12; dx++)
                    {
                        float px = (dx + .5f) / CellPixels, py = (dy + .5f) / CellPixels;
                        if (px * px + py * py > .15f * .15f) continue;
                        var c = image[(cell.y * CellPixels + 40 + dy) * Width + cell.x * CellPixels + 40 + dx];
                        if (c.r <= 15 || Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b)) >= 5) return false;
                    }
                }
                return true;
            }
            Check(stableInterior, "Deep copper interior retains the exact original material colour");
            Check(stableEarth, "Earth outside the deposit retains the exact original material colour");
            Check(AllCoresCopper(after), "Entire 30%-diameter circle in every copper cell stays copper, without brown sidewall cover");
            int outsideCopper = 0;
            for (int y = 4; y < Height - 4; y++) for (int x = 4; x < Width - 4; x++)
            {
                if (overlay.HasTile(new Vector3Int(x / CellPixels, y / CellPixels, 0))) continue;
                var c = after[y * Width + x];
                if (c.r > 15 && Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b)) < 5) outsideCopper++;
            }
            Check(outsideCopper == 0, "No white copper is drawn outside actual copper cells; outside earth walls remain earth");
            controlled = false;
            for (int layer = 0; layer < 5; layer++)
            {
                SetRock(layer); wallHeight = .71f; var old = Render("Copper-L" + (layer + 1) + "-tall-wall");
                wallHeight = .34f; var current = Render("Copper-L" + (layer + 1) + "-shallow-wall");
                int changes = current.Where((c, n) => !c.Equals(old[n])).Count();
                Check(layer < 2 ? changes > 1000 : changes == 0,
                    "Layer " + (layer + 1) + " shallow-wall scope: " + changes + " changed pixels");
            }
            SetRock(1); Deposit(map.registry.GetById(BlockType.DiamondOre));
            wallHeight = .71f; var diamondBefore = Render("Diamond-tall-wall");
            wallHeight = .34f; var diamondAfter = Render("Diamond-shallow-wall");
            Check(diamondBefore.SequenceEqual(diamondAfter), "Other ores are unaffected by copper recess depth");
            Deposit(copper);
            lamp.intensity = 0; var dark = Render("Copper-dark");
            Check(dark.All(c => c.r < 2 && c.g < 2 && c.b < 2), "Recessed copper and earth wall remain dark without scene light");
            lamp.intensity = 1; lamp.color = Color.blue; var blue = Render("Copper-blue-light");
            Check(blue.All(c => c.r < 2 && c.g < 2), "Copper, sidewall and lip follow the real scene light colour");
            lamp.color = Color.white;
            var galleryPairs = new List<(Color32[] tall, Color32[] shallow)>();
            foreach (string shape in new[] { "L-shape", "Broad-branches", "Thin-branches" })
            {
                overlay.ClearAllTiles();
                for (int y = 1; y < 11; y++) for (int x = 1; x < 13; x++)
                {
                    bool occupied;
                    if (shape == "L-shape")
                        occupied = (x >= 4 && x <= 6 && y >= 3 && y <= 8) || (x >= 6 && x <= 10 && y >= 3 && y <= 5);
                    else if (shape == "Broad-branches")
                        occupied = Mathf.Abs(y - (x * .52f + 2)) < 1.65f || (x > 5 && Mathf.Abs(y - (-x * .7f + 12)) < 1.1f);
                    else
                        occupied = (x == 7 && y >= 2 && y <= 9) || (y == 6 && x >= 3 && x <= 10) || (x == 10 && y >= 4 && y <= 6);
                    if (occupied) overlay.SetTile(new Vector3Int(x, y, 0), copper.richOre[0]);
                }
                controlled = true; SetRock(1); wallHeight = .34f;
                Check(AllCoresCopper(Render("Controlled-" + shape)), shape + ": all complete .15-radius copper cores remain free");
                controlled = false; SetRock(1); wallHeight = .71f;
                var tall = Render("Copper-" + shape + "-tall-wall");
                wallHeight = .34f; var shallow = Render("Copper-" + shape + "-shallow-wall");
                galleryPairs.Add((tall, shallow));
                if (shape == "L-shape")
                {
                    var savedPosition = camera.transform.position;
                    camera.transform.position += Vector3.right * 1.1f;
                    var shifted = Render("Copper-L-shape-scroll");
                    double error = 0; int comparisons = 0;
                    for (int y = 20; y < Height - 20; y += 3) for (int x = 20; x < Width - CellPixels - 20; x += 3)
                    {
                        var a = shallow[y * Width + x + CellPixels]; var b = shifted[y * Width + x];
                        error += (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / 3.0;
                        comparisons++;
                    }
                    camera.transform.position = savedPosition;
                    Check(error / comparisons < 1, "Shallow copper, earth wall and shadow remain world-fixed during camera movement: " + error / comparisons);
                }
            }
            // Compact visual review: rows are L-shape, broad branches and thin
            // branches; left is the taller .71 wall, right the shallow .34 wall.
            const int thumbWidth = 560, thumbHeight = 480;
            var gallery = new Texture2D(thumbWidth * 2, thumbHeight * 3, TextureFormat.RGBA32, false);
            generated.Add(gallery);
            var galleryPixels = new Color32[thumbWidth * 2 * thumbHeight * 3];
            for (int row = 0; row < galleryPairs.Count; row++) for (int side = 0; side < 2; side++)
            {
                var original = side == 0 ? galleryPairs[row].tall : galleryPairs[row].shallow;
                for (int y = 0; y < thumbHeight; y++) for (int x = 0; x < thumbWidth; x++)
                    galleryPixels[((2 - row) * thumbHeight + y) * (thumbWidth * 2) + side * thumbWidth + x] = original[(y * 2) * Width + x * 2];
            }
            gallery.SetPixels32(galleryPixels); gallery.Apply();
            File.WriteAllBytes(Output + "/Copper-shallow-gallery.png", gallery.EncodeToPNG());
            Check(!ShaderUtil.ShaderHasError(oreMaterial.shader), "Shallow earth-wall shader compiled and rendered successfully");
            File.WriteAllLines("Temp/CopperShallowWallChecks.txt", passed);
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

    sealed class DisposableMaterial : IDisposable
    {
        public readonly Material Value;
        public DisposableMaterial(Shader shader) { Value = new Material(shader); }
        public void Dispose() { if (Value) Object.DestroyImmediate(Value); }
    }
}
