using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Run through the connected Editor pipeline in Edit Mode. Existing GUIDs are retained.
public static class InstallExoticContent
{
    const string Root = "Assets/Resources/Exotics/";
    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Install exotics in Edit Mode.");
        Directory.CreateDirectory(Root); Directory.CreateDirectory("Assets/Resources/WorkbenchRecipes"); AssetDatabase.Refresh();
        Sprite ironIcon = Draw("IronLadder", DrawLadder), lampIcon = Draw("LavaLamp", DrawLamp), hammerIcon = Draw("PercussionHammer", DrawHammer);
        Sprite ladderTileSprite = Draw("IronLadderSegment", DrawLadderSegment);
        Draw("BlueprintCache", DrawBlueprint); Draw("Boulder", DrawBoulder); Draw("LavaGlow", DrawGlow);
        var ironLadder = MakeItem("IronLadder", Item.IronLadder, "Eisenleiter", ItemCategory.Tool, ironIcon, .4f, 12);
        var lavaLamp = MakeItem("LavaLamp", Item.LavaLamp, "Lavalampe", ItemCategory.Tool, lampIcon, 1.5f, 110);
        var hammer = MakeItem("PercussionHammer", Item.PercussionHammer, "Spitzhammer", ItemCategory.Powerup, hammerIcon, 0, 0);
        MakeRecipe("IronLadder", ironLadder, "Eisenleitern", 5, 2, CraftingRecipe.RecipeCategory.Building,
            Cost(Item.Iron, 3));
        MakeRecipe("LavaLamp", lavaLamp, "Lavalampen", 15, 1, CraftingRecipe.RecipeCategory.Tools,
            Cost(Item.Iron, 4), Cost(Item.Coal, 8), Cost(Item.OrangeGarnet, 1));
        MakeRecipe("PercussionHammer", hammer, "Spitzhämmer", 35, 1, CraftingRecipe.RecipeCategory.Tools,
            Cost(Item.Steel, 8), Cost(Item.Copper, 4), Cost(Item.Rope, 2));
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(Root + "IronLadderTile.asset");
        if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, Root + "IronLadderTile.asset"); }
        tile.sprite = ladderTileSprite; tile.colliderType = Tile.ColliderType.None; tile.color = Color.white;
        var sourceTile = AssetDatabase.LoadAssetAtPath<Tile>("Assets/GameObjects/Map/Ladders/LadderSegment.asset");
        if (sourceTile) tile.transform = sourceTile.transform;
        EditorUtility.SetDirty(tile);
        var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/Ladders/LadderLit.mat");
        if (sourceMaterial)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "WorldLit.mat");
            if (!material) { material = new Material(sourceMaterial); AssetDatabase.CreateAsset(material, Root + "WorldLit.mat"); }
            else material.CopyPropertiesFromMaterial(sourceMaterial);
            EditorUtility.SetDirty(material);
        }
        var catalog = AssetDatabase.LoadAssetAtPath<CraftingCatalog>("Assets/Resources/CraftingCatalog.asset");
        if (!catalog) { catalog = ScriptableObject.CreateInstance<CraftingCatalog>(); AssetDatabase.CreateAsset(catalog, "Assets/Resources/CraftingCatalog.asset"); }
        catalog.recipes = AssetDatabase.FindAssets("t:CraftingRecipe", new[] { "Assets" })
            .Select(g => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(g))).Where(r => r && r.output)
            .OrderBy(r => r.metaUnlockLevel).ThenBy(r => r.output.displayName).ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets(); ItemCatalogBuilder.Refresh(); TiefenhallSetup.UpdateCatalog(); ExoticCatalog.ResetCache();
        return "Installed 3 exotic items/recipes, iron ladder tile, world art and complete crafting catalog (" + catalog.recipes.Length + " recipes).";
    }
    static CraftingIngredient Cost(Item id, int amount)
    {
        var item = AssetDatabase.FindAssets("t:ItemSO", new[] { "Assets/GameObjects/Items" })
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(i => i && i.item == id);
        if (!item) throw new InvalidOperationException("Missing ingredient " + id);
        return new CraftingIngredient(item, amount);
    }
    static ItemSO MakeItem(string file, Item id, string name, ItemCategory category, Sprite icon, float weight, int worth)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(Root + file + ".asset");
        if (!item) { item = ScriptableObject.CreateInstance<ItemSO>(); AssetDatabase.CreateAsset(item, Root + file + ".asset"); }
        item.item = id; item.displayName = name; item.category = category; item.icon = icon;
        item.themeColor = ExoticDesign.Cyan; item.weight = weight; item.worth = worth;
        EditorUtility.SetDirty(item); return item;
    }
    static void MakeRecipe(string file, ItemSO output, string plural, int level, int count, CraftingRecipe.RecipeCategory category, params CraftingIngredient[] costs)
    {
        string path = "Assets/Resources/WorkbenchRecipes/Exotic" + file + ".asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path);
        if (!recipe) { recipe = ScriptableObject.CreateInstance<CraftingRecipe>(); AssetDatabase.CreateAsset(recipe, path); }
        recipe.exotic = true; recipe.exoticId = file; recipe.metaUnlockLevel = level; recipe.output = output;
        recipe.outputAmount = count; recipe.pluralName = plural; recipe.ingredients = costs; recipe.category = category;
        recipe.shopPrice = 0; EditorUtility.SetDirty(recipe);
    }
    const int N = 256;
    static Color32[] pixels;
    static readonly Color Ink = new Color32(22, 28, 34, 255), Metal = new Color32(132, 158, 171, 255), Edge = new Color32(216, 232, 228, 255);
    static Sprite Draw(string name, Action draw)
    {
        string painted = Root + name + "-Painted.png";
        if (File.Exists(painted))
        {
            AssetDatabase.ImportAsset(painted, ImportAssetOptions.ForceSynchronousImport);
            var paintedImporter = (TextureImporter)AssetImporter.GetAtPath(painted);
            paintedImporter.GetSourceTextureWidthAndHeight(out int width, out int height);
            paintedImporter.textureType = TextureImporterType.Sprite;
            paintedImporter.spriteImportMode = SpriteImportMode.Single;
            paintedImporter.spritePixelsPerUnit = Mathf.Max(width, height) / 2f;
            paintedImporter.alphaIsTransparency = true; paintedImporter.mipmapEnabled = false;
            paintedImporter.textureCompression = TextureImporterCompression.Uncompressed;
            paintedImporter.filterMode = FilterMode.Bilinear;
            var paintedSettings = new TextureImporterSettings(); paintedImporter.ReadTextureSettings(paintedSettings);
            paintedSettings.spriteMeshType = SpriteMeshType.FullRect;
            paintedImporter.SetTextureSettings(paintedSettings); paintedImporter.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(painted);
        }
        string path = Root + name + ".png";
        // Preserve approved replacement artwork on every subsequent installation.
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing) return existing;
        pixels = new Color32[N * N]; draw();
        var texture = new Texture2D(N, N, TextureFormat.RGBA32, false); texture.SetPixels32(pixels); texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = name == "IronLadderSegment" ? 512 : 128;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(.5f, .5f); importer.SetTextureSettings(settings); importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static void Pixel(int x, int y, Color color) { if (x >= 0 && x < N && y >= 0 && y < N) pixels[y * N + x] = color; }
    static void Rect(int x, int y, int w, int h, Color color) { for (int a = x; a < x + w; a++) for (int b = y; b < y + h; b++) Pixel(a, b, color); }
    static void Ellipse(float x, float y, float rx, float ry, Color color)
    {
        for (int a = Mathf.FloorToInt(x - rx); a <= x + rx; a++) for (int b = Mathf.FloorToInt(y - ry); b <= y + ry; b++)
            if ((a - x) * (a - x) / (rx * rx) + (b - y) * (b - y) / (ry * ry) <= 1) Pixel(a, b, color);
    }
    static void Line(Vector2 a, Vector2 b, float thickness, Color color)
    {
        float length = Vector2.Distance(a, b); for (float p = 0; p <= length; p += .5f) { Vector2 v = Vector2.Lerp(a, b, p / Mathf.Max(1, length)); Ellipse(v.x, v.y, thickness, thickness, color); }
    }
    static void Polygon(Color color, params Vector2[] points)
    {
        for (int x = 0; x < N; x++) for (int y = 0; y < N; y++)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                if ((points[i].y > y) != (points[j].y > y) && x < (points[j].x - points[i].x) * (y - points[i].y) / (points[j].y - points[i].y) + points[i].x) inside = !inside;
            if (inside) Pixel(x, y, color);
        }
    }
    static void DrawLadder()
    {
        for (int side = 0; side < 2; side++)
        {
            int x = 57 + side * 122; Rect(x, 15, 23, 226, Ink); Rect(x + 4, 19, 15, 218, Metal); Rect(x + 5, 20, 4, 216, Edge);
        }
        for (int y = 38; y < 232; y += 40)
        {
            Rect(68, y, 122, 16, Ink); Rect(71, y + 4, 116, 9, Metal); Rect(71, y + 10, 116, 3, Edge);
            Ellipse(68, y + 8, 3, 3, Edge); Ellipse(190, y + 8, 3, 3, Edge);
        }
    }
    static void DrawLadderSegment()
    {
        for (int side = 0; side < 2; side++) { int x = 48 + side * 140; Rect(x, 0, 18, 256, Ink); Rect(x + 3, 0, 12, 256, Metal); Rect(x + 4, 0, 3, 256, Edge); }
        for (int y = 14; y < 256; y += 64) { Rect(54, y, 145, 13, Ink); Rect(57, y + 4, 139, 6, Metal); Rect(57, y + 9, 139, 2, Edge); }
    }
    static void DrawLamp()
    {
        Color brass = new Color32(153, 118, 69, 255), gold = new Color32(225, 188, 100, 255);
        Ellipse(128, 122, 65, 96, Ink); Ellipse(128, 130, 55, 82, new Color32(73, 42, 27, 255));
        Ellipse(128, 123, 43, 72, new Color32(233, 70, 24, 255)); Ellipse(128, 142, 31, 47, new Color32(255, 146, 38, 255));
        Ellipse(131, 164, 18, 22, new Color32(255, 219, 106, 255)); Ellipse(116, 85, 25, 20, new Color32(255, 177, 53, 255));
        Line(new Vector2(82, 70), new Vector2(79, 183), 5, Metal); Line(new Vector2(179, 68), new Vector2(179, 183), 5, Metal);
        Rect(66, 38, 124, 22, Ink); Rect(70, 42, 116, 15, brass); Rect(73, 53, 110, 4, gold);
        Polygon(Ink, new Vector2(65, 191), new Vector2(88, 219), new Vector2(167, 219), new Vector2(191, 191));
        Polygon(brass, new Vector2(72, 195), new Vector2(91, 214), new Vector2(164, 214), new Vector2(184, 195));
        Line(new Vector2(112, 221), new Vector2(112, 235), 5, brass); Line(new Vector2(144, 221), new Vector2(144, 235), 5, brass);
        Line(new Vector2(112, 235), new Vector2(144, 235), 5, gold); Rect(93, 27, 70, 13, brass);
        Line(new Vector2(94, 168), new Vector2(101, 191), 2, new Color(1, 1, .8f, .7f));
    }
    static void DrawHammer()
    {
        Line(new Vector2(75, 31), new Vector2(160, 184), 17, Ink); Line(new Vector2(75, 31), new Vector2(160, 184), 11, new Color32(101, 65, 40, 255));
        Line(new Vector2(73, 35), new Vector2(153, 181), 3, new Color32(197, 143, 80, 255));
        Polygon(Ink, new Vector2(87, 193), new Vector2(113, 230), new Vector2(188, 214), new Vector2(232, 157), new Vector2(171, 178), new Vector2(107, 165));
        Polygon(Metal, new Vector2(96, 192), new Vector2(116, 222), new Vector2(184, 207), new Vector2(215, 170), new Vector2(170, 187), new Vector2(111, 174));
        Line(new Vector2(118, 218), new Vector2(180, 205), 3, Edge); Rect(114, 186, 17, 21, new Color32(83, 107, 119, 255));
        for (int y = 48; y < 107; y += 15) Line(new Vector2(70 + (y - 35) * .55f, y), new Vector2(84 + (y - 35) * .55f, y - 7), 3, Ink);
        Ellipse(145, 190, 7, 7, ExoticDesign.Cyan);
    }
    static void DrawBlueprint()
    {
        Polygon(Ink, new Vector2(37, 45), new Vector2(211, 64), new Vector2(215, 201), new Vector2(40, 220));
        Rect(44, 51, 166, 156, ExoticDesign.Navy);
        for (int x = 60; x < 210; x += 25) Rect(x, 55, 1, 148, new Color32(32, 71, 84, 255));
        for (int y = 66; y < 202; y += 25) Rect(50, y, 156, 1, new Color32(32, 71, 84, 255));
        Line(new Vector2(49, 59), new Vector2(49, 201), 3, ExoticDesign.Cyan); Line(new Vector2(204, 59), new Vector2(204, 201), 3, ExoticDesign.Cyan);
        Polygon(ExoticDesign.Cyan, new Vector2(128, 184), new Vector2(164, 138), new Vector2(151, 94), new Vector2(127, 69), new Vector2(100, 96), new Vector2(91, 137));
        Polygon(new Color32(35, 136, 154, 255), new Vector2(128, 184), new Vector2(130, 123), new Vector2(127, 69), new Vector2(151, 94), new Vector2(164, 138));
        Line(new Vector2(127, 178), new Vector2(128, 81), 2, Edge);
    }
    static void DrawBoulder()
    {
        Polygon(Ink, new Vector2(7, 39), new Vector2(12, 148), new Vector2(55, 224), new Vector2(147, 242), new Vector2(230, 197), new Vector2(248, 73), new Vector2(217, 19), new Vector2(77, 11));
        Polygon(new Color32(78, 83, 89, 255), new Vector2(17, 44), new Vector2(21, 145), new Vector2(61, 214), new Vector2(146, 231), new Vector2(220, 190), new Vector2(237, 78), new Vector2(210, 29), new Vector2(78, 22));
        Polygon(new Color32(115, 120, 122, 255), new Vector2(21, 145), new Vector2(61, 214), new Vector2(146, 231), new Vector2(171, 167), new Vector2(106, 118));
        Polygon(new Color32(52, 58, 67, 255), new Vector2(106, 118), new Vector2(171, 167), new Vector2(237, 78), new Vector2(210, 29), new Vector2(78, 22));
        Line(new Vector2(71, 214), new Vector2(84, 163), 4, Ink); Line(new Vector2(84, 163), new Vector2(125, 145), 4, Ink);
        Line(new Vector2(208, 180), new Vector2(184, 138), 3, Ink); Line(new Vector2(50, 73), new Vector2(79, 64), 3, new Color32(153, 151, 140, 255));
        var random = new System.Random(937);
        for (int i = 0; i < 90; i++) { int x = random.Next(35, 221), y = random.Next(50, 204); if (pixels[y * N + x].a > 0) Ellipse(x, y, 1.5f, 1, new Color32(105, 109, 109, 255)); }
    }
    static void DrawGlow()
    {
        for (int x = 0; x < N; x++) for (int y = 0; y < N; y++)
        {
            float d = Mathf.Sqrt(Mathf.Pow((x - 128) / 90f, 2) + Mathf.Pow((y - 128) / 120f, 2));
            Pixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - d), 2)));
        }
    }
}
