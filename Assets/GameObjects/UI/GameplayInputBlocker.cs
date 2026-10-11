using System.Collections.Generic;
using UnityEngine;

public static class GameplayInputBlocker
{
    private static readonly HashSet<Behaviour> OpenPanels = new();

    public static bool IsBlocked
    {
        get
        {
            OpenPanels.RemoveWhere(panel => !panel);
            return OpenPanels.Count > 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        OpenPanels.Clear();
        Time.timeScale = 1f;
    }

    public static void SetBlocked(Behaviour panel, bool blocked)
    {
        if (!panel) return;
        if (blocked) OpenPanels.Add(panel);
        else OpenPanels.Remove(panel);
    }

    public static bool IsBlockedExcept(Behaviour allowedPanel)
    {
        OpenPanels.RemoveWhere(panel => !panel);
        foreach (var panel in OpenPanels)
            if (panel != allowedPanel) return true;
        return false;
    }
}
