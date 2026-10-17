using System;
using Unity.Collections;
using UnityEngine;

// Cached world-aligned distance field of the combined deposit, independent of art UVs.
public sealed class CopperOutlineField : IDisposable
{
    public Texture2D Texture { get; private set; }
    float[] distance, scratch, kernel;
    int[] runEnd, summed;
    byte[] occupancy;
    bool[] boundary, boundaryRow, activeColumn;
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
        // A cell is on the boundary when its 5x5 neighbourhood (outside the buffer counts as earth) is mixed.
        // A summed-area table answers that per cell without 25 neighbour lookups.
        if (summed == null || summed.Length != (columns + 1) * (rows + 1))
        { summed = new int[(columns + 1) * (rows + 1)]; boundaryRow = new bool[rows]; activeColumn = new bool[columns]; }
        for (int y = 0; y < rows; y++)
        {
            int line = 0;
            for (int x = 0; x < columns; x++)
            {
                line += occupancy[y * columns + x];
                summed[(y + 1) * (columns + 1) + x + 1] = summed[y * (columns + 1) + x + 1] + line;
            }
        }
        Array.Clear(boundaryRow, 0, rows); Array.Clear(activeColumn, 0, columns);
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            int x0 = Math.Max(0, x - 2), y0 = Math.Max(0, y - 2), x1 = Math.Min(columns, x + 3), y1 = Math.Min(rows, y + 3);
            int count = summed[y1 * (columns + 1) + x1] - summed[y0 * (columns + 1) + x1] - summed[y1 * (columns + 1) + x0] + summed[y0 * (columns + 1) + x0];
            bool mixed = occupancy[y * columns + x] != 0 ? count < 25 : count > 0;
            boundary[y * columns + x] = mixed;
            if (!mixed) continue;
            boundaryRow[y] = true;
            // The horizontal blur reaches 1.25 cells, so columns within two cells of a boundary vary vertically.
            for (int c = Math.Max(0, x - 2); c <= Math.Min(columns - 1, x + 2); c++) activeColumn[c] = true;
        }
        for (int y = 0; y < height; y++)
        {
            int cy = y / scale;
            for (int x = 0; x < width; x++)
            {
                int cx = x / scale;
                bool inside = occupancy[cy * columns + cx] != 0;
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
        }
        // Separable blur. Away from copper edges a whole kernel window holds one constant value; its blurred
        // value is then known in advance (accumulated in the same order, so results stay bit-identical).
        // Rows without boundary cells and columns more than two cells away from one are uniform and filled directly.
        int reach = kernel.Length / 2;
        float inside1 = Blurred(1.5f), outside1 = Blurred(-1.5f);
        float inside2 = Blurred(inside1), outside2 = Blurred(outside1);
        if (runEnd == null || runEnd.Length < Mathf.Max(width, height)) runEnd = new int[Mathf.Max(width, height)];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            if (!boundaryRow[y / scale])
            {
                float uniform = distance[row] > 0 ? inside1 : outside1;
                for (int x = 0; x < width; x++) scratch[row + x] = uniform;
                continue;
            }
            runEnd[width - 1] = width - 1;
            for (int x = width - 2; x >= 0; x--) runEnd[x] = distance[row + x] == distance[row + x + 1] ? runEnd[x + 1] : x;
            for (int x = 0; x < width; x++)
            {
                int from = Mathf.Max(0, x - reach), to = Mathf.Min(width - 1, x + reach);
                float value = distance[row + from];
                if (runEnd[from] >= to && (value == 1.5f || value == -1.5f)) { scratch[row + x] = value > 0 ? inside1 : outside1; continue; }
                float sum = 0;
                for (int k = -reach; k <= reach; k++)
                    sum += distance[row + Mathf.Clamp(x + k, 0, width - 1)] * kernel[k + reach];
                scratch[row + x] = sum;
            }
        }
        for (int x = 0; x < width; x++)
        {
            if (!activeColumn[x / scale])
            {
                float uniform = occupancy[x / scale] != 0 ? inside2 : outside2;
                for (int y = 0; y < height; y++) distance[y * width + x] = uniform;
                continue;
            }
            runEnd[height - 1] = height - 1;
            for (int y = height - 2; y >= 0; y--) runEnd[y] = scratch[y * width + x] == scratch[(y + 1) * width + x] ? runEnd[y + 1] : y;
            for (int y = 0; y < height; y++)
            {
                int from = Mathf.Max(0, y - reach), to = Mathf.Min(height - 1, y + reach);
                float value = scratch[from * width + x];
                if (runEnd[from] >= to && (value == inside1 || value == outside1)) { distance[y * width + x] = value == inside1 ? inside2 : outside2; continue; }
                float sum = 0;
                for (int k = -reach; k <= reach; k++)
                    sum += scratch[Mathf.Clamp(y + k, 0, height - 1) * width + x] * kernel[k + reach];
                distance[y * width + x] = sum;
            }
        }
        Texture.SetPixelData(distance, 0);
        Texture.Apply(false, false);
    }

    float Blurred(float constant)
    {
        float sum = 0;
        for (int k = 0; k < kernel.Length; k++) sum += constant * kernel[k];
        return sum;
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
