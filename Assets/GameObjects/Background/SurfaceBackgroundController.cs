using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Shared camera reference and visibility for one background set.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class SurfaceBackgroundController : MonoBehaviour
{
    [System.Serializable]
    public sealed class LayerEntry
    {
        public ParallaxLayer layer;
    }

    public LayerEntry[] layers = new LayerEntry[0];
    public Camera targetCamera;
    [Tooltip("Camera position at which the authored layer positions apply. Kept fixed to avoid drift.")]
    public Vector2 cameraReferencePosition;
    [Range(0f, 1f)] public float opacity = 1f;

    [Header("Alle Hintergrundebenen")]
    [InspectorName("Y-Versatz"), Tooltip("Zusätzlicher Höhenversatz für alle Ebenen in Welteinheiten. Positiv = nach oben.")]
    public float verticalOffset;
    [Min(0.01f), Tooltip("Größenfaktor für alle Hintergrundbilder um ihre jeweilige Bildmitte. 1 = Originalgröße. Versätze und Spielkamera bleiben unverändert.")]
    public float zoom = 1f;

    [Header("Parallax-Stopp")]
    [Min(0f), InspectorName("Starttiefe (Blöcke)")]
    public float parallaxStopDepth = 20f;
    [Min(0.01f), InspectorName("Übergang (Blöcke)")]
    public float parallaxTransitionDepth = 5f;

    [Header("Untergrund Layer 2")]
    [Min(0.01f), InspectorName("Fade-Breite (Blöcke)")]
    public float layer2FadeDepth = 20f;

    [Header("Untergrund Layer 3")]
    [Min(0.01f), InspectorName("Fade-Breite (Blöcke)")]
    public float layer3FadeDepth = 20f;

    [Header("Oberflächen-Hintergrund")]
    [FormerlySerializedAs("backgroundBrightness")]
    [Range(0f, 2f), InspectorName("Helligkeit")]
    public float surfaceBrightness = 0.8f;
    [Range(0f, 2f), InspectorName("Kontrast")]
    public float surfaceContrast = 1f;
    [Range(0f, 2f), InspectorName("Sättigung")]
    public float surfaceSaturation = 1f;

    [Header("Untergrund-Hintergrund")]
    [Range(0f, 2f), InspectorName("Helligkeit")]
    public float undergroundBrightness = 0.8f;
    [Min(0.01f), InspectorName("Volle Helligkeit ab (Blöcke)")]
    public float undergroundBrightnessTransitionDepth = 30f;

    Transform player;
    MapGenerator map;
    Vector2 frozenCameraDelta;
    bool hasFrozenCameraDelta;

    [Header("Optional transition underground")]
    public bool fadeWithDepth;
    [Tooltip("World Y where the surface background is fully visible.")]
    public float fadeStartY = -5f;
    [Tooltip("World Y where the surface background is fully hidden.")]
    public float fadeEndY = -20f;

    public Camera RenderCamera => targetCamera ? targetCamera : Camera.main;
    public MapLayer[] MapLayers
    {
        get
        {
            if (!map) map = FindFirstObjectByType<MapGenerator>();
            return map ? map.layers : null;
        }
    }
    public float EffectiveZoom => float.IsNaN(zoom) || float.IsInfinity(zoom) ? 1f : Mathf.Max(0.01f, zoom);

    public float GetParallaxInfluence()
    {
        if (!TryGetPlayerDepth(out float depth)) return 1f;
        float blend = Mathf.InverseLerp(parallaxStopDepth,
            parallaxStopDepth + Mathf.Max(0.01f, parallaxTransitionDepth), depth);
        return 1f - Mathf.SmoothStep(0f, 1f, blend);
    }

    public float GetLayer2Blend()
        => GetUndergroundBlend(1, layer2FadeDepth);

    public float GetLayer3Blend()
        => GetUndergroundBlend(2, layer3FadeDepth);

    public float GetLayerBlend(int layerIndex)
    {
        var configured = MapLayers;
        if (!TryGetPlayerDepth(out float depth) || configured == null || layerIndex <= 0 ||
            layerIndex >= configured.Length || configured[layerIndex] == null) return 0f;
        int end = configured[layerIndex].startDepth;
        int width = Mathf.Min(Mathf.Max(0, configured[layerIndex].transitionWidth),
            end - configured[layerIndex - 1].startDepth);
        if (width == 0) return depth >= end ? 1f : 0f;
        float blend = Mathf.InverseLerp(end - width, end, depth);
        return Mathf.SmoothStep(0f, 1f, blend);
    }

    public float GetUndergroundBackgroundBrightness(float depthInBlocks)
    {
        float surface = Mathf.Clamp(surfaceBrightness, 0f, 2f);
        float targetBrightness = Mathf.Clamp(undergroundBrightness, 0f, 2f);
        float blend = Mathf.Clamp01(Mathf.Max(0f, depthInBlocks) /
            Mathf.Max(0.01f, undergroundBrightnessTransitionDepth));
        return Mathf.Lerp(surface, targetBrightness, blend);
    }

    /// <summary>Returns the background brightness at a world-space height.
    /// The transition starts at the actual terrain surface, so it is shared by
    /// the surface panorama and every underground continuation.</summary>
    public float GetBackgroundBrightnessAtWorldY(float worldY)
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain) return GetSurfaceBackgroundBrightness();

        float blockHeight = GetBlockWorldHeight();
        float surfaceY = map.Terrain.CellToWorld(new Vector3Int(0, 1, 0)).y;
        float depthInBlocks = Mathf.Max(0f, (surfaceY - worldY) / blockHeight);
        return GetUndergroundBackgroundBrightness(depthInBlocks);
    }

    public float GetSurfaceBackgroundBrightness()
        => Mathf.Clamp(surfaceBrightness, 0f, 2f);

    public float GetBlockWorldHeight()
    {
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!map || !map.Terrain) return 0.5f;
        float height = map.Terrain.transform.TransformVector(
            Vector3.up * map.Terrain.layoutGrid.cellSize.y).magnitude;
        return height > 0f && !float.IsNaN(height) && !float.IsInfinity(height) ? height : 0.5f;
    }

    float GetUndergroundBlend(int layerIndex, float fadeDepth)
    {
        if (!TryGetPlayerDepth(out float depth) || map.layers == null || map.layers.Length <= layerIndex ||
            map.layers[layerIndex] == null)
            return 0f;
        float midpoint = map.layers[layerIndex].startDepth;
        float halfWidth = Mathf.Max(0.01f, fadeDepth) * 0.5f;
        float blend = Mathf.InverseLerp(midpoint - halfWidth, midpoint + halfWidth, depth);
        return Mathf.SmoothStep(0f, 1f, blend);
    }
    bool TryGetPlayerDepth(out float depth)
    {
        depth = 0f;
        if (!player)
        {
            var movement = FindFirstObjectByType<PlayerMovement>();
            if (movement) player = movement.transform;
        }
        if (!map) map = FindFirstObjectByType<MapGenerator>();
        if (!player || !map || !map.Terrain) return false;

        var terrain = map.Terrain;
        float blockHeight = terrain.transform.TransformVector(
            Vector3.up * terrain.layoutGrid.cellSize.y).magnitude;
        if (blockHeight <= 0f) return false;
        float surfaceY = terrain.CellToWorld(new Vector3Int(0, 1, 0)).y;
        depth = (surfaceY - player.position.y) / blockHeight;
        return true;
    }
    public Vector2 GetParallaxCameraDelta(Camera camera)
    {
        Vector2 currentDelta = (Vector2)camera.transform.position - cameraReferencePosition;
        float influence = GetParallaxInfluence();
        if (influence >= 1f)
        {
            hasFrozenCameraDelta = false;
            return currentDelta;
        }
        if (!hasFrozenCameraDelta)
        {
            frozenCameraDelta = currentDelta;
            hasFrozenCameraDelta = true;
        }
        return Vector2.Lerp(frozenCameraDelta, currentDelta, influence);
    }

    public float GetOpacity(Camera camera)
    {
        if (!isActiveAndEnabled) return 0f;
        float visibility = Mathf.Clamp01(opacity);
        if (!fadeWithDepth || !camera) return visibility;
        float depthFade = Mathf.InverseLerp(fadeEndY, Mathf.Max(fadeStartY, fadeEndY + 0.001f),
            camera.transform.position.y);
        return visibility * Mathf.SmoothStep(0f, 1f, depthFade);
    }

    void Reset() => CaptureCameraReference();

    public void CollectLayers()
    {
        var children = GetComponentsInChildren<ParallaxLayer>(true);
        layers = System.Array.ConvertAll(children, layer => new LayerEntry { layer = layer });
    }

    [ContextMenu("Capture current camera reference")]
    public void CaptureCameraReference()
    {
        if (!targetCamera) targetCamera = Camera.main;
        if (targetCamera) cameraReferencePosition = targetCamera.transform.position;
    }
}
