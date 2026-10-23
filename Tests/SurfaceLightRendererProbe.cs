// Diagnose (temporär): welche Renderer liegen über der Schachtöffnung und welche Shader nutzen sie?
// Run in a running game: unity command run_script --file Tests/SurfaceLightRendererProbe.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SurfaceLightRendererProbe
{
    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires a running game (Play → Neues Spiel).";
        var lighting = Object.FindFirstObjectByType<MapLighting>();
        if (!lighting || !lighting.IsReady) return "Requires a running game (Play → Neues Spiel).";
        var tiles = lighting.GetComponent<MapGenerator>().Terrain;
        var result = new List<string>();
        foreach (var cell in new[] { new Vector3Int(0, 2, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, 0, 0), new Vector3Int(2, 1, 0) })
        {
            Vector3 p = tiles.GetCellCenterWorld(cell);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                if (b.size.x > 400f) continue;
                var mat = r.sharedMaterial;
                result.Add($"{cell}: {r.name} ({r.GetType().Name}) layer {r.sortingLayerName}/{r.sortingOrder} " +
                    $"mat {(mat ? mat.name : "-")} shader {(mat && mat.shader ? mat.shader.name : "-")} light {lighting.GetDrawnLocalLight(cell):0.00}");
            }
        }
        return string.Join("\n", result.Take(80));
    }
}
