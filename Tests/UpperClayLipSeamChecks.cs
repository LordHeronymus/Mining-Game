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
        var material = background ? background.material :
            AssetDatabase.LoadAssetAtPath<Material>(root + "FixedUnderground.mat");
        if (!material || !lip || !clay || material.GetTexture("_CapTex") != lip)
            throw new Exception("The matched soil lip is not active");
        var importer = (TextureImporter)AssetImporter.GetAtPath(root + "UpperClayLip_L1Seamless.png");
        if (importer.maxTextureSize < lip.width || importer.mipmapEnabled ||
            importer.wrapModeU != TextureWrapMode.Repeat ||
            importer.wrapModeV != TextureWrapMode.Clamp)
            throw new Exception("The lip importer changes its baked L1 edge pixels");

        var lipPixels = Load(root + "UpperClayLip_L1Seamless.png");
        var clayPixels = Load(root + "UpperClay.png");
        try
        {
            float capHeight = material.GetVector("_CapSize").y;
            float topOffset = material.GetFloat("_CapTopOffset");
            clayPixels.wrapMode = TextureWrapMode.Repeat;
            float clayV = Mathf.Repeat((capHeight - topOffset) /
                (background ? background.textureHeight : 11f), 1f);
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
            float maximumSideDifference = 0f;
            for (int y = 96; y < lipPixels.height; y++)
            {
                Color left = lipPixels.GetPixel(0, y), right = lipPixels.GetPixel(lipPixels.width - 1, y);
                maximumSideDifference = Mathf.Max(maximumSideDifference,
                    Mathf.Abs(left.r - right.r), Mathf.Abs(left.g - right.g),
                    Mathf.Abs(left.b - right.b), Mathf.Abs(left.a - right.a));
            }
            if (maximumSideDifference > 1f / 255f)
                throw new Exception("The lip's repeated side edges or silhouette do not match");
            if (ShaderUtil.ShaderHasError(material.shader))
                throw new Exception("Underground shader has errors");
            return new { passed = true, maximumPixelDifference = maximumDifference,
                maximumSideDifference, lipWidth = lipPixels.width, samples = (lipPixels.width + 10) / 11 };
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

}
