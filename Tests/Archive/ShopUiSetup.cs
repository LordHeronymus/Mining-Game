using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ShopUiSetup
{
    private const string OverlayPath = "Assets/UI/Shop/UndergroundShopOverlay.png";
    private const string BackgroundPath = "Assets/UI/Shop/UndergroundShopBackground.png";
    private const string SurfacePath = "Assets/UI/Shop/SurfaceShopBackground.png";
    private const string SlotPath = "Assets/GameObjects/Shop/ShopSlot.prefab";
    private const string BuySlotPath = "Assets/GameObjects/Shop/ShopItem.prefab";
    private const string SlotBackgroundPath = "Assets/UI/Shop/OreSlotBackground.png";
    private const string SlotSelectionPath = "Assets/UI/Shop/OreSlotSelection.png";
    private const string CountBadgePath = "Assets/UI/Shop/OreCountBadge.png";

    private static readonly Color Cream = new Color32(255, 245, 229, 255);
    private static readonly Color Muted = new Color32(224, 207, 187, 255);
    private static readonly Color Amber = new Color32(255, 177, 67, 255);
    private static readonly Color Orange = new Color32(186, 68, 17, 255);
    private static readonly Color Brown = new Color32(93, 61, 48, 255);

    [MenuItem("Tools/Shop/Apply Surface UI")]
    public static string Apply() => ApplyTheme(true);

    [MenuItem("Tools/Shop/Apply Underground UI")]
    public static string ApplyUnderground() => ApplyTheme(false);

    private static string ApplyTheme(bool surface)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new System.InvalidOperationException("SampleScene must be the active scene.");

        Transform shop = GameObject.Find("/UI/ScreenCanvas/Windows/ShopUI")?.transform;
        if (!shop) throw new System.InvalidOperationException("ShopUI was not found.");

        var canvasScaler = shop.GetComponentInParent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(2560, 1440);
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;

        ImportSprite(BackgroundPath);
        ImportSprite(OverlayPath);
        if (surface) ImportSprite(SurfacePath);
        CreateSlotSprites();
        Undo.RegisterFullObjectHierarchyUndo(shop.gameObject, "Redesign Shop UI");

        var shopImage = shop.GetComponent<Image>();
        shopImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(surface ? SurfacePath : OverlayPath);
        shopImage.color = Color.white;
        shopImage.type = Image.Type.Simple;
        shopImage.preserveAspect = false;
        shopImage.raycastTarget = true;

        Transform sellPage = shop.Find("SellPage");
        Transform buyPage = shop.Find("BuyPage");
        Transform tabs = shop.Find("Tabs");
        if (!sellPage || !buyPage || !tabs)
            throw new System.InvalidOperationException("The existing shop pages are incomplete.");

        Stretch(sellPage.GetComponent<RectTransform>());
        Stretch(buyPage.GetComponent<RectTransform>());
        ConfigureTabs(shop, tabs);
        ConfigureHeader(shop, sellPage, buyPage);
        ConfigureSellPage(sellPage);
        ConfigureBuyPage(buyPage);
        ConfigureSlotPrefab();
        ConfigureBuySlotPrefab();
        LocalizeOreNames();
        if (surface) ConfigureSurfaceLayout(shop);

        EditorUtility.SetDirty(shop.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        return "Shop UI applied.";
    }

    private static void ConfigureSurfaceLayout(Transform shop)
    {
        TopCenter(shop.Find("ShopTitle").GetComponent<RectTransform>(), 0, 132, 720, 120);
        foreach (string name in new[] { "Tabs", "SellTabFrame", "BuyTabFrame",
            "SellTabBackground", "BuyTabBackground", "CurrentMoney", "CurrentMoneyFrame" })
        {
            var rect = shop.Find(name).GetComponent<RectTransform>();
            rect.anchoredPosition += new Vector2(0, -64);
        }
        TopLeft(shop.Find("SellPage/Items").GetComponent<RectTransform>(), 350, 440, 675, 720);
        TopLeft(shop.Find("BuyPage/Items (1)").GetComponent<RectTransform>(), 350, 440, 675, 720);
        TopLeft(shop.Find("SellPage/Details/SelectedOreIcon").GetComponent<RectTransform>(),
            1105, 460, 160, 160);
        TopLeft(shop.Find("SellPage/Details/Name").GetComponent<RectTransform>(),
            1340, 467, 790, 98);
    }

    private static void ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new System.InvalidOperationException("Missing texture: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static void ConfigureTabs(Transform shop, Transform tabs)
    {
        var layout = tabs.GetComponent<HorizontalLayoutGroup>();
        if (layout) Undo.DestroyObjectImmediate(layout);
        TopCenter(tabs.GetComponent<RectTransform>(), 0, 278, 775, 85);

        ConfigureTab(tabs.Find("Sell"), "Verkaufen", 0, true);
        ConfigureTab(tabs.Find("Buy"), "Kaufen", 395, false);

        Image sellFrame = EnsureImage(shop, "SellTabFrame");
        TopCenter(sellFrame.rectTransform, -198, 271, 392, 94);
        sellFrame.color = new Color32(171, 119, 71, 255);
        sellFrame.raycastTarget = false;
        sellFrame.transform.SetAsFirstSibling();
        Image buyFrame = EnsureImage(shop, "BuyTabFrame");
        TopCenter(buyFrame.rectTransform, 198, 271, 392, 94);
        buyFrame.color = new Color32(171, 119, 71, 255);
        buyFrame.raycastTarget = false;
        buyFrame.transform.SetAsFirstSibling();
        Image sellBackground = EnsureImage(shop, "SellTabBackground");
        TopCenter(sellBackground.rectTransform, -198, 278, 380, 80);
        sellBackground.color = Orange;
        sellBackground.raycastTarget = false;
        Image buyBackground = EnsureImage(shop, "BuyTabBackground");
        TopCenter(buyBackground.rectTransform, 198, 278, 380, 80);
        buyBackground.color = new Color32(57, 36, 28, 255);
        buyBackground.raycastTarget = false;
        sellBackground.transform.SetSiblingIndex(2);
        buyBackground.transform.SetSiblingIndex(3);

        var panel = shop.GetComponent<ShopPanel>();
        SetReference(panel, "sellText", tabs.Find("Sell").GetComponent<TextMeshProUGUI>());
        SetReference(panel, "buyText", tabs.Find("Buy").GetComponent<TextMeshProUGUI>());
        SetReference(panel, "sellTabBackground", sellBackground);
        SetReference(panel, "buyTabBackground", buyBackground);
        SetColor(panel, "tabHighlight", Amber);
        SetColor(panel, "activeTabColor", Orange);
        SetColor(panel, "inactiveTabColor", new Color32(57, 36, 28, 255));
    }

    private static void ConfigureTab(Transform tab, string caption, float x, bool active)
    {
        TopLeft(tab.GetComponent<RectTransform>(), x, 0, 380, 80);
        var text = tab.GetComponent<TextMeshProUGUI>();
        text.enabled = true;
        Style(text, caption, 48, active ? Cream : Muted, TextAlignmentOptions.Center, true);
        text.raycastTarget = true;

        var button = tab.GetComponent<Button>();
        button.targetGraphic = text;
        button.transition = Selectable.Transition.None;
    }

    private static void ConfigureHeader(Transform shop, Transform sellPage, Transform buyPage)
    {
        Transform title = shop.Find("ShopTitle") ?? sellPage.Find("Shop (TMP)");
        title.SetParent(shop, false);
        title.name = "ShopTitle";
        TopCenter(title.GetComponent<RectTransform>(), 0, 63, 720, 120);
        Style(title.GetComponent<TextMeshProUGUI>(), "Shop", 88, Cream, TextAlignmentOptions.Center, true);
        title.SetAsLastSibling();

        Transform buyTitle = buyPage.Find("Shop (TMP) (1)");
        if (buyTitle) buyTitle.gameObject.SetActive(false);

        Transform money = shop.Find("CurrentMoney") ?? sellPage.Find("CurrentMoney");
        money.SetParent(shop, false);
        money.SetAsLastSibling();
        TopRight(money.GetComponent<RectTransform>(), 372, 278, 390, 80);
        Image moneyFrame = EnsureImage(shop, "CurrentMoneyFrame");
        TopRight(moneyFrame.rectTransform, 365, 271, 404, 94);
        moneyFrame.color = new Color32(171, 119, 71, 255);
        moneyFrame.raycastTarget = false;
        moneyFrame.transform.SetSiblingIndex(4);
        money.SetAsLastSibling();
        var layout = money.GetComponent<HorizontalLayoutGroup>();
        if (layout) Undo.DestroyObjectImmediate(layout);
        var background = money.GetComponent<Image>();
        if (!background) background = Undo.AddComponent<Image>(money.gameObject);
        background.color = new Color32(34, 19, 13, 240);
        background.raycastTarget = false;

        Transform label = money.Find("TMP");
        TopLeft(label.GetComponent<RectTransform>(), 20, 27, 115, 60);
        Style(label.GetComponent<TextMeshProUGUI>(), "Geld", 45, Amber, TextAlignmentOptions.MidlineLeft, true);

        Transform amount = money.Find("Amount");
        var fitter = amount.GetComponent<ContentSizeFitter>();
        if (fitter) Undo.DestroyObjectImmediate(fitter);
        TopLeft(amount.GetComponent<RectTransform>(), 135, 27, 175, 60);
        Style(amount.GetComponent<TextMeshProUGUI>(), null, 46, Cream, TextAlignmentOptions.MidlineRight, true);
        amount.GetComponent<TextMeshProUGUI>().textWrappingMode = TextWrappingModes.NoWrap;

        Transform icon = money.Find("MoneyIcon");
        TopLeft(icon.GetComponent<RectTransform>(), 320, 10, 54, 54);
        icon.GetComponent<Image>().raycastTarget = false;
    }

    private static void ConfigureSellPage(Transform page)
    {
        Transform items = page.Find("Items");
        TopLeft(items.GetComponent<RectTransform>(), 302, 390, 675, 825);
        var itemsImage = items.GetComponent<Image>();
        itemsImage.color = new Color32(0, 0, 0, 0);
        var scroll = items.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        Transform content = items.Find("Viewport/Content");
        var grid = content.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(171, 171);
        grid.spacing = new Vector2(40, 32);
        grid.padding = new RectOffset(32, 33, 24, 24);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;
        NormalizePreviewSlots(content);

        Transform details = page.Find("Details");
        var detailsGrid = details.GetComponent<GridLayoutGroup>();
        if (detailsGrid) Undo.DestroyObjectImmediate(detailsGrid);
        Stretch(details.GetComponent<RectTransform>());

        Transform oldOreLabel = details.Find("Ore");
        if (oldOreLabel) oldOreLabel.gameObject.SetActive(false);

        var name = details.Find("Name").GetComponent<TextMeshProUGUI>();
        TopLeft(name.rectTransform, 1405, 429, 790, 98);
        Style(name, null, 68, Cream, TextAlignmentOptions.MidlineLeft, true);

        Image selectedIcon = EnsureImage(details, "SelectedOreIcon");
        TopLeft(selectedIcon.rectTransform, 1105, 420, 190, 190);
        selectedIcon.preserveAspect = true;
        selectedIcon.raycastTarget = false;

        ConfigureDetailRow(details, "Count", "Bestand", 650, 0);
        ConfigureDetailRow(details, "Worth", "Stückpreis", 775, 1);
        ConfigureDetailRow(details, "TotalWorth", "Erlös", 910, 2);
        Sprite coin = page.parent.Find("CurrentMoney/MoneyIcon").GetComponent<Image>().sprite;
        Coin(details, "PriceCoin", coin, 1745, 793, 54);
        Coin(details, "RevenueCoin", coin, 1745, 904, 58);
        Line(details, "RowSeparatorTop", 1100, 630);
        Line(details, "RowSeparator1", 1100, 755);
        Line(details, "RowSeparator2", 1100, 880);

        Transform buttons = page.Find("Buttons");
        var vertical = buttons.GetComponent<VerticalLayoutGroup>();
        if (vertical) Undo.DestroyObjectImmediate(vertical);
        Stretch(buttons.GetComponent<RectTransform>());
        buttons.Find("Button").gameObject.SetActive(false);
        buttons.Find("Button (1)").gameObject.SetActive(false);
        ConfigureAction(buttons.Find("Button (2)"), 1085, 1035, 540, Orange, "Erz verkaufen");
        ConfigureAction(buttons.Find("Button (3)"), 1665, 1035, 530, Brown, "Alles verkaufen");
        ActionFrame(buttons, "PrimaryActionFrame", 1080, 1030, 550);
        ActionFrame(buttons, "SecondaryActionFrame", 1660, 1030, 540);

        var sellPage = page.GetComponent<SellPage>();
        SetReference(sellPage, "selectedOreIcon", selectedIcon);
    }

    private static void ConfigureBuyPage(Transform page)
    {
        Transform items = page.Find("Items (1)");
        if (!items) return;
        TopLeft(items.GetComponent<RectTransform>(), 302, 390, 675, 825);
        items.GetComponent<Image>().color = new Color32(0, 0, 0, 0);
        var grid = items.Find("Viewport/Content")?.GetComponent<GridLayoutGroup>();
        if (!grid) return;
        grid.cellSize = new Vector2(171, 171);
        grid.spacing = new Vector2(40, 32);
        grid.padding = new RectOffset(32, 33, 24, 24);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        NormalizePreviewSlots(items.Find("Viewport/Content"));
    }

    private static void ConfigureDetailRow(Transform details, string labelName, string labelText, float y, int amountIndex)
    {
        var label = details.Find(labelName).GetComponent<TextMeshProUGUI>();
        float textY = amountIndex < 2 ? y + 24 : y;
        TopLeft(label.rectTransform, 1105, textY, 430, 80);
        Style(label, labelText, amountIndex == 2 ? 55 : 46,
            amountIndex == 2 ? Amber : Muted, TextAlignmentOptions.MidlineLeft, amountIndex == 2);

        int amountChild = labelName == "Count" ? 5 : labelName == "Worth" ? 3 : 7;
        var amount = details.GetChild(amountChild).GetComponent<TextMeshProUGUI>();
        TopLeft(amount.rectTransform, 1560, textY, 270, 80);
        Style(amount, null, amountIndex == 2 ? 58 : 50,
            amountIndex == 2 ? Amber : Cream, TextAlignmentOptions.MidlineLeft, true);
    }

    private static void ConfigureAction(Transform action, float x, float y, float width, Color color, string caption)
    {
        TopLeft(action.GetComponent<RectTransform>(), x, y, width, 110);
        var image = action.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        var button = action.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(255, 225, 197, 255);
        colors.pressedColor = new Color32(207, 170, 140, 255);
        colors.disabledColor = new Color32(115, 103, 97, 180);
        button.colors = colors;
        var label = action.Find("Text (TMP)").GetComponent<TextMeshProUGUI>();
        Style(label, caption, 45, Cream, TextAlignmentOptions.Center, true);
        label.raycastTarget = false;
    }

    private static void ConfigureSlotPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SlotPath);
        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(171, 171);
            rect.localScale = Vector3.one;
            var image = root.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBackgroundPath);
            image.color = Color.white;
            image.type = Image.Type.Simple;

            var icon = root.transform.Find("Icon");
            Center(icon.GetComponent<RectTransform>(), 0, 0, 126, 126);
            icon.GetComponent<Image>().preserveAspect = true;

            var count = root.transform.Find("Count (TMP)");
            var countRect = count.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(1, 0);
            countRect.anchorMax = new Vector2(1, 0);
            countRect.pivot = new Vector2(1, 0);
            countRect.anchoredPosition = new Vector2(-6, 6);
            countRect.sizeDelta = new Vector2(52, 47);
            var countText = count.GetComponent<TextMeshProUGUI>();
            Style(countText, null, 34, Cream, TextAlignmentOptions.Center, true);
            float initialBadgeWidth = Mathf.Max(52f,
                Mathf.Ceil(countText.GetPreferredValues(countText.text).x) + 18f);
            countRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, initialBadgeWidth);

            var frame = root.transform.Find("SelectionFrame");
            Center(frame.GetComponent<RectTransform>(), 0, 0, 171, 171);
            frame.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotSelectionPath);
            frame.GetComponent<Image>().color = Color.white;
            frame.SetAsFirstSibling();

            var badge = EnsureImage(root.transform, "CountBadge");
            var badgeRect = badge.rectTransform;
            badgeRect.anchorMin = new Vector2(1, 0);
            badgeRect.anchorMax = new Vector2(1, 0);
            badgeRect.pivot = new Vector2(1, 0);
            badgeRect.anchoredPosition = new Vector2(-6, 6);
            badgeRect.sizeDelta = new Vector2(initialBadgeWidth, 47);
            badge.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CountBadgePath);
            badge.type = Image.Type.Sliced;
            badge.color = Color.white;
            badge.raycastTarget = false;
            SetReference(root.GetComponent<ShopSlot>(), "countBadge", badge);
            count.SetAsLastSibling();
            PrefabUtility.SaveAsPrefabAsset(root, SlotPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureBuySlotPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BuySlotPath);
        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(171, 171);
            rect.localScale = Vector3.one;
            var background = root.GetComponent<Image>();
            background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBackgroundPath);
            background.color = Color.white;
            background.type = Image.Type.Simple;
            var icon = root.transform.Find("Icon").GetComponent<RectTransform>();
            Center(icon, 0, 0, 126, 126);
            root.transform.Find("Icon").GetComponent<Image>().preserveAspect = true;
            var frame = root.transform.Find("SelectionFrame");
            Center(frame.GetComponent<RectTransform>(), 0, 0, 171, 171);
            frame.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotSelectionPath);
            frame.GetComponent<Image>().color = Color.white;
            frame.SetAsFirstSibling();
            PrefabUtility.SaveAsPrefabAsset(root, BuySlotPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void CreateSlotSprites()
    {
        CreateSlotSprite(SlotBackgroundPath, false);
        CreateSlotSprite(SlotSelectionPath, true);
        CreateCountBadgeSprite();
    }

    private static void CreateSlotSprite(string path, bool selected)
    {
        const int size = 130;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        Color top = selected ? new Color32(255, 194, 100, 255) : new Color32(96, 67, 51, 255);
        Color bottom = selected ? new Color32(196, 104, 38, 255) : new Color32(58, 39, 30, 255);

        for (int y = 0; y < size; y++)
        {
            float down = 1f - y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float outer = RoundedMask(x, y, 2, 10, size);
                float bevel = RoundedMask(x, y, 4, 8, size);
                float inner = RoundedMask(x, y, 8, 5, size);
                if (outer <= 0f) continue;

                Color color;
                if (selected)
                {
                    color = Color.Lerp(top, bottom, down);
                    color.a = outer * (1f - inner);
                    if (bevel > 0f) color *= 1.08f;
                }
                else if (bevel <= 0f)
                {
                    color = new Color32(29, 19, 14, 255);
                    color.a = outer;
                }
                else if (inner <= 0f)
                {
                    color = Color.Lerp(new Color32(143, 104, 76, 255),
                        new Color32(76, 50, 36, 255), down);
                    color.a = bevel;
                }
                else
                {
                    float centerLight = 1f - Mathf.Abs((x - 64.5f) / 65f);
                    color = Color.Lerp(top, bottom, down);
                    color += new Color(0.025f, 0.018f, 0.012f, 0f) * centerLight;
                    color.a = inner;
                }
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
        if (!File.Exists(fullPath) || !EqualBytes(File.ReadAllBytes(fullPath), png))
            File.WriteAllBytes(fullPath, png);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static float RoundedMask(int x, int y, int inset, int radius, int size)
    {
        Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
        Vector2 nearest = new Vector2(
            Mathf.Clamp(point.x, inset + radius, size - inset - radius),
            Mathf.Clamp(point.y, inset + radius, size - inset - radius));
        return Mathf.Clamp01(0.5f - (Vector2.Distance(point, nearest) - radius));
    }

    private static void CreateCountBadgeSprite()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float alpha = RoundedMask(x, y, 0, 11, size);
            pixels[y * size + x] = new Color(31f / 255f, 19f / 255f, 14f / 255f,
                235f / 255f * alpha);
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), CountBadgePath);
        if (!File.Exists(fullPath) || !EqualBytes(File.ReadAllBytes(fullPath), png))
            File.WriteAllBytes(fullPath, png);

        AssetDatabase.ImportAsset(CountBadgePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(CountBadgePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(14, 14, 14, 14);
        importer.spritePixelsPerUnit = 100;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static bool EqualBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void NormalizePreviewSlots(Transform content)
    {
        foreach (RectTransform slot in content)
        {
            slot.localScale = Vector3.one;
            slot.sizeDelta = new Vector2(171, 171);
        }
    }

    private static void ActionFrame(Transform parent, string name, float x, float y, float width)
    {
        var frame = EnsureImage(parent, name);
        TopLeft(frame.rectTransform, x, y, width, 120);
        frame.color = new Color32(171, 119, 71, 255);
        frame.raycastTarget = false;
        frame.transform.SetAsFirstSibling();
    }

    private static void Line(Transform parent, string name, float x, float y)
    {
        var image = EnsureImage(parent, name);
        TopLeft(image.rectTransform, x, y, 980, 3);
        image.color = new Color32(116, 79, 59, 180);
        image.raycastTarget = false;
    }

    private static void Coin(Transform parent, string name, Sprite sprite, float x, float y, float size)
    {
        var image = EnsureImage(parent, name);
        TopLeft(image.rectTransform, x, y, size, size);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static void LocalizeOreNames()
    {
        SetOreName("Copper", "Kupfer");
        SetOreName("Silver", "Silber");
        SetOreName("Iron", "Eisen");
    }

    private static void SetOreName(string assetName, string label)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(
            "Assets/GameObjects/Items/Ores/" + assetName + ".asset");
        if (!item || item.displayName == label) return;
        Undo.RecordObject(item, "Localize ore name");
        item.displayName = label;
        EditorUtility.SetDirty(item);
    }

    private static Image EnsureImage(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing) return existing.GetComponent<Image>();
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Add shop image");
        go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }

    private static void SetReference(Object target, string propertyName, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    private static void SetColor(Object target, string propertyName, Color color)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).colorValue = color;
        serialized.ApplyModifiedProperties();
    }

    private static void Style(TextMeshProUGUI text, string caption, float size, Color color,
        TextAlignmentOptions alignment, bool bold)
    {
        if (caption != null) text.text = caption;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void TopCenter(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1);
        rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void TopRight(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void Center(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }
}
