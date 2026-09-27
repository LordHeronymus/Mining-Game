using System.Collections.Generic;
using UnityEngine;

public sealed class TorchLightField
{
    public readonly struct Source
    {
        public readonly int x;
        public readonly int y;
        public readonly float intensity;

        public Source(int x, int y, float intensity)
        {
            this.x = x;
            this.y = y;
            this.intensity = intensity;
        }
    }

    const float MinimumLight = 1f / 255f;
    const float WallSurfaceLight = .75f;
    readonly int width;
    readonly int height;
    readonly bool[] solid;
    readonly float[] light;
    readonly bool[] queued;
    readonly float horizontalFalloff;
    readonly float verticalFalloff;
    readonly float diagonalFalloff;
    readonly Queue<int> pending = new Queue<int>();
    readonly List<int> litCells = new List<int>();
    readonly HashSet<int> affected = new HashSet<int>();

    public TorchLightField(int width, int height, bool[] solid, float cellWidth,
        float cellHeight, float falloffDistance)
    {
        this.width = width;
        this.height = height;
        this.solid = (bool[])solid.Clone();
        light = new float[solid.Length];
        queued = new bool[solid.Length];
        float distance = Mathf.Max(.01f, falloffDistance);
        horizontalFalloff = Mathf.Exp(-Mathf.Max(.01f, cellWidth) / distance);
        verticalFalloff = Mathf.Exp(-Mathf.Max(.01f, cellHeight) / distance);
        diagonalFalloff = Mathf.Exp(-new Vector2(cellWidth, cellHeight).magnitude / distance);
    }

    public float Get(int x, int y)
        => x < 0 || x >= width || y < 0 || y >= height ? 0f : light[y * width + x];

    public float Get(int index) => light[index];

    public bool SetSolid(int x, int y, bool value)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        int index = y * width + x;
        if (solid[index] == value) return false;
        solid[index] = value;
        return true;
    }

    public IEnumerable<int> Rebuild(IReadOnlyList<Source> sources)
    {
        affected.Clear();
        foreach (int index in litCells)
        {
            light[index] = 0f;
            affected.Add(index);
        }
        litCells.Clear();

        foreach (Source source in sources)
        {
            if (source.x < 0 || source.x >= width || source.y < 0 || source.y >= height)
                continue;
            int index = source.y * width + source.x;
            if (!solid[index]) Improve(index, source.intensity, true);
        }

        while (pending.Count > 0)
        {
            int index = pending.Dequeue();
            queued[index] = false;
            int x = index % width, y = index / width;
            float value = light[index];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    if (dx != 0 && dy != 0 &&
                        (solid[y * width + nx] || solid[ny * width + x])) continue;
                    int next = ny * width + nx;
                    float falloff = dx == 0 ? verticalFalloff :
                        dy == 0 ? horizontalFalloff : diagonalFalloff;
                    bool isWall = solid[next];
                    Improve(next, value * falloff * (isWall ? WallSurfaceLight : 1f), !isWall);
                }
        }
        return affected;
    }

    void Improve(int index, float value, bool propagate)
    {
        if (value < MinimumLight || value <= light[index] + .00001f) return;
        if (light[index] == 0f) litCells.Add(index);
        light[index] = value;
        affected.Add(index);
        if (!propagate || queued[index]) return;
        queued[index] = true;
        pending.Enqueue(index);
    }
}
