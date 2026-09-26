using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class UpperClayLipSeamChecks
{
    public static object Main()
    {
        const string root = "Assets/GameObjects/Background/Underground/";
        var background = UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>();
        var lip = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "UpperClayLip_L1Seamless.png");
        var clay = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "UpperClay.png");
        if (!background || !lip || !clay || background.material.GetTexture("_CapTex") != lip)
            throw new Exception("The matched soil lip is not active");
        var importer = (TextureImporter)AssetImporter.GetAtPath(root + "UpperClayLip_L1Seamless.png");
        if (importer.maxTextureSize < lip.width || importer.mipmapEnabled ||
            importer.wrapModeU != TextureWrapMode.Mirror ||
            importer.wrapModeV != TextureWrapMode.Mirror)
            throw new Exception("The lip importer changes its baked L1 edge pixels");

        var lipPixels = Load(root + "UpperClayLip_L1Seamless.png");
        var clayPixels = Load(root + "UpperClay.png");
        try
        {
            float capHeight = background.material.GetVector("_CapSize").y;
            float topOffset = background.material.GetFloat("_CapTopOffset");
            float clayV = Mirror((topOffset - capHeight) / background.textureHeight);
            float maximumDifference = 0f;
            for (int x = 0; x < lipPixels.width; x += 11)
            {
                float u = (x + .5f) / lipPixels.width;
                Color actual = lipPixels.GetPixel(x, 0);
                Color expected = clayPixels.GetPixelBilinear(u, clayV);
                maximumDifference = Mathf.Max(maximumDifference,
                    Mathf.Abs(actual.r - expected.r),
                    Mathf.Abs(actual.g - expected.g),
                    Mathf.Abs(actual.b - expected.b));
                if (actual.a < .99f)
                    throw new Exception("The lip bottom must be opaque");
            }
            if (maximumDifference > 2f / 255f)
                throw new Exception("The lip bottom no longer matches L1: " + maximumDifference);
            if (ShaderUtil.ShaderHasError(background.material.shader))
                throw new Exception("Underground shader has errors");
            return new { passed = true, maximumPixelDifference = maximumDifference,
                lipWidth = lipPixels.width, samples = (lipPixels.width + 10) / 11 };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(lipPixels);
            UnityEngine.Object.DestroyImmediate(clayPixels);
        }
    }

    static Texture2D Load(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + path)));
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    static float Mirror(float value)
        => 1f - Mathf.Abs(Mathf.Repeat(value * .5f, 1f) * 2f - 1f);
}
