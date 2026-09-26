using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

// Each damaged cell keeps its overlay, driven by normalized mining progress.
public sealed class MiningCrackVisual : System.IDisposable
{
    readonly Sprite[] stages = new Sprite[5];
    readonly Dictionary<Vector3Int, SpriteRenderer> renderers = new();
    readonly Tilemap terrain;
    readonly MaterialPropertyBlock properties = new();
    static readonly int GrowthId = Shader.PropertyToID("_CrackGrowth");
    public SpriteRenderer Renderer { get; private set; }

    public MiningCrackVisual(Tilemap terrain)
    {
        this.terrain = terrain;
        var texture = Resources.Load<Texture2D>("Mining/MiningCracksFiveStages");
        if (!terrain || !texture || !Resources.Load<Material>("Mining/MiningCracksLit")) return;
        float side = texture.width / 3f;
        for (int i = 0; i < stages.Length; i++)
        {
            // All stages share the exact same final crack; the shader reveals its branches.
            stages[i] = Sprite.Create(texture, new Rect(side, 0, side, side),
                Vector2.one * .5f, side, 0, SpriteMeshType.FullRect);
            stages[i].name = "Mining cracks " + (i + 1);
            stages[i].hideFlags = HideFlags.HideAndDontSave;
        }
    }

    public void Show(Vector3Int cell, float progress)
    {
        if (!terrain) return;
        if (!terrain || !terrain.HasTile(cell) || progress <= 0f || progress >= 1f)
        {
            Hide(cell);
            return;
        }
        var renderer = GetRenderer(cell);
        if (!renderer) return;
        Renderer = renderer;
        int stage = Mathf.Clamp(Mathf.FloorToInt(progress * stages.Length), 0, stages.Length - 1);
        renderer.sprite = stages[stage];
        renderer.GetPropertyBlock(properties);
        properties.SetFloat(GrowthId, progress);
        renderer.SetPropertyBlock(properties);
        renderer.transform.position = terrain.GetCellCenterWorld(cell);
        // Sprite bounds are one unit; grid spacing already includes the configured cell size.
        Vector3 size = terrain.cellSize;
        renderer.transform.localScale = new Vector3(size.x * .86f, size.y * .86f, 1f);
        renderer.color = Color.white;
        renderer.enabled = true;
    }

    SpriteRenderer GetRenderer(Vector3Int cell)
    {
        if (renderers.TryGetValue(cell, out var current) && current) return current;
        var material = Resources.Load<Material>("Mining/MiningCracksLit");
        if (!material) return null;
        var go = new GameObject("Mining cracks " + cell);
        go.hideFlags = HideFlags.DontSave;
        go.layer = terrain.gameObject.layer;
        go.transform.SetParent(terrain.transform, false);
        current = go.AddComponent<SpriteRenderer>();
        current.sharedMaterial = material;
        var source = terrain.GetComponent<TilemapRenderer>();
        if (source)
        {
            current.sortingLayerID = source.sortingLayerID;
            current.sortingOrder = source.sortingOrder + 2;
        }
        renderers[cell] = current;
        return current;
    }

    public void Refresh(Dictionary<Vector3Int, float> progress)
    {
        foreach (var pair in progress) Show(pair.Key, pair.Value);
        var stale = new List<Vector3Int>();
        foreach (var pair in renderers)
            if (!terrain || !terrain.HasTile(pair.Key) || !progress.ContainsKey(pair.Key)) stale.Add(pair.Key);
        foreach (var cell in stale) Hide(cell);
    }

    public void Hide(Vector3Int cell)
    {
        if (renderers.TryGetValue(cell, out var renderer) && renderer)
        {
            renderer.enabled = false;
            Renderer = renderer;
        }
    }

    public void Dispose()
    {
        foreach (var renderer in renderers.Values) if (renderer) Release(renderer.gameObject);
        renderers.Clear();
        foreach (var sprite in stages) if (sprite) Release(sprite);
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }
}
