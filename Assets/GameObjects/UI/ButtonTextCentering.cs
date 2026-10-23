using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Optical vertical centring for button captions. TMP "Midline" centres the visible glyphs, so a descender
// (g, p, y) or an umlaut moves the caption; "Middle" uses the font's line box, which ignores the text but is not
// optically centred either. This places the middle between baseline and cap height on the centre of the button's
// visible inner frame and limits the font size so that Ä and g always stay inside the gold rim.
// Horizontally, centred lines are placed by their visible ink instead of their advance widths.
// Offset, minimum gap and ink centring: GPS → UI → Knopftexte. Captions without letters or digits (+, −, ×, arrows) centre their glyphs.
[DisallowMultipleComponent]
public sealed class ButtonTextCentering : MonoBehaviour
{
    // Visible gold rim inside the slice border, measured on the source artwork (SelectionButtons-v2: rim ends 48 of
    // 55 px vertically and 170 of 230 px at the end caps; SelectionCards-v2: 87 of 125 px).
    const float WideRimVertical = 48f / 55, WideRimHorizontal = 170f / 230, CardRim = 87f / 125;
    static readonly string[] Ascenders = { "Ä", "Ö", "Ü", "É", "f", "l" }, Descenders = { "g", "j", "p", "q", "y" };
    static readonly Dictionary<TMP_FontAsset, Vector2> fontExtents = new();

    TMP_Text text;
    Image frame;
    float requestedSize = -1, appliedSize = -1, fittedHeight = -1;
    bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => fontExtents.Clear();

    public Image Frame => frame;
    public float AppliedShift { get; private set; }
    public float AppliedShiftX { get; private set; }

    // Centres every caption of the button: vertically centred labels whose box covers the middle of the frame.
    // Counts, hotkey numbers and card titles sit elsewhere in the button and keep their own alignment.
    public static void Apply(Selectable button, Image background = null)
    {
        if (!button) return;
        var image = background ? background : button.targetGraphic as Image;
        if (!image) image = button.GetComponent<Image>();
        if (!image) return;
        foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            if (IsCaption(label, image.rectTransform)) Apply(label, image);
    }

    public static ButtonTextCentering Apply(TMP_Text label, Image background)
    {
        if (!label) return null;
        var centering = label.GetComponent<ButtonTextCentering>();
        if (!centering) centering = label.gameObject.AddComponent<ButtonTextCentering>();
        centering.Configure(label, background);
        return centering;
    }

    public static bool IsCaption(TMP_Text label, RectTransform frame)
    {
        if (!label || !frame) return false;
        var vertical = label.verticalAlignment;
        if (vertical != VerticalAlignmentOptions.Middle && vertical != VerticalAlignmentOptions.Geometry && vertical != VerticalAlignmentOptions.Capline) return false;
        var box = label.rectTransform.rect;
        float top = frame.InverseTransformPoint(label.rectTransform.TransformPoint(new Vector3(0, box.yMax))).y;
        float bottom = frame.InverseTransformPoint(label.rectTransform.TransformPoint(new Vector3(0, box.yMin))).y;
        var rect = frame.rect;
        return top - bottom >= rect.height * .4f && Mathf.Abs((top + bottom) / 2 - rect.center.y) <= rect.height * .2f;
    }

    // Inner edge of the visible frame in the image's local space: left, top, right, bottom insets.
    public static Vector4 FrameInsets(Image image)
    {
        var sprite = image ? image.sprite : null;
        if (!sprite || image.color.a <= .05f) return Vector4.zero;
        var border = sprite.border;
        bool wide = sprite.name.StartsWith("Save"), card = sprite.name.StartsWith("SelectionCard");
        float vertical = wide ? WideRimVertical : card ? CardRim : 1, horizontal = wide ? WideRimHorizontal : card ? CardRim : 1;
        var insets = new Vector4(border.x * horizontal, border.w * vertical, border.z * horizontal, border.y * vertical);
        if (image.type == Image.Type.Sliced || image.type == Image.Type.Tiled)
            return insets / Mathf.Max(.0001f, image.pixelsPerUnitMultiplier * sprite.pixelsPerUnit / 100f);
        var rect = image.rectTransform.rect; var size = sprite.rect.size;
        return new Vector4(insets.x / size.x * rect.width, insets.y / size.y * rect.height, insets.z / size.x * rect.width, insets.w / size.y * rect.height);
    }

