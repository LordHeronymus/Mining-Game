using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Archived 18.10.2026 (applied): procedural copper ore art. InstallLarge / InstallSmallMedium (= Main) install the tiles and
// overwrite the installed sprites when re-run; PreviewSmallMedium renders the size comparison,
// Previews() renders the design rounds V/M/L onto the live stone art. Re-running Main overwrites the installed sprites.
// Output: Design/OreVeins/CopperLargeV3 (Sources/*.png = 544 px overlays, 418 px = one cell; Preview-*.png, Overview.png).
public static class CopperOreArt
{
    const string Out = "Design/OreVeins/CopperLargeV3";
    const int Cell = 418, Canvas = 544, SS = 2;
    static readonly Vector3 Light = new Vector3(-.5f, .6f, .62f).normalized;
    static readonly Vector3 Half = (new Vector3(-.5f, .6f, .62f).normalized + Vector3.forward).normalized;

    enum Kind { Blob, Facet, Crystal }
    enum Mat { Copper, Weathered, Malachite, Stone, Cuprite, Pebble }

    sealed class Piece
    {
        public Kind kind; public Mat mat;
        public Vector2 c; public float r, k = .75f, z, rot, elong = 1;
        public float[] amp = new float[6], ph = new float[6]; public bool veins; public int style; // style 1 = bright gem facets, 2 = cel-shaded bands
        public Vector2[] seeds; public Vector3[] seedN; public Mat[] seedMat;
        public Vector2[] poly, inner; public Vector2 apex; public Vector3 topN;
        public int seed;
    }

    struct Hit { public float height, sd, edge, hn, d; public Vector3 n; public Mat mat; public Piece p; }

