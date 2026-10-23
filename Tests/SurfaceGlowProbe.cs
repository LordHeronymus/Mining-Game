// Diagnose (temporär): Zustand des Oberflächen-Glows. In a running game at night with a lit shaft:
// unity command run_script --file Tests/SurfaceGlowProbe.cs
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SurfaceGlowProbe
{
    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires a running game (Play → Neues Spiel).";
        var go = GameObject.Find("Surface light glow");
        if (!go) return "no glow object (inactive objects are not found by GameObject.Find)";
        var r = go.GetComponent<MeshRenderer>();
        var m = r.sharedMaterial;
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        var tex = m.mainTexture as Texture2D;
        var cam = Camera.main;
        return string.Join("\n", new[] {
            $"active {go.activeInHierarchy} enabled {r.enabled} visible {r.isVisible} layer {LayerMask.LayerToName(go.layer)} camMask {(cam ? cam.cullingMask : 0)}",
            $"sorting {r.sortingLayerName}/{r.sortingOrder} bounds {r.bounds}",
            $"shader {m.shader.name} keywords [{string.Join(",", m.shaderKeywords)}] enabledLocal [{string.Join(",", m.enabledKeywords.Select(k => k.name))}]",
            $"src {m.GetFloat("_SrcBlend")} dst {m.GetFloat("_DstBlend")} glow {m.GetColor("_GlowColor")} tex {(tex ? tex.name + " " + tex.width + "x" + tex.height : "-")}",
            $"mesh verts {string.Join(" ", mesh.vertices)} uv {string.Join(" ", mesh.uv)} uv2 {string.Join(" ", mesh.uv2)}",
            $"camera {(cam ? cam.name + " " + cam.transform.position + " size " + cam.orthographicSize : "-")}" });
    }
}
