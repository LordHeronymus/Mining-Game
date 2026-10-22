using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Logical selection is separate from pointer hover and keyboard focus.
public sealed class GoldButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public bool Chosen { get; private set; }
    Button button;
    Image image, highlight, fillLayer;
    Sprite normal, hover, chosenSprite, requestedNormal, requestedHover;
    Material artworkMaterial, artworkHoverMaterial, artworkSelectedMaterial;
    bool card, labelsStyled;
    bool hovered, focused, selectionManaged;
    float glow, fill;

    public static GoldButtonFeedback Apply(Button button, Sprite normal, Sprite hover, Image background = null)
    {
        var feedback = button.GetComponent<GoldButtonFeedback>();
        if (!feedback) feedback = button.gameObject.AddComponent<GoldButtonFeedback>();
        feedback.Configure(button, background ? background : button.GetComponent<Image>(), normal, hover);
        return feedback;
    }
    public static void Select(Image image, bool chosen)
    {
        var feedback = image ? image.GetComponent<GoldButtonFeedback>() : null;
        if (feedback) feedback.SetChosen(chosen);
    }
    void Configure(Button owner, Image background, Sprite baseSprite, Sprite hoverSprite)
    {
        if (button == owner && image == background && requestedNormal == baseSprite && requestedHover == hoverSprite) return;
        button = owner; image = background; requestedNormal = baseSprite; requestedHover = hoverSprite;
        var size = image.rectTransform.rect.size;
        card = size.x < size.y * 3.2f;
        labelsStyled = false;
        normal = HomeUi.Sprite(card ? "SelectionCardNormal" : "SaveButton");
        hover = HomeUi.Sprite(card ? "SelectionCardHover" : "SaveActive");
        chosenSprite = HomeUi.Sprite(card ? "SelectionCardSelected" : "SaveSelected");
        button.transition = Selectable.Transition.None;
        if (button.targetGraphic) button.targetGraphic.canvasRenderer.SetColor(Color.white);
        image.canvasRenderer.SetColor(Color.white);
        image.sprite = normal; image.color = Color.white; image.material = null;
        image.type = Image.Type.Sliced; image.preserveAspect = false;
        if (!highlight)
        {
            highlight = HomeUi.Rect("Gold Feedback", image.transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            HomeUi.Stretch(highlight.rectTransform); highlight.transform.SetAsFirstSibling();
            highlight.raycastTarget = false;
        }
        highlight.sprite = hover;
        if (!fillLayer)
        {
            fillLayer = HomeUi.Rect("Gold Fill", image.transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            HomeUi.Stretch(fillLayer.rectTransform); fillLayer.raycastTarget = false;
        }
        fillLayer.transform.SetSiblingIndex(highlight.transform.GetSiblingIndex() + 1);
        fillLayer.sprite = chosenSprite;
        fillLayer.color = new Color(1, 1, 1, 0);
        highlight.color = new Color(1, 1, 1, 0);
    }
    public void SetChosen(bool chosen) { selectionManaged = true; Chosen = chosen; }
    public void SetArtwork(Sprite baseSprite, Sprite hoverSprite, Sprite selectedSprite, Material baseMaterial = null, Material hoverMaterial = null, Material selectedMaterial = null)
    {
        if (!baseSprite || !hoverSprite || !selectedSprite) return;
        normal = baseSprite; hover = hoverSprite; chosenSprite = selectedSprite;
        image.sprite = normal; highlight.sprite = hover; fillLayer.sprite = chosenSprite;
        artworkMaterial = baseMaterial; artworkHoverMaterial = hoverMaterial; artworkSelectedMaterial = selectedMaterial;
        image.material = artworkMaterial; highlight.material = artworkHoverMaterial; fillLayer.material = artworkSelectedMaterial;
    }
    public void OnPointerEnter(PointerEventData data) => hovered = true;
    public void OnPointerExit(PointerEventData data) => hovered = false;
    public void OnSelect(BaseEventData data) => focused = true;
    public void OnDeselect(BaseEventData data) => focused = false;
    void LateUpdate()
    {
        if (!image || !button || !highlight) return;
        bool enabled = button.IsInteractable();
        glow = Approach(glow, enabled && (hovered || focused || Chosen) ? 1 : 0, 9);
        fill = Approach(fill, enabled && (Chosen || !selectionManaged && focused && !hovered) ? 1 : 0, 9);
        image.sprite = normal;
        image.type = Image.Type.Sliced; image.material = artworkMaterial;
        var size = image.rectTransform.rect.size;
        SetPixelsPerUnit(image, normal.rect.height / Mathf.Max(1, card ? Mathf.Min(size.x, size.y) : size.y));
        if (!labelsStyled)
        {
            labelsStyled = true;
            if (!card) ProtectLabels();
            ButtonTextCentering.Apply(button, image);
        }
        image.color = enabled ? Color.white : new Color(.45f, .45f, .45f, .8f);
        highlight.type = image.type; highlight.preserveAspect = image.preserveAspect;
        SetPixelsPerUnit(highlight, image.pixelsPerUnitMultiplier);
        fillLayer.type = image.type; fillLayer.preserveAspect = image.preserveAspect;
        SetPixelsPerUnit(fillLayer, image.pixelsPerUnitMultiplier);
        highlight.color = new Color(1, 1, 1, glow);
        fillLayer.color = new Color(1, 1, 1, fill);
    }

    // Exponential approach that settles exactly on the target, so finished fades stop dirtying the mesh.
    public static float Approach(float value, float target, float rate)
    {
        value = Mathf.Lerp(value, target, 1 - Mathf.Exp(-rate * Time.unscaledDeltaTime));
        return Mathf.Abs(value - target) < .002f ? target : value;
    }

    // Image.pixelsPerUnitMultiplier rebuilds the mesh on every assignment, even with an unchanged value.
    public static void SetPixelsPerUnit(Image target, float value)
    {
        if (target && target.pixelsPerUnitMultiplier != Mathf.Max(.01f, value)) target.pixelsPerUnitMultiplier = value;
    }
    void OnDisable()
    {
        hovered = focused = false; glow = fill = 0;
        if (highlight) highlight.color = new Color(1, 1, 1, 0);
        if (fillLayer) fillLayer.color = new Color(1, 1, 1, 0);
    }
    void ProtectLabels()
    {
        // Text is often added after Apply, so reserve the painted wood area on the first rendered frame.
        float guard = normal.border.x / image.pixelsPerUnitMultiplier + 6;
        var left = image.rectTransform.TransformPoint(new Vector3(image.rectTransform.rect.xMin + guard, 0));
        var right = image.rectTransform.TransformPoint(new Vector3(image.rectTransform.rect.xMax - guard, 0));
        foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (((int)label.alignment & 255) != 2) continue;
            var rect = label.rectTransform;
            var margin = label.margin;
            margin.x = Mathf.Max(margin.x, rect.InverseTransformPoint(left).x - rect.rect.xMin);
            margin.z = Mathf.Max(margin.z, rect.rect.xMax - rect.InverseTransformPoint(right).x);
            label.margin = margin;
            if (!label.enableAutoSizing) label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Min(14, label.fontSizeMax);
            label.enableAutoSizing = true;
        }
    }
}

