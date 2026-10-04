using UnityEngine;

// Kept for existing scene references; GpsRuntimePanel owns the view.
public sealed partial class GameplayDebugWindow : MonoBehaviour
{
    void Awake() => enabled=false;
}
