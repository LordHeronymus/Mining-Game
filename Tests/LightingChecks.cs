// Run in the connected editor: unity command run_script --file Tests/LightingChecks.cs
using System;
using System.Linq;
using UnityEngine;

public static class LightingChecks
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Finish(GridDaylight field)
    {
        int visits = 0;
        while (field.HasPendingWork)
        {
            visits += field.Process(256);
            Assert(visits < 2000000, "Light propagation failed to converge.");
        }
    }

    public static object Main()
    {
        const int width = 31, height = 80;
        var solid = Enumerable.Repeat(true, width * height).ToArray();
        for (int y = 0; y <= 50; y++) solid[y * width + 5] = false;
        for (int x = 5; x <= 17; x++) solid[30 * width + x] = false;
        var shaft = new GridDaylight(width, height, solid, 1f, 0.005f, 0.1f, 1f);
        shaft.Reset(); Finish(shaft);
        Assert(Mathf.Abs(shaft[5, 30] - Mathf.Pow(0.995f, 31)) < 0.0001f, "Vertical shaft attenuation is wrong.");
        Assert(Mathf.Abs(shaft[10, 30] - Mathf.Pow(0.995f, 31) * Mathf.Pow(0.9f, 5)) < 0.0001f, "Side tunnel attenuation is wrong.");
        Assert(shaft[17, 30] < shaft[10, 30], "A longer side tunnel should be darker.");
        Assert(shaft[5, 50] > shaft[10, 30], "The deeper straight shaft should be brighter than the corner.");

        var rock = new GridDaylight(5, 40, Enumerable.Repeat(true, 200).ToArray(), 1f, 0.003f, 0.08f, 0.28f);
        rock.Reset(); Finish(rock);
        Assert(Mathf.Abs(rock[2, 0] - 0.997f * 0.72f) < 0.0001f, "Surface rock attenuation is wrong.");
        Assert(rock[2, 3] > 0f && rock[2, 30] == 0f, "Exponential light must have a soft tail ending in black.");

        var stronger = new GridDaylight(5, 40, Enumerable.Repeat(true, 200).ToArray(), 1f, 0.003f, 0.08f, 0.28f, 2f);
        stronger.Reset(); Finish(stronger);
        Assert(Mathf.Abs(stronger[2, 3] - rock[2, 3] * rock[2, 3]) < 0.00001f, "Strength must scale the exponent.");
        Assert(rock[2, 0] - rock[2, 1] > rock[2, 1] - rock[2, 2], "The absolute loss must decrease with depth.");
        Assert(GridDaylight.VisibleLight(0.001f) == 0f, "The tail must reach exact black.");
        Assert(GridDaylight.VisibleLight(0.002f) < GridDaylight.VisibleLight(0.01f), "The dark tail must remain monotonic.");
        Assert(Mathf.Abs(GridDaylight.VisibleLight(0.02f) - 0.02f) < 0.000001f, "The fade must join the exponential curve continuously.");

        shaft.SetSolid(5, 12, true); Finish(shaft);
        Assert(shaft[5, 30] == 0f, "Closing an opaque shaft must darken the lower tunnel.");
        shaft.SetSolid(5, 12, false); Finish(shaft);
        Assert(Mathf.Abs(shaft[5, 30] - Mathf.Pow(0.995f, 31)) < 0.0001f, "Mining must restore the open light path.");

        // Check incremental edits against an independent repeated-relaxation reference.
        const int w = 12, h = 16;
        var random = new System.Random(8341);
        var cells = Enumerable.Range(0, w * h).Select(_ => random.NextDouble() > 0.4).ToArray();
        var incremental = new GridDaylight(w, h, cells, 1f, 0.02f, 0.13f, 0.22f);
        incremental.Reset();
        int compared = 0;
        for (int edit = 0; edit < 40; edit++)
        {
            int index = random.Next(cells.Length);
            cells[index] = !cells[index];
            incremental.SetSolid(index % w, index / w, cells[index]);
            Finish(incremental);
            float[] expected = Reference(w, h, cells);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert(Mathf.Abs(incremental[i % w, i / w] - expected[i]) < 0.00001f,
                    "Incremental lighting mismatch after edit " + edit + " at cell " + i);
                compared++;
            }
        }
        return new { passed = true, straightShaft = shaft[5, 30], sideTunnel = shaft[10, 30], comparedCells = compared };
    }

    static float[] Reference(int w, int h, bool[] cells)
    {
        var result = new float[cells.Length];
        for (int pass = 0; pass < cells.Length; pass++)
        {
            bool changed = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float best = y == 0 ? 0.98f : result[i - w] * 0.98f;
                    if (x > 0) best = Mathf.Max(best, result[i - 1] * 0.87f);
                    if (x + 1 < w) best = Mathf.Max(best, result[i + 1] * 0.87f);
                    if (y + 1 < h) best = Mathf.Max(best, result[i + w] * 0.87f);
                    best *= cells[i] ? 0.78f : 1f;
                    if (best <= GridDaylight.DarknessCutoff) best = 0f;
                    if (best > result[i]) { result[i] = best; changed = true; }
                }
            if (!changed) return result;
        }
        throw new Exception("Reference failed to converge.");
    }
}
