using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Procedural painted-ore art (copper applied 18.10.2026, iron in progress). Install* methods install tiles and
// overwrite the installed sprites when re-run; PreviewSmallMedium renders the size comparison,
// Previews() renders the copper design rounds V/M/L; Main renders the current preview (iron Large schemes).
// Output: Design/OreVeins/<Topic> (544 px overlays, 418 px = one cell).
public static class PaintedOreArt
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
    // Copper (applied 18.10.2026): InstallLarge, InstallSmallMedium.
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

    // Target of InstallTiles; copper by default, switched by InstallIron.
    static string oreSpriteDir = SpriteDir, oreSpritePrefix = "Copper", oreTileDir = TileDir, oreTilePrefix = "Kupfer";

    static OreTile[] InstallTiles(string size, string suffix, int firstNumber, OreRichness richness, int[] seeds,
        Func<int, List<Piece>> build, string designDir, Block block, OreTile reference, List<string> report)
    {
        Directory.CreateDirectory(oreSpriteDir); Directory.CreateDirectory(designDir);
        var tiles = new OreTile[seeds.Length];
        for (int i = 0; i < seeds.Length; i++)
        {
            var pixels = Render(build(seeds[i]));
            string name = oreSpritePrefix + "_" + size + "_0" + (i + 1) + ".png", path = oreSpriteDir + "/" + name;
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
            string tilePath = oreTileDir + "/" + oreTilePrefix + "_" + (firstNumber + i).ToString("00") + "_" + suffix + ".asset";
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
        report.Add(block.displayName + " small/medium/rich = " + string.Join(", ", Array.ConvertAll(block.smallOre, t => t.name)) + " | " +
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

    // ---------- palettes: the painted-ore look per ore type (main body + accent facets) ----------

    sealed class Palette
    {
        public string name;
        public Color[] main, accent;
        public Color specTint = new Color(1, .9f, .76f), bounce = new Color(1, .55f, .3f);
        public float mainSpec = .5f, accentSpec = 1.1f, accentGloss = 70, accentThreshold = .66f, accentScale = 70;
        public float k = .95f, tilt = .4f, elong = .35f, rough = .12f; public int seedsPerRadius = 16;
        public float mainGloss = 45, seamBright = 1.28f, porosity, poreScale = 9, banding, bandWidth = 7;
        public bool pebbles = true; // loose brown host pebbles around the chunks
    }
    static Color C(float r, float g, float b) => new Color(r, g, b);

    static readonly Palette Copper = new Palette
    {
        name = "Kupfer",
        main = new[] { C(.15f, .06f, .035f), C(.42f, .18f, .09f), C(.7f, .36f, .18f), C(.9f, .56f, .32f), C(1, .84f, .62f) },
        accent = new[] { C(.16f, .02f, .03f), C(.42f, .06f, .07f), C(.7f, .15f, .12f), C(.92f, .4f, .3f), C(1, .78f, .66f) },
    };
    static Palette Active = Copper;

    // Iron: slightly more angular chunks than copper (more facets, stronger tilt), four colour schemes.
    static Palette Iron(string name) => new Palette { name = name, k = 1.05f, tilt = .48f, elong = .25f, rough = .14f, seedsPerRadius = 13 };
    static readonly Palette[] IronPalettes =
    {
        Set(Iron("E1-Stahl"), // cool blue-grey steel, darker blue-black facets
            new[] { C(.06f, .07f, .08f), C(.18f, .2f, .23f), C(.36f, .39f, .43f), C(.55f, .58f, .62f), C(.78f, .81f, .85f) },
            new[] { C(.03f, .03f, .04f), C(.1f, .11f, .13f), C(.2f, .22f, .25f), C(.36f, .38f, .42f), C(.62f, .65f, .7f) },
            C(.85f, .92f, 1), C(.4f, .5f, .65f), .45f, .7f, 60, .62f, 70),
        Set(Iron("E2-Haematit"), // dark gunmetal with blood-red hematite facets
            new[] { C(.06f, .06f, .07f), C(.18f, .18f, .2f), C(.34f, .34f, .37f), C(.53f, .53f, .56f), C(.78f, .78f, .8f) },
            new[] { C(.15f, .03f, .03f), C(.38f, .08f, .07f), C(.6f, .17f, .13f), C(.8f, .35f, .28f), C(.98f, .72f, .64f) },
            C(1, .95f, .92f), C(.6f, .25f, .2f), .55f, .9f, 55, .64f, 70),
        Set(Iron("E3-Rost"), // neutral grey metal with matte orange-brown rust crusts
            new[] { C(.08f, .08f, .08f), C(.22f, .22f, .22f), C(.42f, .41f, .4f), C(.61f, .6f, .58f), C(.82f, .81f, .79f) },
            new[] { C(.16f, .07f, .03f), C(.42f, .18f, .07f), C(.66f, .33f, .13f), C(.84f, .53f, .27f), C(.98f, .8f, .58f) },
            C(1, .96f, .9f), C(.55f, .35f, .2f), .5f, .15f, 12, .56f, 45),
        Set(Iron("E4-Magnetit"), // near-black glossy magnetite with blue-violet tarnish facets
            new[] { C(.03f, .03f, .04f), C(.1f, .1f, .12f), C(.21f, .21f, .25f), C(.38f, .39f, .45f), C(.68f, .7f, .78f) },
            new[] { C(.05f, .04f, .08f), C(.14f, .13f, .22f), C(.26f, .26f, .4f), C(.4f, .45f, .6f), C(.7f, .76f, .88f) },
            C(.9f, .95f, 1), C(.35f, .3f, .6f), .9f, 1f, 70, .62f, 60),
    };
    static Palette Set(Palette p, Color[] main, Color[] accent, Color spec, Color bounce, float mainSpec, float accentSpec, float accentGloss, float threshold, float scale)
    {
        p.main = main; p.accent = accent; p.specTint = spec; p.bounce = bounce; p.mainSpec = mainSpec;
        p.accentSpec = accentSpec; p.accentGloss = accentGloss; p.accentThreshold = threshold; p.accentScale = scale; return p;
    }

    const string IronOut = "Design/OreVeins/IronLarge";
    static readonly int[] IronSeeds = { 622, 655, 688, 731 };

    // Large preview: one row per palette (and per installed reference sprite), each on surface dirt, layer 1 and deep grey stone.
    public static string PreviewIronLarge() => PreviewLarge(IronPalettes, IronSeeds, IronOut, new string[0]);
    static string PreviewLarge(Palette[] palettes, int[] seeds, string outDir, string[] referenceSprites)
    {
        Directory.CreateDirectory(outDir + "/Sources");
        var hosts = new[] { Load("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png"), Load("Assets/GameObjects/Map/StoneTest/Layer1/ClayStone.png"),
            Load("Assets/GameObjects/Map/StoneTest/Layer3/DeepStone.png") };
        var report = new List<string>(); var previews = new List<Color[]>();
        Color[] Row(Color[] ore)
        {
            // 6 x 2 cells, two columns per host rock: a pair and a single on each.
            var cells = new List<(int x, int y, Color[] ore)>();
            for (int h = 0; h < 3; h++) { cells.Add((h * 2, 1, ore)); cells.Add((h * 2 + 1, 1, ore)); cells.Add((h * 2 + (h % 2), 0, ore)); }
            return ComposeHosts(cells, hosts, 2, 6, 2);
        }
        try
        {
            for (int v = 0; v < palettes.Length; v++)
            {
                Active = palettes[v];
                var ore = Render(LargeNests(seeds[v]));
                Save(outDir + "/Sources/" + Active.name + ".png", ore, Canvas, Canvas);
                var preview = Row(ore);
                Save(outDir + "/Preview-" + Active.name + ".png", preview, Cell * 3, Cell);
                previews.Add(preview); report.Add(Active.name);
            }
        }
        finally { Active = Copper; }
        foreach (var path in referenceSprites)
        {
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(path));
            previews.Add(Row(t.GetPixels())); UnityEngine.Object.DestroyImmediate(t); report.Add(Path.GetFileNameWithoutExtension(path));
        }
        int pw = Cell * 3, ph = Cell, rows = previews.Count; var overview = new Color[pw * ph * rows];
        for (int v = 0; v < rows; v++)
        {
            int oy = (rows - 1 - v) * ph;
            for (int y = 0; y < ph; y++) Array.Copy(previews[v], y * pw, overview, (oy + y) * pw, pw);
        }
        Save(outDir + "/Overview.png", overview, pw, ph * rows);
        return "Previews: " + string.Join(", ", report);
    }

    // ---------- gold and silver: two schemes each ----------

    const string GoldSilverOut = "Design/OreVeins/GoldSilverLarge";
    // Gold: softer, rounder nuggets (fewer, larger facets). Silver: crisper, more facets for sparkle.
    static Palette Gold(string name) => new Palette { name = name, k = .85f, tilt = .32f, elong = .4f, rough = .14f, seedsPerRadius = 18 };
    static Palette Silver(string name) => new Palette { name = name, k = 1f, tilt = .46f, elong = .3f, rough = .12f, seedsPerRadius = 12 };
    static readonly Palette[] GoldSilverPalettes =
    {
        Set(Gold("G1-Gold-in-Quarz"), // warm yellow gold with milky quartz facets
            new[] { C(.2f, .11f, .02f), C(.52f, .33f, .06f), C(.82f, .6f, .16f), C(.96f, .8f, .36f), C(1, .95f, .7f) },
            new[] { C(.32f, .3f, .28f), C(.58f, .56f, .53f), C(.78f, .77f, .74f), C(.9f, .89f, .87f), C(1, 1, .98f) },
            C(1, .95f, .75f), C(1, .75f, .3f), .7f, .25f, 20, .6f, 60),
        Set(Gold("G2-Gold-dunkel"), // rich deep gold with dark iron-oxide facets
            new[] { C(.18f, .1f, .02f), C(.48f, .3f, .05f), C(.78f, .55f, .12f), C(.94f, .76f, .3f), C(1, .93f, .62f) },
            new[] { C(.06f, .04f, .03f), C(.16f, .11f, .07f), C(.3f, .21f, .13f), C(.46f, .34f, .22f), C(.72f, .6f, .45f) },
            C(1, .93f, .7f), C(1, .7f, .25f), .75f, .3f, 18, .64f, 70),
        Set(Silver("S1-Silber-poliert"), // bright, cool-neutral polished silver with black tarnish facets
            new[] { C(.12f, .12f, .13f), C(.36f, .37f, .39f), C(.64f, .65f, .68f), C(.84f, .85f, .87f), C(1, 1, 1) },
            new[] { C(.05f, .05f, .06f), C(.14f, .14f, .16f), C(.27f, .27f, .3f), C(.44f, .44f, .48f), C(.72f, .72f, .76f) },
            C(1, 1, 1), C(.7f, .72f, .8f), .9f, .5f, 40, .71f, 60),
        Set(Silver("S2-Silber-angelaufen"), // warm silver with golden-violet tarnish facets
            new[] { C(.12f, .11f, .11f), C(.34f, .33f, .32f), C(.58f, .57f, .55f), C(.76f, .75f, .73f), C(.93f, .92f, .9f) },
            new[] { C(.12f, .06f, .1f), C(.32f, .18f, .26f), C(.56f, .38f, .36f), C(.78f, .62f, .44f), C(.96f, .88f, .7f) },
            C(1, .98f, .95f), C(.75f, .6f, .65f), .8f, .7f, 45, .58f, 60),
    };

    // Gold/silver preview (two schemes each): PreviewGoldSilverLarge.
    // G2 follow-up: rich deep gold without the dark facets; the accent facets stay gold.
    static readonly Palette[] GoldG2Palettes =
    {
        Set(Gold("G2a-Gold-satt"), // accent: deeper, warmer gold for subtle facet contrast
            new[] { C(.18f, .1f, .02f), C(.48f, .3f, .05f), C(.78f, .55f, .12f), C(.94f, .76f, .3f), C(1, .93f, .62f) },
            new[] { C(.16f, .07f, .01f), C(.42f, .22f, .03f), C(.7f, .43f, .08f), C(.88f, .64f, .22f), C(1, .86f, .52f) },
            C(1, .93f, .7f), C(1, .7f, .25f), .75f, .8f, 50, .62f, 70),
        Set(Gold("G2b-Gold-hell"), // accent: pale yellow electrum facets that catch the light
            new[] { C(.18f, .1f, .02f), C(.48f, .3f, .05f), C(.78f, .55f, .12f), C(.94f, .76f, .3f), C(1, .93f, .62f) },
            new[] { C(.24f, .18f, .05f), C(.56f, .46f, .16f), C(.84f, .74f, .36f), C(.96f, .9f, .58f), C(1, .99f, .84f) },
            C(1, .95f, .78f), C(1, .78f, .35f), .75f, .9f, 55, .62f, 70),
    };
    // Gold G2 follow-up preview: PreviewGoldG2.
    public static string PreviewGoldG2() => PreviewLarge(GoldG2Palettes, new[] { 1012, 1012 }, GoldSilverOut + "/G2", new string[0]);

    // ---------- coal: matte, porous, blockier chunks (four schemes) ----------

    const string CoalOut = "Design/OreVeins/CoalLarge";
    static Palette Coal(string name) => new Palette { name = name, k = 1f, tilt = .55f, elong = .5f, rough = .18f, seedsPerRadius = 10,
        mainGloss = 14, seamBright = 1.12f, specTint = new Color(.85f, .88f, .95f), bounce = new Color(.25f, .25f, .3f),
        pebbles = false }; // brown pebbles look wrong next to black coal on the dark host rocks
    static Palette Tune(Palette p, float porosity, float poreScale, float banding, float bandWidth, float k, float rough, int seeds, float seam)
    { p.porosity = porosity; p.poreScale = poreScale; p.banding = banding; p.bandWidth = bandWidth; p.k = k; p.rough = rough; p.seedsPerRadius = seeds; p.seamBright = seam; return p; }
    static readonly Palette[] CoalPalettes =
    {
        Tune(Set(Coal("K1-Kohle-matt"), // dull charcoal-black, fine pores, dusty grey facets
            new[] { C(.03f, .03f, .03f), C(.08f, .08f, .085f), C(.15f, .15f, .16f), C(.25f, .25f, .27f), C(.4f, .4f, .43f) },
            new[] { C(.05f, .05f, .05f), C(.12f, .12f, .12f), C(.21f, .21f, .21f), C(.32f, .32f, .32f), C(.47f, .47f, .46f) },
            C(.85f, .88f, .95f), C(.25f, .25f, .3f), .08f, .05f, 10, .6f, 50), .55f, 7, 0, 7, 1f, .18f, 10, 1.12f),
        Tune(Set(Coal("K2-Steinkohle-geschichtet"), // stratified chunks: matte layers alternate with faint glassy bands
            new[] { C(.025f, .025f, .03f), C(.07f, .07f, .08f), C(.14f, .14f, .16f), C(.24f, .24f, .27f), C(.42f, .43f, .47f) },
            new[] { C(.02f, .02f, .025f), C(.06f, .06f, .07f), C(.13f, .13f, .15f), C(.25f, .26f, .29f), C(.5f, .52f, .56f) },
            C(.85f, .88f, .95f), C(.25f, .25f, .3f), .12f, .1f, 16, .64f, 60), .35f, 8, .45f, 6, 1f, .16f, 11, 1.1f),
        Tune(Set(Coal("K3-Anthrazit"), // near-black anthracite, few pores, subtle cool sheen on the facets
            new[] { C(.02f, .02f, .025f), C(.06f, .06f, .075f), C(.13f, .135f, .16f), C(.24f, .25f, .3f), C(.46f, .48f, .55f) },
            new[] { C(.02f, .02f, .03f), C(.06f, .065f, .085f), C(.13f, .14f, .19f), C(.25f, .28f, .36f), C(.5f, .55f, .66f) },
            C(.8f, .86f, 1), C(.2f, .22f, .32f), .3f, .35f, 28, .62f, 60), .15f, 10, 0, 7, 1.05f, .14f, 10, 1.18f),
        Tune(Set(Coal("K4-Braunkohle-poroes"), // brownish-black, coarse spongy pores, crumbly rounded chunks
            new[] { C(.035f, .028f, .022f), C(.1f, .075f, .055f), C(.2f, .15f, .11f), C(.33f, .26f, .19f), C(.5f, .42f, .33f) },
            new[] { C(.05f, .035f, .025f), C(.13f, .09f, .06f), C(.23f, .16f, .1f), C(.35f, .26f, .17f), C(.5f, .4f, .29f) },
            C(.9f, .85f, .8f), C(.3f, .22f, .15f), .04f, .04f, 8, .66f, 70), .85f, 9, 0, 7, .88f, .24f, 13, 1.12f),
    };
    // Coal preview (four schemes): PreviewCoalLarge.
    public static string PreviewCoalLarge() => PreviewLarge(CoalPalettes, new[] { 1401, 1412, 1423, 1434 }, CoalOut, new[]
    {
        "Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Iron/Iron_Large_01.png",
    });

    public static string PreviewGoldSilverLarge() => PreviewLarge(GoldSilverPalettes, new[] { 1001, 1012, 1023, 1034 }, GoldSilverOut, new[]
    {
        "Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Copper/Copper_Large_01.png",
        "Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Iron/Iron_Large_01.png",
    });

    static Color[] ComposeHosts(List<(int x, int y, Color[] ore)> cells, Color[][] hosts, int colsPerHost, int cols, int rows)
    {
        int W = Cell * cols, H = Cell * rows, T = Cell * 3; var img = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            img[y * W + x] = hosts[Mathf.Min(hosts.Length - 1, x / (Cell * colsPerHost))][(y % T) * T + x % T];
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

    // ---------- iron: scheme E1 "Stahl" (applied 18.10.2026) ----------

    static readonly int[] IronLargeSeeds = { 622, 744, 766 }, IronSmallSeeds = { 911, 922 }, IronMediumSeeds = { 933, 944 };

    // Iron install (applied 18.10.2026).
    public static string InstallIron() => InstallOre(BlockType.IronOre, "Iron", "Eisen", Array.Find(IronPalettes, p => p.name == "E1-Stahl"),
        IronLargeSeeds, IronSmallSeeds, IronMediumSeeds, IronOut + "/Installed");

    // Silver install (applied 18.10.2026): scheme S1, Large seed 1023 is the approved preview.
    static readonly int[] SilverLargeSeeds = { 1023, 1066, 1077 }, SilverSmallSeeds = { 1111, 1122 }, SilverMediumSeeds = { 1133, 1144 };
    // Silver install: InstallSilver.
    public static string InstallSilver() => InstallOre(BlockType.SilverOre, "Silver", "Silber", Array.Find(GoldSilverPalettes, p => p.name == "S1-Silber-poliert"),
        SilverLargeSeeds, SilverSmallSeeds, SilverMediumSeeds, GoldSilverOut + "/Installed");

    // Gold install (applied 18.10.2026): scheme G2b, Large seed 1012 is the approved preview.
    static readonly int[] GoldLargeSeeds = { 1012, 1213, 1224 }, GoldSmallSeeds = { 1311, 1322 }, GoldMediumSeeds = { 1333, 1344 };
    // Gold install: InstallGold.
    public static string InstallGold() => InstallOre(BlockType.GoldOre, "Gold", "Gold", Array.Find(GoldG2Palettes, p => p.name == "G2b-Gold-hell"),
        GoldLargeSeeds, GoldSmallSeeds, GoldMediumSeeds, GoldSilverOut + "/Installed");

    // Coal install (applied 18.10.2026): scheme K2 without pebbles, Large seed 1412 is the approved preview.
    static readonly int[] CoalLargeSeeds = { 1412, 1515, 1526 }, CoalSmallSeeds = { 1611, 1622 }, CoalMediumSeeds = { 1633, 1644 };
    public static string Main() => InstallCoal();
    public static string InstallCoal() => InstallOre(BlockType.Coal, "Coal", "Kohle", Array.Find(CoalPalettes, p => p.name == "K2-Steinkohle-geschichtet"),
        CoalLargeSeeds, CoalSmallSeeds, CoalMediumSeeds, CoalOut + "/Installed");

    // Folder = ore folder under Blocks/Ores and sprite prefix (e.g. "Iron"), tilePrefix = German tile name (e.g. "Eisen").
    static string InstallOre(BlockType id, string folder, string tilePrefix, Palette palette, int[] largeSeeds, int[] smallSeeds, int[] mediumSeeds, string designDir)
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        if (palette == null) throw new Exception("Palette missing for " + folder);
        var reference = UnityEditor.AssetDatabase.LoadAssetAtPath<OreTile>("Assets/GameObjects/Map/Blocks/Ores/" + folder + "/" + tilePrefix + "_05_rich.asset");
        var block = reference ? reference.block : null;
        if (!block || block.id != id) throw new Exception(id + " block not found via " + tilePrefix + "_05_rich");
        var report = new List<string>();
        try
        {
            Active = palette;
            oreSpriteDir = "Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/" + folder; oreSpritePrefix = folder;
            oreTileDir = "Assets/GameObjects/Map/Blocks/Ores/" + folder; oreTilePrefix = tilePrefix;
            block.richOre = InstallTiles("Large", "large", 8, OreRichness.Rich, largeSeeds, LargeNests, designDir, block, reference, report);
            block.smallOre = InstallTiles("Small", "small", 11, OreRichness.Small, smallSeeds, SmallNest, designDir, block, reference, report);
            block.mediumOre = InstallTiles("Medium", "medium", 13, OreRichness.Medium, mediumSeeds, MediumNest, designDir, block, reference, report);
        }
        finally { Active = Copper; oreSpriteDir = SpriteDir; oreSpritePrefix = "Copper"; oreTileDir = TileDir; oreTilePrefix = "Kupfer"; }
        return Finish(block, report);
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
        => Facets(Outline(new Piece { kind = Kind.Facet, mat = Mat.Copper, style = 1, c = c, r = r, z = z, k = Active.k, rot = (float)rng.NextDouble() * 6.28f, elong = 1 + (float)rng.NextDouble() * Active.elong, seed = rng.Next() }, rng, Active.rough), rng, 3 + (int)(r / seedsPerRadius), Active.tilt);

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
        var p = GemNugget(rng, c, r, z, Active.seedsPerRadius);
        p.seedMat = new Mat[p.seeds.Length];
        for (int s = 0; s < p.seeds.Length; s++)
        {
            var wpos = p.c + p.seeds[s] * p.r;
            p.seedMat[s] = Fbm(wpos.x / Active.accentScale, wpos.y / Active.accentScale, 23) > Active.accentThreshold || (r < 26 && index % 3 == 1) ? Mat.Cuprite : Mat.Copper;
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
        if (!Active.pebbles) return;
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
                c = Ramp(t, Active.main[0], Active.main[1], Active.main[2], Active.main[3], Active.main[4]);
                if (h.p.kind == Kind.Blob && h.p.style == 0)
                {
                    float tarnish = Smooth(.52f, .72f, Fbm(w.x / 22, w.y / 22, 91));
                    c = Color.Lerp(c, c * new Color(.62f, .46f, .42f), tarnish * .4f);
                    if (h.hn < .45f && Fbm(w.x / 7, w.y / 7, 17) > .64f) c = Color.Lerp(c, new Color(.2f, .55f, .47f) * (.6f + t * .6f), .85f);
                }
                spec = h.p.style == 1 ? Active.mainSpec : h.p.kind == Kind.Blob ? .8f : h.p.kind == Kind.Crystal ? 1f : .3f;
                gloss = h.p.style == 1 ? Active.mainGloss : h.p.kind == Kind.Blob ? 60 : h.p.kind == Kind.Crystal ? 45 : 22; break;
            case Mat.Cuprite:
                c = Ramp(t, Active.accent[0], Active.accent[1], Active.accent[2], Active.accent[3], Active.accent[4]);
                spec = Active.accentSpec; gloss = Active.accentGloss; break;
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
        // Coal-like surfaces: pores (pits with a lit lower rim) and strata bands across each chunk; both dull the shine.
        if ((mat == Mat.Copper || mat == Mat.Cuprite) && Active.porosity > 0)
        {
            float ps = Active.poreScale;
            float pit = Pores(w, ps, Active.porosity);
            float lit = Pores(w + new Vector2(ps * .12f, -ps * .12f), ps, Active.porosity);
            c *= 1 - .7f * pit; c += new Color(.09f, .09f, .095f) * Mathf.Max(0, lit - pit) * (ndl > 0 ? 1 : .4f);
            spec *= 1 - pit;
        }
        if ((mat == Mat.Copper || mat == Mat.Cuprite) && Active.banding > 0)
        {
            var q = Local(h.p, w);
            float band = Mathf.Sin(q.y / Active.bandWidth + Fbm(w.x / 40, w.y / 40, 211) * 4);
            float shiny = Smooth(.55f, .85f, band);
            c *= 1 - Active.banding * Smooth(-.2f, -.7f, band);
            spec *= Mathf.Lerp(.3f, 1.6f, shiny);
        }
        float s = Mathf.Pow(Mathf.Max(0, Vector3.Dot(n, Half)), gloss) * spec * (1 - .6f * occl);
        if (h.p.style == 2) s = Smooth(.35f, .45f, s) * .75f; // hard-edged cartoon highlight
        c += Active.specTint * s;
        if (mat == Mat.Copper || mat == Mat.Cuprite) c += Active.bounce * .08f * Mathf.Pow(1 - n.z, 2) * (ndl < 0 ? 1 : .3f); // warm bounce on the shadow side
        // Facet seams: bright where the facet faces the light, dark otherwise.
        if (h.p.style == 1 ? h.edge < 1.7f : h.edge < 1.3f) c *= ndl > .2f ? (h.p.style == 1 ? Active.seamBright : 1.16f) : (h.p.style == 1 ? .72f : .82f);
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
    // Round pores: at most one per grid cell (density = share of cells with a pore), soft edge. Returns 0..1.
    static float Pores(Vector2 w, float size, float density)
    {
        int cx = Mathf.FloorToInt(w.x / size), cy = Mathf.FloorToInt(w.y / size); float pit = 0;
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
        {
            int x = cx + i, y = cy + j;
            if (Hash(x, y, 501) > density) continue;
            var centre = new Vector2((x + Hash(x, y, 502)) * size, (y + Hash(x, y, 503)) * size);
            float radius = size * (.12f + .22f * Hash(x, y, 504));
            pit = Mathf.Max(pit, Smooth(radius, radius * .7f, (w - centre).magnitude));
        }
        return pit;
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
