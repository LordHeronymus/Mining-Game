using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Still-image comparison for the ore shares per vein thickness (VeinRichness): generates a world like the game
// (GPS values, caves, altar chamber) and renders real veins with their actual stone and ore tiles, each as a pair
// "before | after" (left: previous fixed thresholds, right: VeinRichness defaults), two each thin, medium, thick.
// Temporary additive scene, nothing in SampleScene changes.
// Output: Temp/VeinRichness/Vein_01.png … and Temp/VeinRichnessPreviewChecks.txt.
public static class VeinRichnessPreviewChecks
{
    const string Folder = "Temp/VeinRichness";
    const string ReportPath = "Temp/VeinRichnessPreviewChecks.txt";
    const int Seed = -717317, Layer = 31, PixelsPerCell = 32, Columns = 14, Rows = 14;
    const float Cell = .5f;

    public static string Main()
    {
        if (Application.isPlaying) return "FAIL: Edit Mode erforderlich.";
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) return "FAIL: MapGenerator mit Registry fehlt.";
        Directory.CreateDirectory(Folder);
        var randomBefore = UnityEngine.Random.state;

        var record = GpsSettings.Document.records.Find(entry => entry.key == GpsSchema.ComponentKey(map));
        T Value<T>(string name, T fallback)
        {
            var node = record?.fields.Find(field => field.name == name);
            return node == null ? fallback : (T)GpsCodec.Write(node, GpsSettings.Profile.Resolve);
        }
        int width = Value("mapWidth", map.mapWidth), height = Value("mapHeight", map.mapHeight), offsetX = -width / 2;
        var layers = Value("layers", map.layers);
        var oreSettings = Value("oreSettings", map.oreSettings);
        var sampler = new MapGenerationSampler(map.registry, Seed, height, layers, Value("oreDensityCurve", map.oreDensityCurve),
            Value("oreDensityMultiplierPercent", map.oreDensityMultiplierPercent), map.transitionThickness,
            Value("useOreSettings", map.useOreSettings) ? oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
        var altar = map.AltarChamber;
        var chamber = altar ? altar.ChooseLayout(Seed, width, height) : default;
        var caves = CaveGenerator.Generate(Value("caveGeneration", map.caveGeneration), Seed, width, height,
            reserved: (x, y) => chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
        var blocks = new Block[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector3Int(x + offsetX, -y, 0);
                int index = y * width + x;
                blocks[index] = chamber.IsOpen(cell) || caves[index] ? null :
                    chamber.IsShell(cell) ? sampler.GetBaseBlock(x, y) : sampler.GetBlock(x, y);
            }
        int Minimum(Block ore)
        {
            var setting = oreSettings == null || !ore ? null : Array.Find(oreSettings, entry => entry != null && entry.ore == ore.id);
            if (setting != null && setting.minimumVeinSize > 0) return Mathf.Max(1, setting.minimumVeinSize);
            return ore && ore.id == BlockType.UltroniumOre ? 1 : Mathf.Max(1, map.minimumOreVeinSize);
        }
        ConnectedOreVeins.Generate(blocks, width, height, Seed, sampler, Minimum,
            (x, y) => caves[y * width + x] || chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
        var before = OreVeins.Build(blocks, width, height, Seed);
        var after = OreVeins.Build(blocks, width, height, Seed, new VeinRichnessSettings());
        var depth = new int[blocks.Length];
        {
            // Distance to the vein boundary, as in OreVeins.BuildSteps.
            var queue = new int[blocks.Length];
            int head = 0, tail = 0;
            for (int i = 0; i < blocks.Length; i++)
            {
                var block = blocks[i];
                if (!block || !block.HasOreOverlays) continue;
                int x = i % width, y = i / width;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1 || blocks[i - 1] != block || blocks[i + 1] != block ||
                    blocks[i - width] != block || blocks[i + width] != block) { depth[i] = 1; queue[tail++] = i; }
            }
            while (head < tail)
            {
                int i = queue[head++], x = i % width, y = i / width;
                foreach (int to in new[] { x > 0 ? i - 1 : -1, x + 1 < width ? i + 1 : -1, y > 0 ? i - width : -1, y + 1 < height ? i + width : -1 })
                    if (to >= 0 && depth[to] == 0 && blocks[to] == blocks[i]) { depth[to] = depth[i] + 1; queue[tail++] = to; }
            }
        }

        // Two veins per thickness (thin, medium, thick), different ores where possible.
        string[] kinds = { "dünn", "mittel", "dick" };
        var rows = new List<(int[] cells, OreRichness[] right, string kind)>();
        var perKind = new int[3];
        var seen = new HashSet<(BlockType, int)>();
        foreach (var cells in Veins(blocks, width, height))
        {
            var ore = blocks[cells[0]];
            int kind = Mathf.Min(2, cells.Max(i => depth[i]) - 1);
            if (perKind[kind] >= 2 || cells.Length < 10 || cells.Length > 40 || Span(cells, width) > Columns - 2 || !seen.Add((ore.id, kind))) continue;
            if (!cells.Any(i => after[i] != before[i])) continue;
            perKind[kind]++;
            rows.Add((cells, after, kinds[kind]));
        }
        rows.Sort((a, b) => Array.IndexOf(kinds, a.kind).CompareTo(Array.IndexOf(kinds, b.kind)));
        if (rows.Count < 4) return "FAIL: zu wenige Beispieladern (" + rows.Count + ").";

        var previous = SceneManager.GetActiveScene();
        var lights = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var enabled = lights.Select(light => light.enabled).ToArray();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        int imageWidth = (Columns * 2 + 1) * PixelsPerCell, imageHeight = Rows * PixelsPerCell;
        var target = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(imageWidth, imageHeight, TextureFormat.RGBA32, false);
        var oldTarget = RenderTexture.active;
        var lines = new List<string>();
        try
        {
            foreach (var light in lights) light.enabled = false;
            SceneManager.SetActiveScene(scene);
            var sun = new GameObject("Preview Light", typeof(Light2D)).GetComponent<Light2D>();
            sun.gameObject.layer = Layer; sun.lightType = Light2D.LightType.Global; sun.intensity = 1;
            var camera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = Rows * Cell / 2f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.05f, .05f, .06f, 1);
            camera.cullingMask = 1 << Layer; camera.allowHDR = false; camera.targetTexture = target;
            int failed = 0;
            for (int row = 0; row < rows.Count; row++)
            {
                var (cells, right, kind) = rows[row];
                int minX = cells.Min(i => i % width), maxX = cells.Max(i => i % width), minY = cells.Min(i => i / width), maxY = cells.Max(i => i / width);
                int padX = (Columns - (maxX - minX + 1)) / 2, padY = (Rows - (maxY - minY + 1)) / 2;
                // Each pair sits in its own strip far below the previous one: left before, right after.
                float top = -row * (Rows + 4) * Cell;
                for (int side = 0; side < 2; side++)
                    for (int gy = 0; gy < Rows; gy++)
                        for (int gx = 0; gx < Columns; gx++)
                        {
                            int x = minX - padX + gx, y = minY - padY + gy;
                            if (x < 0 || x >= width || y < 0 || y >= height) continue;
                            var position = new Vector3((side * (Columns + 1) + gx + .5f) * Cell, top - (gy + .5f) * Cell, 0);
                            int index = y * width + x;
                            var block = blocks[index];
                            if (!block) continue;
                            Place(StoneSprite(map, block.HasOreOverlays ? sampler.GetBaseBlock(x, y) : block, x, y), position, Matrix4x4.identity, 0, material);
                            if (!block.HasOreOverlays) continue;
                            var variants = block.GetOreVariants(side == 0 ? before[index] : right[index]);
                            var tile = variants[OreVeins.Hash(Seed, x, y, 0x5678u) % (uint)variants.Length];
                            Place(tile.sprite, position, tile.transform, 1, material);
                        }
                camera.transform.position = new Vector3((Columns * 2 + 1) * Cell / 2f, top - Rows * Cell / 2f, -10);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
                pixels.Apply();
                string file = Path.Combine(Folder, "Vein_" + (row + 1).ToString("00") + ".png");
                File.WriteAllBytes(file, pixels.EncodeToPNG());

                // The pair must differ visibly; the picture must not be empty.
                var data = pixels.GetPixels32();
                int differing = 0;
                float brightness = 0;
                for (int py = 0; py < imageHeight; py++)
                    for (int px = 0; px < Columns * PixelsPerCell; px++)
                    {
                        var left = data[py * imageWidth + px];
                        var rightPixel = data[py * imageWidth + px + (Columns + 1) * PixelsPerCell];
                        brightness += (left.r + left.g + left.b) / (3f * 255f);
                        if (Mathf.Abs(left.r - rightPixel.r) + Mathf.Abs(left.g - rightPixel.g) + Mathf.Abs(left.b - rightPixel.b) > 24) differing++;
                    }
                brightness /= imageHeight * Columns * PixelsPerCell;
                int medium = cells.Count(i => right[i] == OreRichness.Medium && before[i] != OreRichness.Medium);
                lines.Add(file + " (" + kind + "): " + blocks[cells[0]].displayName + ", " + cells.Length + " Blöcke, Generierungszelle " + minX + "/" + minY +
                    ", +" + medium + " Medium, abweichende Pixel " + differing + ", Helligkeit " + brightness.ToString("0.000") + ".");
                if (differing == 0 || brightness < .02f) failed++;
            }
            lines.Insert(0, "Links vorher, rechts nachher (" + imageWidth + "x" + imageHeight + " px, " + PixelsPerCell + " px pro Kachel, Seed " + Seed + ").");
            if (failed > 0) lines.Insert(0, "FAIL " + failed + " Vergleichspaare leer oder ohne sichtbaren Unterschied.");
        }
        finally
        {
            RenderTexture.active = oldTarget;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].enabled = enabled[i];
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(material);
        }
        if (!UnityEngine.Random.state.Equals(randomBefore)) lines.Insert(0, "FAIL UnityEngine.Random-Zustand verändert.");
        File.WriteAllText(ReportPath, string.Join("\n", lines) + "\n");
        return lines.Any(line => line.StartsWith("FAIL")) ? "FAIL " + lines.First(line => line.StartsWith("FAIL")) : "PASS " + rows.Count + " Vergleichspaare gerendert.";
    }

    static void Place(Sprite sprite, Vector3 position, Matrix4x4 transform, int order, Material material)
    {
        if (!sprite) return;
        var go = new GameObject(sprite.name, typeof(SpriteRenderer)) { layer = Layer };
        go.transform.position = position + (Vector3)transform.GetColumn(3);
        go.transform.rotation = transform.rotation;
        go.transform.localScale = transform.lossyScale;
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.sortingOrder = order;
    }

    // The tiles MapGenerator draws for this stone (test stones of the uniform terrain if they are active).
    static Sprite StoneSprite(MapGenerator map, Block stone, int x, int y)
    {
        if (map.uniformTestStone)
        {
            if (map.surfaceDirtTile && stone.id == BlockType.Dirt) return map.surfaceDirtTile.sprite;
            if (map.layerOneTile && stone == map.layerOneTile.block) return map.layerOneTile.sprite;
            if (map.layerThreeTile && stone == map.layerThreeTile.block) return map.layerThreeTile.sprite;
            if (map.layerFourTile && stone == map.layerFourTile.block) return map.layerFourTile.sprite;
            if (map.uniformTestTile) return map.uniformTestTile.sprite;
        }
        var variants = stone.variants;
        if (variants == null || variants.Length == 0) return null;
        var tile = variants[OreVeins.Hash(map.ActiveSeed, x, y, 0x1234u) % (uint)variants.Length];
        // Layer stones are rule tiles (tilemap extras); their default sprite is a public field.
        return tile is Tile plain ? plain.sprite : tile ? tile.GetType().GetField("m_DefaultSprite")?.GetValue(tile) as Sprite : null;
    }

    static int Span(int[] cells, int width) => Mathf.Max(cells.Max(i => i % width) - cells.Min(i => i % width), cells.Max(i => i / width) - cells.Min(i => i / width)) + 1;

    static List<int[]> Veins(Block[] blocks, int width, int height)
    {
        var result = new List<int[]>();
        var visited = new bool[blocks.Length];
        var queue = new int[blocks.Length];
        for (int start = 0; start < blocks.Length; start++)
        {
            var ore = blocks[start];
            if (visited[start] || !ore || !ore.HasOreOverlays) continue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                int i = queue[head++], x = i % width, y = i / width;
                if (x > 0) Visit(i - 1);
                if (x + 1 < width) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y + 1 < height) Visit(i + width);
            }
            var cells = new int[tail];
            Array.Copy(queue, cells, tail);
            Array.Sort(cells);
            result.Add(cells);

            void Visit(int index)
            {
                if (visited[index] || blocks[index] != ore) return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }
        return result;
    }
}
