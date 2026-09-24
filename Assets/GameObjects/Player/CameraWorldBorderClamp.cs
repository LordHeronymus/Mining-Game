using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Camera))]
public sealed class CameraWorldBorderClamp : MonoBehaviour
{
    [SerializeField] MapWorldBorders borders;
    Camera view;
    void Awake() { view = GetComponent<Camera>(); }
    void LateUpdate()
    {
        if (!borders) borders = FindFirstObjectByType<MapWorldBorders>();
        if (borders) transform.position = borders.ClampCameraCenter(view, transform.position);
    }
}
