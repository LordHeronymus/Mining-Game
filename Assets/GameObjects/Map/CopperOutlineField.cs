using System;
using Unity.Collections;
using UnityEngine;

// Cached world-aligned distance field of the combined deposit, independent of art UVs.
public sealed class CopperOutlineField : IDisposable
{
    public Texture2D Texture { get; private set; }
    float[] distance, scratch, kernel;
    byte[] occupancy;
    bool[] boundary;
    int density;

    public void Build(NativeArray<Color32> cells, int columns, int rows)
    {
        int scale = 4;
        int width = columns * scale, height = rows * scale;
        bool changed = !Texture || Texture.width != width || Texture.height != height;
        if (occupancy == null || occupancy.Length != cells.Length)
        { occupancy = new byte[cells.Length]; boundary = new bool[cells.Length]; }
        bool any = false;
        for (int i = 0; i < cells.Length; i++)
        {
            byte value = cells[i].r == (int)BlockType.CopperOre + 1 ? (byte)1 : (byte)0;
            changed |= occupancy[i] != value;
            occupancy[i] = value; any |= value != 0;
        }
        if (!any) { Dispose(); return; }
        if (!changed) return;
        if (!Texture || Texture.width != width || Texture.height != height)
        {
            Dispose();
            Texture = new Texture2D(width, height, TextureFormat.RFloat, false, true)
            {
                name = "Copper combined contour", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            distance = new float[width * height]; scratch = new float[distance.Length];
        }
        if (density != scale || kernel == null)
        {
            density = scale;
            int radius = Mathf.CeilToInt(scale * 1.2f);
            kernel = new float[2 * radius + 1];
            float sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += kernel[k + radius] = Mathf.Exp(-k * k / (2 * scale * scale * .4f * .4f));
            for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;
        }
        bool Copper(int x, int y) => x >= 0 && y >= 0 && x < columns && y < rows &&
            occupancy[y * columns + x] != 0;
        // Most of a camera buffer is uniform earth. Classify that once per cell,
        // rather than repeating all neighbour comparisons for every field sample.
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            bool inside = Copper(x, y), mixed = false;
            for (int dy = -2; dy <= 2 && !mixed; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (Copper(x + dx, y + dy) != inside) { mixed = true; break; }
            boundary[y * columns + x] = mixed;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int cx = x / scale, cy = y / scale;
            bool inside = Copper(cx, cy);
            if (!boundary[cy * columns + cx])
            { distance[y * width + x] = inside ? 1.5f : -1.5f; continue; }
            float px = (x % scale + .5f) / scale, py = (y % scale + .5f) / scale;
            float nearest = 1.5f * 1.5f;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Copper(cx + dx, cy + dy) == inside) continue;
                float qx = Mathf.Max(Mathf.Abs(px - dx - .5f) - .5f, 0);
                float qy = Mathf.Max(Mathf.Abs(py - dy - .5f) - .5f, 0);
                nearest = Mathf.Min(nearest, qx * qx + qy * qy);
            }
            distance[y * width + x] = Mathf.Sqrt(nearest) * (inside ? 1 : -1);
        }
        int reach = kernel.Length / 2;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int k = -reach; k <= reach; k++)
                sum += distance[y * width + Mathf.Clamp(x + k, 0, width - 1)] * kernel[k + reach];
            scratch[y * width + x] = sum;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int k = -reach; k <= reach; k++)
                sum += scratch[Mathf.Clamp(y + k, 0, height - 1) * width + x] * kernel[k + reach];
            distance[y * width + x] = sum;
        }
        Texture.SetPixelData(distance, 0);
        Texture.Apply(false, false);
    }

    public void Dispose()
    {
        if (Texture)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
        }
        Texture = null; distance = scratch = null;
    }
}