    public static string Previews()
    {
        Directory.CreateDirectory(Out + "/Sources");
        // Round 1: V1-Gediegen/Nuggets, V2-Kristall/Crystals, V3-Malachitbrocken/Boulder, V4-Patinaknollen/Patina.
        // Round 2: M1-Facettennuggets/GemNuggets, M2-Cel-Nuggets/CelNuggets, M3-Cuprit-Nuggets/CupriteNuggets, M4-Grosser-Klumpen/BigLump.
        var names = new[] { "L1-Drei-Grosse", "L2-Zwei-Nester", "L3-Gestreut", "L4-Kern-mit-Kranz" };
        var builders = new Func<List<Piece>>[] { LargeThree, LargeNests, LargeScattered, LargeCore };
        var dirt = Load("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png");
        var clay = Load("Assets/GameObjects/Map/StoneTest/Layer1/ClayStone.png");
        var overlays = new Color[names.Length][];
        var report = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            var pieces = builders[i]();
            overlays[i] = Render(pieces);
            Save(Out + "/Sources/" + names[i] + ".png", overlays[i], Canvas, Canvas);
            var preview = Preview(overlays[i], dirt, clay);
            Save(Out + "/Preview-" + names[i] + ".png", preview, Cell * 3, Cell * 3 / 2);
            report.Add(names[i] + ": " + pieces.Count + " pieces");
        }
        // 2x2 overview of the half-resolution previews.
        int pw = Cell * 3, ph = Cell * 3 / 2;
        var overview = new Color[pw * 2 * ph * 2];
        for (int i = 0; i < 4; i++)
        {
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Out + "/Preview-" + names[i] + ".png"));
            var px = t.GetPixels(); int ox = (i % 2) * pw, oy = (1 - i / 2) * ph;
            for (int y = 0; y < ph; y++) for (int x = 0; x < pw; x++) overview[(oy + y) * pw * 2 + ox + x] = px[y * pw + x];
            UnityEngine.Object.DestroyImmediate(t);
        }
        Save(Out + "/Overview-Large.png", overview, pw * 2, ph * 2);
        return string.Join("\n", report);
    }

    // ---------- install: L2 "Zwei Nester" as the copper Large (rich) ore tiles ----------

    const string SpriteDir = "Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Copper";
    const string TileDir = "Assets/GameObjects/Map/Blocks/Ores/Copper";
    static readonly int[] InstallSeeds = { 622, 655, 688 };

    public static string InstallLarge()
    {
        var block = CopperBlock(out var reference); var report = new List<string>();
        block.richOre = InstallTiles("Large", "large", 8, OreRichness.Rich, InstallSeeds, LargeNests, Out + "/Installed", block, reference, report);
        return Finish(block, report);
    }

    // Applied 18.10.2026: Small/Medium in the Large style (2 variants each).
    public static string Main() => InstallSmallMedium();
    public static string InstallSmallMedium()
    {
        var block = CopperBlock(out var reference); var report = new List<string>();
        block.smallOre = InstallTiles("Small", "small", 11, OreRichness.Small, SmallSeeds, SmallNest, SmallMediumOut + "/Installed", block, reference, report);
        block.mediumOre = InstallTiles("Medium", "medium", 13, OreRichness.Medium, MediumSeeds, MediumNest, SmallMediumOut + "/Installed", block, reference, report);
        return Finish(block, report);
    }

    static Block CopperBlock(out OreTile reference)
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        reference = UnityEditor.AssetDatabase.LoadAssetAtPath<OreTile>(TileDir + "/Kupfer_05_rich.asset");
        var block = reference ? reference.block : null;
        if (!block || block.id != BlockType.CopperOre) throw new Exception("Copper block not found via Kupfer_05_rich");
        return block;
    }

    static OreTile[] InstallTiles(string size, string suffix, int firstNumber, OreRichness richness, int[] seeds,
        Func<int, List<Piece>> build, string designDir, Block block, OreTile reference, List<string> report)
    {
        Directory.CreateDirectory(SpriteDir); Directory.CreateDirectory(designDir);
        var tiles = new OreTile[seeds.Length];
        for (int i = 0; i < seeds.Length; i++)
        {
            var pixels = Render(build(seeds[i]));
            string name = "Copper_" + size + "_0" + (i + 1) + ".png", path = SpriteDir + "/" + name;
            Save(path, pixels, Canvas, Canvas);
            Save(designDir + "/" + name, pixels, Canvas, Canvas);
            UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
            importer.textureType = UnityEditor.TextureImporterType.Sprite;
            importer.spriteImportMode = UnityEditor.SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 836; // 418 px = one cell (0.5 units), the 544 px canvas overhangs slightly
            var settings = new UnityEditor.TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = true; importer.alphaIsTransparency = true; importer.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput;
            importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true; importer.filterMode = FilterMode.Trilinear; importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024; importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            string tilePath = TileDir + "/Kupfer_" + (firstNumber + i).ToString("00") + "_" + suffix + ".asset";
            var tile = UnityEditor.AssetDatabase.LoadAssetAtPath<OreTile>(tilePath);
            bool create = !tile; if (create) tile = ScriptableObject.CreateInstance<OreTile>();
            tile.sprite = sprite;
            tile.color = new Color(1, 1, 1, 254f / 255f); // painted-ore marker for OreOverlayLit
            tile.flags = reference.flags; tile.colliderType = reference.colliderType; tile.transform = Matrix4x4.identity;
            tile.block = block; tile.richness = richness;
            if (create) UnityEditor.AssetDatabase.CreateAsset(tile, tilePath); else UnityEditor.EditorUtility.SetDirty(tile);
            tiles[i] = tile;
            report.Add(tilePath + " <- " + path + " (" + sprite.rect.size + " px, " + sprite.bounds.size.x.ToString("0.000") + " units)");
        }
        // Saves address tiles by GUID through the catalog; add only the new tiles.
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SaveAssetCatalog>("Assets/Resources/SaveAssetCatalog.asset");
        var entries = new List<SaveAssetCatalog.Entry>(catalog.tiles);
        foreach (var tile in tiles)
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(tile));
            if (!entries.Exists(e => e.key == guid)) entries.Add(new SaveAssetCatalog.Entry { key = guid, tile = tile });
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        catalog.tiles = entries.ToArray(); UnityEditor.EditorUtility.SetDirty(catalog);
        return tiles;
    }

    static string Finish(Block block, List<string> report)
    {
        UnityEditor.EditorUtility.SetDirty(block);
        UnityEditor.AssetDatabase.SaveAssets();
        report.Add("Copper small/medium/rich = " + string.Join(", ", Array.ConvertAll(block.smallOre, t => t.name)) + " | " +
            string.Join(", ", Array.ConvertAll(block.mediumOre, t => t.name)) + " | " + string.Join(", ", Array.ConvertAll(block.richOre, t => t.name)));
        return string.Join("\n", report);
    }

    // ---------- Small / Medium in the Large style ----------

    const string SmallMediumOut = "Design/OreVeins/CopperSmallMedium";
    static readonly int[] SmallSeeds = { 711, 722 }, MediumSeeds = { 811, 822 };

    static List<Piece> SmallNest(int seed)
    {
        var rng = new System.Random(seed);
        var start = new Vector2(272, 262) + new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * 70;
        var chunks = Cluster(rng, new float[] { 58, 34, 24 }, start, true);
        Scatter(rng, new float[] { 17 }, 1.4f, chunks);
        return Build(rng, chunks, 6);
    }

    static List<Piece> MediumNest(int seed)
    {
        var rng = new System.Random(seed);
        var jitter = new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * 40;
        var chunks = Cluster(rng, new float[] { 80, 50, 38, 26, 20 }, new Vector2(240, 290) + jitter, true);
        chunks.AddRange(Cluster(rng, new float[] { 36, 22 }, new Vector2(385, 165) + jitter * .5f, true));
        return Build(rng, chunks, 8);
    }

    public static string PreviewSmallMedium()
    {
        Directory.CreateDirectory(SmallMediumOut + "/Sources");
        var small = new Color[2][]; var medium = new Color[2][]; var large = new Color[3][];
        for (int i = 0; i < 2; i++)
        {
            small[i] = Render(SmallNest(SmallSeeds[i])); Save(SmallMediumOut + "/Sources/Copper_Small_0" + (i + 1) + ".png", small[i], Canvas, Canvas);
            medium[i] = Render(MediumNest(MediumSeeds[i])); Save(SmallMediumOut + "/Sources/Copper_Medium_0" + (i + 1) + ".png", medium[i], Canvas, Canvas);
        }
        for (int i = 0; i < 3; i++)
        {
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes("Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Copper/Copper_Large_0" + (i + 1) + ".png"));
            large[i] = t.GetPixels(); UnityEngine.Object.DestroyImmediate(t);
        }
        var dirt = Load("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png");
        var clay = Load("Assets/GameObjects/Map/StoneTest/Layer1/ClayStone.png");
        // Rows from top: Small A/B, Medium A/B, Large 1-3, then a mixed deposit across both rocks.
        var cells = new List<(int x, int y, Color[] ore)>();
        for (int side = 0; side < 2; side++)
        {
            int o = side * 3;
            cells.Add((o, 3, small[0])); cells.Add((o + 1, 3, small[1])); cells.Add((o + 2, 3, small[0]));
            cells.Add((o, 2, medium[0])); cells.Add((o + 1, 2, medium[1])); cells.Add((o + 2, 2, medium[0]));
            cells.Add((o, 1, large[0])); cells.Add((o + 1, 1, large[1])); cells.Add((o + 2, 1, large[2]));
        }
        var mixed = new[] { large[0], medium[0], small[1], medium[1], large[1], small[0] };
        for (int x = 0; x < 6; x++) cells.Add((x, 0, mixed[x]));
        var preview = Compose(cells, dirt, clay, 6, 4);
        Save(SmallMediumOut + "/Preview-Sizes.png", preview, Cell * 3, Cell * 2);
        return "Preview: " + SmallMediumOut + "/Preview-Sizes.png";
    }

    static Color[] Compose(List<(int x, int y, Color[] ore)> cells, Color[] dirt, Color[] clay, int cols, int rows)
    {
        int W = Cell * cols, H = Cell * rows; var img = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            img[y * W + x] = (x < Cell * 3 ? dirt : clay)[(y % (Cell * 3)) * Cell * 3 + x % (Cell * 3)];
        foreach (var cell in cells)
        {
            int ox = cell.x * Cell + Cell / 2 - Canvas / 2, oy = cell.y * Cell + Cell / 2 - Canvas / 2;
            for (int y = 0; y < Canvas; y++) for (int x = 0; x < Canvas; x++)
            {
                int tx = ox + x, ty = oy + y; if (tx < 0 || ty < 0 || tx >= W || ty >= H) continue;
                var c = cell.ore[y * Canvas + x]; if (c.a <= 0) continue;
                var b = img[ty * W + tx]; img[ty * W + tx] = new Color(Mathf.Lerp(b.r, c.r, c.a), Mathf.Lerp(b.g, c.g, c.a), Mathf.Lerp(b.b, c.b, c.a), 1);
            }
        }
        var half = new Color[W / 2 * (H / 2)];
        for (int y = 0; y < H / 2; y++) for (int x = 0; x < W / 2; x++)
            half[y * (W / 2) + x] = (img[2 * y * W + 2 * x] + img[2 * y * W + 2 * x + 1] + img[(2 * y + 1) * W + 2 * x] + img[(2 * y + 1) * W + 2 * x + 1]) * .25f;
        return half;
    }

    // ---------- variants ----------

    // Irregular cluster: every new nugget leans against an earlier one, overhang up to half a radius past the cell.
    static List<(Vector2 c, float r)> Cluster(System.Random rng, float[] radii, Vector2 start, bool compact = false)
    {
        var placed = new List<(Vector2 c, float r)>();
        const float lo = 63, hi = 481;
        foreach (var r in radii)
        {
            if (placed.Count == 0) { placed.Add((start, r)); continue; }
            float bestScore = float.MaxValue; Vector2 best = default; int valid = 0;
            for (int attempt = 0; attempt < 800; attempt++)
            {
                var anchor = placed[rng.Next(placed.Count)];
                float a = (float)rng.NextDouble() * 6.2832f, dist = (anchor.r + r) * (.55f + (float)rng.NextDouble() * .3f);
                var c = anchor.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
                if (c.x < lo + r * .5f || c.x > hi - r * .5f || c.y < lo + r * .5f || c.y > hi - r * .5f) continue;
                bool ok = true; foreach (var o in placed) if ((o.c - c).magnitude < (o.r + r) * (compact ? .78f : .55f)) { ok = false; break; }
                if (!ok) continue;
                if (!compact) { placed.Add((c, r)); break; }
                // Compact: keep the best of several valid spots, close to the cluster centre but not perfectly round.
                float score = (c - start).magnitude + (float)rng.NextDouble() * 45;
                if (score < bestScore) { bestScore = score; best = c; }
                if (++valid >= 40) break;
            }
            if (compact && valid > 0) placed.Add((best, r));
        }
        return placed;
    }

    static List<Piece> Nuggets()
    {
        var rng = new System.Random(111); var list = new List<Piece>();
        var cluster = Cluster(rng, new float[] { 98, 74, 66, 54, 46, 40, 34, 30, 26, 22 }, new Vector2(258, 262));
        int i = 0;
        foreach (var (c, r) in cluster)
            list.Add(Outline(new Piece { kind = Kind.Blob, mat = Mat.Copper, c = c, r = r, z = (i++ * 7) % 23, k = .62f, rot = (float)rng.NextDouble() * 6.28f, elong = 1 + (float)rng.NextDouble() * .35f, seed = rng.Next() }, rng, .13f));
        Pebbles(list, rng, 12);
        return list;
    }

    static List<Piece> Crystals()
    {
        var rng = new System.Random(222); var list = new List<Piece>();
        void Spray(Vector2 root, int count, float scale, float centerDeg, float spread, int cuprite)
        {
            for (int i = 0; i < count; i++)
            {
                float deg = centerDeg + (float)(rng.NextDouble() * 2 - 1) * spread;
                if (i == 0) deg = centerDeg + (float)(rng.NextDouble() - .5) * 20;
                float a = deg * Mathf.Deg2Rad, r = scale * (i == 0 ? 1 : .45f + (float)rng.NextDouble() * .45f);
                float dist = r * (.35f + (float)rng.NextDouble() * .45f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                list.Add(Crystal(new Piece { kind = Kind.Crystal, mat = i % 4 == 3 && cuprite-- > 0 ? Mat.Cuprite : Mat.Copper, c = root + dir * dist, r = r, rot = a, elong = 1.45f + (float)rng.NextDouble() * .55f, z = 60 - dist * .4f + (float)rng.NextDouble() * 10, seed = rng.Next() }, rng));
            }
        }
        Spray(new Vector2(262, 205), 11, 96, 92, 80, 2);
        Spray(new Vector2(395, 360), 5, 54, 55, 55, 1);
        Spray(new Vector2(140, 375), 4, 44, 125, 50, 0);
        Pebbles(list, rng, 10);
        return list;
    }

    static List<Piece> Boulder()
    {
        var rng = new System.Random(333); var list = new List<Piece>();
        void Rock(float x, float y, float r, float el, int seeds, float z)
        {
            var p = Facets(Outline(new Piece { kind = Kind.Facet, mat = Mat.Stone, veins = true, c = new Vector2(x, y), r = r, z = z, k = .5f, elong = el, rot = (float)rng.NextDouble() * 6.28f, seed = rng.Next() }, rng, .26f), rng, seeds, .3f);
            list.Add(p);
        }
        Rock(262, 258, 168, 1.35f, 52, 0);
        Rock(450, 135, 58, 1.2f, 10, -12);
        Rock(96, 420, 48, 1.1f, 8, -12);
        // A few copper nuggets breaking out of the rock face.
        foreach (var (x, y, r) in new[] { (330f, 318f, 32f), (205f, 212f, 22f), (362f, 196f, 15f) })
            list.Add(Outline(new Piece { kind = Kind.Blob, mat = Mat.Copper, c = new Vector2(x, y), r = r, z = 70, k = .45f, elong = 1.2f, rot = (float)rng.NextDouble() * 6.28f, seed = rng.Next() }, rng, .15f));
        Pebbles(list, rng, 8);
        return list;
    }

    static List<Piece> Patina()
    {
        var rng = new System.Random(444); var list = new List<Piece>();
        var cluster = Cluster(rng, new float[] { 104, 82, 70, 60, 52, 44, 36, 30 }, new Vector2(262, 258));
        int i = 0;
        foreach (var (c, r) in cluster)
            list.Add(Facets(Outline(new Piece { kind = Kind.Facet, mat = Mat.Weathered, c = c, r = r, z = (i++ * 9) % 29, k = .68f, elong = 1 + (float)rng.NextDouble() * .3f, rot = (float)rng.NextDouble() * 6.28f, seed = rng.Next() }, rng, .12f), rng, 12 + (int)(r / 8), .2f));
        Pebbles(list, rng, 12);
        return list;
    }

    // ---------- round 2: V1 nugget shapes with the bright V2 gem look ----------

    static Piece GemNugget(System.Random rng, Vector2 c, float r, float z, int seedsPerRadius = 20)
        => Facets(Outline(new Piece { kind = Kind.Facet, mat = Mat.Copper, style = 1, c = c, r = r, z = z, k = .95f, rot = (float)rng.NextDouble() * 6.28f, elong = 1 + (float)rng.NextDouble() * .35f, seed = rng.Next() }, rng, .12f), rng, 3 + (int)(r / seedsPerRadius), .4f);

    static List<Piece> GemNuggets()
    {
        var rng = new System.Random(511); var list = new List<Piece>(); int i = 0;
        foreach (var (c, r) in Cluster(rng, new float[] { 108, 82, 72, 60, 50, 42, 34, 28, 22 }, new Vector2(258, 262), true))
            list.Add(GemNugget(rng, c, r, (i++ * 7) % 23, 14));
        Pebbles(list, rng, 12);
        return list;
    }

    static List<Piece> CelNuggets()
    {
        var rng = new System.Random(522); var list = new List<Piece>(); int i = 0;
        foreach (var (c, r) in Cluster(rng, new float[] { 108, 80, 70, 58, 48, 40, 32, 26 }, new Vector2(262, 258), true))
            list.Add(Outline(new Piece { kind = Kind.Blob, mat = Mat.Copper, style = 2, c = c, r = r, z = (i++ * 7) % 23, k = .8f, rot = (float)rng.NextDouble() * 6.28f, elong = 1 + (float)rng.NextDouble() * .35f, seed = rng.Next() }, rng, .12f));
        Pebbles(list, rng, 12);
        return list;
    }

    static List<Piece> CupriteNuggets()
    {
        var rng = new System.Random(533); var list = new List<Piece>(); int i = 0;
        foreach (var (c, r) in Cluster(rng, new float[] { 104, 84, 68, 56, 46, 38, 30, 24 }, new Vector2(262, 262), true))
        {
            var p = GemNugget(rng, c, r, (i++ * 7) % 23, 18);
            // Ruby-red cuprite patches on some facets; small nuggets are sometimes entirely cuprite.
            p.seedMat = new Mat[p.seeds.Length];
            for (int s = 0; s < p.seeds.Length; s++)
            {
                var wpos = p.c + p.seeds[s] * p.r;
                p.seedMat[s] = Fbm(wpos.x / 70, wpos.y / 70, 23) > .66f || (r < 30 && i % 2 == 0) ? Mat.Cuprite : Mat.Copper;
            }
            list.Add(p);
        }
        Pebbles(list, rng, 12);
        return list;
    }

    static List<Piece> BigLump()
    {
        var rng = new System.Random(544); var list = new List<Piece>();
        list.Add(GemNugget(rng, new Vector2(262, 268), 150, 0, 9));
        foreach (var (x, y, r) in new[] { (420f, 150f, 46f), (120f, 160f, 36f), (432f, 400f, 30f), (150f, 420f, 24f), (330f, 96f, 22f) })
            list.Add(GemNugget(rng, new Vector2(x, y), r, -10));
        Pebbles(list, rng, 14);
        return list;
    }

    // ---------- round 3: M3 look, Large = several chunks of clearly different sizes ----------

    static Piece CupriteNugget(System.Random rng, Vector2 c, float r, float z, int index)
    {
        var p = GemNugget(rng, c, r, z, 16);
        p.seedMat = new Mat[p.seeds.Length];
        for (int s = 0; s < p.seeds.Length; s++)
        {
            var wpos = p.c + p.seeds[s] * p.r;
            p.seedMat[s] = Fbm(wpos.x / 70, wpos.y / 70, 23) > .66f || (r < 26 && index % 3 == 1) ? Mat.Cuprite : Mat.Copper;
        }
        return p;
    }

    // Spread chunks over the whole cell; minGap scales the allowed distance (1 = just touching).
    static List<(Vector2 c, float r)> Scatter(System.Random rng, float[] radii, float minGap, List<(Vector2 c, float r)> placed = null)
    {
        placed ??= new List<(Vector2 c, float r)>();
        const float lo = 63, hi = 481;
        foreach (var r in radii)
            for (int attempt = 0; attempt < 3000; attempt++)
            {
                var c = new Vector2(lo + r * .5f + (float)rng.NextDouble() * (hi - lo - r), lo + r * .5f + (float)rng.NextDouble() * (hi - lo - r));
                bool ok = true; foreach (var o in placed) if ((o.c - c).magnitude < (o.r + r) * minGap) { ok = false; break; }
                if (ok) { placed.Add((c, r)); break; }
            }
        return placed;
    }

    static List<Piece> Build(System.Random rng, List<(Vector2 c, float r)> chunks, int pebbles)
    {
        var list = new List<Piece>(); int i = 0;
        foreach (var (c, r) in chunks) { list.Add(CupriteNugget(rng, c, r, (i * 7) % 23, i)); i++; }
        Pebbles(list, rng, pebbles);
        return list;
    }

    static List<Piece> LargeThree()
    {
        var rng = new System.Random(611);
        return Build(rng, Scatter(rng, new float[] { 105, 88, 74, 42, 36, 30, 24, 20 }, .78f), 10);
    }

    static List<Piece> LargeNests() => LargeNests(622);
    static List<Piece> LargeNests(int seed)
    {
        var rng = new System.Random(seed);
        var jitter = seed == 622 ? Vector2.zero : new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f) * 40;
        var chunks = Cluster(rng, new float[] { 92, 60, 46, 32, 24 }, new Vector2(185, 320) + jitter, true);
        chunks.AddRange(Cluster(rng, new float[] { 80, 54, 40, 28, 20 }, new Vector2(355, 195) - jitter, true));
        Scatter(rng, new float[] { 26, 20 }, 1.05f, chunks);
        return Build(rng, chunks, 10);
    }

    static List<Piece> LargeScattered()
    {
        var rng = new System.Random(633);
        return Build(rng, Scatter(rng, new float[] { 80, 70, 62, 54, 48, 42, 36, 30, 26, 22 }, .85f), 8);
    }

    static List<Piece> LargeCore()
    {
        var rng = new System.Random(644);
        var chunks = new List<(Vector2 c, float r)> { (new Vector2(262, 268), 104) };
        // Ring of smaller chunks around the core, a few reaching over the cell edge.
        float start = (float)rng.NextDouble() * 6.28f;
        float[] sizes = { 56, 30, 48, 26, 40, 60, 28, 42 };
        for (int k = 0; k < sizes.Length; k++)
        {
            float a = start + k / (float)sizes.Length * 6.2832f + (float)(rng.NextDouble() - .5) * .4f;
            float dist = 104 + sizes[k] * .45f + (float)rng.NextDouble() * 30;
            chunks.Add((new Vector2(262, 268) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist, sizes[k]));
        }
        return Build(rng, chunks, 8);
    }

    static void Pebbles(List<Piece> list, System.Random rng, int count)
    {
        int placed = 0;
        for (int attempt = 0; attempt < 4000 && placed < count; attempt++)
        {
            var p = new Vector2(40 + (float)rng.NextDouble() * (Canvas - 80), 40 + (float)rng.NextDouble() * (Canvas - 80));
            float sd = float.MaxValue; foreach (var q in list) if (q.mat != Mat.Pebble) sd = Mathf.Min(sd, SignedDistance(q, p));
            if (sd < 2 || sd > 22) continue;
            var pebble = Outline(new Piece { kind = Kind.Facet, mat = Mat.Pebble, c = p, r = 7 + (float)rng.NextDouble() * 9, z = -80, k = .6f, elong = 1.2f, rot = (float)rng.NextDouble() * 6.28f, seed = rng.Next() }, rng, .12f);
            list.Add(Facets(pebble, rng, 4, .3f)); placed++;
        }
    }

    // ---------- piece construction ----------

    static Piece Outline(Piece p, System.Random rng, float rough)
    {
        for (int i = 2; i < 6; i++) { p.amp[i] = rough * (float)rng.NextDouble() / (i - 1) * 1.6f; p.ph[i] = (float)rng.NextDouble() * 6.28f; }
        return p;
    }

    static Piece Facets(Piece p, System.Random rng, int count, float tilt)
    {
        var seeds = new List<Vector2> { new Vector2((float)(rng.NextDouble() - .5) * .3f, (float)(rng.NextDouble() - .5) * .3f) };
        for (int attempt = 0; attempt < 3000 && seeds.Count < count; attempt++)
        {
            var s = new Vector2((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * 1.05f;
            if (s.magnitude > 1.05f) continue;
            bool ok = true; float min = 1.5f / Mathf.Sqrt(count);
            foreach (var o in seeds) if ((o - s).magnitude < min) { ok = false; break; }
            if (ok) seeds.Add(s);
        }
        p.seeds = seeds.ToArray(); p.seedN = new Vector3[p.seeds.Length];
        for (int i = 0; i < p.seeds.Length; i++)
        {
            var s = p.seeds[i]; float rho = Mathf.Min(s.magnitude, .88f);
            var dir = s.magnitude > 1e-4f ? s.normalized : Vector2.zero;
            float slope = p.k * rho / Mathf.Sqrt(1 - rho * rho);
            var n = new Vector3(dir.x * slope + (float)(rng.NextDouble() - .5) * tilt * 2, dir.y * slope + (float)(rng.NextDouble() - .5) * tilt * 2, 1).normalized;
            p.seedN[i] = n;
        }
        return p;
    }

    static Piece Crystal(Piece p, System.Random rng)
    {
        int n = 5 + rng.Next(3);
        p.poly = new Vector2[n]; p.inner = new Vector2[n];
        p.apex = new Vector2(p.r * p.elong * .38f, (float)(rng.NextDouble() - .5) * p.r * .2f);
        float top = .3f + (float)rng.NextDouble() * .2f;
        for (int i = 0; i < n; i++)
        {
            float a = (i + (float)(rng.NextDouble() - .5) * .5f) / n * 6.2832f;
            float rr = p.r * (.8f + (float)rng.NextDouble() * .3f);
            p.poly[i] = new Vector2(Mathf.Cos(a) * rr * p.elong, Mathf.Sin(a) * rr);
            p.inner[i] = p.apex + (p.poly[i] - p.apex) * top;
        }
        p.k = p.r * (.9f + (float)rng.NextDouble() * .3f); // apex height in px
        p.topN = new Vector3(.25f + (float)(rng.NextDouble() - .5) * .3f, (float)(rng.NextDouble() - .5) * .3f, 1).normalized;
        return p;
    }

    // ---------- sampling ----------

    static Vector2 Local(Piece p, Vector2 w)
    {
        var d = w - p.c; float cs = Mathf.Cos(-p.rot), sn = Mathf.Sin(-p.rot);
        return new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
    }
    static Vector3 World(Piece p, Vector3 n)
    {
        float cs = Mathf.Cos(p.rot), sn = Mathf.Sin(p.rot);
        return new Vector3(n.x * cs - n.y * sn, n.x * sn + n.y * cs, n.z).normalized;
    }
    static float Radius(Piece p, float theta)
    {
        float f = 1; for (int i = 2; i < 6; i++) f += p.amp[i] * Mathf.Cos(i * theta + p.ph[i]);
        return p.r * f;
    }

    static float SignedDistance(Piece p, Vector2 w)
    {
        var q = Local(p, w);
        if (p.kind == Kind.Crystal) return PolyDistance(p.poly, q);
        var e = new Vector2(q.x / p.elong, q.y);
        return (e.magnitude - Radius(p, Mathf.Atan2(e.y, e.x))) * Mathf.Lerp(1, p.elong, Mathf.Abs(e.x) / (e.magnitude + 1e-4f));
    }

    static float BlobHeight(Piece p, Vector2 w, out float d)
    {
        var q = Local(p, w); var e = new Vector2(q.x / p.elong, q.y);
        d = e.magnitude / Radius(p, Mathf.Atan2(e.y, e.x));
        if (d >= 1) return -1;
        float b = Mathf.Sqrt(1 - d * d);
        float lumps = (Fbm(w.x / (p.r * .8f), w.y / (p.r * .8f), p.seed & 1023) - .5f) * p.r * .12f * b;
        return p.k * p.r * Mathf.Pow(b, .75f) + lumps;
    }

    static bool Sample(Piece p, Vector2 w, out Hit hit)
    {
        hit = default; hit.p = p; hit.mat = p.mat;
        if (p.kind == Kind.Blob)
        {
            float h = BlobHeight(p, w, out float d); if (h < 0) return false;
            const float e = .6f;
            float hx = BlobHeight(p, w + Vector2.right * e, out _) - BlobHeight(p, w - Vector2.right * e, out _);
            float hy = BlobHeight(p, w + Vector2.up * e, out _) - BlobHeight(p, w - Vector2.up * e, out _);
            if (BlobHeight(p, w + Vector2.right * e, out _) < 0 || BlobHeight(p, w - Vector2.right * e, out _) < 0) hx = Mathf.Sign(hx) * 3 * e;
            if (BlobHeight(p, w + Vector2.up * e, out _) < 0 || BlobHeight(p, w - Vector2.up * e, out _) < 0) hy = Mathf.Sign(hy) * 3 * e;
            hit.n = new Vector3(-hx / (2 * e), -hy / (2 * e), 1).normalized;
            hit.height = h; hit.hn = h / (p.k * p.r); hit.d = d; hit.sd = SignedDistance(p, w); hit.edge = 99;
            return true;
        }
        if (p.kind == Kind.Facet)
        {
            var q = Local(p, w); var e = new Vector2(q.x / p.elong, q.y);
            float R = Radius(p, Mathf.Atan2(e.y, e.x)), d = e.magnitude / R; if (d >= 1) return false;
            var s = e / p.r; int best = 0; float d1 = 9, d2 = 9;
            for (int i = 0; i < p.seeds.Length; i++)
            {
                float dd = (p.seeds[i] - s).sqrMagnitude;
                if (dd < d1) { d2 = d1; d1 = dd; best = i; } else if (dd < d2) d2 = dd;
            }
            var a = p.seeds[best]; float edge = 99;
            for (int i = 0; i < p.seeds.Length; i++)
            {
                if (i == best) continue; var b = p.seeds[i]; var mid = (a + b) * .5f; var dir = (b - a).normalized;
                edge = Mathf.Min(edge, Vector2.Dot(mid - s, dir) * p.r);
            }
            var n = p.seedN[best];
            hit.n = World(p, new Vector3(n.x / p.elong, n.y, n.z));
            hit.height = p.k * p.r * Mathf.Sqrt(1 - d * d); hit.hn = Mathf.Sqrt(1 - d * d); hit.d = d;
            hit.sd = SignedDistance(p, w); hit.edge = edge;
            if (p.seedMat != null) hit.mat = p.seedMat[best];
            return true;
        }
        {
            var q = Local(p, w); int n = p.poly.Length;
            if (PolyDistance(p.poly, q) > 0) return false;
            for (int i = 0; i < n; i++)
            {
                var v0 = p.poly[i]; var v1 = p.poly[(i + 1) % n];
                if (!InTriangle(q, p.apex, v0, v1)) continue;
                Vector3 normal; float height;
                var u0 = p.inner[i]; var u1 = p.inner[(i + 1) % n];
                if (InTriangle(q, p.apex, u0, u1)) { normal = p.topN; height = p.k; }
                else
                {
                    var A = new Vector3(v0.x, v0.y, 0); var B = new Vector3(v1.x, v1.y, 0); var C = new Vector3(u0.x, u0.y, p.k);
                    normal = Vector3.Cross(B - A, C - A).normalized; if (normal.z < 0) normal = -normal;
                    height = -(normal.x * (q.x - A.x) + normal.y * (q.y - A.y)) / Mathf.Max(.05f, normal.z);
                }
                // Distance to the facet seams for a crisp, gem-like edge line.
                float seam = Mathf.Min(SegDist(q, v0, u0), SegDist(q, u0, u1));
                hit.n = World(p, normal); hit.height = Mathf.Max(0, height); hit.hn = Mathf.Clamp01(height / p.k);
                hit.sd = PolyDistance(p.poly, q); hit.edge = seam; hit.d = 1 - hit.hn;
                return true;
            }
            return false;
        }
    }

    // ---------- rendering ----------

    static Color[] Render(List<Piece> pieces)
    {
        int size = Canvas * SS; var hi = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var w = new Vector2((x + .5f) / SS, (y + .5f) / SS);
                Hit best = default; float top = float.MinValue; bool any = false;
                foreach (var p in pieces)
                    if (Sample(p, w, out var h) && p.z + h.height > top) { top = p.z + h.height; best = h; any = true; }
                float buried = any && best.p.mat != Mat.Pebble ? Cover(best, w) : 0;
                if (any && buried < .8f)
                {
                    var c = Shade(best, w, pieces);
                    c *= Mathf.Lerp(1, .5f, Smooth(.6f, .8f, buried)); // dark crevice where the soil swallows the ore
                    c.a = 1; hi[y * size + x] = c; continue;
                }
                if (any) { float s = .28f; hi[y * size + x] = new Color(.09f * s, .055f * s, .035f * s, s); continue; }
                // Contact shadow and soft cast shadow toward the lower right (light comes from the upper left).
                float near = float.MaxValue, cast = float.MaxValue;
                foreach (var p in pieces)
                {
                    float grow = p.mat == Mat.Pebble ? .4f : 1;
                    near = Mathf.Min(near, SignedDistance(p, w) / grow);
                    cast = Mathf.Min(cast, SignedDistance(p, w + new Vector2(-7, 9) * grow) / grow);
                }
                float a1 = .4f * Smooth(7, 0, near), a2 = .35f * Smooth(12, 0, cast);
                float a = 1 - (1 - a1) * (1 - a2);
                hi[y * size + x] = new Color(.09f * a, .055f * a, .035f * a, a); // premultiplied
            }
        var result = new Color[Canvas * Canvas];
        for (int y = 0; y < Canvas; y++)
            for (int x = 0; x < Canvas; x++)
            {
                Color sum = Color.clear;
                for (int j = 0; j < SS; j++) for (int i = 0; i < SS; i++)
                {
                    var c = hi[(y * SS + j) * size + x * SS + i];
                    sum += c.a >= 1 ? c : c; // shadow pixels are premultiplied, solid pixels have a=1
                }
                sum /= SS * SS;
                result[y * Canvas + x] = sum.a > 1e-4f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
            }
        return result;
    }

    // How deeply the surrounding soil covers this point: outer rim and lower side are buried, broken up by noise.
    static float Cover(Hit h, Vector2 w)
    {
        var p = h.p; float rel = (w - p.c).y / Mathf.Max(1, p.r);
        float rim = p.kind == Kind.Crystal ? Smooth(.78f, 1, 1 - h.hn) : Smooth(p.style == 1 ? .72f : .6f, 1, h.d);
        return Fbm(w.x / 16, w.y / 16, 313) * .55f + rim * .7f - rel * .12f - .05f;
    }

    static Color Shade(Hit h, Vector2 w, List<Piece> pieces)
    {
        var n = h.n; float ndl = Vector3.Dot(n, Light);
        float t = Mathf.Pow(Mathf.Clamp01(.5f + .5f * ndl), 1.25f);
        float ao = Mathf.Lerp(.62f, 1, Mathf.Clamp01(h.hn * 1.4f));
        // Pieces standing in front cast a soft shadow on lower ones.
        float occl = 0;
        foreach (var o in pieces)
        {
            if (o == h.p || o.mat == Mat.Pebble) continue;
            if (Sample(o, w + new Vector2(-6, 8), out var oh) && o.z + oh.height > h.p.z + h.height + 6) { occl = 1; break; }
        }
        t *= ao * (1 - .35f * occl);
        if (h.p.kind == Kind.Facet && h.p.style == 0) t = Mathf.Min(t, .86f);
        if (h.p.kind == Kind.Crystal) t = Mathf.Min(t, .92f);
        if (h.p.style == 1) t = Mathf.Min(Mathf.Pow(t, 1.15f) * 1.04f, .89f);
        if (h.p.style == 2) { float band = t * 4, f = band - Mathf.Floor(band); t = Mathf.Min((Mathf.Floor(band) + Smooth(.42f, .58f, f)) / 4, .93f); }
        Mat mat = h.mat; float spec = 0, gloss = 8, noiseAmt = .07f;
        if (mat == Mat.Weathered)
        {
            // Weathered copper: green crust collects in hollows and seams, raised faces stay worn and bright.
            float m = Fbm(w.x / 34, w.y / 34, 29) + (1 - h.hn) * .25f;
            mat = m > .6f ? Mat.Malachite : Mat.Copper;
        }
        if (h.p.veins && mat == Mat.Stone)
        {
            // Copper seams running through the rock, framed by a malachite rind and a dark groove.
            float v1 = Mathf.Abs(Fbm(w.x / 120, w.y / 120, 41) - .5f), v2 = Mathf.Abs(Fbm(w.x / 80, w.y / 80, 59) - .5f) * 1.4f;
            float vein = Mathf.Min(v1, v2);
            if (vein < .04f) mat = Mat.Copper; else if (vein < .056f) mat = Mat.Malachite;
            else if (vein < .07f) t *= .6f;
        }
        Color c;
        switch (mat)
        {
            case Mat.Copper:
                c = Ramp(t, new Color(.15f, .06f, .035f), new Color(.42f, .18f, .09f), new Color(.7f, .36f, .18f), new Color(.9f, .56f, .32f), new Color(1, .84f, .62f));
                if (h.p.kind == Kind.Blob && h.p.style == 0)
                {
                    float tarnish = Smooth(.52f, .72f, Fbm(w.x / 22, w.y / 22, 91));
                    c = Color.Lerp(c, c * new Color(.62f, .46f, .42f), tarnish * .4f);
                    if (h.hn < .45f && Fbm(w.x / 7, w.y / 7, 17) > .64f) c = Color.Lerp(c, new Color(.2f, .55f, .47f) * (.6f + t * .6f), .85f);
                }
                spec = h.p.style == 1 ? .5f : h.p.kind == Kind.Blob ? .8f : h.p.kind == Kind.Crystal ? 1f : .3f;
                gloss = h.p.style == 1 ? 45 : h.p.kind == Kind.Blob ? 60 : h.p.kind == Kind.Crystal ? 45 : 22; break;
            case Mat.Cuprite:
                c = Ramp(t, new Color(.16f, .02f, .03f), new Color(.42f, .06f, .07f), new Color(.7f, .15f, .12f), new Color(.92f, .4f, .3f), new Color(1, .78f, .66f));
                spec = 1.1f; gloss = 70; break;
            case Mat.Malachite:
                c = Ramp(t, new Color(.04f, .17f, .16f), new Color(.1f, .36f, .31f), new Color(.2f, .56f, .47f), new Color(.42f, .76f, .63f), new Color(.72f, .93f, .82f));
                float band = Mathf.Sin((w.x * .7f + w.y) / 5.5f + Fbm(w.x / 30, w.y / 30, 5) * 9);
                c *= 1 - .12f * Smooth(.3f, .9f, band); spec = .1f; gloss = 10; noiseAmt = .06f; break;
            case Mat.Stone:
                c = Ramp(t, new Color(.13f, .09f, .07f), new Color(.26f, .19f, .14f), new Color(.4f, .3f, .22f), new Color(.53f, .41f, .31f), new Color(.66f, .53f, .41f));
                spec = .05f; gloss = 10; noiseAmt = .12f; break;
            default:
                c = Ramp(t * .9f, new Color(.15f, .1f, .07f), new Color(.3f, .21f, .15f), new Color(.46f, .34f, .25f), new Color(.6f, .48f, .36f), new Color(.74f, .62f, .5f));
                spec = .04f; noiseAmt = .1f; break;
        }
        float s = Mathf.Pow(Mathf.Max(0, Vector3.Dot(n, Half)), gloss) * spec * (1 - .6f * occl);
        if (h.p.style == 2) s = Smooth(.35f, .45f, s) * .75f; // hard-edged cartoon highlight
        c += new Color(1, .9f, .76f) * s;
        if (mat == Mat.Copper || mat == Mat.Cuprite) c += new Color(1, .55f, .3f) * .08f * Mathf.Pow(1 - n.z, 2) * (ndl < 0 ? 1 : .3f); // warm bounce on the shadow side
        // Facet seams: bright where the facet faces the light, dark otherwise.
        if (h.p.style == 1 ? h.edge < 1.7f : h.edge < 1.3f) c *= ndl > .2f ? (h.p.style == 1 ? 1.28f : 1.16f) : (h.p.style == 1 ? .72f : .82f);
        // Dark silhouette line keeps the ore readable against busy stone.
        c *= Mathf.Lerp(.55f, 1, Smooth(-2.2f, -.4f, -h.sd) );
        c *= 1 + noiseAmt * (Fbm(w.x / 6, w.y / 6, 77) - .5f) * 2;
        return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1);
    }

    // ---------- preview ----------

    static Color[] Preview(Color[] ore, Color[] dirt, Color[] clay)
    {
        int W = Cell * 6, H = Cell * 3; var img = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            img[y * W + x] = (x < Cell * 3 ? dirt : clay)[(y % (Cell * 3)) * Cell * 3 + x % (Cell * 3)];
        // Cells: an L of three, a pair and singles on each stone type. Mirroring alternates like in-game variation.
        var cells = new (int x, int y, bool flip)[] { (0, 2, false), (1, 2, true), (1, 1, false), (2, 0, true), (0, 0, false),
                                                       (3, 0, true), (4, 0, false), (4, 1, true), (5, 2, false), (3, 2, true) };
        foreach (var cell in cells)
        {
            int ox = cell.x * Cell + Cell / 2 - Canvas / 2, oy = cell.y * Cell + Cell / 2 - Canvas / 2;
            for (int y = 0; y < Canvas; y++) for (int x = 0; x < Canvas; x++)
            {
                int tx = ox + x, ty = oy + y; if (tx < 0 || ty < 0 || tx >= W || ty >= H) continue;
                var c = ore[y * Canvas + (cell.flip ? Canvas - 1 - x : x)]; if (c.a <= 0) continue;
                var b = img[ty * W + tx]; img[ty * W + tx] = new Color(Mathf.Lerp(b.r, c.r, c.a), Mathf.Lerp(b.g, c.g, c.a), Mathf.Lerp(b.b, c.b, c.a), 1);
            }
        }
        var half = new Color[W / 2 * (H / 2)];
        for (int y = 0; y < H / 2; y++) for (int x = 0; x < W / 2; x++)
            half[y * (W / 2) + x] = (img[2 * y * W + 2 * x] + img[2 * y * W + 2 * x + 1] + img[(2 * y + 1) * W + 2 * x] + img[(2 * y + 1) * W + 2 * x + 1]) * .25f;
        return half;
    }

    // ---------- helpers ----------

    static Color[] Load(string path)
    {
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(path));
        if (t.width != Cell * 3 || t.height != Cell * 3) throw new Exception("Unexpected stone size " + path);
        var px = t.GetPixels(); UnityEngine.Object.DestroyImmediate(t); return px;
    }
    static void Save(string path, Color[] px, int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
    }
    static Color Ramp(float t, Color a, Color b, Color c, Color d, Color e)
    {
        t = Mathf.Clamp01(t);
        if (t < .3f) return Color.Lerp(a, b, t / .3f);
        if (t < .6f) return Color.Lerp(b, c, (t - .3f) / .3f);
        if (t < .85f) return Color.Lerp(c, d, (t - .6f) / .25f);
        return Color.Lerp(d, e, (t - .85f) / .15f);
    }
    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross(b - a, p - a), c2 = Cross(c - b, p - b), c3 = Cross(a - c, p - c);
        return (c1 >= 0 && c2 >= 0 && c3 >= 0) || (c1 <= 0 && c2 <= 0 && c3 <= 0);
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
        return (a + ab * t - p).magnitude;
    }
    static float PolyDistance(Vector2[] poly, Vector2 p)
    {
        float d = float.MaxValue; bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            d = Mathf.Min(d, SegDist(p, poly[j], poly[i]));
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        }
        return inside ? -d : d;
    }
    static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xffffff) / 16777215f;
        }
    }
    static float Noise(float x, float y, int s)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy, s), b = Hash(ix + 1, iy, s), c = Hash(ix, iy + 1, s), d = Hash(ix + 1, iy + 1, s);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }
    static float Fbm(float x, float y, int s)
    {
        float sum = 0, amp = .5f, norm = 0;
        for (int o = 0; o < 4; o++) { sum += Noise(x, y, s + o * 31) * amp; norm += amp; x *= 2.03f; y *= 2.03f; amp *= .5f; }
        return sum / norm;
    }
}
