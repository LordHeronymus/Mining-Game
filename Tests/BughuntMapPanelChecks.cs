using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks for the map panel fixes: no panel in the main menu (no per-frame world search there)
// and dragging follows the pointer regardless of the canvas scale factor.
public static class BughuntMapPanelChecks
{
    const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var type = typeof(PlayerMapPanel);
        var before = new HashSet<PlayerMapPanel>(Object.FindObjectsByType<PlayerMapPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        var temporary = new List<GameObject>();
        try
        {
            // The AfterSceneLoad entry point stays a single parameterless method, so Unity resolves it unambiguously.
            var create = type.GetMethods(StaticMembers).Where(method => method.Name == "Create").ToArray();
            Check(create.Length == 1 && create[0].GetParameters().Length == 0 &&
                create[0].GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null,
                "Create bleibt eindeutiger AfterSceneLoad-Einstieg");

            // Loading the main menu builds no map panel (it would search for the world every frame there).
            var createFor = type.GetMethod("CreateFor", StaticMembers, null, new[] { typeof(string) }, null);
            Check(createFor != null, "CreateFor(sceneName) vorhanden");
            var canvas = GameObject.Find("ScreenCanvas");
            int canvasChildren = canvas ? canvas.transform.childCount : -1;
            createFor.Invoke(null, new object[] { RunNavigation.MenuScene });
            Check(Object.FindObjectsByType<PlayerMapPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == before.Count &&
                (canvas ? canvas.transform.childCount : -1) == canvasChildren, "Im Hauptmenü entsteht kein Kartenpanel");

            // Dragging converts both pointer positions into viewport space, so the canvas scale factor counts.
            var dragCells = type.GetMethod("DragCells", StaticMembers);
            Check(dragCells != null, "DragCells vorhanden");
            var root = new GameObject("BughuntMapPanel Canvas", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(root);
            var layout = new GameObject("Layout", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave }.GetComponent<RectTransform>();
            temporary.Add(layout.gameObject);
            var viewport = new GameObject("Viewport", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave }.GetComponent<RectTransform>();
            temporary.Add(viewport.gameObject);
            layout.SetParent(root.transform, false); viewport.SetParent(layout, false);
            viewport.sizeDelta = new Vector2(1308f, 680f);
            float Cells(float canvasScale, float fitScale, Vector2 from, Vector2 to, float cellSize, out float y)
            {
                root.transform.localScale = Vector3.one * canvasScale; layout.localScale = Vector3.one * fitScale;
                var result = (Vector2)dragCells.Invoke(null, new object[] { viewport, null, from, to, cellSize });
                y = result.y; return result.x;
            }
            // 1920x1080: canvas scale .75, fit 1.5, 18 px cells -> one cell is 20.25 screen pixels.
            float x = Cells(.75f, 1.5f, new Vector2(400f, 300f), new Vector2(400f + 20.25f * 4f, 300f - 20.25f * 2f), 18f, out float y);
            Check(Mathf.Abs(x - 4f) < .001f && Mathf.Abs(y + 2f) < .001f, "Full HD: Karte folgt dem Zeiger 1:1 (" + x + ", " + y + ")");
            // 4K: canvas scale 1.5, fit 1 -> one cell is 27 screen pixels.
            x = Cells(1.5f, 1f, new Vector2(1000f, 800f), new Vector2(1000f - 27f * 3f, 800f + 27f * 5f), 18f, out y);
            Check(Mathf.Abs(x + 3f) < .001f && Mathf.Abs(y - 5f) < .001f, "4K: Karte folgt dem Zeiger 1:1 (" + x + ", " + y + ")");
            // Reference resolution keeps the previous behaviour.
            x = Cells(1f, 1.5f, new Vector2(10f, 10f), new Vector2(10f + 27f, 10f), 18f, out y);
            Check(Mathf.Abs(x - 1f) < .001f && Mathf.Abs(y) < .001f, "1440p: unverändert eine Zelle pro 27 px");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntMapPanelChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } inner) ex = inner.InnerException;
            File.WriteAllText("Temp/BughuntMapPanelChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks)); throw ex;
        }
        finally
        {
            foreach (var panel in Object.FindObjectsByType<PlayerMapPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!before.Contains(panel)) Object.DestroyImmediate(panel.gameObject);
            for (int i = temporary.Count - 1; i >= 0; i--) if (temporary[i]) Object.DestroyImmediate(temporary[i]);
        }
    }
}
