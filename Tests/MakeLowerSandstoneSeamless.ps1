Add-Type -AssemblyName System.Drawing
$drawingReferences = @(
    [System.Drawing.Bitmap].Assembly.Location,
    [System.Drawing.Color].Assembly.Location,
    [System.Reflection.Assembly]::Load('System.Private.Windows.GdiPlus').Location,
    [System.Reflection.Assembly]::Load('System.Private.Windows.Core').Location
)

Add-Type -ReferencedAssemblies $drawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class LowerSandstoneSeam
{
    static byte Clamp(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));

    public static void Run(string sourcePath, string targetPath, string previewPath)
    {
        using (var source = new Bitmap(sourcePath))
        using (var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(canvas)) graphics.DrawImage(source, 0, 0);
            int width = canvas.Width, height = canvas.Height;
            var rect = new Rectangle(0, 0, width, height);
            var data = canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            if (data.Stride != width * 4) throw new InvalidOperationException("Unexpected bitmap stride");
            var original = new byte[height * data.Stride];
            Marshal.Copy(data.Scan0, original, 0, original.Length);
            canvas.UnlockBits(data);

            var shifted = new byte[original.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = (((y + height / 2) % height) * width + (x + width / 2) % width) * 4;
                int to = (y * width + x) * 4;
                for (int channel = 0; channel < 4; channel++) shifted[to + channel] = original[from + channel];
            }

            var pixels = (byte[])shifted.Clone();
            int xRadius = width / 8, xOffset = width / 4;
            for (int y = 0; y < height; y++)
            for (int x = width / 2 - xRadius; x <= width / 2 + xRadius; x++)
            {
                float weight = .5f + .5f * (float)Math.Cos(Math.PI * (x - width / 2f) / xRadius);
                int to = (y * width + x) * 4;
                int patch = (y * width + x + xOffset) * 4;
                for (int channel = 0; channel < 3; channel++)
                    pixels[to + channel] = Clamp(shifted[to + channel] * (1 - weight) + shifted[patch + channel] * weight);
            }

            var afterHorizontal = (byte[])pixels.Clone();
            int yRadius = height / 8, yOffset = height / 4;
            for (int y = height / 2 - yRadius; y <= height / 2 + yRadius; y++)
            for (int x = 0; x < width; x++)
            {
                float weight = .5f + .5f * (float)Math.Cos(Math.PI * (y - height / 2f) / yRadius);
                int to = (y * width + x) * 4;
                int patch = ((y + yOffset) * width + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                    pixels[to + channel] = Clamp(afterHorizontal[to + channel] * (1 - weight) + afterHorizontal[patch + channel] * weight);
            }

            for (int y = 0; y < height; y++)
            for (int channel = 0; channel < 3; channel++)
            {
                int left = (y * width) * 4 + channel;
                int right = (y * width + width - 1) * 4 + channel;
                int difference = pixels[right] - pixels[left];
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4 + channel;
                    pixels[index] = Clamp(pixels[index] + difference * (.5f - x / (float)(width - 1)));
                }
            }
            for (int x = 0; x < width; x++)
            for (int channel = 0; channel < 3; channel++)
            {
                int bottom = x * 4 + channel;
                int top = ((height - 1) * width + x) * 4 + channel;
                int difference = pixels[top] - pixels[bottom];
                for (int y = 0; y < height; y++)
                {
                    int index = (y * width + x) * 4 + channel;
                    pixels[index] = Clamp(pixels[index] + difference * (.5f - y / (float)(height - 1)));
                }
            }

            var outputData = canvas.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, outputData.Scan0, pixels.Length);
            canvas.UnlockBits(outputData);
            canvas.Save(targetPath, ImageFormat.Png);

            using (var preview = new Bitmap(1536, 1024))
            using (var graphics = Graphics.FromImage(preview))
            {
                for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    graphics.DrawImage(canvas, new Rectangle(x * 768, y * 512, 768, 512));
                preview.Save(previewPath, ImageFormat.Png);
            }
        }
    }
}
'@

$project = Split-Path $PSScriptRoot -Parent
[LowerSandstoneSeam]::Run(
    (Join-Path $project 'Assets/Design/LowerSandstoneSeamlessSource.png'),
    (Join-Path $project 'Assets/GameObjects/Background/Underground/LowerSandstone.png'),
    (Join-Path $project 'Assets/Design/LowerSandstoneSeamlessPreview.png')
)
