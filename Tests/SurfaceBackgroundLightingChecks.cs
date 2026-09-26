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

        var layer = UnityEngine.Object.FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.undergroundTile);
        Check(layer, "Layer-1 underground background is missing");
        float imageHeight = layer.height * controller.EffectiveZoom;
        Vector3 scale = layer.transform.lossyScale;
        float surfaceWidth = 0f;
        foreach (Sprite sprite in layer.segments)
            surfaceWidth += imageHeight * sprite.rect.width / sprite.rect.height * scale.x;
        float tileHeight = surfaceWidth * layer.undergroundTile.rect.height /
            layer.undergroundTile.rect.width * scale.y / scale.x;
        Vector3 anchor = layer.transform.TransformPoint(new Vector3(layer.horizontalOffset,
            layer.verticalOffset, 0f));
        anchor.y += controller.verticalOffset;
        Vector2 delta = controller.GetParallaxCameraDelta(controller.RenderCamera);
        anchor.y += delta.y * (1f - Mathf.Clamp01(layer.verticalParallax));
        float undergroundTop = anchor.y - imageHeight * scale.y * 0.5f +
            layer.undergroundYOffsetPixels * tileHeight / layer.undergroundTile.rect.height;
        Camera camera = controller.RenderCamera;
        Check(camera && camera.orthographic, "Parallax camera is missing");
        float originalSize = camera.orthographicSize;
        object result;
        try
        {
            camera.orthographicSize = Mathf.Max(originalSize,
                Mathf.Abs(undergroundTop - camera.transform.position.y) + tileHeight + 1f);
            layer.Refresh();

            var surfaceRenderer = layer.Renderers.FirstOrDefault(renderer => renderer && renderer.enabled &&
                layer.segments.Contains(renderer.sprite));
            var undergroundRenderer = layer.Renderers.Where(renderer => renderer && renderer.enabled &&
                renderer.sprite == layer.undergroundTile).OrderByDescending(renderer => renderer.bounds.max.y)
                .FirstOrDefault();
            Check(surfaceRenderer && undergroundRenderer,
                "Generated surface/underground renderers are missing");
            float seamOverlap = undergroundRenderer.bounds.max.y - surfaceRenderer.bounds.min.y;
            float oneSourcePixel = undergroundRenderer.bounds.size.y /
                layer.undergroundTile.rect.height;
            Check(seamOverlap >= -0.0001f,
                "Surface and underground background contain a geometric gap");
            float seamAllowancePixels = Mathf.Max(1f, layer.undergroundYOffsetPixels) + 0.1f;
            Check(seamOverlap <= oneSourcePixel * seamAllowancePixels + 0.0001f,
                "Surface and underground background overlap by more than the seam allowance");
            Check(surfaceRenderer.sortingOrder > undergroundRenderer.sortingOrder,
                "The surface edge no longer covers the one-pixel underground overlap");

            var properties = new MaterialPropertyBlock();
            surfaceRenderer.GetPropertyBlock(properties);
            Check(Near(properties.GetFloat("_LightTop"), surface) &&
                Near(properties.GetFloat("_LightBottom"), surface),
                "Surface renderer should use its configured brightness uniformly");

            properties.Clear();
            undergroundRenderer.GetPropertyBlock(properties);
            Check(Near(properties.GetFloat("_LightTop"), 1f) &&
                Near(properties.GetFloat("_LightBottom"), 1f),
                "Underground renderer should no longer apply a background brightness curve");

            result = new
            {
                passed = true,
                surfaceBrightness = surface,
                seamOverlapPixels = seamOverlap / oneSourcePixel,
                layer1TopWorldY = undergroundRenderer.bounds.max.y,
                layer1TopBrightness = properties.GetFloat("_LightTop")
            };
        }
        finally
        {
            camera.orthographicSize = originalSize;
            layer.Refresh();
        }
        return result;
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
