using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent, DefaultExecutionOrder(1350)]
[RequireComponent(typeof(MapGenerator), typeof(Tilemap))]
public sealed class MapLighting : MonoBehaviour
{
    public bool lightingEnabled = true;
    [Range(0, 1)] public float daylightStrength = 1f;
    [Range(0.0001f, 1)] public float downwardLoss = 0.003f;
    [Range(0.0001f, 1)] public float sidewaysLoss = 0.08f;
    [Range(0, 1)] public float blockLoss = 0.28f;
    [Range(0.1f, 5f)] public float exponentialStrength = 1f;
    [Range(0, 1)] public float ambientBrightness = 0f;
    [SerializeField] Shader darknessShader;
    [SerializeField] Light2D headlamp;

    static readonly int HeadlampOriginRange = Shader.PropertyToID("_HeadlampOriginRange");
    static readonly int HeadlampDirectionAngles = Shader.PropertyToID("_HeadlampDirectionAngles");
    static readonly int HeadlampInnerRadius = Shader.PropertyToID("_HeadlampInnerRadius");

    MapGenerator map;
    Tilemap tiles;
    GridDaylight field;
    Texture2D texture;
    Material material;
    Mesh mesh;
    GameObject overlay;
    Color32[] pixels;
    int width, height;
    bool rebuild = true, textureDirty;
    readonly HashSet<Vector3Int> changed = new HashSet<Vector3Int>();
    readonly Stopwatch timer = new Stopwatch();
    public bool IsReady => field != null;
    public bool IsCalculating => field != null && field.HasPendingWork;
    LightingSettingsData sceneDefaults;

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        sceneDefaults = CaptureSettings();
        GameplaySettings.RegisterLighting(sceneDefaults);
        GameplaySettings.Changed += ApplyDebugSettings;
        ApplyDebugSettings();
#endif
    }

    LightingSettingsData CaptureSettings() => new LightingSettingsData {
        enabled = lightingEnabled, daylight = daylightStrength, ambient = ambientBrightness,
        downLoss = downwardLoss, sideLoss = sidewaysLoss, blockLoss = blockLoss, strength = exponentialStrength
    };

    void ApplyDebugSettings()
    {
        var settings = GameplaySettings.Lighting;
        if (JsonUtility.ToJson(settings) == JsonUtility.ToJson(CaptureSettings())) return;
        lightingEnabled = settings.enabled;
        daylightStrength = settings.daylight;
        ambientBrightness = settings.ambient;
        downwardLoss = settings.downLoss;
        sidewaysLoss = settings.sideLoss;
        blockLoss = settings.blockLoss;
        exponentialStrength = settings.strength;
        rebuild = true;
    }

    void OnDestroy() => GameplaySettings.Changed -= ApplyDebugSettings;

    void OnEnable()
    {
        map = GetComponent<MapGenerator>();
        tiles = GetComponent<Tilemap>();
        if (!headlamp)
        {
            var miner = FindFirstObjectByType<MinerPlayerVisual>();
            if (miner) headlamp = miner.headlamp;
        }
        map.GenerationCompleted += RequestRebuild;
        Tilemap.tilemapTileChanged += TilesChanged;
        rebuild = true;
    }

    void OnDisable()
    {
        if (map) map.GenerationCompleted -= RequestRebuild;
        Tilemap.tilemapTileChanged -= TilesChanged;
        ReleaseResources();
    }

    void OnValidate()
    {
        daylightStrength = Valid(daylightStrength, 1f, 0f);
        downwardLoss = Valid(downwardLoss, 0.003f, 0.0001f);
        sidewaysLoss = Valid(sidewaysLoss, 0.08f, 0.0001f);
        blockLoss = Valid(blockLoss, 0.28f, 0f);
        exponentialStrength = float.IsNaN(exponentialStrength) || float.IsInfinity(exponentialStrength)
            ? 1f : Mathf.Clamp(exponentialStrength, 0.1f, 5f);
        ambientBrightness = Valid(ambientBrightness, 0f, 0f);
        rebuild = true;
    }

    static float Valid(float value, float fallback, float min)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, 1f);

    bool preparedForStreamingGeneration;

    void RequestRebuild()
    {
        if (preparedForStreamingGeneration)
        {
            preparedForStreamingGeneration = false;
            return;
        }
        rebuild = true;
    }

    // Uses the already generated tile data, so the darkness mask is complete before
    // the visible Tilemap is streamed into the scene.
    public void PrepareForStreamingGeneration(MapGenerator.MapGenerationSnapshot snapshot)
    {
        if (!lightingEnabled || snapshot == null) return;
        if (!map) map = GetComponent<MapGenerator>();
        if (!tiles) tiles = GetComponent<Tilemap>();
        Initialize(snapshot.width, snapshot.height, snapshot.terrainTiles);
        if (field == null) return;
        while (field.HasPendingWork) field.Process(16384);
        if (textureDirty)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            textureDirty = false;
        }
        overlay.SetActive(true);
        UpdateHeadlamp();
        preparedForStreamingGeneration = true;
    }

    public void NotifyTileChanged(Vector3Int cell)
    {
        if (field != null) changed.Add(cell);
    }

    void TilesChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (source != tiles || field == null || rebuild || changes == null ||
            !map || !map.IsGenerated || map.IsGenerationStreaming) return;
        foreach (var change in changes) changed.Add(change.position);
    }

    void LateUpdate()
    {
        if (!lightingEnabled)
        {
            if (overlay) overlay.SetActive(false);
            return;
        }
        if (!map || !map.IsGenerated || map.IsGenerationStreaming) return;
        if (rebuild || field == null) Initialize();
        if (field == null) return;
        overlay.SetActive(true);
        foreach (var cell in changed)
            field.SetSolid(cell.x + width / 2, -cell.y, tiles.HasTile(cell));
        changed.Clear();

        // Spread the work across frames for large shafts and low attenuation.
        timer.Restart();
        int processed = 0;
        while (field.HasPendingWork && processed < 8192 && timer.Elapsed.TotalMilliseconds < 2)
            processed += field.Process(256);
        timer.Stop();
        if (textureDirty)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            textureDirty = false;
        }
        UpdateHeadlamp();
    }

    void UpdateHeadlamp()
    {
        if (!material) return;
        if (!headlamp || !headlamp.isActiveAndEnabled || headlamp.intensity <= 0)
        {
            material.SetVector(HeadlampOriginRange, Vector4.zero);
            return;
        }
        Vector3 origin = headlamp.transform.position;
        Vector3 direction = headlamp.transform.up;
        float inner = Mathf.Cos(headlamp.pointLightInnerAngle * .5f * Mathf.Deg2Rad);
        float outer = Mathf.Cos(headlamp.pointLightOuterAngle * .5f * Mathf.Deg2Rad);
        material.SetVector(HeadlampOriginRange,
            new Vector4(origin.x, origin.y, headlamp.pointLightOuterRadius, headlamp.intensity));
        material.SetVector(HeadlampDirectionAngles, new Vector4(direction.x, direction.y, inner, outer));
        material.SetFloat(HeadlampInnerRadius, headlamp.pointLightInnerRadius);
    }

    void Initialize()
    {
        int mapWidth = map.GeneratedWidth;
        int mapHeight = map.GeneratedHeight;
        var allTiles = tiles.GetTilesBlock(new BoundsInt(-mapWidth / 2, 1 - mapHeight, 0, mapWidth, mapHeight, 1));
        Initialize(mapWidth, mapHeight, allTiles);
    }

    void Initialize(int mapWidth, int mapHeight, TileBase[] allTiles)
    {
        ReleaseResources();
        width = mapWidth;
        height = mapHeight;
        if (width <= 0 || height <= 0 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            UnityEngine.Debug.LogError("Map lighting: map dimensions exceed the supported texture size.", this);
            enabled = false;
            return;
        }
        if (!darknessShader) darknessShader = Shader.Find("Mining Game/Map Darkness");
        if (!darknessShader)
        {
            UnityEngine.Debug.LogError("Map lighting: darkness shader is missing.", this);
            enabled = false;
            return;
        }
        if (allTiles == null || allTiles.Length != width * height)
        {
            UnityEngine.Debug.LogError("Map lighting: generated tile data is incomplete.", this);
            enabled = false;
            return;
        }
        var solid = new bool[width * height];
        pixels = new Color32[solid.Length];
        byte dark = (byte)Mathf.RoundToInt(255f * (1f - ambientBrightness));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                solid[y * width + x] = allTiles[(height - 1 - y) * width + x] != null;
                pixels[y * width + x] = new Color32(0, 0, 0, dark);
            }
        field = new GridDaylight(width, height, solid, daylightStrength, downwardLoss, sidewaysLoss, blockLoss, exponentialStrength);
        field.LightChanged += UpdatePixel;
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Daylight mask", filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };
        material = new Material(darknessShader) { name = "Map darkness", hideFlags = HideFlags.DontSave };
        material.mainTexture = texture;
        overlay = new GameObject("Daylight Overlay") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
        overlay.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Daylight bounds", hideFlags = HideFlags.DontSave };
        int left = -width / 2, bottom = 1 - height;
        mesh.vertices = new[] {
            tiles.CellToLocal(new Vector3Int(left, bottom, 0)),
            tiles.CellToLocal(new Vector3Int(left + width, bottom, 0)),
            tiles.CellToLocal(new Vector3Int(left, 1, 0)),
            tiles.CellToLocal(new Vector3Int(left + width, 1, 0)) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = overlay.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = SortingLayer.layers[SortingLayer.layers.Length - 1].id;
        renderer.sortingOrder = 32760;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        field.Reset();
        textureDirty = true;
        rebuild = false;
    }

    void UpdatePixel(int index, float light)
    {
        int x = index % width, y = index / width;
        float brightness = Mathf.Max(ambientBrightness, GridDaylight.VisibleLight(light));
        pixels[(height - 1 - y) * width + x] = new Color32(0, 0, 0,
            (byte)Mathf.RoundToInt(255f * (1f - brightness)));
        textureDirty = true;
    }

    public float GetBrightness(Vector3Int cell)
    {
        if (!lightingEnabled) return Mathf.Max(ambientBrightness, daylightStrength);
        int x = cell.x + width / 2, y = -cell.y;
        float mapBrightness = cell.y > 0
            ? Mathf.Max(ambientBrightness, daylightStrength)
            : field == null || x < 0 || x >= width || y >= height
            ? ambientBrightness
            : Mathf.Max(ambientBrightness, GridDaylight.VisibleLight(field[x, y]));
        if (!headlamp || !headlamp.isActiveAndEnabled || headlamp.intensity <= 0 || !tiles)
            return mapBrightness;
        return Mathf.Max(mapBrightness, GetHeadlampBrightness(tiles.GetCellCenterWorld(cell)));
    }

    float GetHeadlampBrightness(Vector2 worldPosition)
    {
        Vector2 delta = worldPosition - (Vector2)headlamp.transform.position;
        float range = headlamp.pointLightOuterRadius;
        float distanceSquared = delta.sqrMagnitude;
        if (range <= 0 || distanceSquared >= range * range) return 0f;
        float distance = Mathf.Sqrt(distanceSquared);
        float innerAngle = Mathf.Cos(headlamp.pointLightInnerAngle * .5f * Mathf.Deg2Rad);
        float outerAngle = Mathf.Cos(headlamp.pointLightOuterAngle * .5f * Mathf.Deg2Rad);
        float angle = Vector2.Dot(delta, (Vector2)headlamp.transform.up) / Mathf.Max(distance, .0001f);
        float beam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outerAngle, innerAngle, angle));
        float innerRadius = Mathf.Max(headlamp.pointLightInnerRadius, .0001f);
        float centerGlow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, innerRadius, distance));
        float falloff = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(headlamp.pointLightInnerRadius, range, distance));
        return Mathf.Clamp01(Mathf.Max(beam, centerGlow) * falloff * headlamp.intensity);
    }

    void ReleaseResources()
    {
        if (overlay) { overlay.SetActive(false); Destroy(overlay); }
        if (texture) Destroy(texture);
        if (material) Destroy(material);
        if (mesh) Destroy(mesh);
        field = null;
        pixels = null;
        overlay = null;
        texture = null;
        material = null;
        mesh = null;
        changed.Clear();
    }
}
