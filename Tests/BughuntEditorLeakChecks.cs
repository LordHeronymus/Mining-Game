using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// LastPlayedMap must not keep the initial map snapshot of every world started in one Play session.
// Each new world reloads the scene; snapshots of destroyed maps are never written and must be dropped.
public static class BughuntEditorLeakChecks
{
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    const string Report = "Temp/BughuntEditorLeakChecks.txt";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear(); Directory.CreateDirectory("Temp");
        var snapshots = (Dictionary<MapGenerator, MapGenerator.MapGenerationSnapshot>)typeof(LastPlayedMap)
            .GetField("initialSnapshots", StaticPrivate).GetValue(null);
        var scheduled = (HashSet<MapGenerator>)typeof(LastPlayedMap).GetField("scheduledInitialCaptures", StaticPrivate).GetValue(null);
        var savedSnapshots = new Dictionary<MapGenerator, MapGenerator.MapGenerationSnapshot>(snapshots);
        var savedScheduled = new HashSet<MapGenerator>(scheduled);
        GameObject deadObject = null, liveObject = null;
        try
        {
            var forget = typeof(LastPlayedMap).GetMethod("ForgetDestroyedMaps", StaticPrivate);
            Check(forget != null, "LastPlayedMap hat eine Bereinigung für zerstörte Maps");
            var store = typeof(LastPlayedMap).GetMethod("StoreInitialSnapshot", StaticPrivate);
            Check(store != null && Calls(store, forget), "Jede neue Welt bereinigt vor dem Eintragen ihres Snapshots");

            // Inactive objects: no OnEnable of the ExecuteAlways appearance components in Edit Mode.
            deadObject = new GameObject("BughuntEditorLeakDead") { hideFlags = HideFlags.HideAndDontSave };
            deadObject.SetActive(false);
            var dead = deadObject.AddComponent<MapGenerator>();
            liveObject = new GameObject("BughuntEditorLeakLive") { hideFlags = HideFlags.HideAndDontSave };
            liveObject.SetActive(false);
            var live = liveObject.AddComponent<MapGenerator>();
            var deadSnapshot = new MapGenerator.MapGenerationSnapshot(1, 1, 1, 0, new TileBase[1], new TileBase[1], new TileBase[1]);
            var liveSnapshot = new MapGenerator.MapGenerationSnapshot(2, 1, 1, 0, new TileBase[1], new TileBase[1], new TileBase[1]);
            snapshots.Clear(); scheduled.Clear();
            snapshots[dead] = deadSnapshot; snapshots[live] = liveSnapshot;
            scheduled.Add(dead); scheduled.Add(live);
            Object.DestroyImmediate(deadObject); deadObject = null;
            Check(!dead && (object)dead != null, "Map der vorherigen Welt ist zerstört, der Schlüssel aber noch vorhanden");

            forget.Invoke(null, null);
            Check(!snapshots.ContainsKey(dead), "Snapshot der zerstörten Map wird freigegeben");
            Check(!scheduled.Contains(dead), "Zerstörte Map fällt aus der Sicherungs-Warteschlange");
            Check(snapshots.Count == 1 && snapshots.TryGetValue(live, out var kept) && kept == liveSnapshot,
                "Snapshot der laufenden Map bleibt für das Play-Ende erhalten");
            Check(scheduled.Count == 1 && scheduled.Contains(live), "Laufende Map bleibt zur Sicherung vorgemerkt");

            forget.Invoke(null, null);
            Check(snapshots.Count == 1 && scheduled.Count == 1, "Wiederholte Bereinigung ohne zerstörte Maps ändert nichts");

            string report = string.Join("\n", checks) + "\n";
            File.WriteAllText(Report, report);
            return checks.Count + " checks passed";
        }
        catch (Exception ex) { File.WriteAllText(Report, string.Join("\n", checks) + "\nFAIL " + ex.Message + "\n"); throw; }
        finally
        {
            if (deadObject) Object.DestroyImmediate(deadObject);
            if (liveObject) Object.DestroyImmediate(liveObject);
            snapshots.Clear(); foreach (var entry in savedSnapshots) snapshots[entry.Key] = entry.Value;
            scheduled.Clear(); scheduled.UnionWith(savedScheduled);
        }
    }

    // Looks for a direct call of target in the IL of method (opcode call = 0x28 followed by a metadata token).
    static bool Calls(MethodInfo method, MethodInfo target)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return false;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            try { if (method.Module.ResolveMethod(token) == target) return true; }
            catch (Exception) { }
        }
        return false;
    }
}
