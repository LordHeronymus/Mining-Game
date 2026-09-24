using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class IncrementalTextureUpdateChecks
{
    public static object Main()
    {
        if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            return new { passed = true, skipped = "Partial texture copies are unsupported; runtime uses the full-upload fallback." };

        const int width = 128, height = 128, chunkSize = 32;
        var initial = new Color32[width * height];
        for (int i = 0; i < initial.Length; i++)
            initial[i] = new Color32((byte)(i * 17), (byte)(i * 31), (byte)(i * 7), 255);
        var expectedPixels = (Color32[])initial.Clone();
        var actualPixels = (Color32[])initial.Clone();
        var expected = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var actual = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var patch = new Texture2D(chunkSize, chunkSize, TextureFormat.RGBA32, false, true);
        try
        {
            expected.SetPixels32(expectedPixels); expected.Apply(false, false);
            actual.SetPixels32(actualPixels); actual.Apply(false, false);

            // Include isolated mined cells and changes across chunk and texture edges.
            var changes = new[] { new Vector2Int(5, 7), new Vector2Int(31, 31),
                new Vector2Int(32, 32), new Vector2Int(62, 61), new Vector2Int(63, 63) };
            var dirty = new HashSet<Vector2Int>();
            foreach (var point in changes)
            {
                var color = new Color32((byte)(point.x * 3), (byte)(point.y * 5), 0, 255);
                expectedPixels[point.y * width + point.x] = color;
                actualPixels[point.y * width + point.x] = color;
                dirty.Add(new Vector2Int(point.x / chunkSize, point.y / chunkSize));
            }
            expected.SetPixels32(expectedPixels); expected.Apply(false, false);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            foreach (var chunk in dirty)
            {
                minX = Mathf.Min(minX, chunk.x); minY = Mathf.Min(minY, chunk.y);
                maxX = Mathf.Max(maxX, chunk.x); maxY = Mathf.Max(maxY, chunk.y);
            }
            int left = minX * chunkSize, bottom = minY * chunkSize;
            int copyWidth = Mathf.Min(width, (maxX + 1) * chunkSize) - left;
            int copyHeight = Mathf.Min(height, (maxY + 1) * chunkSize) - bottom;
            int patchWidth = Mathf.NextPowerOfTwo(copyWidth), patchHeight = Mathf.NextPowerOfTwo(copyHeight);
            UnityEngine.Object.DestroyImmediate(patch);
            patch = new Texture2D(patchWidth, patchHeight, TextureFormat.RGBA32, false, true);
            var patchPixels = new Color32[patchWidth * patchHeight];
            for (int y = 0; y < copyHeight; y++)
                Array.Copy(actualPixels, (bottom + y) * width + left, patchPixels, y * patchWidth, copyWidth);
            patch.SetPixels32(patchPixels); patch.Apply(false, false);
            Graphics.CopyTexture(patch, 0, 0, 0, 0, copyWidth, copyHeight, actual, 0, 0, left, bottom);

            // The occupancy mask also uses the same path for a single-cell update at the texture edge.
            var edgeColor = new Color32(3, 7, 11, 255);
            expectedPixels[(height - 1) * width + width - 1] = edgeColor;
            actualPixels[(height - 1) * width + width - 1] = edgeColor;
            expected.SetPixels32(expectedPixels); expected.Apply(false, false);
            UnityEngine.Object.DestroyImmediate(patch);
            patch = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            patch.SetPixel(0, 0, edgeColor); patch.Apply(false, false);
            Graphics.CopyTexture(patch, 0, 0, 0, 0, 1, 1, actual, 0, 0, width - 1, height - 1);

            var expectedReadback = AsyncGPUReadback.Request(expected, 0); expectedReadback.WaitForCompletion();
            var actualReadback = AsyncGPUReadback.Request(actual, 0); actualReadback.WaitForCompletion();
            if (expectedReadback.hasError || actualReadback.hasError)
                throw new Exception("GPU texture readback failed.");
            var expectedData = expectedReadback.GetData<Color32>();
            var actualData = actualReadback.GetData<Color32>();
            if (expectedData.Length != actualData.Length) throw new Exception("Texture readback sizes differ.");
            int differences = 0;
            for (int i = 0; i < expectedData.Length; i++)
                if (!expectedData[i].Equals(actualData[i])) differences++;
            if (differences != 0) throw new Exception("Partial texture upload differs from a full upload at " + differences + " pixels.");
            return new { passed = true, checkedPixels = expectedData.Length, changedCells = changes.Length + 1, updatedChunks = dirty.Count };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(expected);
            UnityEngine.Object.DestroyImmediate(actual);
            UnityEngine.Object.DestroyImmediate(patch);
        }
    }
}
