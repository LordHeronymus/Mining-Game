using System;
using System.Collections.Generic;
using UnityEngine;

// Maximum remaining daylight over all paths from the open upper map boundary.
// Coordinates use depth: y = 0 at the surface, increasing downwards.
public sealed class GridDaylight
{
    readonly int width, height;
    readonly bool[] solid;
    readonly float[] light;
    readonly bool[] queued;
    readonly Queue<int> queue = new Queue<int>();
    readonly float daylight, downFactor, sideFactor, blockFactor;
    public const float DarknessCutoff = 0.001f;

    // Fade the negligible exponential tail to true black without a visible hard edge.
    public static float VisibleLight(float value)
        => value * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DarknessCutoff, 0.02f, value));

    public event Action<int, float> LightChanged;
    public bool HasPendingWork => queue.Count > 0;
    public float this[int x, int depth] => light[depth * width + x];

    public GridDaylight(int width, int height, bool[] solid, float daylight,
        float downLoss, float sideLoss, float blockLoss, float exponentialStrength = 1f)
    {
        if (width <= 0 || height <= 0 || solid == null || solid.Length != (long)width * height)
            throw new ArgumentException("Invalid daylight grid dimensions.");
        this.width = width;
        this.height = height;
        this.solid = (bool[])solid.Clone();
        light = new float[solid.Length];
        queued = new bool[solid.Length];
        this.daylight = Mathf.Clamp01(daylight);
        float strength = Mathf.Clamp(exponentialStrength, 0.1f, 5f);
        downFactor = Mathf.Pow(1f - Mathf.Clamp(downLoss, 0.0001f, 1f), strength);
        sideFactor = Mathf.Pow(1f - Mathf.Clamp(sideLoss, 0.0001f, 1f), strength);
        blockFactor = Mathf.Pow(1f - Mathf.Clamp01(blockLoss), strength);
    }

    public void Reset()
    {
        queue.Clear();
        Array.Clear(queued, 0, queued.Length);
        for (int i = 0; i < light.Length; i++)
        {
            if (light[i] == 0f) continue;
            light[i] = 0f;
            LightChanged?.Invoke(i, 0f);
        }
        for (int x = 0; x < width; x++)
            Spread(x, daylight * downFactor);
    }

    public void SetSolid(int x, int depth, bool value)
    {
        if (x < 0 || x >= width || depth < 0 || depth >= height) return;
        int i = depth * width + x;
        if (solid[i] == value) return;
        solid[i] = value;
        if (value)
        {
            // Rebuilding on placement removes light from paths now blocked.
            // Mining only increases light and can propagate locally.
            Reset();
            return;
        }
        float best = depth == 0 ? daylight * downFactor : 0f;
        if (depth > 0) best = Mathf.Max(best, light[i - width] * downFactor);
        if (depth + 1 < height) best = Mathf.Max(best, light[i + width] * sideFactor);
        if (x > 0) best = Mathf.Max(best, light[i - 1] * sideFactor);
        if (x + 1 < width) best = Mathf.Max(best, light[i + 1] * sideFactor);
        Improve(i, best);
    }

    public int Process(int maxCells)
    {
        int processed = 0;
        while (queue.Count > 0 && processed < maxCells)
        {
            int i = queue.Dequeue();
            queued[i] = false;
            int x = i % width;
            int y = i / width;
            float value = light[i];
            if (y + 1 < height) Spread(i + width, value * downFactor);
            if (y > 0) Spread(i - width, value * sideFactor);
            if (x > 0) Spread(i - 1, value * sideFactor);
            if (x + 1 < width) Spread(i + 1, value * sideFactor);
            processed++;
        }
        return processed;
    }

    void Spread(int target, float value) => Improve(target, value * (solid[target] ? blockFactor : 1f));

    void Improve(int i, float value)
    {
        if (value <= DarknessCutoff || value <= light[i]) return;
        light[i] = value;
        LightChanged?.Invoke(i, value);
        if (queued[i]) return;
        queued[i] = true;
        queue.Enqueue(i);
    }
}
