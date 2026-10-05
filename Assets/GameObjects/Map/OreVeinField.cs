using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// A camera-sized, disposable lookup. Persistent OreTiles remain the sole gameplay data.
// A border around the visible cells also supplies neighbours at the screen edge.
public sealed class OreVeinField : IDisposable
{
    public Texture2D Texture { get; private set; }
    public BoundsInt Bounds { get; private set; }
    public int Revision { get; private set; }

    bool dirty = true;
    bool containsCopper;
    readonly CopperOutlineField copperOutline = new CopperOutlineField();
    Tilemap source;
    Tilemap substrate;

    public void Invalidate() => dirty = true;

    public void Changed(Tilemap map, Tilemap.SyncTile[] changes)
    {
        if (dirty || (map != source && map != substrate) || changes == null) return;
        foreach (var change in changes)
            // A visible copper tree may continue beyond this camera's buffer.
            if (containsCopper || Bounds.Contains(change.position)) { dirty = true; return; }
    }

    public bool Prepare(MapGenerator map, Camera camera)
    {
        if (!map || !map.OreOverlay || !camera || !camera.orthographic) return false;
        var terrain = map.Terrain;
        float h = camera.orthographicSize, w = h * camera.aspect;
        var center = camera.transform.position;
        var lo = terrain.WorldToCell(center - new Vector3(w, h, 0));
        var hi = terrain.WorldToCell(center + new Vector3(w, h, 0));
        // Extremely zoomed-out editor/overview cameras keep the regular ore sprites.
        int width = Mathf.NextPowerOfTwo(Mathf.Max(32, hi.x - lo.x + 17));
        int height = Mathf.NextPowerOfTwo(Mathf.Max(32, hi.y - lo.y + 17));
        if (width > 512 || height > 512) return false;
        if (!Texture || source != map.OreOverlay || substrate != terrain ||
            Bounds.size.x != width || Bounds.size.y != height ||
            lo.x < Bounds.xMin + 5 || lo.y < Bounds.yMin + 5 ||
            hi.x >= Bounds.xMax - 5 || hi.y >= Bounds.yMax - 5)
        {
            var middle = terrain.WorldToCell(center);
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
        if (dirty) Rebuild(map);
        return true;
    }

    void Rebuild(MapGenerator map)
    {
        var ores = source.GetTilesBlock(Bounds);
        var terrain = substrate.GetTilesBlock(Bounds);
        var pixels = Texture.GetPixelData<Color32>(0);
        containsCopper = false;
        for (int i = 0; i < ores.Length; i++)
        {
            var ore = ores[i] as OreTile;
            pixels[i] = ore && ore.block && terrain[i]
                ? new Color32((byte)((int)ore.block.id + 1), (byte)((int)ore.richness + 1), 0, 255)
                : new Color32(0,0,0,terrain[i]?(byte)255:(byte)0);
            containsCopper |= pixels[i].r == (byte)((int)BlockType.CopperOre + 1);
        }
        CopperVeinTopology.Fill(source, substrate, Bounds, pixels, map.ActiveSeed);
        copperOutline.Build(pixels, Bounds.size.x, Bounds.size.y);

        Texture.Apply(false, false);
        dirty = false; Revision++;
    }

    public void Bind(MaterialPropertyBlock properties, MapGenerator map)
    {
        var origin = map.Terrain.CellToWorld(Vector3Int.zero);
        var dx = map.Terrain.CellToWorld(Vector3Int.right) - origin;
        var dy = map.Terrain.CellToWorld(Vector3Int.up) - origin;
        properties.SetFloat("_VeinEnabled", 1);
        properties.SetTexture("_VeinCells", Texture);
        properties.SetTexture("_CopperOutlineField", copperOutline.Texture ? copperOutline.Texture : Texture2D.blackTexture);

        properties.SetVector("_VeinBounds", new Vector4(Bounds.xMin, Bounds.yMin, Bounds.size.x, Bounds.size.y));
        properties.SetVector("_VeinGrid", new Vector4(dx.x, dy.y, origin.x, origin.y));
        properties.SetFloat("_VeinSeed", (uint)map.ActiveSeed % 8191);
    }

    public int TypeAt(Vector3Int cell)
    {
        if (!Texture || !Bounds.Contains(cell)) return -1;
        var pixel = Texture.GetPixelData<Color32>(0)[(cell.y - Bounds.yMin) * Bounds.size.x + cell.x - Bounds.xMin];
        return pixel.r - 1;
    }

    public int CopperLinksAt(Vector3Int cell) => !Texture || !Bounds.Contains(cell) ? 0 :
        Texture.GetPixelData<Color32>(0)[(cell.y-Bounds.yMin)*Bounds.size.x+cell.x-Bounds.xMin].b;

    public void Dispose() { ReleaseTexture(); copperOutline.Dispose(); source = substrate = null; dirty = true; }

    void ReleaseTexture()
    {
        if (!Texture) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
        else UnityEngine.Object.DestroyImmediate(Texture);
        Texture = null;
    }
}
