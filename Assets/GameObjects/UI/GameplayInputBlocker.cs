using System.Collections.Generic;
using UnityEngine;

public static class GameplayInputBlocker
{
    private static readonly HashSet<MonoBehaviour> OpenPanels = new();

    public static bool IsBlocked => OpenPanels.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => OpenPanels.Clear();

    public static void SetBlocked(MonoBehaviour panel, bool blocked)
    {
        if (!panel) return;
        if (blocked) OpenPanels.Add(panel);
        else OpenPanels.Remove(panel);
    }
}
