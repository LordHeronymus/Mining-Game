using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Crack overlays of mined or regenerated cells are released into a pool instead of piling up,
// and the per-frame refresh neither scans released cells nor allocates.
public static class BughuntCracksChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var root = new GameObject("Bughunt crack checks", typeof(Grid)) { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(-20000, -20000, 0);
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.hideFlags = HideFlags.HideAndDontSave;
        MiningCrackVisual visual = null;
        try
        {
            var terrainObject = new GameObject("Terrain") { hideFlags = HideFlags.HideAndDontSave };
            terrainObject.transform.SetParent(root.transform, false);
            var terrain = terrainObject.AddComponent<Tilemap>();
            terrainObject.AddComponent<TilemapRenderer>();
            visual = new MiningCrackVisual(terrain);
            var renderers = (Dictionary<Vector3Int, SpriteRenderer>)typeof(MiningCrackVisual)
                .GetField("renderers", Private).GetValue(visual);
            var progress = new Dictionary<Vector3Int, float>();

            // Mine many cells one after another like CompleteMining: hit, remove tile, hide.
            const int mined = 200;
            for (int i = 0; i < mined; i++)
            {
                var cell = new Vector3Int(i, 0, 0);
                terrain.SetTile(cell, tile);
                visual.Show(cell, .5f);
                if (i == 0) Check(visual.Renderer && visual.Renderer.enabled, "Teiltreffer zeigt Risse");
                terrain.SetTile(cell, null);
                visual.Hide(cell);
            }
            Check(!visual.Renderer.enabled, "Abgebaute Zelle zeigt keine Risse mehr");
            Check(renderers.Count == 0, "Abgebaute Zellen behalten keinen Riss-Renderer (" + renderers.Count + ")");
            Check(terrain.transform.childCount == 1, "Nacheinander abgebaute Zellen teilen sich ein Riss-Objekt (" + terrain.transform.childCount + ")");

            // Cells that lose their tile while damaged are released by Refresh (e.g. dynamite, regeneration).
            for (int i = 0; i < 8; i++)
            {
                var cell = new Vector3Int(i, 2, 0);
                terrain.SetTile(cell, tile);
                progress[cell] = .4f;
            }
            visual.Refresh(progress);
            Check(renderers.Count == 8 && terrain.transform.childCount == 8, "Gleichzeitig beschädigte Zellen haben je ein Riss-Objekt");
            for (int i = 0; i < 8; i++) terrain.SetTile(new Vector3Int(i, 2, 0), null);
            visual.Refresh(progress);
            Check(renderers.Count == 0, "Refresh gibt Risse von Zellen ohne Tile frei");
            progress.Clear();
            visual.Refresh(progress);
            Check(terrain.transform.childCount == 8, "Freigegebene Riss-Objekte bleiben im Pool statt neu zu entstehen");

            // A pooled renderer shows correctly on a new cell.
            var reuseCell = new Vector3Int(3, 5, 0);
            terrain.SetTile(reuseCell, tile);
            visual.Show(reuseCell, .7f);
            var reused = visual.Renderer;
            var properties = new MaterialPropertyBlock(); reused.GetPropertyBlock(properties);
            Check(reused.enabled && terrain.transform.childCount == 8, "Pool-Renderer wird wiederverwendet");
            Check(Vector3.Distance(reused.transform.position, terrain.GetCellCenterWorld(reuseCell)) < .001f, "Pool-Renderer sitzt auf der neuen Zelle");
            Check(Mathf.Abs(properties.GetFloat("_CrackGrowth") - .7f) < .001f && reused.sprite && reused.sprite.name.EndsWith("4"), "Pool-Renderer zeigt die neue Schadensstufe");
            progress[reuseCell] = .7f;
            visual.Refresh(progress);
            Check(reused.enabled && renderers.Count == 1, "Refresh lässt aktive Risse sichtbar");
            progress.Remove(reuseCell);
            visual.Refresh(progress);
            Check(!reused.enabled && renderers.Count == 0, "Refresh blendet Risse ohne Fortschritt aus");

            // Refresh allocates nothing per frame, with and without damage.
            var staleField = typeof(MiningCrackVisual).GetField("stale", Private);
            Check(staleField != null && staleField.GetValue(visual) is List<Vector3Int>, "Refresh nutzt eine wiederverwendete Liste");
            progress[reuseCell] = .3f;
            visual.Refresh(progress);
            var damaged = new Dictionary<Vector3Int, float>(progress);
            var empty = new Dictionary<Vector3Int, float>();
            visual.Refresh(empty);
            long probe = GC.GetAllocatedBytesForCurrentThread();
            var allocation = new byte[256];
            bool measurable = GC.GetAllocatedBytesForCurrentThread() - probe > 0 && allocation.Length > 0;
            if (measurable)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) visual.Refresh(empty);
                long idle = GC.GetAllocatedBytesForCurrentThread() - before;
                visual.Refresh(damaged);
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) visual.Refresh(damaged);
                long active = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(idle == 0 && active == 0, "Refresh allokiert pro Frame nichts (ohne Schaden " + idle + " B, mit Schaden " + active + " B)");
            }
            else checks.Add("INFO Allokationsmessung in diesem Editor nicht verfügbar; Listen-Wiederverwendung geprüft");

            visual.Dispose();
            Check(terrain.transform.childCount == 0, "Dispose entfernt aktive und gepoolte Riss-Objekte");
            visual = null;

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntCracksChecks.txt", report);
            return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            File.WriteAllText("Temp/BughuntCracksChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks));
            throw;
        }
        finally
        {
            visual?.Dispose();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
        }
    }
}
