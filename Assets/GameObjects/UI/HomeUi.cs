using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class HomeUi
{
    static readonly Dictionary<string, Sprite> sprites = new();
    static readonly Dictionary<string, Material> panelWoodMaterials = new();
    public static readonly Color Cream = new Color32(255, 241, 209, 255);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var sprite in sprites.Values) if (sprite) UnityEngine.Object.Destroy(sprite);
        sprites.Clear();
        foreach (var material in panelWoodMaterials.Values) if (material) UnityEngine.Object.Destroy(material);
        panelWoodMaterials.Clear();
    }
    public static Sprite Sprite(string part)
    {
        if (sprites.TryGetValue(part, out var found) && found) return found;
        if (part == "InputField")
        {
            var texture = Resources.Load<Texture2D>("Homescreen/InputField-v3");
            if (!texture) return null;
            float inputScaleX = texture.width / 2172f, inputScaleY = texture.height / 724f;
            found = UnityEngine.Sprite.Create(texture, new Rect(86 * inputScaleX, 266 * inputScaleY, 2001 * inputScaleX, 225 * inputScaleY),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(90 * inputScaleX, 75 * inputScaleY, 90 * inputScaleX, 75 * inputScaleY));
            found.name = "Tiefenhall Input Field"; sprites[part] = found; return found;
        }
        if (part == "SaveButton" || part == "SaveActive" || part == "SaveSelected" || part.StartsWith("SelectionCard"))
        {
            bool card = part.StartsWith("SelectionCard");
            var texture = Resources.Load<Texture2D>(card ? "Homescreen/SelectionCards-v2" : "Homescreen/SelectionButtons-v2");
            if (texture)
            {
                int state = part == "SaveSelected" || part == "SelectionCardSelected" ? 2 :
                    part == "SaveActive" || part == "SelectionCardHover" ? 1 : 0;
                float selectionScaleX = texture.width / 2048f, selectionScaleY = texture.height / 768f;
                var selectionBox = card ? new Rect(12 + 674 * state, 20, 670, 668) : new Rect(45, 25 + 235 * state, 1958, 240);
                var region = new Rect(selectionBox.x * selectionScaleX, texture.height - (selectionBox.y + selectionBox.height) * selectionScaleY, selectionBox.width * selectionScaleX, selectionBox.height * selectionScaleY);
                var selectionBorder = card ? new Vector4(125 * selectionScaleX, 125 * selectionScaleY, 125 * selectionScaleX, 125 * selectionScaleY) : new Vector4(230 * selectionScaleX, 55 * selectionScaleY, 230 * selectionScaleX, 55 * selectionScaleY);
                found = UnityEngine.Sprite.Create(texture, region, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, selectionBorder);
                found.name = part; sprites[part] = found; return found;
            }
        }
        if (part == "SaveButton" || part == "SaveActive" || part == "SaveHandle")
        {
            var texture = Resources.Load<Texture2D>("Homescreen/SaveButtons");
            if (!texture) return null;
            float xScale = texture.width / 2172f, yScale = texture.height / 724f;
            var region = new Rect(68 * xScale, texture.height - ((part == "SaveActive" ? 363 : 48) + 270) * yScale,
                (part == "SaveHandle" ? 185 : 2036) * xScale, 270 * yScale);
            found = UnityEngine.Sprite.Create(texture, region, new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, part == "SaveHandle" ? Vector4.zero : new Vector4(185 * xScale, 48 * yScale, 185 * xScale, 48 * yScale));
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
        image.raycastTarget = false; StylePanelWood(image); return image;
    }
    public static void StylePanelWood(Image image)
    {
        if (!image || !image.sprite) return;
        var sprite = image.sprite;
        string key = sprite.name == "Home Panel" ? "Home Panel" : sprite.texture.name;
        Rect[] regions = key switch
        {
            "BuyBackground-v2" => new[] { new Rect(164, 113, 1340, 741), new Rect(604, 39, 464, 49) },
            "WorkshopBackground-v2" => new[] { new Rect(174, 137, 1316, 54), new Rect(178, 225, 787, 619),
                new Rect(1018, 225, 479, 625), new Rect(606, 40, 456, 49) },
            "InventoryFrame" => new[] { new Rect(214, 159, 1246, 684), new Rect(595, 48, 478, 63) },
            "BuyFrames-v2" => new[] { new Rect(40, 110, 1175, 325), new Rect(35, 490, 1185, 340) },
            "GameWonPanel" or "GameOverPanelNoCoin" => new[] { new Rect(204, 480, 1260, 217),
                new Rect(299, 850, 1060, 44), new Rect(209, 195, 220, 70), new Rect(1230, 195, 200, 70) },
            "GameWonContinue" or "GameOverContinue" => new[] { new Rect(204,480,1260,414),
                new Rect(209,195,220,70), new Rect(1230,195,200,70) },
            _ => null
        };
        if (key != "Home Panel" && regions == null) return;
        if (!panelWoodMaterials.TryGetValue(key, out var panelWoodMaterial) || !panelWoodMaterial)
        {
            var shader = Resources.Load<Shader>("Homescreen/PanelWoodTone");
            if (!shader) return;
            panelWoodMaterial = new Material(shader) { name = "Tiefenhall Dark Panel Wood", hideFlags = HideFlags.DontSave };
            var rect = sprite.rect;
            panelWoodMaterial.SetVector("_WoodUvRect", new Vector4(rect.x / sprite.texture.width,
                rect.y / sprite.texture.height, rect.width / sprite.texture.width, rect.height / sprite.texture.height));
            bool cardAtlas = key == "BuyFrames-v2";
            if (cardAtlas) panelWoodMaterial.SetVector("_WoodUvRect", new Vector4(0, 0, 1, 1));
            if (regions != null)
            {
                panelWoodMaterial.SetFloat("_UseRegions", 1);
                if (cardAtlas || key == "GameWonPanel" || key == "GameOverPanelNoCoin" || key == "GameWonContinue" || key == "GameOverContinue") panelWoodMaterial.SetFloat("_BrownOnly", 1);
                for (int i = 0; i < regions.Length; i++)
                {
                    var region = regions[i];
                    float width = cardAtlas ? 1280 : 1672, height = cardAtlas ? 1280 : 941;
                    panelWoodMaterial.SetVector("_Region" + i, new Vector4(region.x / width,
                        1 - (region.y + region.height) / height, region.width / width, region.height / height));
                }
            }
            panelWoodMaterials[key] = panelWoodMaterial;
        }
        image.material = panelWoodMaterial;
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
        var feedback = image.gameObject.AddComponent<HomeButtonFeedback>();
        StyleSaveButton(button, Mathf.Min(36, size.y * .36f));
        button.onClick.AddListener(() => { HomeClickAudio.Play(); feedback.Click(); action?.Invoke(); }); return button;
    }
    public static void StyleSaveButton(Button button, float fontSize = 23)
    {
        button.transition = Selectable.Transition.None;
        var size = button.GetComponent<RectTransform>().sizeDelta;
        var image = button.GetComponent<Image>();
        image.canvasRenderer.SetColor(Color.white);
        image.sprite = Sprite("SaveButton");
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = image.sprite.rect.height / size.y;
        var feedback = button.GetComponent<HomeButtonFeedback>();
        feedback.normalPart = "SaveButton"; feedback.activePart = "SaveActive"; feedback.animateScale = false;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.rectTransform.sizeDelta = new Vector2(size.x - 100 * size.y / 68f, size.y * 44 / 68f);
        label.richText = false; label.fontStyle = FontStyles.Normal;
        label.fontSize = fontSize; label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(14, fontSize); label.fontSizeMax = fontSize;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
    }
    public static void StyleInputField(TMP_InputField input, bool search = false)
    {
        if (!input || !input.textComponent || !input.textViewport) return;
        var image = input.targetGraphic as Image;
        if (image)
        {
            float height = Mathf.Max(1, image.rectTransform.rect.height);
            image.sprite = Sprite("InputField"); image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = image.sprite ? image.sprite.rect.height / height : 1;
            image.material = null; image.color = Color.white; image.canvasRenderer.SetColor(Color.white);
            image.raycastTarget = true;
            input.transition = Selectable.Transition.None;
            float padding = Mathf.Clamp(height * .28f, 10, 26);
            float left = padding;
            if (search)
            {
                var glyph = input.GetComponentInChildren<WorkbenchGlyph>();
                float glyphSize = height * .46f;
                if (glyph)
                {
                    var rect = (RectTransform)glyph.transform;
                    rect.pivot = rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                    rect.sizeDelta = new Vector2(glyphSize, glyphSize);
                    rect.anchoredPosition = new Vector2(padding, 0);
                    glyph.color = Cream; glyph.raycastTarget = false;
                }
                left += glyphSize + height * .16f;
            }
            var viewport = input.textViewport;
            viewport.pivot = new Vector2(.5f, .5f);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            float vertical = Mathf.Clamp(height * .05f, 2, 4);
            viewport.offsetMin = new Vector2(left, vertical);
            viewport.offsetMax = new Vector2(-padding, -vertical);
            var mask = viewport.GetComponent<RectMask2D>();
            if (mask) mask.softness = Vector2Int.zero;
        }
        var text = input.textComponent;
        text.rectTransform.pivot = new Vector2(.5f, .5f);
        Stretch(text.rectTransform);
        text.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        text.color = Cream; text.richText = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableAutoSizing = false; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        if (input.placeholder is TextMeshProUGUI placeholder)
        {
            placeholder.rectTransform.pivot = new Vector2(.5f, .5f);
            Stretch(placeholder.rectTransform);
            placeholder.font = text.font; placeholder.alignment = text.alignment;
            placeholder.color = new Color32(189, 164, 134, 255);
            placeholder.margin = new Vector4(Mathf.Max(11, placeholder.margin.x), 0, 0, 0);
        }
        LichtfadenCaret.Apply(input);
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

public sealed class HomeButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    public bool primary;
    public bool selectionManaged;
    public bool pressOnlySelection;
    public string normalPart = "Button", activePart = "Active";
    public bool animateScale = true;
    bool hovered, selected, pointerHeld;
    float amount, glow, pressed, fill;
    Button button; Image image, highlight, chosenImage;
    void Awake()
    {
        button = GetComponent<Button>(); image = GetComponent<Image>();
        highlight = HomeUi.Rect("Highlight", transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        HomeUi.Stretch(highlight.rectTransform); highlight.transform.SetAsFirstSibling();
        highlight.raycastTarget = false; highlight.color = new Color(1, 1, 1, 0);
        chosenImage = HomeUi.Rect("Selection", transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        HomeUi.Stretch(chosenImage.rectTransform); chosenImage.transform.SetSiblingIndex(1);
        chosenImage.raycastTarget = false; chosenImage.color = new Color(1, 1, 1, 0);
    }
    public void OnPointerEnter(PointerEventData data) => hovered = true;
    public void OnPointerExit(PointerEventData data) => hovered = false;
    public void OnSelect(BaseEventData data) => selected = true;
    public void OnDeselect(BaseEventData data) => selected = false;
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && button.IsInteractable()) pointerHeld = true; }
    public void OnPointerUp(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pointerHeld = false; }
    void OnApplicationFocus(bool focused) { if (!focused) pointerHeld = false; }
    public void Click() => pressed = .12f;
    void OnDisable() { pointerHeld = hovered = selected = false; amount = glow = pressed = fill = 0; transform.localScale = Vector3.one; if (highlight) highlight.color = new Color(1, 1, 1, 0); if (chosenImage) chosenImage.color = new Color(1, 1, 1, 0); }
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
        fill = pressOnlySelection
            ? button.IsInteractable() && (pointerHeld && hovered || selected && Input.GetButton("Submit")) ? 1 : 0
            : Mathf.Lerp(fill, button.IsInteractable() && (primary || !selectionManaged && selected && !hovered) ? 1 : 0,
                1 - Mathf.Exp(-9 * Time.unscaledDeltaTime));
        chosenImage.sprite = HomeUi.Sprite(normalPart.StartsWith("SelectionCard") ? "SelectionCardSelected" : "SaveSelected"); chosenImage.type = image.type;
        chosenImage.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        chosenImage.preserveAspect = image.preserveAspect;
        chosenImage.color = new Color(1, 1, 1, fill);
        highlight.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        highlight.preserveAspect = image.preserveAspect;
        highlight.color = new Color(1, 1, 1, glow);
        image.color = button.interactable ? Color.Lerp(Color.white, new Color(1, .93f, .76f), amount * .2f) : new Color(.42f, .42f, .42f, .8f);
    }
}
