#if UNITY_EDITOR
using UnityEngine;
public static class GameplayDebugDefaults
{
    public static bool QueueCurrent(out string message) => GpsSettings.Save(out message);
    public static bool QueueComponentValue(Component component,string propertyPath,out string error)
    { GpsSettings.Capture(component); error=null; return true; }
    public static bool ApplyPending() => false;
}
#endif
