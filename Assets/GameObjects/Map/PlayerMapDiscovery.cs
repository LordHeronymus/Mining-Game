using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent, DefaultExecutionOrder(1450)]
[RequireComponent(typeof(MapGenerator), typeof(MapLighting))]
public sealed class PlayerMapDiscovery : MonoBehaviour
{

    static readonly Unity.Profiling.ProfilerMarker ScanMarker = new Unity.Profiling.ProfilerMarker("Mining.Discovery");
    const int SaveVersion = 1;

    static readonly Vector3Int[] Neighbors =
    {
        Vector3Int.up, Vector3Int.right, Vector3Int.down, Vector3Int.left
    };

    [Range(0f, 1f)] public float revealLightThreshold = .3f;
    [Range(0f, 1f)] public float panelAlpha = .88f;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public event Action<int, int> CellChanged;
    public event Action MapReset;

    [Serializable]
    public sealed class State
    {
        public int version = SaveVersion;
        public int seed;
        public int width;
        public int height;
        public byte[] discoveredBits;
        public byte[] blockTypes;
        public ArtifactState[] artifacts;
    }

    [Serializable]
    public struct ArtifactState
    {
        public int index;
        public string tileName;
    }

    struct LocalLight
    {
        public Vector2 position;
        public float radius;
        public float intensity;
    }

    MapGenerator map;
    MapLighting lighting;
    Tilemap tiles;
    byte[] discoveredBits = Array.Empty<byte>();
    byte[] blockTypes = Array.Empty<byte>();
    readonly Dictionary<int, ArtifactTile> artifacts = new Dictionary<int, ArtifactTile>();
    readonly Dictionary<Vector3Int, float> brightnessCache = new Dictionary<Vector3Int, float>();
    readonly List<Vector3Int> visibleUltronium = new List<Vector3Int>();
    readonly List<LocalLight> localLights = new List<LocalLight>(32);
    Vector3Int lastLightCameraCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    float nextLightRefresh;
    UltroniumAltarChamber altar;
    UltroniumChamberLayout altarLayout;
    Vector4 altarLightSource;
    Vector2 gridOrigin, gridRight, gridUp;
    float gridDeterminant;
    BoundsInt scanBounds;
    TileBase[] scanTiles = Array.Empty<TileBase>();
    Vector3 viewportCenter, viewportRight, viewportUp;
    bool affineViewport;

    void Awake() { GpsSettings.ApplyComponent(this); ResolveDependencies(); }

    void OnEnable()
    {
        ResolveDependencies();
        if (!map) return;
        map.Generated += ResetDiscovery;
        if (map.IsGenerated) ResetDiscovery();
    }

    void OnDisable()
    {
        if (map) map.Generated -= ResetDiscovery;
    }

    void OnValidate()
    {
        revealLightThreshold = Mathf.Clamp01(revealLightThreshold);
        panelAlpha = Mathf.Clamp01(panelAlpha);
    }

