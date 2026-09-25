using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class OreCurvePreviewChecks
{
    public static object Main()
    {
        var type = typeof(GameplaySettingsWindow);
        float At(string method, params object[] args) => (float)type.GetMethod(method,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, args);
        int Depth(float x, int start, int end) => (int)type.GetMethod("CurveDepthAtX",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(null, new object[] { x, start, end });
        float PreciseDepth(float x, int start, int end) => (float)type.GetMethod("CurveDepthValueAtX",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(null, new object[] { x, start, end });
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        Check(Mathf.Approximately(At("DensityLayerX", 0, 1000), 0f), "L1 marker is not at X=0.");
        Check(Mathf.Abs(At("DensityLayerX", 30, 1000) - 30f / 999f) < .00001f,
            "Global L2 marker does not match the sampler depth axis.");
        Check(Mathf.Abs(At("DensityLayerX", 600, 1000) - 600f / 999f) < .00001f,
            "Global L4 marker does not match the sampler depth axis.");
        Check(Mathf.Approximately(At("CurveLayerX", 30, 30, 600), 0f),
            "Ore curve must start at the first selected layer.");
        Check(Mathf.Abs(At("CurveLayerX", 200, 30, 600) - 170f / 569f) < .00001f,
            "Ore layer marker does not match the ore curve progress.");
        Check(Mathf.Approximately(At("CurveLayerX", 599, 30, 600), 1f),
            "Ore curve must end at the last selected layer.");
        Check(Depth(0f, 0, 2000) == 0 && Depth(1f, 0, 2000) == 1999,
            "Global X-axis does not show absolute block depths.");
        Check(Depth(0f, 30, 600) == 30 && Depth(1f, 30, 600) == 599 &&
            Depth(170f / 569f, 30, 600) == 200,
            "Ore X-axis does not show its absolute layer depths.");
        Check(Mathf.Abs(PreciseDepth(170.25f / 569f, 30, 600) - 200.25f) < .001f,
            "Selected key depth loses precision while dragging.");
        int Step(float visible, float width) => (int)type.GetMethod("DepthTickStep",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(null, new object[] { visible, width });
        Check(Step(23f, 2400f) == 1,
            "Wide zoomed previews must show individual block depths.");
        Check(Step(1999f, 800f) >= 100 && Step(1999f, 800f) <= 250,
            "Full-map previews have too many or too few depth labels.");
        var absence = type.GetMethod("OreAbsenceAtX",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var layers = new[]
        {
            new MapLayer { startDepth = 0 }, new MapLayer { startDepth = 30 },
            new MapLayer { startDepth = 200 }, new MapLayer { startDepth = 600 }
        };
        var selected = new HashSet<int> { 0, 2 };
        var flat = AnimationCurve.Constant(0f, 1f, 1f);
        int State(float x, AnimationCurve curve, float weight) => (int)absence.Invoke(null,
            new object[] { x, 0, 600, layers, selected, curve, weight });
        Check(State(10f / 599f, flat, 1f) == 0 && State(100f / 599f, flat, 1f) == 1,
            "Preview did not distinguish selected and unselected layers.");
        var zeroAtTop = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.1f, 0f),
            new Keyframe(.2f, 1f), new Keyframe(1f, 1f));
        Check(State(10f / 599f, zeroAtTop, 1f) == 2 && State(10f / 599f, flat, 0f) == 2,
            "Preview did not mark zero effective ore weight.");
        var zoom = type.GetMethod("ZoomCurveView", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var pan = type.GetMethod("PanCurveView", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var view = new Vector2(0f, 1f);
        float boundary = At("DensityLayerX", 30, 2000);
        for (int i = 0; i < 9; i++)
            view = (Vector2)zoom.Invoke(null, new object[] { view, boundary, .65f, 4f / 2000f });
        Check(view.y - view.x < .03f && view.x <= boundary && view.y >= boundary,
            "Zoom cannot enlarge a 30-block layer in a 2000-block map.");
        var moved = (Vector2)pan.Invoke(null, new object[] { view, .01f });
        Check(moved.x > view.x && Mathf.Approximately(moved.y - moved.x, view.y - view.x),
            "Panning changed the zoom level.");
        var clamped = (Vector2)pan.Invoke(null, new object[] { view, -100f });
        Check(Mathf.Approximately(clamped.x, 0f), "Panning crossed the map start.");
        return new { passed = true };
    }
}
