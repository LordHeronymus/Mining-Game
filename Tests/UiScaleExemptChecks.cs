using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Edit-Mode-Pruefung fuer UiScaleExempt: Gebaeude-Fenster behalten bei jeder UI-Groesse ihre
// Bildschirmgroesse und -lage, normale UI-Elemente skalieren weiter. Temporaere Szene, keine PlayerPrefs.
public static class UiScaleExemptChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }

    public static object Main()
    {
        checks.Clear();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var reference = new Vector2(2560, 1440); scaler.referenceResolution = reference; scaler.matchWidthOrHeight = .5f;
            var windows = Child("Windows", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var stretched = Child("Stretched", windows, Vector2.zero, Vector2.one, new Vector2(40, -20), new Vector2(-120, -60));
            var fixedPanel = Child("Fixed", windows, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(150, -80), new Vector2(900, 600));
            var normal = Child("Normal", windows, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(150, -80), new Vector2(900, 600));
            var a = stretched.gameObject.AddComponent<UiScaleExempt>();
            var b = fixedPanel.gameObject.AddComponent<UiScaleExempt>();

            Rect[] Measure(float uiScale)
            {
                scaler.referenceResolution = reference / uiScale;
                Canvas.ForceUpdateCanvases(); scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver); Canvas.ForceUpdateCanvases();
                a.Apply(uiScale); b.Apply(uiScale); Canvas.ForceUpdateCanvases();
                return new[] { ScreenRect(stretched), ScreenRect(fixedPanel), ScreenRect(normal) };
            }
            var baseline = Measure(1f);
            foreach (float s in new[] { .8f, 1.2f, 1f })
            {
                var r = Measure(s);
                Check(Close(r[0], baseline[0]), $"Gestreckt bei {s:0.0} gleich ({r[0]} vs {baseline[0]})");
                Check(Close(r[1], baseline[1]), $"Fest bei {s:0.0} gleich ({r[1]} vs {baseline[1]})");
                if (!Mathf.Approximately(s, 1f))
                    Check(Mathf.Abs(r[2].width / baseline[2].width - s) < .02f, $"Normales UI skaliert bei {s:0.0} ({r[2].width / baseline[2].width:0.000})");
            }
            return $"OK {checks.Count} Pruefungen: " + string.Join("; ", checks);
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static RectTransform Child(string name, Transform parent, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }

    static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    static bool Close(Rect a, Rect b) => Mathf.Abs(a.xMin - b.xMin) < 1f && Mathf.Abs(a.yMin - b.yMin) < 1f &&
        Mathf.Abs(a.width - b.width) < 1f && Mathf.Abs(a.height - b.height) < 1f;
}
