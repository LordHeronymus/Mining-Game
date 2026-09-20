// Run outside Play Mode: unity command run_script --file Tests/ParallaxChecks.cs
using System;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ParallaxChecks
{
    static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static SpriteRenderer[] Visible(ParallaxLayer layer) => layer.GetComponentsInChildren<SpriteRenderer>(true)
        .Where(r => r.enabled && r.gameObject.activeInHierarchy).OrderBy(r => r.bounds.min.x).ToArray();

    static void CheckCoverage(ParallaxLayer layer, Camera camera)
    {
        layer.Refresh();
        var tiles = Visible(layer);
        float left = camera.transform.position.x - camera.orthographicSize * camera.aspect;
        float right = camera.transform.position.x + camera.orthographicSize * camera.aspect;
        Assert(tiles.Length >= 3, "Expected a repeated panorama.");
        Assert(tiles[0].bounds.min.x <= left && tiles.Last().bounds.max.x >= right, "Camera is not covered.");
        for (int i = 1; i < tiles.Length; i++)
            Assert(Mathf.Abs(tiles[i - 1].bounds.max.x - tiles[i].bounds.min.x) < 0.005f,
                "Gap or overlap between consecutive segments.");
    }

    public static object Main()
    {
        Assert(!Application.isPlaying, "Run these checks outside Play Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        Texture2D texture = null;
        Sprite a = null, b = null, underground = null;
        Material material = null;
        try
        {
            var root = new GameObject("Parallax test");
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            var controller = root.AddComponent<SurfaceBackgroundController>();
            controller.targetCamera = camera;
            controller.cameraReferencePosition = Vector2.zero;
            controller.fadeWithDepth = false;
            var child = new GameObject("Layer");
            child.transform.SetParent(root.transform, false);
            var layer = child.AddComponent<ParallaxLayer>();
            texture = new Texture2D(96, 32);
            a = Sprite.Create(texture, new Rect(0, 0, 64, 32), new Vector2(0.2f, 0.8f), 16f, 0, SpriteMeshType.FullRect);
            b = Sprite.Create(texture, new Rect(64, 0, 32, 32), new Vector2(0.9f, 0.1f), 16f, 0, SpriteMeshType.FullRect);
            material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            layer.material = material;
            layer.sortingLayerName = "Default";
            layer.segments = new[] { a, b };
            layer.height = 10f;
            layer.horizontalOffset = 2f;
            layer.verticalOffset = 3f;
            layer.horizontalParallax = 0.25f;
            layer.verticalParallax = 0.5f;
            root.transform.position = new Vector3(7f, 5f, 0f);
            child.transform.localScale = new Vector3(1.5f, 2f, 1f);
            CheckCoverage(layer, camera);
            Assert(Mathf.Abs(Visible(layer)[0].bounds.center.y - 11f) < 0.001f, "Scaled vertical offset is wrong.");
            Assert(Mathf.Abs(Visible(layer)[0].bounds.size.y - 20f) < 0.001f, "Layer height/scaling is wrong.");
            float initialX = Visible(layer)[0].bounds.min.x;
            camera.transform.position += new Vector3(1f, 4f, 0f);
            CheckCoverage(layer, camera);
            Assert(Mathf.Abs(Visible(layer)[0].bounds.min.x - initialX - 0.75f) < 0.001f, "Horizontal parallax factor is wrong.");
            Assert(Mathf.Abs(Visible(layer)[0].bounds.center.y - 13f) < 0.001f, "Vertical parallax factor is wrong.");

            foreach (float x in new[] { -10000f, 10000f, -45f, 0f, 45f })
            foreach (float zoom in new[] { 3f, 20f, 100f })
            {
                camera.transform.position = new Vector3(x, 0f, -10f);
                camera.orthographicSize = zoom;
                CheckCoverage(layer, camera);
            }
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographicSize = 10f;
            CheckCoverage(layer, camera);
            int poolSize = layer.GetComponentsInChildren<SpriteRenderer>(true).Length;
            for (int i = 0; i < 100; i++)
            {
                camera.transform.position += Vector3.right * 2f;
                CheckCoverage(layer, camera);
            }
            Assert(layer.GetComponentsInChildren<SpriteRenderer>(true).Length == poolSize, "Pool grows during ordinary movement.");
            camera.transform.position = new Vector3(0f, 0f, -10f);
            CheckCoverage(layer, camera);
            Assert(Mathf.Abs(Visible(layer)[0].bounds.min.x - initialX) < 0.001f, "Movement accumulated drift.");

            // Global controls compose with the individual offset and non-uniform Transform scale.
            controller.verticalOffset = 4f;
            foreach (float backgroundZoom in new[] { 0.5f, 2f })
            {
                controller.zoom = backgroundZoom;
                CheckCoverage(layer, camera);
                Assert(Mathf.Abs(Visible(layer)[0].bounds.center.y - 15f) < 0.001f,
                    "Global Y offset must be additive and independent of zoom and layer scale.");
                Assert(Mathf.Abs(Visible(layer)[0].bounds.size.y - 20f * backgroundZoom) < 0.001f,
                    "Global zoom did not scale the background height.");
                Assert(Mathf.Abs(camera.orthographicSize - 10f) < 0.001f, "Background zoom changed the game camera.");
            }
            controller.verticalOffset = 0f;
            controller.zoom = 1f;

            layer.horizontalParallax = 0f;
            layer.verticalParallax = 0f;
            layer.Refresh();
            var fixedScreen = Visible(layer)[0].bounds.center - camera.transform.position;
            camera.transform.position += new Vector3(1f, 2f, 0f);
            layer.Refresh();
            Assert(Vector3.Distance(Visible(layer)[0].bounds.center - camera.transform.position, fixedScreen) < 0.001f,
                "Factor zero must be screen fixed.");
            layer.horizontalParallax = 1f;
            layer.verticalParallax = 1f;
            layer.Refresh();
            Vector3 fixedWorld = Visible(layer)[0].bounds.center;
            camera.transform.position += new Vector3(1f, 2f, 0f);
            layer.Refresh();
            Assert(Vector3.Distance(Visible(layer)[0].bounds.center, fixedWorld) < 0.001f, "Factor one must be world fixed.");

            layer.verticalOffset += 5f;
            layer.Refresh();
            Assert(Mathf.Abs(Visible(layer)[0].bounds.center.y - fixedWorld.y - 10f) < 0.001f, "Live offset edits failed.");
            controller.fadeWithDepth = true;
            controller.fadeStartY = 0f;
            controller.fadeEndY = -10f;
            controller.opacity = 0.8f;
            camera.transform.position = new Vector3(0f, -5f, -10f);
            layer.Refresh();
            Assert(Mathf.Abs(Visible(layer)[0].color.a - 0.4f) < 0.001f, "Depth fade is wrong.");
            layer.opacity = 0.5f;
            layer.Refresh();
            Assert(Mathf.Abs(Visible(layer)[0].color.a - 0.2f) < 0.001f,
                "Layer opacity must multiply global opacity and depth fade.");
            layer.opacity = 0f;
            layer.Refresh();
            Assert(Visible(layer).Length == 0, "Zero layer opacity must hide the layer.");
            layer.opacity = 1f;
            camera.transform.position = new Vector3(0f, -11f, -10f);
            layer.Refresh();
            Assert(Visible(layer).Length == 0, "Background must disappear below the fade range.");
            controller.fadeWithDepth = false;
            controller.enabled = false;
            layer.Refresh();
            Assert(Visible(layer).Length == 0, "Disabling the controller must hide its set.");
            controller.enabled = true;
            layer.segments = new[] { a, (Sprite)null };
            layer.Refresh();
            Assert(Visible(layer).Length == 0, "Invalid segments must not leave stale renderers.");
            layer.segments = new[] { a };
            CheckCoverage(layer, camera);
            layer.enabled = false;
            Assert(layer.transform.childCount == 0, "Disabling the layer must release generated objects.");
            layer.enabled = true;
            CheckCoverage(layer, camera);
            layer.extendBottomToCamera = true;
            layer.opacity = 0.5f;
            foreach (float backgroundZoom in new[] { 0.5f, 2f })
            {
                controller.zoom = backgroundZoom;
                layer.Refresh();
                var mainTiles = Visible(layer).Where(r => r.sprite == a).ToArray();
                var fills = Visible(layer).Where(r => r.sprite != a).ToArray();
                Assert(Visible(layer).All(r => Mathf.Abs(r.color.a - 0.4f) < 0.001f),
                    "Layer opacity must also apply to the sky continuation.");
                Assert(fills.Length == mainTiles.Length, "Each sky tile needs a bottom continuation.");
                for (int i = 0; i < fills.Length; i++)
                {
                    Assert(Mathf.Abs(fills[i].bounds.max.y - mainTiles[i].bounds.min.y) < 0.001f,
                        "Sky continuation must meet the image edge without a gap or overlap.");
                    Assert(Mathf.Abs(fills[i].bounds.min.x - mainTiles[i].bounds.min.x) < 0.001f &&
                        Mathf.Abs(fills[i].bounds.max.x - mainTiles[i].bounds.max.x) < 0.001f,
                        "Sky continuation must match its panorama width.");
                    Assert(fills[i].bounds.min.y < camera.transform.position.y - camera.orthographicSize,
                        "Sky continuation must reach below the viewport.");
                    Assert(fills[i].sprite.texture == texture, "Sky continuation should share the original texture.");
                }
            }
            layer.extendBottomToCamera = false;
            layer.Refresh();
            Assert(Visible(layer).All(r => r.sprite == a), "Turning off sky continuation must hide the strips.");
            layer.segments = new[] { a, b };
            layer.horizontalCount = 1;
            layer.Refresh();
            var singlePanorama = Visible(layer);
            Assert(singlePanorama.Length == 2 && singlePanorama[0].sprite == a && singlePanorama[1].sprite == b,
                "Disabling repetition must show each segment exactly once, in order.");
            Assert(Mathf.Abs(singlePanorama[0].bounds.max.x - singlePanorama[1].bounds.min.x) < 0.001f,
                "Non-repeating segments must remain adjacent.");
            Vector3 singlePosition = singlePanorama[0].bounds.center;
            camera.transform.position += Vector3.right * 1000f;
            layer.Refresh();
            Assert(Visible(layer).Length == 2 && Vector3.Distance(Visible(layer)[0].bounds.center, singlePosition) < 0.001f,
                "A non-repeating world-fixed panorama must not wrap after a camera teleport.");
            layer.extendBottomToCamera = true;
            layer.Refresh();
            Assert(Visible(layer).Length == 4, "Only the two original segments should receive sky extensions.");
            layer.extendBottomToCamera = false;
            foreach (int count in new[] { 2, 3 })
            {
                layer.horizontalCount = count;
                layer.Refresh();
                var copies = Visible(layer);
                Assert(copies.Length == 2 * count, "Finite repetition must repeat the complete segment sequence.");
                for (int i = 1; i < copies.Length; i++)
                    Assert(Mathf.Abs(copies[i - 1].bounds.max.x - copies[i].bounds.min.x) < 0.001f,
                        "Finite repetitions must remain adjacent.");
                float center = (copies[0].bounds.min.x + copies.Last().bounds.max.x) * 0.5f;
                Assert(Mathf.Abs(center - layer.transform.TransformPoint(new Vector3(layer.horizontalOffset, 0f, 0f)).x) < 0.001f,
                    "Even and odd repetition counts must stay centered on the layer anchor.");
            }
            layer.horizontalCount = 0;
            CheckCoverage(layer, camera);

            underground = Sprite.Create(texture, new Rect(0, 0, 96, 32),
                new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            layer.undergroundTile = underground;
            layer.horizontalCount = 1;
            layer.horizontalParallax = 1f;
            layer.verticalParallax = 1f;
            layer.verticalOffset = 0f;
            layer.opacity = 1f;
            controller.zoom = 1f;
            controller.opacity = 1f;
            controller.fadeWithDepth = false;
            camera.transform.position = new Vector3(0f, -10f, -10f);
            layer.Refresh();
            var surface = Visible(layer).Where(r => r.sprite != underground).ToArray();
            var earth = Visible(layer).Where(r => r.sprite == underground).ToArray();
            Assert(earth.Length > 0, "Underground tiles should cover the camera beneath the surface.");
            float seamY = surface.Min(r => r.bounds.min.y);
            float pixelHeight = earth[0].bounds.size.y / underground.rect.height;
            float undergroundTop = seamY + pixelHeight;
            Assert(Mathf.Abs(earth.Max(r => r.bounds.max.y) - undergroundTop) < 0.001f,
                "Underground must overlap the surface by one source pixel.");
            Assert(earth.All(r => r.sortingOrder == layer.sortingOrder + 1),
                "Underground must render over the surface in the overlap.");
            var firstRow = earth.Where(r => Mathf.Abs(r.bounds.max.y - undergroundTop) < 0.001f)
                .OrderBy(r => r.bounds.min.x).ToArray();
            Assert(firstRow.First().bounds.min.x <= camera.transform.position.x - camera.orthographicSize * camera.aspect &&
                firstRow.Last().bounds.max.x >= camera.transform.position.x + camera.orthographicSize * camera.aspect,
                "Underground must repeat across the viewport.");
            for (int i = 1; i < firstRow.Length; i++)
                Assert(Mathf.Abs(firstRow[i - 1].bounds.max.x - firstRow[i].bounds.min.x) < 0.001f,
                    "Underground horizontal tiles must meet without gaps.");
            camera.transform.position = new Vector3(0f, -100f, -10f);
            layer.Refresh();
            earth = Visible(layer).Where(r => r.sprite == underground).ToArray();
            Assert(earth.Length > 0 && earth.Min(r => r.bounds.min.y) <= -110f &&
                earth.Max(r => r.bounds.max.y) >= -90f,
                "Underground must repeat vertically at depth.");
            controller.fadeWithDepth = true;
            controller.fadeStartY = 0f;
            controller.fadeEndY = -10f;
            layer.ignoreDepthFade = true;
            layer.Refresh();
            Assert(Visible(layer).Where(r => r.sprite != underground).All(r => Mathf.Abs(r.color.a - 1f) < 0.001f),
                "NearHills must keep full opacity below the depth fade range.");
            Assert(Visible(layer).Where(r => r.sprite == underground).All(r => Mathf.Abs(r.color.a - 1f) < 0.001f),
                "Underground must keep full opacity below the depth fade range.");

            return "PASS: coverage, segment seams, underground X/Y repetition, one-pixel overlap, full NearHills opacity, zoom, parallax, depth fade and lifecycle.";
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (a) UnityEngine.Object.DestroyImmediate(a);
            if (b) UnityEngine.Object.DestroyImmediate(b);
            if (underground) UnityEngine.Object.DestroyImmediate(underground);
            if (texture) UnityEngine.Object.DestroyImmediate(texture);
            if (material) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
