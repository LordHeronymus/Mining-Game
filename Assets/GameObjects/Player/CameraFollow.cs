using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;     // Spieler
    public float smoothSpeed = 5f;
    public Vector3 offset;       // optionaler Abstand (z. B. (0, 0, -10))
    [Min(0f)] public float surfaceYOffset = 1.5f;
    public float centeredAtY = -10f;

    public float GetDepthYOffset(float targetY) =>
        surfaceYOffset * Mathf.InverseLerp(centeredAtY, 0f, targetY);

    Camera view;
    // GPS edits all three offset components; a depth at or in front of the near plane (or past the far plane)
    // cut the whole world at z = 0 out of the picture. X/Y stay as set.
    public Vector3 EffectiveOffset
    {
        get
        {
            if (!view) view = GetComponent<Camera>();
            if (!view) return offset;
            return new Vector3(offset.x, offset.y, Mathf.Clamp(offset.z, -(view.farClipPlane - .01f), -(view.nearClipPlane + .01f)));
        }
    }

    Vector3 DesiredPosition() => target.position + EffectiveOffset + Vector3.up * GetDepthYOffset(target.position.y);

    void Awake()
    {
        GpsSettings.ApplyComponent(this);
        if (target) transform.position = DesiredPosition();
    }

    void LateUpdate()
    {
        if (!target || !GameplayTestSettings.CameraFollowEnabled) return;

        Vector3 desired = DesiredPosition();
        Vector3 smoothed = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        smoothed.z = EffectiveOffset.z; // sicherstellen, dass Kamera z-Abstand behält
        transform.position = smoothed;
    }
}
