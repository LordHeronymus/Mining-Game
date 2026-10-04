using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class HomeUi
{
    static readonly Dictionary<string, Sprite> sprites = new();
    public static readonly Color Cream = new Color32(255, 241, 209, 255);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var sprite in sprites.Values) if (sprite) UnityEngine.Object.Destroy(sprite);
        sprites.Clear();
    }
    public static Sprite Sprite(string part)
    {
        if (sprites.TryGetValue(part, out var found) && found) return found;
        if (part == "SaveButton" || part == "SaveActive")
        {
            var texture = Resources.Load<Texture2D>("Homescreen/SaveButtons");
            if (!texture) return null;
            float xScale = texture.width / 2172f, yScale = texture.height / 724f;
            var region = new Rect(68 * xScale, texture.height - ((part == "SaveActive" ? 363 : 48) + 270) * yScale,
                2036 * xScale, 270 * yScale);
            found = UnityEngine.Sprite.Create(texture, region, new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(185 * xScale, 48 * yScale, 185 * xScale, 48 * yScale));
            found.name = part; sprites[part] = found; return found;
        }
        var atlas = Resources.Load<Texture2D>("Homescreen/MenuAtlas"); if (!atlas) return null;
        // Atlas coordinates measured on the approved 1536x1024 source, from its upper-left corner.
        Rect box = part switch
        {
            "Title" => new Rect(38, 56, 704, 260),
            "Panel" => new Rect(39, 339, 701, 632),
            "Active" => new Rect(813, 527, 679, 180),
            _ => new Rect(813, 112, 679, 171)
        };
        float sx = atlas.width / 1536f, sy = atlas.height / 1024f;
        var rect = new Rect(box.x * sx, atlas.height - (box.y + box.height) * sy, box.width * sx, box.height * sy);
        var border = part == "Panel" ? new Vector4(92, 92, 92, 92) : part == "Title" ? new Vector4(100, 70, 100, 70) : new Vector4(55, 45, 55, 45);
        border *= sx;
        found = UnityEngine.Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
        found.name = "Home " + part; sprites[part] = found; return found;
    }
    public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    public static RectTransform Panel(string name, Transform parent)
    {
        var root = Rect(name, parent, Vector2.zero, Vector2.zero);
        Stretch(root);
        if (!Application.isPlaying) root.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = Application.isPlaying ? 1 : 0;
        group.interactable = group.blocksRaycasts = Application.isPlaying;
        return root;
    }
    public static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    public static Image Image(string name, Transform parent, Vector2 position, Vector2 size, string part = null)
    {
        var rect = Rect(name, parent, position, size); var image = rect.gameObject.AddComponent<Image>();
        image.sprite = part == null ? null : Sprite(part); image.type = part == null ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
        image.raycastTarget = false; return image;
    }
    public static TextMeshProUGUI Label(string name, Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value; text.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        text.fontSize = fontSize; text.color = Cream; text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false; text.outlineColor = new Color32(20, 11, 8, 255); text.outlineWidth = .12f;
        return text;
    }
    public static Button Button(string name, Transform parent, string label, Vector2 position, Vector2 size, Action action, bool primary = false)
    {
        var image = Image(name, parent, position, size, primary ? "Active" : "Button"); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
        Label("Label", image.transform, label, Vector2.zero, size - new Vector2(45, 10), Mathf.Min(36, size.y * .36f));
        var feedback = image.gameObject.AddComponent<HomeButtonFeedback>(); feedback.primary = primary;
        button.onClick.AddListener(() => { HomeClickAudio.Play(); feedback.Click(); action?.Invoke(); }); return button;
    }
    public static void Fit(RectTransform layout, Vector2 reference)
    {
        if (!layout || !(layout.parent is RectTransform parent)) return;
        var size = parent.rect.size; layout.localScale = Vector3.one * Mathf.Min(size.x / reference.x, size.y / reference.y);
    }
    public static void EnsureEventSystem()
    {
        if (EventSystem.current) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}

public sealed class HomeButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public bool primary;
    public string normalPart = "Button", activePart = "Active";
    public bool animateScale = true;
    bool hovered, selected;
    float amount, glow, pressed;
    Button button; Image image, highlight;
    void Awake()
    {
        button = GetComponent<Button>(); image = GetComponent<Image>();
        highlight = HomeUi.Rect("Highlight", transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        HomeUi.Stretch(highlight.rectTransform); highlight.transform.SetAsFirstSibling();
        highlight.raycastTarget = false; highlight.color = new Color(1, 1, 1, 0);
    }
    public void OnPointerEnter(PointerEventData data) => hovered = true;
    public void OnPointerExit(PointerEventData data) => hovered = false;
    public void OnSelect(BaseEventData data) => selected = true;
    public void OnDeselect(BaseEventData data) => selected = false;
    public void Click() => pressed = .12f;
    void OnDisable() { hovered = selected = false; amount = glow = pressed = 0; transform.localScale = Vector3.one; if (highlight) highlight.color = new Color(1, 1, 1, 0); }
    void Update()
    {
        float target = button.interactable && (hovered || selected) ? 1 : 0;
        amount = Mathf.Lerp(amount, target, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
        pressed = Mathf.Max(0, pressed - Time.unscaledDeltaTime);
        transform.localScale = animateScale ? Vector3.one * (1 + .018f * amount - (pressed > 0 ? .012f : 0)) : Vector3.one;
        glow = Mathf.Lerp(glow, button.interactable && (primary || hovered || selected) ? 1 : 0,
            1 - Mathf.Exp(-9 * Time.unscaledDeltaTime));
        image.sprite = HomeUi.Sprite(normalPart);
        highlight.sprite = HomeUi.Sprite(activePart); highlight.type = image.type;
        highlight.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        highlight.preserveAspect = image.preserveAspect;
        highlight.color = new Color(1, 1, 1, glow);
        image.color = button.interactable ? Color.Lerp(Color.white, new Color(1, .93f, .76f), amount * .2f) : new Color(.42f, .42f, .42f, .8f);
    }
}
