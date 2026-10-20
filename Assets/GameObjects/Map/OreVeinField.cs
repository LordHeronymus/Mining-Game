using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

// A camera-sized, disposable lookup. Persistent OreTiles remain the sole gameplay data.
// A border around the visible cells also supplies neighbours at the screen edge.
public sealed class OreVeinField : IDisposable
{
    public Texture2D Texture { get; private set; }
    public BoundsInt Bounds { get; private set; }

    // Cells kept around the visible area. The buffer only re-centres (a full rebuild) when the view comes
    // closer than Margin cells to its edge. A larger border would rebuild less often, but every rebuild scales
    // with the buffer area, and short rebuilds keep frame times even.
    const int Border = 16, Margin = 5;
    static readonly Unity.Profiling.ProfilerMarker RebuildMarker = new("Mining.OreVeins.Rebuild");

    bool dirty = true, pixelsChanged;
    TileBase[] oreBuffer = Array.Empty<TileBase>(), terrainBuffer = Array.Empty<TileBase>();
    Tilemap source;
    Tilemap substrate;

    public void Invalidate() => dirty = true;

    // A change can only affect its own cell, so changed cells are patched in place.
    public void Changed(Tilemap map, NativeArray<Vector3Int> positions)
    {
        if (dirty || !Texture || (map != source && map != substrate)) return;
        var bounds = Bounds;
        NativeArray<Color32> pixels = default;
        foreach (var cell in positions)
        {
            if (!bounds.Contains(cell)) continue;
            if (!pixels.IsCreated) pixels = Texture.GetPixelData<Color32>(0);
            int index = (cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin;
            var before = pixels[index];
            var after = Pixel(source.GetTile<OreTile>(cell), substrate.HasTile(cell));
            if (before.r == after.r && before.g == after.g && before.a == after.a) continue;
            pixels[index] = after;
            pixelsChanged = true;
        }
    }

    static Color32 Pixel(OreTile ore, bool terrain) => ore && ore.block && terrain
        ? new Color32((byte)((int)ore.block.id + 1), (byte)((int)ore.richness + 1), 0, 255)
        : new Color32(0, 0, 0, terrain ? (byte)255 : (byte)0);

    public bool Prepare(MapGenerator map, Camera camera)
    {
        if (!map || !map.OreOverlay || !camera || !camera.orthographic) return false;
        var terrain = map.Terrain;
        float h = camera.orthographicSize, w = h * camera.aspect;
        var center = camera.transform.position;
        var lo = terrain.WorldToCell(center - new Vector3(w, h, 0));
        var hi = terrain.WorldToCell(center + new Vector3(w, h, 0));
        // Extremely zoomed-out editor/overview cameras keep the regular ore sprites.
        int width = Mathf.NextPowerOfTwo(Mathf.Max(32, hi.x - lo.x + 1 + Border));
        int height = Mathf.NextPowerOfTwo(Mathf.Max(32, hi.y - lo.y + 1 + Border));
        if (width > 512 || height > 512) return false;
        if (!Texture || source != map.OreOverlay || substrate != terrain ||
            Bounds.size.x != width || Bounds.size.y != height ||
            lo.x < Bounds.xMin + Margin || lo.y < Bounds.yMin + Margin ||
            hi.x >= Bounds.xMax - Margin || hi.y >= Bounds.yMax - Margin)
        {
            var middle = terrain.WorldToCell(center);
            // A view that reached an edge keeps moving that way: place the buffer ahead of it so the next rebuild
            // comes about twice as late. The view stays at least Margin cells inside either way.
            if (Texture && Bounds.size.x == width && Bounds.size.y == height)
            {
                int aheadX = (width - (hi.x - lo.x + 1)) / 2 - Margin - 1, aheadY = (height - (hi.y - lo.y + 1)) / 2 - Margin - 1;
                if (aheadX > 0) middle.x += lo.x < Bounds.xMin + Margin ? -aheadX : hi.x >= Bounds.xMax - Margin ? aheadX : 0;
                if (aheadY > 0) middle.y += lo.y < Bounds.yMin + Margin ? -aheadY : hi.y >= Bounds.yMax - Margin ? aheadY : 0;
            }
            Bounds = new BoundsInt(middle.x - width / 2, middle.y - height / 2, 0, width, height, 1);
            source = map.OreOverlay; substrate = terrain;
            if (!Texture || Texture.width != width || Texture.height != height)
            {
                ReleaseTexture();
                Texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = "Visible ore vein neighbours", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
            }
            dirty = true;
        }
        if (dirty) Rebuild();
        else if (pixelsChanged)
        {
            Texture.Apply(false, false);
            pixelsChanged = false;
        }
        return true;
    }

    void Rebuild()
    {
        using var measure = RebuildMarker.Auto();
        int count = Bounds.size.x * Bounds.size.y;
        if (oreBuffer.Length != count) { oreBuffer = new TileBase[count]; terrainBuffer = new TileBase[count]; }
        source.GetTilesBlockNonAlloc(Bounds, oreBuffer);
        substrate.GetTilesBlockNonAlloc(Bounds, terrainBuffer);
        var pixels = Texture.GetPixelData<Color32>(0);
        for (int i = 0; i < count; i++) pixels[i] = Pixel(oreBuffer[i] as OreTile, terrainBuffer[i]);
        // Drop the tile references again so the buffers do not keep anything alive.
        Array.Clear(oreBuffer, 0, count); Array.Clear(terrainBuffer, 0, count);
        Texture.Apply(false, false);
        dirty = pixelsChanged = false;
    }

    public void Bind(MaterialPropertyBlock properties, MapGenerator map)
    {
        var origin = map.Terrain.CellToWorld(Vector3Int.zero);
        var dx = map.Terrain.CellToWorld(Vector3Int.right) - origin;
        var dy = map.Terrain.CellToWorld(Vector3Int.up) - origin;
        properties.SetFloat(VeinEnabled, 1);
        properties.SetTexture(VeinCells, Texture);
        properties.SetVector(VeinBounds, new Vector4(Bounds.xMin, Bounds.yMin, Bounds.size.x, Bounds.size.y));
        properties.SetVector(VeinGrid, new Vector4(dx.x, dy.y, origin.x, origin.y));
        properties.SetFloat(VeinSeed, (uint)map.ActiveSeed % 8191);
    }
    static readonly int VeinEnabled = Shader.PropertyToID("_VeinEnabled"), VeinCells = Shader.PropertyToID("_VeinCells"),
        VeinBounds = Shader.PropertyToID("_VeinBounds"),
        VeinGrid = Shader.PropertyToID("_VeinGrid"), VeinSeed = Shader.PropertyToID("_VeinSeed");

    public void Dispose() { ReleaseTexture(); source = substrate = null; dirty = true; pixelsChanged = false; }

    void ReleaseTexture()
    {
        if (!Texture) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
        else UnityEngine.Object.DestroyImmediate(Texture);
        Texture = null;
    }
}