    void ResolveDependencies()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (!lighting) lighting = GetComponent<MapLighting>();
        if (!tiles && map) tiles = map.Terrain;
    }

    public void ResetDiscovery()
    {
        ResolveDependencies();
        Width = map ? Mathf.Max(0, map.GeneratedWidth) : 0;
        Height = map ? Mathf.Max(0, map.GeneratedHeight) : 0;
        int count = checked(Width * Height);
        discoveredBits = new byte[(count + 7) / 8];
        blockTypes = new byte[count];
        artifacts.Clear();
        brightnessCache.Clear();
        visibleUltronium.Clear();
        localLights.Clear();
        lastLightCameraCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        nextLightRefresh = 0f;
        MapReset?.Invoke();
    }

    public bool TryGetSnapshot(int x, int depth, out BlockType type, out ArtifactTile artifact)
    {
        type = BlockType.Empty;
        artifact = null;
        if (x < 0 || x >= Width || depth < 0 || depth >= Height) return false;
        int index = depth * Width + x;
        if (!IsDiscovered(index)) return false;
        type = (BlockType)blockTypes[index];
        artifacts.TryGetValue(index, out artifact);
        return true;
    }

    void LateUpdate() => ScanVisibleCells();

    public int ScanVisibleCells(Camera camera = null)
    {
        using var profile = ScanMarker.Auto();
        ResolveDependencies();
        if (!Application.isPlaying || !map || !map.IsGenerated || map.IsGenerationStreaming ||
            !lighting || !lighting.isActiveAndEnabled || !lighting.lightingEnabled ||
            !lighting.IsReady || lighting.IsCalculating || GameplayTestSettings.GlobalLighting ||
            GameplayInputBlocker.IsBlocked || Time.timeScale <= 0f || !tiles ||
            Width != map.GeneratedWidth || Height != map.GeneratedHeight)
            return 0;

        if (!camera) camera = Camera.main;
        if (!camera || !camera.isActiveAndEnabled) return 0;

        Vector3 origin = tiles.CellToWorld(Vector3Int.zero);
        Vector3 projectedOrigin = camera.WorldToViewportPoint(origin);
        Vector3 projectedRight = camera.WorldToViewportPoint(
            tiles.CellToWorld(Vector3Int.right)) - projectedOrigin;
        Vector3 projectedUp = camera.WorldToViewportPoint(
            tiles.CellToWorld(Vector3Int.up)) - projectedOrigin;
        var halfViewportCell = new Vector2(
            (Mathf.Abs(projectedRight.x) + Mathf.Abs(projectedUp.x)) * .5f,
            (Mathf.Abs(projectedRight.y) + Mathf.Abs(projectedUp.y)) * .5f);

        float distance = Vector3.Dot(tiles.transform.position - camera.transform.position,
            camera.transform.forward);
        if (distance <= 0f) return 0;

        Vector3Int minimum = new Vector3Int(int.MaxValue, int.MaxValue, 0);
        Vector3Int maximum = new Vector3Int(int.MinValue, int.MinValue, 0);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3Int cell = tiles.WorldToCell(camera.ViewportToWorldPoint(
                new Vector3(corner & 1, corner >> 1, distance)));
            minimum = Vector3Int.Min(minimum, cell);
            maximum = Vector3Int.Max(maximum, cell);
        }

        int left = Mathf.Max(-Width / 2, minimum.x);
        int right = Mathf.Min(-Width / 2 + Width - 1, maximum.x);
        int bottom = Mathf.Max(1 - Height, minimum.y);
        int top = Mathf.Min(0, maximum.y);
        if (left > right || bottom > top) return 0;

        scanBounds = new BoundsInt(left - 1, bottom - 1, 0,
            right - left + 3, top - bottom + 3, 1);
        int scanCount = checked(scanBounds.size.x * scanBounds.size.y);
        if (scanTiles.Length < scanCount) scanTiles = new TileBase[Mathf.NextPowerOfTwo(scanCount)];
        tiles.GetTilesBlockNonAlloc(scanBounds, scanTiles);
        // Orthographic projection is affine: three projections cover every cell.
        affineViewport = camera.orthographic;
        if (affineViewport)
        {
            viewportCenter = camera.WorldToViewportPoint(tiles.GetCellCenterWorld(Vector3Int.zero));
            viewportRight = projectedRight;
            viewportUp = projectedUp;
        }
        PrepareLocalLights(camera, minimum, maximum);
        brightnessCache.Clear();
        float threshold = Mathf.Clamp01(revealLightThreshold);
        int changed = 0;
        for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (!IsOnScreen(cell, camera, halfViewportCell)) continue;
                TileBase terrain = TerrainAt(cell);
                if (terrain)
                {
                    // The open side controls whether the wall can be recognized. The wall
                    // itself may be darker because the map light attenuates inside rock.
                    if (!HasVisibleSurface(cell, camera, halfViewportCell, threshold) ||
                        GetVisibleBrightness(cell) < threshold * .2f) continue;
                }
                else if (GetVisibleBrightness(cell) < threshold) continue;

                int index = -y * Width + x + Width / 2;
                OreTile ore = terrain && map.OreOverlay ? map.OreOverlay.GetTile<OreTile>(cell) : null;
                Block block = terrain ? ore && ore.block ? ore.block : map.registry ? map.registry.FromTile(terrain) : null : null;
                BlockType type = block ? block.id : BlockType.Empty;
                ArtifactTile artifact = block ? map.GetArtifactAt(cell) : null;
                if (IsDiscovered(index) && blockTypes[index] == (byte)type &&
                    CurrentArtifactMatches(index, artifact)) continue;

                discoveredBits[index >> 3] |= (byte)(1 << (index & 7));
                blockTypes[index] = (byte)type;
                if (artifact) artifacts[index] = artifact;
                else artifacts.Remove(index);
                changed++;
                CellChanged?.Invoke(x + Width / 2, -y);
            }
        return changed;
    }

    bool HasVisibleSurface(Vector3Int cell, Camera camera, Vector2 halfViewportCell,
        float threshold)
    {
        foreach (Vector3Int offset in Neighbors)
        {
            Vector3Int neighbor = cell + offset;
            if (TerrainAt(neighbor) || !IsOnScreen(neighbor, camera, halfViewportCell)) continue;
            if (GetVisibleBrightness(neighbor) >= threshold) return true;
        }
        return false;
    }

    void PrepareLocalLights(Camera camera, Vector3Int minimum, Vector3Int maximum)
    {
        Vector2 origin = tiles.CellToWorld(Vector3Int.zero);
        gridOrigin = origin;
        gridRight = (Vector2)tiles.CellToWorld(Vector3Int.right) - origin;
        gridUp = (Vector2)tiles.CellToWorld(Vector3Int.up) - origin;
        gridDeterminant = gridRight.x * gridUp.y - gridRight.y * gridUp.x;

        altar = map.AltarChamber;
        altarLayout = altar ? altar.Layout : default;
        altarLightSource = altar ? altar.LightSource : Vector4.zero;

        localLights.Clear();
        if (lighting.ultroniumLightIntensity <= 0f) return;

        Vector3Int cameraCell = tiles.WorldToCell(camera.transform.position);
        if (cameraCell != lastLightCameraCell || Time.time >= nextLightRefresh)
        {
            lastLightCameraCell = cameraCell;
            nextLightRefresh = Time.time + .2f;
            visibleUltronium.Clear();
            int minX = Mathf.Max(-Width / 2, minimum.x - 2);
            int maxX = Mathf.Min(-Width / 2 + Width - 1, maximum.x + 2);
            int minY = Mathf.Max(1 - Height, minimum.y - 2);
            int maxY = Mathf.Min(0, maximum.y + 2);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    OreTile ore = map.GetOreAt(cell);
                    if (ore && ore.block && ore.block.id == BlockType.UltroniumOre)
                        visibleUltronium.Add(cell);
                }
            visibleUltronium.Sort((a, b) =>
            {
                int ax = a.x - cameraCell.x, ay = a.y - cameraCell.y;
                int bx = b.x - cameraCell.x, by = b.y - cameraCell.y;
                return (ax * ax + ay * ay).CompareTo(bx * bx + by * by);
            });
        }

        int sourceCount = Mathf.Min(32, visibleUltronium.Count);
        float radius = Mathf.Max(1f,
            tiles.layoutGrid.cellSize.x * tiles.transform.lossyScale.x * 1.8f);
        for (int i = 0; i < sourceCount; i++)
        {
            Vector3Int cell = visibleUltronium[i];
            OreTile ore = map.GetOreAt(cell);
            if (!ore || !ore.block || ore.block.id != BlockType.UltroniumOre) continue;
            localLights.Add(new LocalLight
            {
                position = tiles.GetCellCenterWorld(cell),
                radius = radius,
                intensity = lighting.ultroniumLightIntensity *
                    MapLighting.UltroniumPulse(cell, Time.time,
                        lighting.ultroniumPulsesPerMinute)
            });
        }
    }

    float GetVisibleBrightness(Vector3Int cell)
    {
        if (brightnessCache.TryGetValue(cell, out float cached)) return cached;

        float brightness = lighting.GetBrightness(cell);
        Vector2 target = tiles.GetCellCenterWorld(cell);
        for (int i = 0; i < localLights.Count; i++)
        {
            LocalLight source = localLights[i];
            float distance = Vector2.Distance(source.position, target);
            if (distance >= source.radius) continue;
            float contribution = Mathf.Clamp01(source.intensity *
                (1f - Mathf.SmoothStep(0f, 1f, distance / source.radius)));
            if (contribution > brightness && HasClearLine(source.position, target))
                brightness = contribution;
        }

        if (altar && altarLayout.valid && altarLightSource.w > 0f)
        {
            bool chamberCell = altarLayout.IsOpen(cell) && !TerrainAt(cell);
            bool exposedShell = altarLayout.IsShell(cell) && HasOpenNeighbor(cell);
            if (altar.ChamberLightMask && (chamberCell || exposedShell))
                brightness = Mathf.Max(brightness, Mathf.Clamp01(.72f + altarLightSource.w * .2f));

            float radius = altarLightSource.z;
            if (radius > 0f)
            {
                Vector2 source = new Vector2(altarLightSource.x, altarLightSource.y);
                float distance = Vector2.Distance(source, target);
                if (distance < radius)
                {
                    float contribution = Mathf.Clamp01(altarLightSource.w *
                        (1f - Mathf.SmoothStep(0f, 1f, distance / radius)));
                    if (contribution > brightness && HasClearLine(source, target))
                        brightness = contribution;
                }
            }
        }

        brightnessCache[cell] = brightness;
        return brightness;
    }

    bool HasOpenNeighbor(Vector3Int cell)
    {
        foreach (Vector3Int offset in Neighbors)
            if (!TerrainAt(cell + offset) && !altarLayout.IsShell(cell + offset)) return true;
        return false;
    }

    bool HasClearLine(Vector2 source, Vector2 target)
    {
        if (Mathf.Abs(gridDeterminant) < .000001f) return false;
        Vector2 start = WorldToGrid(source);
        Vector2 end = WorldToGrid(target);
        int x = Mathf.FloorToInt(start.x), y = Mathf.FloorToInt(start.y);
        int endX = Mathf.FloorToInt(end.x), endY = Mathf.FloorToInt(end.y);
        int sourceX = x, sourceY = y;
        float dx = end.x - start.x, dy = end.y - start.y;
        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1f / dx);
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1f / dy);
        float tMaxX = stepX == 0 ? float.PositiveInfinity :
            ((stepX > 0 ? x + 1f : x) - start.x) / dx;
        float tMaxY = stepY == 0 ? float.PositiveInfinity :
            ((stepY > 0 ? y + 1f : y) - start.y) / dy;

        int remaining = Mathf.Abs(endX - x) + Mathf.Abs(endY - y) + 2;
        while ((x != endX || y != endY) && remaining-- > 0)
        {
            if (Mathf.Abs(tMaxX - tMaxY) <= .00001f)
            {
                if (IsOccluder(x + stepX, y, sourceX, sourceY, endX, endY) ||
                    IsOccluder(x, y + stepY, sourceX, sourceY, endX, endY)) return false;
                x += stepX;
                y += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            if (IsOccluder(x, y, sourceX, sourceY, endX, endY)) return false;
        }
        return x == endX && y == endY;
    }

    Vector2 WorldToGrid(Vector2 world)
    {
        Vector2 offset = world - gridOrigin;
        return new Vector2(
            (offset.x * gridUp.y - offset.y * gridUp.x) / gridDeterminant,
            (gridRight.x * offset.y - gridRight.y * offset.x) / gridDeterminant);
    }

    bool IsOccluder(int x, int y, int sourceX, int sourceY, int targetX, int targetY)
    {
        if ((x == sourceX && y == sourceY) || (x == targetX && y == targetY))
            return false;
        var cell = new Vector3Int(x, y, 0);
        return TerrainAt(cell) || (altarLayout.valid && altarLayout.IsShell(cell));
    }

    bool IsOnScreen(Vector3Int cell, Camera camera, Vector2 halfViewportCell)
    {
        Vector3 point = affineViewport
            ? viewportCenter + viewportRight * cell.x + viewportUp * cell.y
            : camera.WorldToViewportPoint(tiles.GetCellCenterWorld(cell));
        return point.z > 0f && point.x + halfViewportCell.x > 0f &&
            point.x - halfViewportCell.x < 1f && point.y + halfViewportCell.y > 0f &&
            point.y - halfViewportCell.y < 1f;
    }

    TileBase TerrainAt(Vector3Int cell)
    {
        if (scanBounds.Contains(cell))
            return scanTiles[(cell.y - scanBounds.yMin) * scanBounds.size.x + cell.x - scanBounds.xMin];
        return tiles.GetTile(cell);
    }

    bool IsDiscovered(int index)
        => index >= 0 && index < blockTypes.Length &&
           (discoveredBits[index >> 3] & (1 << (index & 7))) != 0;

    bool CurrentArtifactMatches(int index, ArtifactTile artifact)
        => artifacts.TryGetValue(index, out var stored) ? stored == artifact : !artifact;

    public State CaptureState()
    {
        ResolveDependencies();
        var storedArtifacts = new List<ArtifactState>(artifacts.Count);
        foreach (var entry in artifacts)
            if (entry.Value)
                storedArtifacts.Add(new ArtifactState { index = entry.Key, tileName = entry.Value.name });
        storedArtifacts.Sort((a, b) => a.index.CompareTo(b.index));
        return new State
        {
            seed = map ? map.ActiveSeed : 0,
            width = Width,
            height = Height,
            discoveredBits = (byte[])discoveredBits.Clone(),
            blockTypes = (byte[])blockTypes.Clone(),
            artifacts = storedArtifacts.ToArray()
        };
    }

    public bool RestoreState(State state)
    {
        ResolveDependencies();
        if (!map || !map.IsGenerated || state == null || state.version != SaveVersion ||
            state.seed != map.ActiveSeed || state.width != map.GeneratedWidth ||
            state.height != map.GeneratedHeight || state.width <= 0 || state.height <= 0)
            return false;

        int count = checked(state.width * state.height);
        if (state.discoveredBits == null || state.discoveredBits.Length != (count + 7) / 8 ||
            state.blockTypes == null || state.blockTypes.Length != count)
            return false;

        Width = state.width;
        Height = state.height;
        discoveredBits = (byte[])state.discoveredBits.Clone();
        blockTypes = (byte[])state.blockTypes.Clone();
        artifacts.Clear();
        if (state.artifacts != null && map.artifactSettings != null)
            foreach (ArtifactState stored in state.artifacts)
            {
                if (stored.index < 0 || stored.index >= count || !IsDiscovered(stored.index) ||
                    string.IsNullOrEmpty(stored.tileName)) continue;
                foreach (var setting in map.artifactSettings)
                    if (setting != null && setting.tile && setting.tile.name == stored.tileName)
                    {
                        artifacts[stored.index] = setting.tile;
                        break;
                    }
            }
        MapReset?.Invoke();
        return true;
    }
}
