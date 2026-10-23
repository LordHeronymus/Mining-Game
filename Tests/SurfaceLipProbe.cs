// Diagnose (temporär): welche Renderer liegen an der Schachtöffnung (auch breite Hintergründe)?
// Run in a running game with an open shaft at x = 0: unity command run_script --file Tests/SurfaceLipProbe.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SurfaceLipProbe
{
    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires a running game (Play → Neues Spiel).";
        var lighting = Object.FindFirstObjectByType<MapLighting>();
        if (!lighting || !lighting.IsReady) return "Requires a running game (Play → Neues Spiel).";
        var tiles = lighting.GetComponent<MapGenerator>().Terrain;
        Vector3 up = tiles.transform.TransformVector(Vector3.up * tiles.layoutGrid.cellSize.y);
        Vector3 edge = tiles.CellToWorld(new Vector3Int(0, 1, 0)) + tiles.transform.TransformVector(Vector3.right * tiles.layoutGrid.cellSize.x * .5f);
        var lines = new List<string>();
        foreach (float offset in new[] { -.3f, -.1f, .1f, .3f, .6f })
        {
            Vector3 p = edge + up * offset;
            lines.Add($"-- point {offset:+0.0;-0.0} cells from surface edge, world {p}");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                var mat = r.sharedMaterial;
                string sprite = r is SpriteRenderer sr && sr.sprite ? " sprite " + sr.sprite.name : "";
                string alpha = "";
                if (r is SpriteRenderer s && s.sprite && s.sprite.texture && s.sprite.texture.isReadable)
                {
                    Vector3 local = s.transform.InverseTransformPoint(p);
                    var rect = s.sprite.rect; float ppu = s.sprite.pixelsPerUnit;
                    int px = Mathf.FloorToInt(local.x * ppu + s.sprite.pivot.x + rect.x);
                    int py = Mathf.FloorToInt(local.y * ppu + s.sprite.pivot.y + rect.y);
                    alpha = $" texel a {s.sprite.texture.GetPixel(px, py).a:0.00}";
                }
                lines.Add($"{r.name} ({r.GetType().Name}) path {Path(r.transform)} layer {r.sortingLayerName}/{r.sortingOrder} " +
                    $"shader {(mat && mat.shader ? mat.shader.name : "-")}{sprite}{alpha} bounds y {b.min.y:0.00}..{b.max.y:0.00}");
            }
        }
        return string.Join("\n", lines);
    }

    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
