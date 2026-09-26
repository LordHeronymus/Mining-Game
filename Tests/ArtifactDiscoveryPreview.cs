using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class ArtifactDiscoveryPreview
{
    const string Folder = "Assets/Design/ArtifactFirstFind";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        if (GameObject.Find("Artifact Discovery Review Runner")) return "Review already running.";
        new GameObject("Artifact Discovery Review Runner");
        EditorApplication.isPaused = false;
        StatsManager.Instance.StartCoroutine(Run());
        return "Started real Game-view captures and first-discovery checks.";
    }

    static IEnumerator Run()
    {
        var stats = StatsManager.Instance;
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var miner = UnityEngine.Object.FindFirstObjectByType<TileMiner>();
        var camera = Camera.main;
        var cameraScripts = camera.GetComponents<MonoBehaviour>().Where(c => c.enabled && (c is CameraFollow || c is CameraWorldBorderClamp)).ToArray();
        Vector3 oldPlayer = miner.transform.position, oldCamera = camera.transform.position;
        float oldZoom = camera.orthographicSize, oldScale = Time.timeScale;
        var rb = miner.GetComponent<Rigidbody2D>();
        bool oldSimulated = rb.simulated;
        var cells = new Dictionary<Vector3Int, TileBase[]>();
        var torches = PlacedTorch.Active.ToArray();
        var inventory = InventoryManager.Instance;
        var torchItem = inventory.GetSnapshot().Select(x => x.Key).First(x => x.item == Item.Torche);
        int torchCount = inventory.GetCount(torchItem);
        var found = (HashSet<ArtifactTile>)typeof(StatsManager).GetField("collectedArtifactTypes", Private).GetValue(stats);
        var oldFound = found.ToArray();
        int oldPoints = stats.Points, oldArtifactPoints = stats.ArtifactPoints;
        var results = new List<string>();
        try
        {
            foreach (var script in cameraScripts) script.enabled = false;
            rb.simulated = false;
            for (int x = -10; x <= 10; x++)
                for (int y = -28; y <= -25; y++)
                {
                    Vector3Int c = new(x, y, 0);
                    cells[c] = new[] { map.Terrain.GetTile(c), map.OreOverlay.GetTile(c), map.ArtifactOverlay.GetTile(c) };
                    map.Terrain.SetTile(c, null); map.OreOverlay.SetTile(c, null); map.ArtifactOverlay.SetTile(c, null);
                }
            miner.transform.position = map.Terrain.GetCellCenterWorld(new Vector3Int(0, -28, 0));
            camera.transform.position = map.Terrain.GetCellCenterWorld(new Vector3Int(0, -26, 0)) + new Vector3(0, 0, -10);
            camera.orthographicSize = 6.2f;
            foreach (int x in new[] { -6, 6 })
            {
                var p = map.Terrain.GetCellCenterWorld(new Vector3Int(x, -28, 0));
                PlacedTorch.TryPlace(map, torchItem, p, p, 2);
            }
            yield return null;
            yield return new WaitForEndOfFrame();

            for (int index = 1; index <= 10; index++)
            {
                var artifact = AssetDatabase.LoadAssetAtPath<ArtifactTile>("Assets/GameObjects/Map/Artifacts/Artifact_" + index.ToString("00") + ".asset");
                ArtifactDiscoveryView.ShowArtifact(artifact);
                var view = ArtifactDiscoveryView.Instance;
                view.SetPresentationTime(1.85f);
                yield return new WaitForEndOfFrame();
                var title = view.GetComponentInChildren<TextMeshProUGUI>();
                if (title.isTextOverflowing) throw new Exception("Title overflow: " + artifact.displayName);
                Capture(Folder + "/InGame-" + index.ToString("00") + ".png");
                results.Add(artifact.displayName + ": sprite, theme and full title rendered; font size " + title.fontSize);
                if (index == 3)
                {
                    foreach (float t in new[] { .05f, .22f, .45f, 1.2f, 2.4f, 2.85f })
                    {
                        view.SetPresentationTime(t);
                        yield return new WaitForEndOfFrame();
                        Capture(Folder + "/Animation-" + t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    }
                }
                typeof(ArtifactDiscoveryView).GetMethod("Finish", Private).Invoke(view, null);
            }

            var jade = AssetDatabase.LoadAssetAtPath<ArtifactTile>("Assets/GameObjects/Map/Artifacts/Artifact_03.asset");
            found.Remove(jade);
            float started = Time.realtimeSinceStartup;
            stats.CollectArtifact(jade, 0, 1);
            if (!ArtifactDiscoveryView.Instance.IsShowing || Time.timeScale != 0 || !GameplayInputBlocker.IsBlocked)
                throw new Exception("First collection did not show and pause.");
            float expectedDuration = ArtifactDiscoveryView.Instance.DurationSeconds;
            while (ArtifactDiscoveryView.Instance.IsShowing) yield return null;
            float duration = Time.realtimeSinceStartup - started;
            if (duration < expectedDuration - .1f || duration > expectedDuration + .3f)
                throw new Exception("Incorrect duration: " + duration);
            if (Time.timeScale != oldScale || GameplayInputBlocker.IsBlocked) throw new Exception("Pause/input not restored.");
            stats.CollectArtifact(jade, 0, 1);
            if (ArtifactDiscoveryView.Instance.IsShowing) throw new Exception("Repeat collection opened discovery.");
            results.Add("Actual CollectArtifact first discovery: " + duration.ToString("0.000") + " seconds; repeat suppressed; time and input restored.");

            ArtifactDiscoveryView.ShowArtifact(jade);
            var ruby = AssetDatabase.LoadAssetAtPath<ArtifactTile>("Assets/GameObjects/Map/Artifacts/Artifact_07.asset");
            ArtifactDiscoveryView.ShowArtifact(ruby);
            ArtifactDiscoveryView.Instance.SetPresentationTime(ArtifactDiscoveryView.Instance.DurationSeconds - .01f);
            yield return null;
            yield return null;
            if (ArtifactDiscoveryView.Instance.CurrentArtifact != ruby) throw new Exception("Queued discovery lost.");
            typeof(ArtifactDiscoveryView).GetMethod("Finish", Private).Invoke(ArtifactDiscoveryView.Instance, null);
            results.Add("Simultaneous first discoveries queue without replacing the active reveal.");
            results.Add("PASS");
        }
        finally
        {
            UnityEngine.Object.Destroy(GameObject.Find("Artifact Discovery Review Runner"));
            if (ArtifactDiscoveryView.Instance) UnityEngine.Object.Destroy(ArtifactDiscoveryView.Instance.gameObject);
            foreach (var pair in cells)
            {
                map.Terrain.SetTile(pair.Key, pair.Value[0]);
                map.OreOverlay.SetTile(pair.Key, pair.Value[1]);
                map.ArtifactOverlay.SetTile(pair.Key, pair.Value[2]);
            }
            foreach (var torch in PlacedTorch.Active.ToArray()) if (!torches.Contains(torch)) UnityEngine.Object.Destroy(torch.gameObject);
            int missing = torchCount - inventory.GetCount(torchItem);
            if (missing > 0) inventory.Add(torchItem, missing);
            miner.transform.position = oldPlayer;
            rb.simulated = oldSimulated;
            camera.transform.position = oldCamera;
            camera.orthographicSize = oldZoom;
            foreach (var script in cameraScripts) script.enabled = true;
            Time.timeScale = oldScale;
            found.Clear(); foreach (var artifact in oldFound) found.Add(artifact);
            typeof(StatsManager).GetField("<Points>k__BackingField", Private).SetValue(stats, oldPoints);
            typeof(StatsManager).GetField("<ArtifactPoints>k__BackingField", Private).SetValue(stats, oldArtifactPoints);
            HUDPoints.Instance?.UpdatePoints(oldPoints, PointType.Points);
            File.WriteAllLines(Folder + "/checks.txt", results);
        }
    }

    static void Capture(string path)
    {
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
    }
}
