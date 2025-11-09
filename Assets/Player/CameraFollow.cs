using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;     // Spieler
    public float smoothSpeed = 5f;
    public Vector3 offset;       // optionaler Abstand (z. B. (0, 0, -10))


    void Awake()
    {
        if (target) transform.position = target.position + offset;
    }

    void LateUpdate()
    {
        if (!target) return;

        Vector3 desired = target.position + offset;
        Vector3 smoothed = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        smoothed.z = offset.z; // sicherstellen, dass Kamera z-Abstand behält
        transform.position = smoothed;
    }
}