    public static Rect InnerFrame(Image image)
    {
        var rect = image.rectTransform.rect; var insets = FrameInsets(image);
        return Rect.MinMaxRect(rect.xMin + insets.x, rect.yMin + insets.w, rect.xMax - insets.z, rect.yMax - insets.y);
    }

    void Configure(TMP_Text label, Image background)
    {
        text = label; frame = background;
        text.verticalAlignment = VerticalAlignmentOptions.Middle;
        if (isActiveAndEnabled) { Subscribe(); Refresh(); }
    }

    void OnEnable() { if (text) { Subscribe(); Refresh(); } }
    void OnDisable()
    {
        if (!subscribed) return;
        subscribed = false;
        if (text) text.OnPreRenderText -= Shift;
        GpsSettings.Changed -= Refresh;
    }
    void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        text.OnPreRenderText += Shift;
        GpsSettings.Changed += Refresh;
    }
    // The frame artwork settles after the first frames (GoldButtonFeedback sets its pixel density in LateUpdate,
    // layouts resize buttons), so the fit follows changes of the visible inner height.
    void LateUpdate()
    {
        if (!frame) return;
        float height = InnerFrame(frame).height;
        if (Mathf.Abs(height - fittedHeight) > .01f) Refresh();
    }

    void Refresh()
    {
        if (!text) return;
        FitSize();
        text.SetVerticesDirty();
    }

    // Largest font size at which the highest umlaut and the lowest descender fit around the centred cap line.
    void FitSize()
    {
        if (!frame) return;
        var inner = InnerFrame(frame);
        fittedHeight = inner.height;
        if (!text.font) return;
        float current = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
        if (requestedSize < 0 || Mathf.Abs(current - appliedSize) > .01f) requestedSize = current;
        float height = Mathf.Max(0, text.rectTransform.InverseTransformVector(frame.rectTransform.TransformVector(new Vector3(0, inner.height))).y);
        var face = text.font.faceInfo;
        var extents = Extents(text.font);
        float half = Mathf.Max(extents.x - face.capLine / 2, face.capLine / 2 - extents.y);
        float perPoint = face.scale / Mathf.Max(1, face.pointSize);
        float available = height - 2 * Mathf.Max(0, GpsSettings.Preferences.buttonTextPadding);
        float size = requestedSize;
        if (half > 0 && available > 0) size = Mathf.Min(size, available / (2 * half * perPoint));
        size = Mathf.Max(1, Mathf.Floor(size * 10) / 10);
        appliedSize = size;
        if (text.enableAutoSizing) { text.fontSizeMax = size; text.fontSizeMin = Mathf.Min(text.fontSizeMin, size); }
        else text.fontSize = size;
        KeepInsideSides(inner);
    }

    // The caption box must not reach into the end caps of the frame.
    void KeepInsideSides(Rect inner)
    {
        var rect = text.rectTransform;
        float left = rect.InverseTransformPoint(frame.rectTransform.TransformPoint(new Vector3(inner.xMin, inner.center.y))).x;
        float right = rect.InverseTransformPoint(frame.rectTransform.TransformPoint(new Vector3(inner.xMax, inner.center.y))).x;
        var margin = text.margin;
        margin.x = Mathf.Max(margin.x, left - rect.rect.xMin);
        margin.z = Mathf.Max(margin.z, rect.rect.xMax - right);
        if (margin != text.margin) text.margin = margin;
    }

    // Highest glyph top (x) and lowest glyph bottom (y) of the probe characters, in font units.
    static Vector2 Extents(TMP_FontAsset font)
    {
        if (fontExtents.TryGetValue(font, out var cached)) return cached;
        var face = font.faceInfo;
        var result = new Vector2(face.capLine, 0);
        foreach (string s in Ascenders)
            if (font.HasCharacter(s[0], false, true) && font.characterLookupTable.TryGetValue(s[0], out var c))
                result.x = Mathf.Max(result.x, c.glyph.metrics.horizontalBearingY);
        foreach (string s in Descenders)
            if (font.HasCharacter(s[0], false, true) && font.characterLookupTable.TryGetValue(s[0], out var c))
                result.y = Mathf.Min(result.y, c.glyph.metrics.horizontalBearingY - c.glyph.metrics.height);
        if (result.y == 0) result.y = face.descentLine;
        fontExtents[font] = result;
        return result;
    }

    void Shift(TMP_TextInfo info)
    {
        AppliedShift = 0;
        int first = -1, last = -1; bool letters = false;
        float inkTop = float.MinValue, inkBottom = float.MaxValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || c.textElement == null) continue;
            if (first < 0) first = i;
            last = i;
            letters |= char.IsLetterOrDigit(c.character);
            var m = c.textElement.glyph.metrics;
            inkTop = Mathf.Max(inkTop, c.baseLine + m.horizontalBearingY * c.scale);
            inkBottom = Mathf.Min(inkBottom, c.baseLine + (m.horizontalBearingY - m.height) * c.scale);
        }
        if (first < 0) return;
        var head = info.characterInfo[first];
        float top = letters ? head.baseLine + head.fontAsset.faceInfo.capLine * head.scale : inkTop;
        float bottom = letters ? info.characterInfo[last].baseLine : inkBottom;
        float target;
        var rect = text.rectTransform;
        if (frame)
            target = rect.InverseTransformPoint(frame.rectTransform.TransformPoint(InnerFrame(frame).center)).y;
        else
        {
            var margin = text.margin; var box = rect.rect;
            target = (box.yMin + margin.w + box.yMax - margin.y) / 2;
        }
        target += GpsSettings.Preferences.buttonTextOffset * head.pointSize;
        float shift = target - (top + bottom) / 2;
        AppliedShiftX = 0;
        if (GpsSettings.Preferences.buttonTextInkCenterX) CentreInk(info);
        if (Mathf.Abs(shift) < .001f) return;
        AppliedShift = shift;
        var offset = new Vector3(0, shift, 0);
        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            var vertices = info.meshInfo[m].vertices;
            if (vertices == null) continue;
            for (int v = 0; v < info.meshInfo[m].vertexCount; v++) vertices[v] += offset;
        }
        for (int i = 0; i < info.characterCount; i++)
        {
            ref var c = ref info.characterInfo[i];
            c.bottomLeft += offset; c.topLeft += offset; c.topRight += offset; c.bottomRight += offset;
            c.baseLine += shift; c.ascender += shift; c.descender += shift;
        }
        for (int l = 0; l < info.lineCount; l++)
        {
            ref var line = ref info.lineInfo[l];
            line.baseline += shift; line.ascender += shift; line.descender += shift;
        }
    }

    // TMP centres a line by its advance widths, which include the side bearings of the first and last glyph
    // (the space right of "n" is wider than left of "W"). Each centred line moves so that its visible ink sits
    // where TMP put the middle of its advances.
    void CentreInk(TMP_TextInfo info)
    {
        if (text.horizontalAlignment != HorizontalAlignmentOptions.Center) return;
        for (int l = 0; l < info.lineCount; l++)
        {
            var line = info.lineInfo[l];
            float inkLeft = float.MaxValue, inkRight = float.MinValue, advanceLeft = float.MaxValue, advanceRight = float.MinValue;
            for (int i = line.firstVisibleCharacterIndex; i >= 0 && i <= line.lastVisibleCharacterIndex && i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible || c.textElement == null) continue;
                var m = c.textElement.glyph.metrics;
                inkLeft = Mathf.Min(inkLeft, c.origin + m.horizontalBearingX * c.scale);
                inkRight = Mathf.Max(inkRight, c.origin + (m.horizontalBearingX + m.width) * c.scale);
                advanceLeft = Mathf.Min(advanceLeft, c.origin);
                advanceRight = Mathf.Max(advanceRight, c.xAdvance);
            }
            if (inkLeft > inkRight) continue;
            float shift = (advanceLeft + advanceRight) / 2 - (inkLeft + inkRight) / 2;
            if (Mathf.Abs(shift) < .001f) continue;
            if (l == 0) AppliedShiftX = shift;
            var offset = new Vector3(shift, 0, 0);
            for (int i = line.firstCharacterIndex; i <= line.lastCharacterIndex && i < info.characterCount; i++)
            {
                ref var c = ref info.characterInfo[i];
                c.bottomLeft += offset; c.topLeft += offset; c.topRight += offset; c.bottomRight += offset;
                c.origin += shift; c.xAdvance += shift;
                if (!c.isVisible) continue;
                var vertices = info.meshInfo[c.materialReferenceIndex].vertices;
                if (vertices == null) continue;
                for (int v = 0; v < 4; v++) vertices[c.vertexIndex + v] += offset;
            }
        }
    }
}
