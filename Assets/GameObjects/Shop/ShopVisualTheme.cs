using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShopVisualTheme : MonoBehaviour
{
    public TMP_FontAsset Font { get; private set; }
    public Material FontMaterial { get; private set; }
    public Sprite CardFrame { get; private set; }
    public Sprite SelectedFrame { get; private set; }
    public Sprite ActionFrame { get; private set; }
    readonly List<Sprite> sprites = new();
    TextMeshProUGUI money;
    RectTransform moneyCoin;
    readonly List<ChromeState> buyChrome = new();
    Sprite buyBackground;

    sealed class ChromeState
    {
        public RectTransform rect;
        public Vector2 min, max, pivot, position, size;
        public Image image;
        public Sprite sprite;
        public Image.Type type;
        public Color color;
        public float ppu;
        public void Restore()
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            if (image) { image.sprite = sprite; image.type = type; image.color = color; image.pixelsPerUnitMultiplier = ppu; }
        }
    }
    List<ChromeState> CaptureChrome()
    {
        var result = new List<ChromeState>();
        foreach (string name in new[] { "ShopTitle", "Tabs", "SellTabFrame", "BuyTabFrame", "SellTabBackground", "BuyTabBackground", "CurrentMoneyFrame", "CurrentMoney" })
        {
            var target = transform.Find(name);
            if (!target) continue;
            foreach (var rect in target.GetComponentsInChildren<RectTransform>(true))
            {
                var image = rect.GetComponent<Image>();
                result.Add(new ChromeState {rect=rect,min=rect.anchorMin,max=rect.anchorMax,pivot=rect.pivot,
                    position=rect.anchoredPosition,size=rect.sizeDelta,image=image,sprite=image?image.sprite:null,
                    type=image?image.type:Image.Type.Simple,color=image?image.color:Color.white,ppu=image?image.pixelsPerUnitMultiplier:1});
            }
        }
        return result;
    }
    public void UseBuyLayout(bool buying)
    {
        foreach (var state in buyChrome) state.Restore();
        var image = GetComponent<Image>();
        if (image) { image.sprite = buyBackground; HomeUi.StylePanelWood(image); }
    }

    public static ShopVisualTheme Ensure(Transform shop)
    {
        var theme = shop.GetComponent<ShopVisualTheme>();
        return theme ? theme : shop.gameObject.AddComponent<ShopVisualTheme>();
    }
    void Awake()
    {
        Font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (Font)
        {
            FontMaterial = new Material(Font.material);
            FontMaterial.SetFloat("_OutlineWidth", .065f);
            FontMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
        }
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames-v2");
        CardFrame = Slice(sheet, "Shop Card", 40, 110, 1175, 325);
        SelectedFrame = Slice(sheet, "Shop Selected", 35, 490, 1185, 340);
        ActionFrame = Slice(sheet, "Shop Action", 38, 890, 1178, 275);
        var background = GetComponent<Image>();
        buyBackground = Resources.Load<Sprite>("Shop/BuyBackground-v2");
        if (background) { background.sprite = buyBackground; HomeUi.StylePanelWood(background); }
        foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true)) Style(text);
        var title = transform.Find("ShopTitle")?.GetComponent<TextMeshProUGUI>();
        if (title) { Place(title.rectTransform, 875, 35, 810, 126); title.fontSize = 88; title.alignment = TextAlignmentOptions.Midline; }
        var tabs = transform.Find("Tabs");
        if (tabs)
        {
            Place((RectTransform)tabs, 918, 184, 720, 86);
            int i = 0;
            foreach (Transform tab in tabs)
            {
                Place((RectTransform)tab, i++ * 367, 0, 353, 86);
                var text = tab.GetComponent<TextMeshProUGUI>();
                if (text) { text.fontSize = 42; text.alignment = TextAlignmentOptions.Midline; }
            }
        }
        Frame("SellTabFrame", 914, 180, 361, 94, CardFrame);
        Frame("BuyTabFrame", 1281, 180, 361, 94, CardFrame);
        Position("SellTabBackground", 926, 192, 337, 70);
        Position("BuyTabBackground", 1293, 192, 337, 70);
        Frame("CurrentMoneyFrame", 1740, 184, 535, 86, CardFrame);
        var currentMoney = transform.Find("CurrentMoney");
        if (currentMoney)
        {
            Place((RectTransform)currentMoney, 1740, 184, 535, 86);
            var label = currentMoney.Find("TMP");
            if (label) label.gameObject.SetActive(false);
            money = currentMoney.Find("Amount")?.GetComponent<TextMeshProUGUI>();
            if (money) { money.fontSize = 48; money.alignment = TextAlignmentOptions.MidlineLeft; }
            moneyCoin = currentMoney.Find("MoneyIcon") as RectTransform;
        }
        buyChrome.AddRange(CaptureChrome());
    }
    public void Style(TextMeshProUGUI text)
    {
        if (!Font || !text) return;
        text.font = Font; text.fontSharedMaterial = FontMaterial;
        text.fontStyle = FontStyles.Normal; text.margin = Vector4.zero;
        text.color = new Color32(255, 245, 229, 255);
    }
    void LateUpdate()
    {
        if (!money || !moneyCoin) return;
        float panelWidth = ((RectTransform)money.transform.parent).rect.width;
        float width = Mathf.Min(panelWidth - 90, money.GetPreferredValues(money.text).x);
        float start = (panelWidth - width - 62) * .5f;
        Place(moneyCoin, start, 15, 55, 55);
        Place(money.rectTransform, start + 62, 0, width + 2, 86);
    }
    void Frame(string name, float x, float y, float w, float h, Sprite sprite)
    {
        Position(name, x, y, w, h);
        var image = transform.Find(name)?.GetComponent<Image>();
        if (image) { image.sprite = sprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4; image.color = Color.white; HomeUi.StylePanelWood(image); }
    }
    void Position(string name, float x, float y, float w, float h)
    {
        var rect = transform.Find(name) as RectTransform;
        if (rect) Place(rect, x, y, w, h);
    }
    Sprite Slice(Texture2D texture, string name, float x, float y, float w, float h)
    {
        if (!texture) return null;
        float sx = texture.width / 1280f, sy = texture.height / 1280f;
        var sprite = Sprite.Create(texture, new Rect(x * sx, (1280 - y - h) * sy, w * sx, h * sy),
            new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 80 * sy, 100 * sx, 80 * sy));
        sprite.name = name; sprites.Add(sprite); return sprite;
    }
    void OnDestroy()
    {
        foreach (var sprite in sprites) if (sprite) Destroy(sprite);
        if (FontMaterial) Destroy(FontMaterial);
    }
    public static void CenterImage(Image image)
    {
        // PreserveAspect aligns its fitted quad using the RectTransform pivot.
        // Move the pivot without moving the image's allocated layout rectangle.
        var rect = image.rectTransform;
        var center = Vector2.one * .5f;
        rect.anchoredPosition += Vector2.Scale(center - rect.pivot, rect.rect.size);
        rect.pivot = center;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
    }
    public static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
    }
}
