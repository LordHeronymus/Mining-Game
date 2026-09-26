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

    Vector3 DesiredPosition() => target.position + offset + Vector3.up * GetDepthYOffset(target.position.y);

    void Awake()
    {
        if (target) transform.position = DesiredPosition();
    }

    void LateUpdate()
    {
        if (!target) return;

        Vector3 desired = DesiredPosition();
        Vector3 smoothed = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        smoothed.z = offset.z; // sicherstellen, dass Kamera z-Abstand behält
        transform.position = smoothed;
    }
}
