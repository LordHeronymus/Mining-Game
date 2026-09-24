using System;
using UnityEngine;
using UnityEngine.EventSystems;

public static class HotbarMiningClickChecks
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Use Play Mode");

        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        if (!hud) throw new Exception("Compact HUD missing");
        var bar = hud.transform.Find("Hotbar");
        if (!bar || bar.childCount == 0) throw new Exception("Hotbar missing");

        Canvas.ForceUpdateCanvases();
        var slot = (RectTransform)bar.GetChild(0);
        var screenPoint = RectTransformUtility.WorldToScreenPoint(null, slot.TransformPoint(slot.rect.center));
        if (!TileMiner.IsPointerOverUi(screenPoint))
            throw new Exception("Hotbar pointer must be recognized as UI before mining");

        return "PASS: Hotbar pointer is intercepted before mining.";
    }
}
