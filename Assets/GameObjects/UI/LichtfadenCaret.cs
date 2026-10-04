using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

// Uses TMP's own caret geometry so scrolling, IME and font metrics stay authoritative.
public sealed class LichtfadenCaret : MonoBehaviour, ISelectHandler
{
    TMP_InputField field;
    TMP_SelectionCaret nativeCaret;
    LichtfadenGraphic visual;
    Mesh sourceMesh;
    readonly List<Vector3> vertices = new List<Vector3>(4);
    float focusTime;
    bool wasFocused;

    public static void Apply(TMP_InputField input)
    {
        if (!input || !input.textComponent) return;
        if (input.GetComponent<LichtfadenCaret>()) return;
        // TMP can scroll the text rect flush with the viewport, so reserve space in its own margin.
        var margin = input.textComponent.margin;
        margin.x = Mathf.Max(margin.x, 11);
        input.textComponent.margin = margin;
        bool enabled = input.enabled;
        input.enabled = false;
        input.customCaretColor = true;
        input.caretColor = Color.clear;
        input.caretWidth = 1;
        input.caretBlinkRate = 0;
        input.enabled = enabled;
        input.gameObject.AddComponent<LichtfadenCaret>().field = input;
    }
    public void OnSelect(BaseEventData eventData)
    {
        if (!field || field.readOnly) return;
        int end = field.text.Length;
        field.selectionAnchorPosition = end;
        field.selectionFocusPosition = end;
        field.caretPosition = end;
    }

    void OnEnable() => Canvas.willRenderCanvases += Render;
    void OnDisable()
    {
        Canvas.willRenderCanvases -= Render;
        wasFocused = false;
        if (visual) { visual.Opacity = 0; visual.SetVerticesDirty(); }
    }

    void Render()
    {
        if (!Application.isPlaying || !field || !field.textComponent) return;
        bool focused = field.enabled && field.isFocused && field.IsInteractable() && !field.readOnly;
        if (focused && !wasFocused) focusTime = Time.unscaledTime;
        wasFocused = focused;
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
        bool show = focused &&
            field.selectionAnchorPosition == field.selectionFocusPosition;
        sourceMesh = nativeCaret.canvasRenderer.GetMesh();
        vertices.Clear(); if (sourceMesh) sourceMesh.GetVertices(vertices);
        if (!show || vertices.Count != 4)
        {
            visual.Opacity = 0;
        }
        else
        {
            visual.Bottom = vertices[0]; visual.Top = vertices[1];
            float pulse = .5f + .5f * Mathf.Cos((Time.unscaledTime - focusTime) * Mathf.PI * 2 / 1.6f);
            visual.Opacity = Mathf.SmoothStep(0, 1, pulse);
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
    Material glowMaterial;
    protected override void Awake()
    {
        base.Awake();
        var shader = Resources.Load<Shader>("Homescreen/Lichtfaden");
        if (shader)
        {
            glowMaterial = new Material(shader) { name = "Lichtfaden Glow", hideFlags = HideFlags.DontSave };
            material = glowMaterial;
        }
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (glowMaterial) Destroy(glowMaterial);
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (Opacity <= 0 || Top.y <= Bottom.y) return;
        float height = Top.y - Bottom.y;
        if (glowMaterial)
        {
            glowMaterial.SetFloat("_CaretHeight", height);
            materialForRendering.SetFloat("_CaretHeight", height);
        }
        const float halo = 13;
        float x = Bottom.x + .5f;
        var tint = new Color(1, 1, 1, Opacity);
        // Analytic light falloff in the additive shader keeps both the core and halo smooth.
        vh.AddVert(new Vector3(x - halo, Bottom.y - halo), tint, new Vector2(-halo, -halo));
        vh.AddVert(new Vector3(x - halo, Top.y + halo), tint, new Vector2(-halo, height + halo));
        vh.AddVert(new Vector3(x + halo, Top.y + halo), tint, new Vector2(halo, height + halo));
        vh.AddVert(new Vector3(x + halo, Bottom.y - halo), tint, new Vector2(halo, -halo));
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
    }
}