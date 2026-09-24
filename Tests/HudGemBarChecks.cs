using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class HudGemBarChecks
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Use Play Mode");
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var top = hud.transform.Find("Status Strip");
        var health = top.Find("Health Fill").GetComponent<HudGemBar>();
        var energy = top.Find("Energy Fill").GetComponent<HudGemBar>();
        if (!health || !energy || !health.isActiveAndEnabled || !energy.isActiveAndEnabled)
            throw new Exception("Gem graphics missing");
        if (!health.GetComponent<CanvasRenderer>() || !energy.GetComponent<CanvasRenderer>())
            throw new Exception("Gem graphics cannot render without CanvasRenderer");
        var mesh = new VertexHelper();
        typeof(HudGemBar).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(VertexHelper) }, null)
            .Invoke(health, new object[] { mesh });
        if (mesh.currentVertCount < 100) throw new Exception("Gem mesh is empty");
        float oldEnergy = hud.energy.energy;
        try
        {
            hud.energy.energy = hud.energy.stats.MaxEnergy * .42f;
            typeof(CompactHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hud, null);
            if (Mathf.Abs(energy.FillAmount - .42f) > .001f)
                throw new Exception("Energy segments do not follow the current energy value");
        }
        finally
        {
            hud.energy.energy = oldEnergy;
            typeof(CompactHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hud, null);
        }
        return "PASS: gemstone bars render and energy segments track the live value.";
    }
}
