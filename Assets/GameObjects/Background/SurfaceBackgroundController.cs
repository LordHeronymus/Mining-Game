using UnityEngine;

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

    [Header("Optional transition underground")]
    public bool fadeWithDepth;
    [Tooltip("World Y where the surface background is fully visible.")]
    public float fadeStartY = -5f;
    [Tooltip("World Y where the surface background is fully hidden.")]
    public float fadeEndY = -20f;

    public Camera RenderCamera => targetCamera ? targetCamera : Camera.main;
    public float EffectiveZoom => float.IsNaN(zoom) || float.IsInfinity(zoom) ? 1f : Mathf.Max(0.01f, zoom);

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
