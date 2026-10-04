using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Uses TMP's own caret geometry so scrolling, IME and font metrics stay authoritative.
public sealed class LichtfadenCaret : MonoBehaviour
{
    TMP_InputField field;
    TMP_SelectionCaret nativeCaret;
    LichtfadenGraphic visual;
    Mesh sourceMesh;
    readonly List<Vector3> vertices = new List<Vector3>(4);
    float focusTime;
    int lastPosition = -1;
    string lastText;

    public static void Apply(TMP_InputField input)
    {
        if (!input || !input.textComponent) return;
        if (input.GetComponent<LichtfadenCaret>()) return;
        bool enabled = input.enabled;
        input.enabled = false;
        input.customCaretColor = true;
        input.caretColor = Color.clear;
        input.caretWidth = 1;
        input.caretBlinkRate = 0;
        input.enabled = enabled;
        input.gameObject.AddComponent<LichtfadenCaret>().field = input;
    }
    void OnEnable() => Canvas.willRenderCanvases += Render;
    void OnDisable()
    {
        Canvas.willRenderCanvases -= Render;
        if (visual) { visual.Opacity = 0; visual.SetVerticesDirty(); }
    }

    void Render()
    {
        if (!Application.isPlaying || !field || !field.textComponent) return;
        if (!nativeCaret)
        {
            nativeCaret = field.textComponent.transform.parent.GetComponentInChildren<TMP_SelectionCaret>(true);
            if (!nativeCaret) return;
        }
        if (!visual)
        {
            var root = new GameObject("Lichtfaden", typeof(RectTransform), typeof(CanvasRenderer));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(nativeCaret.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.pivot = ((RectTransform)nativeCaret.transform).pivot;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            visual = root.AddComponent<LichtfadenGraphic>();
            visual.raycastTarget = false;

        }
        bool show = field.enabled && field.isFocused && field.IsInteractable() && !field.readOnly &&
            field.selectionAnchorPosition == field.selectionFocusPosition;
        sourceMesh = nativeCaret.canvasRenderer.GetMesh();
        vertices.Clear(); if (sourceMesh) sourceMesh.GetVertices(vertices);
        if (!show || vertices.Count != 4)
        {
            focusTime = Time.unscaledTime;
            lastPosition = -1;
            visual.Opacity = 0;
        }
        else
        {
            if (lastPosition != field.caretPosition || lastText != field.text)
            {
                focusTime = Time.unscaledTime;
                lastPosition = field.caretPosition; lastText = field.text;
            }
            visual.Bottom = vertices[0]; visual.Top = vertices[1];
            float pulse = .5f + .5f * Mathf.Cos((Time.unscaledTime - focusTime) * Mathf.PI * 2 / 1.6f);
            visual.Opacity = Mathf.SmoothStep(.18f, 1, pulse);
        }
        visual.SetVerticesDirty();
        // TMP has rebuilt its mesh earlier in this canvas event; update in the same rendered frame.
        visual.Rebuild(CanvasUpdate.PreRender);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class LichtfadenGraphic : MaskableGraphic
{
    public Vector2 Bottom, Top;
    public float Opacity;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (Opacity <= 0 || Top.y <= Bottom.y) return;
        float height = Top.y - Bottom.y;
        float width = Mathf.Clamp(height * .045f, .8f, 1.7f);
        float x = Bottom.x + width * .5f;
        var gold = new Color(1, .61f, .13f, Opacity);
        var light = new Color(1, .91f, .57f, Opacity);
        // Layered alpha gradients give the line a restrained halo with the standard masked UI shader.
        Strip(vh, x, Bottom.y, Top.y, width * .5f + 3, new Color(1, .48f, .06f, Opacity * .16f));
        Strip(vh, x, Bottom.y, Top.y, width * .5f + 1.3f, new Color(1, .66f, .12f, Opacity * .35f));
        Quad(vh, x - width * .5f, Bottom.y, x + width * .5f, Top.y, gold, light);
        float radius = Mathf.Clamp(height * .06f, 1.25f, 2.1f);
        Disc(vh, new Vector2(x, Top.y), radius * 3, new Color(1, .55f, .08f, Opacity * .28f), true);
        Disc(vh, new Vector2(x, Top.y), radius, light, false);
        Disc(vh, new Vector2(x, Bottom.y), width * .7f, gold, true);
    }
    static void Strip(VertexHelper vh, float x, float bottom, float top, float radius, Color color)
    {
        var clear = new Color(color.r, color.g, color.b, 0);
        GradientQuad(vh, x - radius, bottom, x, top, clear, color);
        GradientQuad(vh, x, bottom, x + radius, top, color, clear);
    }
    static void GradientQuad(VertexHelper vh, float left, float bottom, float right, float top, Color leftColor, Color rightColor)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(left, bottom), leftColor, Vector2.zero);
        vh.AddVert(new Vector3(left, top), leftColor, Vector2.zero);
        vh.AddVert(new Vector3(right, top), rightColor, Vector2.zero);
        vh.AddVert(new Vector3(right, bottom), rightColor, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
    }
    static void Quad(VertexHelper vh, float left, float bottom, float right, float top, Color bottomColor, Color topColor)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(left, bottom), bottomColor, Vector2.zero);
        vh.AddVert(new Vector3(left, top), topColor, Vector2.zero);
        vh.AddVert(new Vector3(right, top), topColor, Vector2.zero);
        vh.AddVert(new Vector3(right, bottom), bottomColor, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
    }
    static void Disc(VertexHelper vh, Vector2 center, float radius, Color color, bool fade)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        var edge = fade ? new Color(color.r, color.g, color.b, 0) : color;
        const int segments = 16;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, edge, Vector2.zero);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}