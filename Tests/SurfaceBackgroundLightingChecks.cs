using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public static class SurfaceBackgroundLightingChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static bool Near(float actual, float expected, float tolerance = 0.002f)
        => Mathf.Abs(actual - expected) <= tolerance;

    public static object Main()
    {
        Check(!Application.isPlaying, "Run Main in edit mode");
        var controller = UnityEngine.Object.FindFirstObjectByType<SurfaceBackgroundController>();
        Check(controller, "SurfaceBackgroundController missing");

        float surface = controller.GetSurfaceBackgroundBrightness();

        var background = UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>();
        Check(background && background.isActiveAndEnabled, "Fixed underground background is missing");
        var layer = UnityEngine.Object.FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == "NearHills");
        Check(layer, "Surface panorama is missing");
        layer.Refresh();
        Check(layer.Renderers.Where(renderer => renderer && renderer.enabled).All(renderer =>
            layer.segments.Contains(renderer.sprite) || layer.IsBottomEdge(renderer.sprite)),
            "Parallax has generated an underground renderer");
        var surfaceRenderer = layer.Renderers.FirstOrDefault(renderer => renderer && renderer.enabled &&
            layer.segments.Contains(renderer.sprite));
        Check(surfaceRenderer, "Surface panorama renderer is missing");
        var properties = new MaterialPropertyBlock();
        surfaceRenderer.GetPropertyBlock(properties);
        Check(Near(properties.GetFloat("_LightTop"), surface) &&
            Near(properties.GetFloat("_LightBottom"), surface),
            "Surface renderer should use its configured brightness uniformly");
        background.Refresh(controller.RenderCamera);
        Check(background.GetComponentInChildren<MeshRenderer>(true),
            "Fixed underground renderer is missing");
        return new { passed = true, surfaceBrightness = surface, undergroundOwner = background.name };
    }

    public static object Runtime()
    {
        Check(Application.isPlaying, "Run Runtime in play mode");
        var lighting = UnityEngine.Object.FindFirstObjectByType<MapLighting>();
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(lighting && map && map.Terrain, "Map lighting is missing");
        Check(lighting.IsReady && !lighting.IsCalculating, "Map lighting is not ready");

        float bestRange = 0f;
        int bestDepth = 0;
        int halfWidth = map.mapWidth / 2;
        int maxDepth = Mathf.Min(map.mapHeight - 1, 160);
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            float minimum = 1f;
            float maximum = 0f;
            for (int x = -halfWidth; x < map.mapWidth - halfWidth; x += 2)
            {
                float value = lighting.GetBrightness(new Vector3Int(x, -depth, 0));
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }
            if (maximum - minimum > bestRange)
            {
                bestRange = maximum - minimum;
                bestDepth = depth;
            }
        }
        Check(bestRange > 0.05f, "Lighting mask is not spatially local");

        var materialField = typeof(MapLighting).GetField("material",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var darknessMaterial = materialField?.GetValue(lighting) as Material;
        Check(darknessMaterial, "MapLighting darkness material is missing");
        var overlayRenderer = lighting.GetComponentsInChildren<MeshRenderer>(true)
            .FirstOrDefault(candidate => candidate.sharedMaterial == darknessMaterial);
        Check(overlayRenderer && overlayRenderer.sharedMaterial &&
            overlayRenderer.sharedMaterial.mainTexture, "Daylight mask texture is missing");
        Check(overlayRenderer.sortingOrder == 32760,
            "Daylight overlay no longer renders above foreground and background");

        var miner = UnityEngine.Object.FindFirstObjectByType<MinerPlayerVisual>();
        Check(miner && miner.headlamp, "Player headlamp is missing");
        float originalIntensity = miner.headlamp.intensity;
        Vector4 lamp;
        var updateHeadlamp = typeof(MapLighting).GetMethod("UpdateHeadlamp",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Check(updateHeadlamp != null, "MapLighting headlamp update is missing");
        try
        {
            miner.headlamp.intensity = 1f;
            updateHeadlamp.Invoke(lighting, null);
            lamp = darknessMaterial.GetVector("_HeadlampOriginRange");
            Check(lamp.z > 0f && lamp.w > 0f,
                "Headlamp is not connected to the local light overlay");
            Check(Near(lamp.x, miner.headlamp.transform.position.x) &&
                Near(lamp.y, miner.headlamp.transform.position.y),
                "Headlamp world position is not passed to the local light overlay");
        }
        finally
        {
            miner.headlamp.intensity = originalIntensity;
            updateHeadlamp.Invoke(lighting, null);
        }

        return new
        {
            passed = true,
            localLightRange = bestRange,
            strongestVariationDepth = bestDepth,
            headlampRange = lamp.z,
            headlampIntensity = lamp.w
        };
    }
}
