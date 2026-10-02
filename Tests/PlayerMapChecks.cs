using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class PlayerMapChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static object Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var discovery = UnityEngine.Object.FindFirstObjectByType<PlayerMapDiscovery>();
        var lighting = UnityEngine.Object.FindFirstObjectByType<MapLighting>();
        var camera = Camera.main;
        Check(map && discovery && lighting && camera, "Map discovery dependencies are missing.");
        Check(!GameplayTestSettings.GlobalLighting, "Disable Global Lighting for this check.");
        Check(!GameplayInputBlocker.IsBlocked && Time.timeScale > 0f,
            "Close modal panels and resume play for this check.");

        Check(map.IsGenerated && !map.IsGenerationStreaming && lighting.IsReady && !lighting.IsCalculating,
            "Wait for map generation and lighting to finish before running the check.");

        var originalState = discovery.CaptureState();
        float originalThreshold = discovery.revealLightThreshold;
        float originalDaylight = lighting.daylightStrength;
        float originalAmbient = lighting.ambientBrightness;
        Vector3 originalCameraPosition = camera.transform.position;
        TileSnapshot originalTile = default;
        TileSnapshot originalOre = default;
        TileSnapshot originalArtifact = default;
        var decorations = new List<DecorationSnapshot>();
        Vector3Int editedCell = default;
        bool edited = false;

        try
        {
            Check(discovery.Width == map.GeneratedWidth && discovery.Height == map.GeneratedHeight,
                "Discovery dimensions do not match the generated map.");

            lighting.daylightStrength = .45f;
            lighting.ambientBrightness = 0f;

            discovery.revealLightThreshold = 1f;
            discovery.ResetDiscovery();
            discovery.ScanVisibleCells(camera);
            var highLightState = discovery.CaptureState();

            discovery.revealLightThreshold = .001f;
            int lowLightChanges = discovery.ScanVisibleCells(camera);
            Check(lowLightChanges > 0, "Visible lit cells were not revealed.");

            bool foundThresholdDifference = false;
            bool foundSolid = false;
            int solidX = -1, solidDepth = -1;
            BlockType solidType = BlockType.Empty;
            ArtifactTile solidArtifact = null;
            for (int depth = 0; depth < discovery.Height; depth++)
                for (int x = 0; x < discovery.Width; x++)
                {
                    if (!discovery.TryGetSnapshot(x, depth, out var type, out var artifact)) continue;
                    int index = depth * discovery.Width + x;
                    if ((highLightState.discoveredBits[index >> 3] & (1 << (index & 7))) == 0)
                        foundThresholdDifference = true;
                    if (type == BlockType.Empty || (foundSolid && solidDepth > 0)) continue;
                    if (foundSolid && depth == 0) continue;
                    foundSolid = true;
                    solidX = x;
                    solidDepth = depth;
                    solidType = type;
                    solidArtifact = artifact;
                }
            Check(foundThresholdDifference, "Changing the light threshold did not affect discovery.");
            Check(foundSolid, "No visible solid cell was revealed for the last-seen check.");

            int distantX = solidX < discovery.Width / 2 ? discovery.Width - 1 : 0;
            var distantCell = new Vector3Int(distantX - discovery.Width / 2, -solidDepth, 0);
            Vector3 distantViewport = camera.WorldToViewportPoint(map.Terrain.GetCellCenterWorld(distantCell));
            Check(distantViewport.x < 0f || distantViewport.x > 1f,
                "The selected distant cell is unexpectedly on screen.");
            Check(!discovery.TryGetSnapshot(distantX, solidDepth, out _, out _),
                "An offscreen cell was revealed.");

            var saved = discovery.CaptureState();
            discovery.ResetDiscovery();
            Check(!discovery.TryGetSnapshot(solidX, solidDepth, out _, out _),
                "Reset did not hide a previously revealed cell.");
            Check(discovery.RestoreState(saved), "Valid discovery state was rejected.");
            Check(discovery.TryGetSnapshot(solidX, solidDepth, out var restoredType,
                      out var restoredArtifact) &&
                  restoredType == solidType && restoredArtifact == solidArtifact,
                "Restored discovery lost a visible tile snapshot.");

            var invalid = discovery.CaptureState();
            invalid.seed++;
            Check(!discovery.RestoreState(invalid), "A discovery state from another map seed was accepted.");

            editedCell = new Vector3Int(solidX - discovery.Width / 2, -solidDepth, 0);
            originalTile = TileSnapshot.Capture(map.Terrain, editedCell);
            originalOre = TileSnapshot.Capture(map.OreOverlay, editedCell);
            originalArtifact = TileSnapshot.Capture(map.ArtifactOverlay, editedCell);
            Check(originalTile.tile, "The revealed solid cell has no terrain tile.");
            if (solidDepth == 0)
                foreach (var layer in new[] { map.GrassOverlay, map.GrassHangLeftOverlay,
                             map.GrassHangRightOverlay })
                    if (layer)
                        for (int offset = -1; offset <= 1; offset++)
                        {
                            var cell = editedCell + new Vector3Int(offset, 0, 0);
                            decorations.Add(new DecorationSnapshot(layer, cell,
                                TileSnapshot.Capture(layer, cell)));
                        }

            camera.transform.position = originalCameraPosition + Vector3.right *
                Mathf.Max(100f, discovery.Width * 3f);
            map.Terrain.SetTile(editedCell, null);
            edited = true;
            discovery.ScanVisibleCells(camera);
            Check(discovery.TryGetSnapshot(solidX, solidDepth, out var hiddenType,
                      out var hiddenArtifact) &&
                  hiddenType == solidType && hiddenArtifact == solidArtifact,
                "An offscreen terrain change leaked into the last-seen map snapshot.");

            camera.transform.position = originalCameraPosition;
            discovery.ScanVisibleCells(camera);
            Check(discovery.TryGetSnapshot(solidX, solidDepth, out var updatedType, out _) &&
                  updatedType == BlockType.Empty,
                "The last-seen snapshot did not update after the changed cell became visible again.");

            return new { passed = true, lowLightChanges, thresholdAffectedDiscovery = true,
                offscreenStayedUnknown = true, lastSeenUpdatedOnReturn = true };
        }
        finally
        {
            camera.transform.position = originalCameraPosition;
            if (edited)
            {
                originalTile.Restore(map.Terrain, editedCell);
                originalOre.Restore(map.OreOverlay, editedCell);
                originalArtifact.Restore(map.ArtifactOverlay, editedCell);
                foreach (var decoration in decorations) decoration.Restore();
            }
            discovery.revealLightThreshold = originalThreshold;
            discovery.RestoreState(originalState);
            lighting.daylightStrength = originalDaylight;
            lighting.ambientBrightness = originalAmbient;
        }
    }

    public static object Panel()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var panel = UnityEngine.Object.FindFirstObjectByType<PlayerMapPanel>();
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var discovery = UnityEngine.Object.FindFirstObjectByType<PlayerMapDiscovery>();
        Check(panel && map && map.IsGenerated && discovery, "Runtime map panel is not ready.");
        Check(!panel.IsOpen && !GameplayInputBlocker.IsBlocked && Time.timeScale > 0f,
            "Close other modal panels before testing the map panel.");
        float previousScale = Time.timeScale;
        var open = typeof(PlayerMapPanel).GetMethod("Open", BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic);
        Check(open != null, "Map open handler is missing.");
        try
        {
            open.Invoke(panel, null);
            Check(panel.IsOpen && Mathf.Approximately(Time.timeScale, 0f) &&
                  GameplayInputBlocker.IsBlocked,
                "Opening the map did not pause and block gameplay input.");
            Check(discovery.ScanVisibleCells() == 0,
                "The paused map view revealed additional terrain.");
            return new { passed = true, mapOpen = panel.IsOpen, paused = true,
                gameplayBlocked = true };
        }
        finally
        {
            panel.Close();
            Check(!panel.IsOpen && Mathf.Approximately(Time.timeScale, previousScale) &&
                  !GameplayInputBlocker.IsBlocked,
                "Closing the map did not resume gameplay input and time.");
        }
    }

    readonly struct TileSnapshot
    {
        public readonly TileBase tile;
        readonly Color color;
        readonly Matrix4x4 transform;
        readonly TileFlags flags;

        TileSnapshot(TileBase tile, Color color, Matrix4x4 transform, TileFlags flags)
        {
            this.tile = tile;
            this.color = color;
            this.transform = transform;
            this.flags = flags;
        }

        public static TileSnapshot Capture(Tilemap map, Vector3Int cell)
            => map ? new TileSnapshot(map.GetTile(cell), map.GetColor(cell),
                map.GetTransformMatrix(cell), map.GetTileFlags(cell)) : default;

        public void Restore(Tilemap map, Vector3Int cell)
        {
            if (!map) return;
            map.SetTile(cell, tile);
            if (!tile) return;
            map.SetTileFlags(cell, TileFlags.None);
            map.SetColor(cell, color);
            map.SetTransformMatrix(cell, transform);
            map.SetTileFlags(cell, flags);
        }
    }

    readonly struct DecorationSnapshot
    {
        readonly Tilemap map;
        readonly Vector3Int cell;
        readonly TileSnapshot tile;

        public DecorationSnapshot(Tilemap map, Vector3Int cell, TileSnapshot tile)
        {
            this.map = map;
            this.cell = cell;
            this.tile = tile;
        }

        public void Restore() => tile.Restore(map, cell);
    }
}
