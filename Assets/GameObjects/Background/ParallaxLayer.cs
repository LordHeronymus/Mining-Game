using System.Collections.Generic;
using UnityEngine;

/// <summary>Repeats an ordered panorama horizontally, using full sprite rectangles.</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class ParallaxLayer : MonoBehaviour
{
    [Header("Panorama (left to right)")]
    [Tooltip("One seamless panorama, or consecutive segments forming one seamless cycle.")]
    public Sprite[] segments = new Sprite[0];
    [Min(0), InspectorName("Horizontale Anzahl"), Tooltip("Anzahl der vollständigen Panoramen: 1 = einmal, 3 = dreimal. 0 = endlos. Begrenzte Wiederholungen werden gemeinsam um die Ebenenmitte zentriert.")]
    public int horizontalCount = -1;
    // Preserve serialized toggle settings when loading scenes created before the count control.
    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("repeatHorizontally")]
    bool legacyRepeatHorizontally = true;
    [Min(0.01f), Tooltip("Full image height, including transparent pixels, in local world units.")]
    public float height = 20f;
    [Tooltip("Horizontal offset relative to this layer's Transform.")]
    public float horizontalOffset;
    [Tooltip("Vertical offset relative to this layer's Transform. Positive moves up.")]
    public float verticalOffset;

    [Header("Parallax: 0 = screen fixed, 1 = world fixed")]
    [Range(0f, 1f)] public float horizontalParallax = 0.25f;
    [Range(0f, 1f)] public float verticalParallax = 0.25f;

    [Header("Rendering")]
    public string sortingLayerName = "Background";
    public int sortingOrder;
    public Color tint = Color.white;
    [Range(0f, 1f), Tooltip("Deckkraft dieser Ebene: 0 = unsichtbar, 1 = vollständig sichtbar. Wird mit globaler Opacity, Tint-Alpha und Tiefen-Ausblendung multipliziert.")]
    public float opacity = 1f;
    [Tooltip("Shared unlit sprite material. Assign an asset so the shader is included in builds.")]
    public Material material;
    [Tooltip("Extend the bottom pixel row down to the camera edge. Use for the sky to avoid a visible rectangular lower edge behind translucent scenery.")]
    public bool extendBottomToCamera;

    [Header("Untergrund")]
    public Sprite undergroundTile;
    [InspectorName("Layer-2-Bild")] public Sprite undergroundLayer2Tile;
    [InspectorName("Layer-3-Bild")] public Sprite undergroundLayer3Tile;
    [Min(0)] public int undergroundHorizontalCount;
    [Min(0)] public int undergroundVerticalCount;
    [InspectorName("Y-Versatz (Bildpixel)")]
    public float undergroundYOffsetPixels = 1f;

    readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
    readonly Dictionary<Sprite, Sprite> bottomEdges = new Dictionary<Sprite, Sprite>();
    MaterialPropertyBlock lightingProperties;
    SurfaceBackgroundController controller;
    GameObject generatedRoot;

    public IReadOnlyList<SpriteRenderer> Renderers => renderers;
    public Color LightingTint { get; set; } = Color.white;
    public bool IsBottomEdge(Sprite sprite) => bottomEdges.ContainsValue(sprite);

    void OnEnable()
    {
        if (horizontalCount < 0) horizontalCount = legacyRepeatHorizontally ? 0 : 1;
        controller = GetComponentInParent<SurfaceBackgroundController>();
        Refresh();
    }
    void OnTransformParentChanged() => controller = GetComponentInParent<SurfaceBackgroundController>();
    void LateUpdate() => Refresh();
    void OnDisable() => Release();
    void OnDestroy() => Release();

    /// <summary>Updates the preview and runtime pool without changing authored transforms.</summary>
    public void Refresh()
    {
        if (!isActiveAndEnabled) return;
        if (!controller) controller = GetComponentInParent<SurfaceBackgroundController>();
        Camera camera = controller ? controller.RenderCamera : null;
        Vector3 scale = transform.lossyScale;
        if (!camera || !camera.orthographic || segments == null || segments.Length == 0 ||
            height <= 0f || scale.x <= 0f || scale.y <= 0f ||
            Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.01f)
        {
            HideUnused(0);
            return;
        }

        float imageHeight = height * controller.EffectiveZoom;
        float cycleWidth = 0f;
        foreach (Sprite sprite in segments)
        {
            if (!sprite || sprite.rect.height <= 0f) { HideUnused(0); return; }
            cycleWidth += imageHeight * sprite.rect.width / sprite.rect.height * scale.x;
        }
        if (cycleWidth <= 0.0001f || float.IsNaN(cycleWidth) || float.IsInfinity(cycleWidth))
        {
            HideUnused(0);
            return;
        }

        Vector3 anchor = transform.TransformPoint(new Vector3(horizontalOffset, verticalOffset, 0f));
        anchor.y += controller.verticalOffset;
        Vector2 delta = controller.GetParallaxCameraDelta(camera);
        anchor.x += delta.x * (1f - Mathf.Clamp01(horizontalParallax));
        anchor.y += delta.y * (1f - Mathf.Clamp01(verticalParallax));

        // Viewport corners also cover a rotated orthographic camera and aspect/zoom changes.
        float distance = Vector3.Dot(anchor - camera.transform.position, camera.transform.forward);
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        float viewBottom = float.PositiveInfinity, viewTop = float.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 cornerPosition = camera.ViewportToWorldPoint(new Vector3(corner % 2, corner / 2, distance));
            left = Mathf.Min(left, cornerPosition.x);
            right = Mathf.Max(right, cornerPosition.x);
            viewBottom = Mathf.Min(viewBottom, cornerPosition.y);
            viewTop = Mathf.Max(viewTop, cornerPosition.y);
        }

        // Work relative to the camera, so negative coordinates and teleports need no catch-up loop.
        double cycleStart = anchor.x - cycleWidth * 0.5;
        int count = Mathf.Max(0, horizontalCount);
        double first = count == 0 ? System.Math.Floor((left - cycleStart) / cycleWidth) - 1.0 : -(count - 1) * 0.5;
        int cycles = count == 0 ? Mathf.CeilToInt((right - left) / cycleWidth) + 3 : count;
        float worldHeight = imageHeight * scale.y;
        float globalOpacity = controller.isActiveAndEnabled ? Mathf.Clamp01(controller.opacity) : 0f;
        Color color = tint * LightingTint;
        color.a *= Mathf.Clamp01(opacity) * controller.GetOpacity(camera);
        Color undergroundColor = tint * LightingTint;
        undergroundColor.a *= Mathf.Clamp01(opacity) * globalOpacity;
        if (color.a <= 0f && ((!undergroundTile && !undergroundLayer2Tile && !undergroundLayer3Tile) || undergroundColor.a <= 0f))
        {
            HideUnused(0);
            return;
        }

        EnsureRoot();
        int used = 0;
        if (color.a > 0f)
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            double x = cycleStart + (first + cycle) * cycleWidth;
            foreach (Sprite sprite in segments)
            {
                float width = imageHeight * sprite.rect.width / sprite.rect.height * scale.x;
                SpriteRenderer renderer = GetRenderer(used++);
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = color;
                renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = sortingOrder;
                renderer.gameObject.layer = gameObject.layer;

                // Compensate for arbitrary pivots; transparent margins remain part of the width.
                float pivotX = sprite.pivot.x / sprite.rect.width;
                float pivotY = sprite.pivot.y / sprite.rect.height;
                renderer.transform.position = new Vector3((float)(x + width * pivotX),
                    anchor.y + worldHeight * (pivotY - 0.5f), anchor.z);
                float spriteScale = imageHeight * sprite.pixelsPerUnit / sprite.rect.height;
                renderer.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
                ApplySurfaceLighting(renderer);
                renderer.enabled = true;
                float imageBottom = anchor.y - worldHeight * 0.5f;
                if (extendBottomToCamera && imageBottom > viewBottom - 1f)
                {
                    Sprite edge = GetBottomEdge(sprite);
                    SpriteRenderer fill = GetRenderer(used++);
                    fill.sprite = edge;
                    fill.sharedMaterial = material;
                    fill.color = color;
                    fill.sortingLayerName = sortingLayerName;
                    fill.sortingOrder = sortingOrder;
                    fill.gameObject.layer = gameObject.layer;
                    // Top-pivoted strip shares the exact boundary with the panorama.
                    fill.transform.position = new Vector3((float)(x + width * 0.5f), imageBottom, anchor.z);
                    fill.transform.localScale = new Vector3(
                        width * edge.pixelsPerUnit / edge.rect.width / scale.x,
                        (imageBottom - viewBottom + 1f) * edge.pixelsPerUnit / scale.y, 1f);
                    ApplySurfaceLighting(fill);
                    fill.enabled = true;
                }
                x += width;
            }
        }
        // The fixed background owns the underground when present; this layer then
        // remains responsible only for the surface panorama.
        bool fixedUnderground = controller.GetComponentInChildren<FixedUndergroundBackground>(true) is { isActiveAndEnabled: true };
        if (undergroundColor.a > 0f && !fixedUnderground)
        {
            var configured = controller.MapLayers;
            bool useConfigured = undergroundTile && configured != null && configured.Length > 0;
            if (useConfigured)
                foreach (var layer in configured)
                    if (layer == null || !layer.backgroundSprite) { useConfigured = false; break; }
            if (useConfigured)
            {
                int firstVisible = 0;
                for (int i = 1; i < configured.Length; i++)
                    if (controller.GetLayerBlend(i) >= 1f) firstVisible = i;
                for (int i = firstVisible; i < configured.Length; i++)
                {
                    float blend = i == 0 ? 1f : controller.GetLayerBlend(i);
                    if (blend <= 0f) continue;
                    Color layerColor = undergroundColor;
                    layerColor.a *= blend;
                    DrawUnderground(configured[i].backgroundSprite, anchor, cycleWidth, worldHeight,
                        scale, left, right, viewTop, viewBottom, layerColor,
                        sortingOrder + i * 2 - 1, i == 0, ref used);
                }
            }
            else
            {
                float layer2Blend = undergroundLayer2Tile ? controller.GetLayer2Blend() : 0f;
                float layer3Blend = undergroundLayer3Tile ? controller.GetLayer3Blend() : 0f;
                if (layer3Blend < 1f)
                {
                    if (undergroundTile && layer2Blend < 1f)
                        DrawUnderground(undergroundTile, anchor, cycleWidth, worldHeight,
                            scale, left, right, viewTop, viewBottom, undergroundColor, sortingOrder - 1,
                            true, ref used);
                    if (undergroundLayer2Tile && layer2Blend > 0f)
                    {
                        Color layer2Color = undergroundColor;
                        layer2Color.a *= layer2Blend;
                        DrawUnderground(undergroundLayer2Tile, anchor, cycleWidth, worldHeight,
                            scale, left, right, viewTop, viewBottom, layer2Color, sortingOrder + 2,
                            false, ref used);
                    }
                }
                if (undergroundLayer3Tile && layer3Blend > 0f)
                {
                    Color layer3Color = undergroundColor;
                    layer3Color.a *= layer3Blend;
                    DrawUnderground(undergroundLayer3Tile, anchor, cycleWidth, worldHeight,
                        scale, left, right, viewTop, viewBottom, layer3Color, sortingOrder + 3,
                        false, ref used);
                }
            }
        }
        HideUnused(used);
    }

    void DrawUnderground(Sprite sprite, Vector3 anchor, float surfaceWidth, float surfaceHeight,
        Vector3 scale, float left, float right, float viewTop, float viewBottom,
        Color color, int order, bool useSurfaceAppearance, ref int used)
    {
        if (sprite.rect.width <= 0f || sprite.rect.height <= 0f) return;

        float tileWidth = surfaceWidth;
        float tileHeight = tileWidth * sprite.rect.height / sprite.rect.width * scale.y / scale.x;
        if (tileHeight <= 0f || float.IsNaN(tileHeight) || float.IsInfinity(tileHeight)) return;
        float top = anchor.y - surfaceHeight * 0.5f +
            undergroundYOffsetPixels * tileHeight / sprite.rect.height;
        int firstRow = Mathf.Max(0, Mathf.FloorToInt((top - viewTop) / tileHeight));
        int lastRow = Mathf.Max(0, Mathf.CeilToInt((top - viewBottom) / tileHeight));
        if (undergroundVerticalCount > 0)
            lastRow = Mathf.Min(lastRow, undergroundVerticalCount);

        int horizontalCopies = Mathf.Max(0, undergroundHorizontalCount);
        double start = anchor.x - tileWidth * 0.5;
        double firstCycle = horizontalCopies == 0
            ? System.Math.Floor((left - start) / tileWidth) - 1.0
            : -(horizontalCopies - 1) * 0.5;
        int cycles = horizontalCopies == 0
            ? Mathf.CeilToInt((right - left) / tileWidth) + 3
            : horizontalCopies;
        float xScale = tileWidth * sprite.pixelsPerUnit / sprite.rect.width / scale.x;
        float yScale = tileHeight * sprite.pixelsPerUnit / sprite.rect.height / scale.y;
        float pivotX = sprite.pivot.x / sprite.rect.width;
        float pivotY = sprite.pivot.y / sprite.rect.height;
        for (int row = firstRow; row < lastRow; row++)
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            SpriteRenderer renderer = GetRenderer(used++);
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = color;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = order;
            renderer.gameObject.layer = gameObject.layer;
            renderer.transform.position = new Vector3(
                (float)(start + (firstCycle + cycle) * tileWidth + tileWidth * pivotX),
                top - row * tileHeight - tileHeight * (1f - pivotY), anchor.z);
            renderer.transform.localScale = new Vector3(xScale, yScale, 1f);
            ApplyUndergroundLighting(renderer, useSurfaceAppearance);
            renderer.enabled = true;
        }
    }

    void ApplySurfaceLighting(SpriteRenderer renderer)
    {
        if (lightingProperties == null) lightingProperties = new MaterialPropertyBlock();
        Bounds bounds = renderer.bounds;
        lightingProperties.Clear();
        lightingProperties.SetFloat("_LightBottomY", bounds.min.y);
        lightingProperties.SetFloat("_LightTopY", bounds.max.y);
        lightingProperties.SetFloat("_LightBottom", controller.GetSurfaceBackgroundBrightness());
        lightingProperties.SetFloat("_LightTop", controller.GetSurfaceBackgroundBrightness());
        lightingProperties.SetFloat("_Contrast", Mathf.Clamp(controller.surfaceContrast, 0f, 2f));
        lightingProperties.SetFloat("_Saturation", Mathf.Clamp(controller.surfaceSaturation, 0f, 2f));
        renderer.SetPropertyBlock(lightingProperties);
    }

    void ApplyUndergroundLighting(SpriteRenderer renderer, bool useSurfaceColorAdjustment)
    {
        if (lightingProperties == null) lightingProperties = new MaterialPropertyBlock();
        Bounds bounds = renderer.bounds;
        lightingProperties.Clear();
        lightingProperties.SetFloat("_LightBottomY", bounds.min.y);
        lightingProperties.SetFloat("_LightTopY", bounds.max.y);
        lightingProperties.SetFloat("_LightBottom", 1f);
        lightingProperties.SetFloat("_LightTop", 1f);
        lightingProperties.SetFloat("_Contrast", useSurfaceColorAdjustment ? Mathf.Clamp(controller.surfaceContrast, 0f, 2f) : 1f);
        lightingProperties.SetFloat("_Saturation", useSurfaceColorAdjustment ? Mathf.Clamp(controller.surfaceSaturation, 0f, 2f) : 1f);
        renderer.SetPropertyBlock(lightingProperties);
    }

    Sprite GetBottomEdge(Sprite source)
    {
        if (bottomEdges.TryGetValue(source, out Sprite edge) && edge) return edge;
        Rect rect = source.textureRect;
        rect.height = 1f;
        edge = Sprite.Create(source.texture, rect, new Vector2(0.5f, 1f),
            source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        edge.name = source.name + " (bottom continuation)";
        edge.hideFlags = HideFlags.HideAndDontSave;
        bottomEdges[source] = edge;
        return edge;
    }

    void EnsureRoot()
    {
        if (generatedRoot) return;
        renderers.Clear();
        generatedRoot = new GameObject("Parallax preview (generated)");
        generatedRoot.hideFlags = HideFlags.HideAndDontSave;
        generatedRoot.transform.SetParent(transform, false);
    }

    SpriteRenderer GetRenderer(int index)
    {
        if (index < renderers.Count && renderers[index]) return renderers[index];
        var child = new GameObject("Panorama tile");
        child.hideFlags = HideFlags.HideAndDontSave;
        child.transform.SetParent(generatedRoot.transform, false);
        var renderer = child.AddComponent<SpriteRenderer>();
        if (index < renderers.Count) renderers[index] = renderer;
        else renderers.Add(renderer);
        return renderer;
    }

    void HideUnused(int used)
    {
        for (int i = used; i < renderers.Count; i++)
            if (renderers[i]) renderers[i].enabled = false;
    }

    void Release()
    {
        if (generatedRoot)
        {
            generatedRoot.SetActive(false);
            if (Application.isPlaying) Destroy(generatedRoot);
            else DestroyImmediate(generatedRoot);
        }
        generatedRoot = null;
        renderers.Clear();
        foreach (Sprite edge in bottomEdges.Values)
        {
            if (!edge) continue;
            if (Application.isPlaying) Destroy(edge);
            else DestroyImmediate(edge);
        }
        bottomEdges.Clear();
    }
}
