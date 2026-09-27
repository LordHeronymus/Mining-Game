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

    [Header("Oberflächen-Hintergrund")]
    [FormerlySerializedAs("backgroundBrightness")]
    [Range(0f, 2f), InspectorName("Helligkeit")]
    public float surfaceBrightness = 0.8f;
    [Range(0f, 2f), InspectorName("Kontrast")]
    public float surfaceContrast = 1f;
    [Range(0f, 2f), InspectorName("Sättigung")]
    public float surfaceSaturation = 1f;

    Transform player;
    MapGenerator map;
    Vector2 frozenCameraDelta;
    bool hasFrozenCameraDelta;

    public Camera RenderCamera => targetCamera ? targetCamera : Camera.main;
    public float EffectiveZoom => float.IsNaN(zoom) || float.IsInfinity(zoom) ? 1f : Mathf.Max(0.01f, zoom);

    public float GetParallaxInfluence()
    {
        if (!TryGetPlayerDepth(out float depth)) return 1f;
        float blend = Mathf.InverseLerp(parallaxStopDepth,
            parallaxStopDepth + Mathf.Max(0.01f, parallaxTransitionDepth), depth);
        return 1f - Mathf.SmoothStep(0f, 1f, blend);
    }

    public float GetSurfaceBackgroundBrightness()
        => Mathf.Clamp(surfaceBrightness, 0f, 2f);

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
        return Mathf.Clamp01(opacity);
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
