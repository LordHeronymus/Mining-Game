using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WorkbenchUiSetup
{
    const string Ui = "Assets/UI/Workbench/";
    const string Icons = "Assets/GameObjects/Items/Sprites/Crafting/";
    const string RecipeFolder = "Assets/GameObjects/Workbench/Recipes/";

    [MenuItem("Tools/Workbench/Install UI")]
    public static string Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Use Edit Mode.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new System.InvalidOperationException("Open SampleScene.");
        AssetDatabase.Refresh();
        Import(Ui + "WorkbenchBackground.png");
        Import(Icons + "PlantFiber.png");
        SliceIcons();
        MakePanelSprite("Row.png", new Color32(46, 29, 20, 235), new Color32(143, 104, 76, 255));
        MakePanelSprite("SelectedRow.png", new Color32(91, 44, 15, 245), new Color32(255, 177, 67, 255));
        MakePanelSprite("Action.png", new Color32(186, 68, 17, 255), new Color32(255, 177, 67, 255));

        var sprites = AssetDatabase.LoadAllAssetsAtPath(Icons + "CraftingIcons.png").OfType<Sprite>().ToDictionary(s => s.name);
        var wood = ItemAsset("Materials/Wood", Item.Wood, "Holz", sprites["Wood"], ItemCategory.Misc);
        var rope = ItemAsset("Materials/Rope", Item.Rope, "Seil", sprites["Rope"], ItemCategory.Misc);
        var nails = ItemAsset("Materials/Nails", Item.Nails, "Nägel", sprites["Nails"], ItemCategory.Misc);
        var bridge = ItemAsset("Tools/BridgePart", Item.BridgePart, "Brückenteil", sprites["BridgePart"], ItemCategory.Tool);
        var fibers = ItemAsset("Materials/PlantFiber", Item.PlantFiber, "Fasern",
            AssetDatabase.LoadAssetAtPath<Sprite>(Icons + "PlantFiber.png"), ItemCategory.Misc);
        var torch = LoadItem("Tools/Torche");
        var dynamite = LoadItem("Tools/Dynamite");
        var ladder = LoadItem("Tools/Ladder");
        var toolSprites = AssetDatabase.LoadAllAssetsAtPath(Icons + "ToolIcons.png").OfType<Sprite>().ToDictionary(s => s.name);
        torch.icon = toolSprites["Torch"]; dynamite.icon = toolSprites["Dynamite"]; ladder.icon = toolSprites["Ladder"];
        torch.displayName = "Fackel"; dynamite.displayName = "Dynamit"; ladder.displayName = "Leiter";
        EditorUtility.SetDirty(torch); EditorUtility.SetDirty(dynamite); EditorUtility.SetDirty(ladder);
        var coal = LoadItem("Ores/Coal");
        var iron = LoadItem("Ores/Iron");
        var copper = LoadItem("Ores/Copper");
        var recipes = new[] {
            Recipe("Torch", torch, 4, "Fackeln", new(wood, 2), new(coal, 1)),
            Recipe("Dynamite", dynamite, 1, "Dynamit", new(coal, 3), new(copper, 2), new(rope, 1)),
            Recipe("BridgePart", bridge, 1, "Brückenteile", new(wood, 6), new(iron, 2), new(rope, 1)),
            Recipe("Ladder", ladder, 1, "Leitern", new(wood, 4), new(rope, 1)),
            Recipe("Rope", rope, 1, "Seile", new CraftingIngredient(fibers, 3)),
            Recipe("Nails", nails, 4, "Nägel", new CraftingIngredient(iron, 1))
        };

        var shop = GameObject.Find("/UI/ScreenCanvas/Windows/ShopUI");
        var windows = shop.transform.parent;
        var existing = windows.Find("WorkbenchUI");
        var root = existing ? existing.gameObject : new GameObject("WorkbenchUI", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        if (!existing) { root.transform.SetParent(windows, false); Undo.RegisterCreatedObjectUndo(root, "Create workbench"); }
        Undo.RegisterFullObjectHierarchyUndo(root, "Configure workbench");
        root.layer = shop.layer;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        var image = root.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "WorkbenchBackground.png");
        image.color = Color.white; image.raycastTarget = true;
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0; group.interactable = group.blocksRaycasts = false;
        var controller = root.GetComponent<WorkbenchPanel>();
        if (!controller) controller = Undo.AddComponent<WorkbenchPanel>(root);
        var shopText = shop.transform.Find("ShopTitle").GetComponent<TextMeshProUGUI>();
        controller.font = shopText.font;
        controller.fontMaterial = shopText.fontSharedMaterial;
        controller.recipes = recipes;
        controller.rowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "Row.png");
        controller.selectedRowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "SelectedRow.png");
        controller.actionSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "Action.png");
        EditorUtility.SetDirty(controller);
        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Workbench installed: B / Escape, six recipes.";
    }

    static ItemSO LoadItem(string path) => AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/" + path + ".asset");

    static ItemSO ItemAsset(string path, Item id, string name, Sprite icon, ItemCategory category)
    {
        var item = LoadItem(path);
        if (item) return item;
        item = ScriptableObject.CreateInstance<ItemSO>();
        item.item = id; item.displayName = name; item.icon = icon; item.category = category;
        AssetDatabase.CreateAsset(item, "Assets/GameObjects/Items/" + path + ".asset");
        return item;
    }

    static CraftingRecipe Recipe(string name, ItemSO output, int count, string plural, params CraftingIngredient[] ingredients)
    {
        string path = RecipeFolder + name + ".asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path);
        if (recipe) return recipe;
        recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
        recipe.output = output; recipe.outputAmount = count; recipe.pluralName = plural; recipe.ingredients = ingredients;
        AssetDatabase.CreateAsset(recipe, path);
        return recipe;
    }

    static TextureImporter Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048; importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true; importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return importer;
    }

    static void SliceIcons()
    {
        SliceSheet("CraftingIcons.png", 2, 2, new[] { "Wood", "BridgePart", "Rope", "Nails" });
        SliceSheet("ToolIcons.png", 3, 1, new[] { "Torch", "Dynamite", "Ladder" });
    }

    static void SliceSheet(string file, int columns, int rows, string[] names)
    {
        string path = Icons + file;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 100;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        var factories = new SpriteDataProviderFactories(); factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldRects = provider.GetSpriteRects();
        var source = new Texture2D(2, 2);
        source.LoadImage(File.ReadAllBytes(path));
        var pixels = source.GetPixels32();
        var rects = names.Select((name, i) => new SpriteRect {
            name = name, rect = AlphaBounds(pixels, width, height, columns, rows, i),
            pivot = new Vector2(.5f, .5f), alignment = SpriteAlignment.Center,
            spriteID = oldRects.FirstOrDefault(r => r.name == name)?.spriteID ?? GUID.Generate()
        }).ToArray();
        Object.DestroyImmediate(source);
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply(); importer.SaveAndReimport();
    }

    static Rect AlphaBounds(Color32[] pixels, int width, int height, int columns, int rows, int index)
    {
        int left = index % columns * width / columns, right = (index % columns + 1) * width / columns;
        int bottom = (rows - 1 - index / columns) * height / rows, top = (rows - index / columns) * height / rows;
        int minX = right, minY = top, maxX = left, maxY = bottom;
        for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
        {
            if (pixels[y * width + x].a <= 8) continue;
            minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
        }
        if (minX > maxX) return new Rect(left, bottom, right - left, top - bottom);
        minX = Mathf.Max(left, minX - 4); minY = Mathf.Max(bottom, minY - 4);
        maxX = Mathf.Min(right, maxX + 5); maxY = Mathf.Min(top, maxY + 5);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    static void MakePanelSprite(string name, Color fill, Color border)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float outer = Mask(x, y, 0, 7, size), inner = Mask(x, y, 2, 5, size);
            Color shaded = fill * Mathf.Lerp(.85f, 1.12f, y / 63f); shaded.a = fill.a;
            var color = Color.Lerp(border, shaded, inner);
            color.a *= outer; pixels[y * size + x] = color;
        }
        texture.SetPixels(pixels); texture.Apply();
        var bytes = texture.EncodeToPNG(); Object.DestroyImmediate(texture);
        string path = Ui + name;
        File.WriteAllBytes(path, bytes);
        var importer = Import(path);
        importer.spriteBorder = new Vector4(10, 10, 10, 10); importer.SaveAndReimport();
    }

    static float Mask(int x, int y, int inset, int radius, int size)
    {
        var p = new Vector2(x + .5f, y + .5f);
        var q = new Vector2(Mathf.Clamp(p.x, inset + radius, size - inset - radius), Mathf.Clamp(p.y, inset + radius, size - inset - radius));
        return Mathf.Clamp01(.5f - (Vector2.Distance(p, q) - radius));
    }
}
