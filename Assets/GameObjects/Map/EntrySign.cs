using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Gold-framed wooden entry sign floating above a surface building ("Lager öffnen", "Shop öffnen", ...).
// The width follows the caption so the text never runs into the brass end caps.
public sealed class EntrySign : MonoBehaviour
{
    const float FadeDuration = .12f, Height = 92, FontSize = 40, CapMargin = 86, WorldScale = .01f;
    static readonly Color Cream = new Color32(255, 245, 229, 255);

    public bool Visible { get; set; }
    public Button Button { get; private set; }
    public CanvasGroup Group { get; private set; }
    public TextMeshProUGUI Label { get; private set; }
    RectTransform rect;
    Material fontMaterial;
    float baseY, parentScale = 1;

    // The building artwork is the largest sprite; smaller ones are signboards, lanterns or crystals.
    public static SpriteRenderer MainSprite(Transform building)
    {
        SpriteRenderer best = null;
        foreach (var renderer in building.GetComponentsInChildren<SpriteRenderer>(true))
            if (!best || Area(renderer) > Area(best)) best = renderer;
        return best;
    }

    static float Area(SpriteRenderer renderer) => renderer.bounds.size.x * renderer.bounds.size.y;

    static readonly System.Collections.Generic.Dictionary<Sprite, float> visibleCenters = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => visibleCenters.Clear();

    // World x of the centre of the opaque pixels. Building sprites carry uneven transparent margins
    // (the workshop's art sits 60 px right of its image centre), so the bounds centre is off.
    // The texture is not CPU-readable; a small GPU copy is read once per sprite and cached.
    public static float VisibleCenterX(SpriteRenderer renderer)
    {
        var sprite = renderer.sprite;
        if (!sprite) return renderer.bounds.center.x;
        if (!visibleCenters.TryGetValue(sprite, out float local))
        {
            local = sprite.bounds.center.x;
            try
            {
                var texture = sprite.texture;
                var area = sprite.textureRect;
                const int width = 256;
                int height = Mathf.Max(1, Mathf.RoundToInt(width * area.height / area.width));
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(texture, target, new Vector2(area.width / texture.width, area.height / texture.height),
                    new Vector2(area.x / texture.width, area.y / texture.height));
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply(false);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                var pixels = copy.GetPixels32();
                Destroy(copy);
                int min = width, max = -1;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (pixels[y * width + x].a > 128) { if (x < min) min = x; if (x > max) max = x; }
                if (max >= min) local = ((min + max + 1) * .5f / width * area.width - sprite.pivot.x) / sprite.pixelsPerUnit;
            }
            catch (System.Exception) { }
            visibleCenters[sprite] = local;
        }
        return renderer.transform.TransformPoint(new Vector3(renderer.flipX ? -local : local, 0, 0)).x;
    }

    public static EntrySign Create(Transform building, SpriteRenderer sprite, string caption, UnityAction onClick, int layer)
    {
        var canvasObject = new GameObject("EntrySign", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.layer = layer;
        var sign = canvasObject.AddComponent<EntrySign>();
        sign.Build(building, sprite, caption, onClick);
        return sign;
    }

    void Build(Transform building, SpriteRenderer sprite, string caption, UnityAction onClick)
    {
        rect = (RectTransform)transform;
        rect.SetParent(building, false);
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingLayerID = sprite.sortingLayerID;
        canvas.sortingOrder = sprite.sortingOrder + 2;

        var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.layer = gameObject.layer;
        var buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.SetParent(rect, false);
        buttonRect.anchorMin = Vector2.zero; buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = buttonRect.offsetMax = Vector2.zero;
        Button = buttonObject.GetComponent<Button>();
        Button.targetGraphic = buttonObject.GetComponent<Image>();
        Button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (onClick != null) Button.onClick.AddListener(onClick);

        // The label sits beside the button so GoldButtonFeedback does not shrink it to the narrow centre of the plaque.
        Label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        Label.gameObject.layer = gameObject.layer;
        Label.rectTransform.SetParent(rect, false);
        Label.rectTransform.anchorMin = Vector2.zero; Label.rectTransform.anchorMax = Vector2.one;
        Label.rectTransform.offsetMin = new Vector2(CapMargin, 0); Label.rectTransform.offsetMax = new Vector2(-CapMargin, 0);
        var font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (font)
        {
            Label.font = font;
            fontMaterial = new Material(font.material);
            fontMaterial.SetFloat("_OutlineWidth", .065f);
            fontMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
            Label.fontSharedMaterial = fontMaterial;
        }
        Label.text = caption;
        Label.fontSize = FontSize; Label.enableAutoSizing = false; Label.color = Cream;
        Label.alignment = TextAlignmentOptions.Center;
        Label.textWrappingMode = TextWrappingModes.NoWrap;
        Label.raycastTarget = false;

        float textWidth = Label.GetPreferredValues(caption, 10000, Height).x;
        rect.sizeDelta = new Vector2(Mathf.Ceil(textWidth) + 2 * CapMargin, Height);
        // World-space placement and size: buildings may be scaled (the shop is), the sign must not be.
        parentScale = Mathf.Max(.0001f, Mathf.Abs(building.lossyScale.x));
        baseY = sprite.bounds.max.y + .58f;
        rect.position = new Vector3(VisibleCenterX(sprite), baseY, building.position.z);
        rect.localScale = Vector3.one * (WorldScale / parentScale);
        // Apply after sizing: GoldButtonFeedback picks the wide button artwork from the final aspect ratio.
        GoldButtonFeedback.Apply(Button, null, null);

        Group = GetComponent<CanvasGroup>();
        Group.alpha = 0;
        Group.interactable = Group.blocksRaycasts = false;
    }

    void LateUpdate()
    {
        Group.alpha = Mathf.MoveTowards(Group.alpha, Visible ? 1 : 0, Time.unscaledDeltaTime / FadeDuration);
        // Stay interactable while fading out so GoldButtonFeedback does not grey the sign; only clicks stop.
        Group.blocksRaycasts = Visible;
        Group.interactable = Visible || Group.alpha > 0;
        // Pops in slightly and floats gently so the sign reads as an inviting interaction.
        float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * .04f * Group.alpha;
        rect.localScale = Vector3.one * (WorldScale / parentScale * (.92f + .08f * Group.alpha));
        var position = rect.position;
        rect.position = new Vector3(position.x, baseY + bob, position.z);
    }

    void OnDisable()
    {
        Visible = false;
        if (Group) { Group.alpha = 0; Group.interactable = Group.blocksRaycasts = false; }
    }

    void OnDestroy()
    {
        if (fontMaterial) Destroy(fontMaterial);
    }
}
