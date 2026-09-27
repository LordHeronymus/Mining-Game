using System;
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
    const int TextureUploadChunkSize = 32;
    public bool lightingEnabled = true;
    [Range(0, 1)] public float daylightStrength = 1f;
    [Range(0.0001f, 1)] public float downwardLoss = 0.003f;
    [Range(0.0001f, 1)] public float sidewaysLoss = 0.08f;
    [Range(0, 1)] public float blockLoss = 0.28f;
    [Range(0.1f, 5f)] public float exponentialStrength = 1f;
    [Range(0, 1)] public float ambientBrightness = 0f;
    [Range(0f, 1f)] public float ultroniumLightIntensity = 0.18f;
    [Range(0, 120)] public int ultroniumPulsesPerMinute = 30;
    public Color ultroniumLightColor = new Color(.42f, .22f, 1f, 1f);
    public Color ultroniumParticleColor = new Color(.53f, .34f, 1f, 1f);
    [SerializeField] Shader darknessShader;
    [SerializeField] Light2D headlamp;

    static readonly int HeadlampOriginRange = Shader.PropertyToID("_HeadlampOriginRange");
    static readonly int HeadlampDirectionAngles = Shader.PropertyToID("_HeadlampDirectionAngles");
    static readonly int HeadlampInnerRadius = Shader.PropertyToID("_HeadlampInnerRadius");
    static readonly int TerrainOcclusionTexId = Shader.PropertyToID("_TerrainOcclusionTex");
    static readonly int TerrainOcclusionRectId = Shader.PropertyToID("_TerrainOcclusionRect");
    static readonly int TerrainOcclusionSizeId = Shader.PropertyToID("_TerrainOcclusionSize");
    static readonly int UltroniumSourcesId = Shader.PropertyToID("_UltroniumSources");
    static readonly int UltroniumCountId = Shader.PropertyToID("_UltroniumCount");
    const int MaximumUltroniumLights = 32;
    readonly List<TorchLightField.Source> torchSources = new List<TorchLightField.Source>();
    readonly Vector4[] ultroniumSources = new Vector4[MaximumUltroniumLights];
    readonly List<Light2D> ultroniumLights = new List<Light2D>();
    readonly List<Vector3Int> visibleUltronium = new List<Vector3Int>();
    Vector3Int lastOreCameraCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    float nextOreRefresh;

    MapGenerator map;
    Tilemap tiles;
    GridDaylight field;
    TorchLightField torchField;
    Texture2D texture;
    Texture2D torchLightTexture;
    Sprite torchLightSprite;
    Color32[] torchLightPixels;
    Light2D torchLight;
    Material material;
    Mesh mesh;
    GameObject overlay;
    Texture2D uploadPatch;
    Color32[] uploadPatchPixels;
    readonly HashSet<Vector2Int> dirtyTextureChunks = new HashSet<Vector2Int>();
    Color32[] pixels;
    int width, height;
    bool rebuild = true, torchFieldDirty = true, textureDirty, fullTextureUpload;
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
        PlacedTorch.Changed += OnTorchChanged;
        rebuild = true;
    }

    void OnDisable()
    {
        if (map) map.GenerationCompleted -= RequestRebuild;
        Tilemap.tilemapTileChanged -= TilesChanged;
        PlacedTorch.Changed -= OnTorchChanged;
        ReleaseResources();
        ReleaseUltroniumLights();
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
        ultroniumLightIntensity = Valid(ultroniumLightIntensity, .18f, 0f);
        ultroniumPulsesPerMinute = Mathf.Clamp(ultroniumPulsesPerMinute, 0, 120);
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
        if (!lightingEnabled || GameplayTestSettings.GlobalLighting || snapshot == null) return;
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
            fullTextureUpload = false;
            dirtyTextureChunks.Clear();
        }
        overlay.SetActive(true);
        UpdateHeadlamp();
        preparedForStreamingGeneration = true;
    }

    public void NotifyTileChanged(Vector3Int cell)
    {
        if (field != null) changed.Add(cell);
        nextOreRefresh = 0f;
    }

    void OnTorchChanged(MapGenerator ownerMap)
    {
        if (ownerMap == map) torchFieldDirty = true;
    }

    void TilesChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (source != tiles || field == null || rebuild || changes == null ||
            !map || !map.IsGenerated || map.IsGenerationStreaming) return;
        foreach (var change in changes) changed.Add(change.position);
        nextOreRefresh = 0f;
    }

    void LateUpdate()
    {
        if (!lightingEnabled || GameplayTestSettings.GlobalLighting)
        {
            if (overlay) overlay.SetActive(false);
            if (torchLight) torchLight.enabled = false;
            SetUltroniumLightsActive(false);
            return;
        }
        if (!map || !map.IsGenerated || map.IsGenerationStreaming) return;
        if (rebuild || field == null) Initialize();
        if (field == null) return;
        overlay.SetActive(true);
        foreach (var cell in changed)
        {
            bool solid = tiles.HasTile(cell);
            field.SetSolid(cell.x + width / 2, -cell.y, solid);
            if (torchField.SetSolid(cell.x + width / 2, -cell.y, solid))
                torchFieldDirty = true;
            UpdateSolidityPixel(cell, solid);
        }
        changed.Clear();

        // Spread the work across frames for large shafts and low attenuation.
        timer.Restart();
        int processed = 0;
        while (field.HasPendingWork && processed < 8192 && timer.Elapsed.TotalMilliseconds < 2)
            processed += field.Process(256);
        timer.Stop();
        UpdateTorchLighting();
        if (torchLight) torchLight.enabled = torchSources.Count > 0;
        UploadLightingTexture();
        UpdateHeadlamp();
    }

    void UpdateHeadlamp()
    {
        if (!material) return;
        UpdateUltroniumLights();
        var altar = map ? map.AltarChamber : null;
        material.SetVector("_AltarSource", altar ? altar.LightSource : Vector4.zero);
        material.SetTexture("_AltarMask", altar && altar.ChamberLightMask ? altar.ChamberLightMask : Texture2D.blackTexture);
        material.SetVector("_AltarRect", altar ? altar.ChamberLightRect : Vector4.zero);
        material.SetVector("_AltarMaskSize", altar ? altar.ChamberLightSize : Vector4.zero);
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

    void SetUltroniumLightsActive(bool active)
    {
        foreach (var light in ultroniumLights)
            if (light) light.gameObject.SetActive(active);
        if (!active && material) material.SetInt(UltroniumCountId, 0);
    }

    void ReleaseUltroniumLights()
    {
        foreach (var light in ultroniumLights)
            if (light)
            {
                if (Application.isPlaying) Destroy(light.gameObject);
                else DestroyImmediate(light.gameObject);
            }
        ultroniumLights.Clear();
        visibleUltronium.Clear();
    }

    void UpdateUltroniumLights()
    {
        if (!material || !map || !map.IsGenerated || !lightingEnabled ||
            ultroniumLightIntensity <= 0f || !Camera.main)
        {
            SetUltroniumLightsActive(false);
            return;
        }
        var camera = Camera.main;
        Vector3Int cameraCell = tiles.WorldToCell(camera.transform.position);
        if (cameraCell != lastOreCameraCell || Time.time >= nextOreRefresh)
        {
            lastOreCameraCell = cameraCell;
            nextOreRefresh = Time.time + .2f;
            visibleUltronium.Clear();
            float distance = Vector3.Dot(tiles.transform.position - camera.transform.position, camera.transform.forward);
            Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, 0);
            Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, 0);
            for (int corner = 0; corner < 4; corner++)
            {
                var cell = tiles.WorldToCell(camera.ViewportToWorldPoint(
                    new Vector3(corner % 2, corner / 2, distance)));
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);
            }
            // A small margin keeps lights steady when their source is just outside the view.
            for (int y = min.y - 2; y <= max.y + 2; y++)
                for (int x = min.x - 2; x <= max.x + 2; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    var ore = map.GetOreAt(cell);
                    if (ore && ore.block && ore.block.id == BlockType.UltroniumOre)
                        visibleUltronium.Add(cell);
                }
            visibleUltronium.Sort((a, b) =>
            {
                var center = cameraCell;
                int da = (a.x-center.x)*(a.x-center.x)+(a.y-center.y)*(a.y-center.y);
                int db = (b.x-center.x)*(b.x-center.x)+(b.y-center.y)*(b.y-center.y);
                return da.CompareTo(db);
            });
        }
        int count = Mathf.Min(MaximumUltroniumLights, visibleUltronium.Count);
        float radius = Mathf.Max(1f, tiles.layoutGrid.cellSize.x * tiles.transform.lossyScale.x * 1.8f);
        for (int i = 0; i < count; i++)
        {
            if (i >= ultroniumLights.Count)
            {
                var go = new GameObject("Ultronium light (generated)");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform, false);
                var light = go.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.shadowsEnabled = false;
                ultroniumLights.Add(light);
            }
            var current = ultroniumLights[i];
            var cell = visibleUltronium[i];
            var position = tiles.GetCellCenterWorld(cell);
            float intensity = ultroniumLightIntensity * UltroniumPulse(cell, Time.time, ultroniumPulsesPerMinute);
            current.transform.position = position;
            current.color = ultroniumLightColor;
            current.intensity = intensity;
            current.pointLightInnerRadius = radius * .2f;
            current.pointLightOuterRadius = radius;
            current.gameObject.SetActive(true);
            ultroniumSources[i] = new Vector4(position.x, position.y, radius, intensity);
        }
        for (int i = count; i < ultroniumLights.Count; i++)
            ultroniumLights[i].gameObject.SetActive(false);
        material.SetVectorArray(UltroniumSourcesId, ultroniumSources);
        material.SetInt(UltroniumCountId, count);
    }

    internal static float UltroniumPulse(Vector3Int cell, float time, int pulsesPerMinute)
    {
        if (pulsesPerMinute <= 0) return 1f;
        float cycle = time * pulsesPerMinute / 60f;
        float seed = cell.x * .6180339f + cell.y * .41421356f;
        float phase = cycle + seed
            + .18f * Mathf.Sin(2f * Mathf.PI * (cycle * .19f + seed * 1.7f))
            + .07f * Mathf.Sin(2f * Mathf.PI * (cycle * .43f + seed * .4f));
        float crest = .5f + .5f * Mathf.Cos(2f * Mathf.PI * phase);
        return .55f + .65f * crest * crest;
    }

    void UpdateTorchLighting()
    {
        if (!torchFieldDirty || torchField == null) return;
        torchFieldDirty = false;
        torchSources.Clear();
        foreach (var torch in PlacedTorch.Active)
        {
            if (!torch || torch.OwnerMap != map) continue;
            Vector3Int cell = tiles.WorldToCell(torch.LightPosition);
            torchSources.Add(new TorchLightField.Source(
                cell.x + width / 2, -cell.y, torch.Intensity));
        }
        bool lightChanged = false;
        foreach (int index in torchField.Rebuild(torchSources))
        {
            int x = index % width, depth = index / width;
            int pixelY = height - 1 - depth;
            int pixelIndex = pixelY * width + x;
            byte brightness = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(torchField.Get(index)));
            if (torchLightPixels[pixelIndex].a != brightness)
            {
                Color32 lightPixel = torchLightPixels[pixelIndex];
                lightPixel.a = brightness;
                torchLightPixels[pixelIndex] = lightPixel;
                lightChanged = true;
            }
            if (pixels[pixelIndex].g == brightness) continue;
            Color32 pixel = pixels[pixelIndex];
            pixel.g = brightness;
            pixels[pixelIndex] = pixel;
            textureDirty = true;
            dirtyTextureChunks.Add(new Vector2Int(x / TextureUploadChunkSize,
                pixelY / TextureUploadChunkSize));
        }
        if (lightChanged)
        {
            torchLightTexture.SetPixels32(torchLightPixels);
            torchLightTexture.Apply(false, false);
        }
        if (torchLight) torchLight.enabled = torchSources.Count > 0;
    }

    public void ApplyBackgroundLighting(MaterialPropertyBlock properties)
    {
        bool active = lightingEnabled && !GameplayTestSettings.GlobalLighting &&
            texture && overlay && overlay.activeInHierarchy && material;
        properties.SetFloat("_UseMapLighting", active ? 1f : 0f);
        if (!active) return;
        var bounds = overlay.GetComponent<MeshRenderer>().bounds;
        properties.SetTexture("_DaylightTex", texture);
        properties.SetVector("_DaylightRect", new Vector4(bounds.min.x, bounds.min.y,
            1f / Mathf.Max(bounds.size.x, .001f), 1f / Mathf.Max(bounds.size.y, .001f)));
        properties.SetVector(HeadlampOriginRange, material.GetVector(HeadlampOriginRange));
        properties.SetVector(HeadlampDirectionAngles, material.GetVector(HeadlampDirectionAngles));
        properties.SetFloat(HeadlampInnerRadius, material.GetFloat(HeadlampInnerRadius));
        properties.SetTexture(TerrainOcclusionTexId, texture);
        properties.SetVector(TerrainOcclusionRectId, material.GetVector(TerrainOcclusionRectId));
        properties.SetVector(TerrainOcclusionSizeId, new Vector4(width, height, 0f, 0f));
        properties.SetVectorArray(UltroniumSourcesId, ultroniumSources);
        properties.SetInt(UltroniumCountId, material.GetInt(UltroniumCountId));
        properties.SetVector("_AltarSource", material.GetVector("_AltarSource"));
        properties.SetTexture("_AltarMask", material.GetTexture("_AltarMask") ?? Texture2D.blackTexture);
        properties.SetVector("_AltarRect", material.GetVector("_AltarRect"));
        properties.SetVector("_AltarMaskSize", material.GetVector("_AltarMaskSize"));
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
        var altarShell = new bool[solid.Length];
        var chamber = map ? map.AltarChamber : null;
        pixels = new Color32[solid.Length];
        byte dark = (byte)Mathf.RoundToInt(255f * (1f - ambientBrightness));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                solid[y * width + x] = allTiles[(height - 1 - y) * width + x] != null;
                altarShell[y * width + x] = chamber && chamber.Protects(new Vector3Int(x - width / 2, -y, 0));
                pixels[(height - 1 - y) * width + x] = new Color32(
                    solid[y * width + x] ? (byte)255 : (byte)0, 0, 0, dark);
            }
        field = new GridDaylight(width, height, solid, daylightStrength, downwardLoss, sidewaysLoss, blockLoss, exponentialStrength, altarShell);
        field.LightChanged += UpdatePixel;
        Vector3 cellWidth = tiles.transform.TransformVector(Vector3.right * tiles.layoutGrid.cellSize.x);
        Vector3 cellHeight = tiles.transform.TransformVector(Vector3.up * tiles.layoutGrid.cellSize.y);
        torchField = new TorchLightField(width, height, solid, cellWidth.magnitude,
            cellHeight.magnitude, PlacedTorch.PropagationDistance);
        torchFieldDirty = true;
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Daylight mask", filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };
        torchLightTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Torch light field", filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };
        torchLightPixels = new Color32[pixels.Length];
        for (int i = 0; i < torchLightPixels.Length; i++)
            torchLightPixels[i] = new Color32(255, 255, 255, 0);
        torchLightTexture.SetPixels32(torchLightPixels);
        torchLightTexture.Apply(false, false);
        torchLightSprite = Sprite.Create(torchLightTexture, new Rect(0, 0, width, height),
            new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
        torchLightSprite.name = "Torch light field";
        torchLightSprite.hideFlags = HideFlags.DontSave;
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
        var bounds = renderer.bounds;
        var torchObject = new GameObject("Torch field light")
            { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
        torchObject.transform.SetParent(transform, false);
        torchObject.transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);
        torchObject.transform.localScale = new Vector3(bounds.size.x / width, bounds.size.y / height, 1f);
        torchLight = torchObject.AddComponent<Light2D>();
        torchLight.enabled = false;
        torchLight.lightType = Light2D.LightType.Sprite;
        torchLight.lightCookieSprite = torchLightSprite;
        torchLight.color = new Color(1f, .58f, .24f, 1f);
        torchLight.intensity = 1.25f;
        torchLight.shadowsEnabled = false;
        material.SetTexture(TerrainOcclusionTexId, texture);
        material.SetVector(TerrainOcclusionRectId, new Vector4(bounds.min.x, bounds.min.y,
            1f / Mathf.Max(bounds.size.x, .001f), 1f / Mathf.Max(bounds.size.y, .001f)));
        material.SetVector(TerrainOcclusionSizeId, new Vector4(width, height, 0f, 0f));
        UpdateTorchLighting();
        field.Reset();
        textureDirty = true;
        fullTextureUpload = true;
        dirtyTextureChunks.Clear();
        rebuild = false;
    }

    void UpdatePixel(int index, float light)
    {
        int x = index % width, y = index / width;
        float brightness = Mathf.Max(ambientBrightness, GridDaylight.VisibleLight(light));
        int pixelIndex = (height - 1 - y) * width + x;
        Color32 pixel = pixels[pixelIndex];
        pixel.a = (byte)Mathf.RoundToInt(255f * (1f - brightness));
        pixels[pixelIndex] = pixel;
        textureDirty = true;
        int pixelY = height - 1 - y;
        dirtyTextureChunks.Add(new Vector2Int(x / TextureUploadChunkSize, pixelY / TextureUploadChunkSize));
    }

    void UpdateSolidityPixel(Vector3Int cell, bool solid)
    {
        int x = cell.x + width / 2, depth = -cell.y;
        if (x < 0 || x >= width || depth < 0 || depth >= height) return;
        int pixelY = height - 1 - depth;
        int index = pixelY * width + x;
        byte value = solid ? (byte)255 : (byte)0;
        if (pixels[index].r == value) return;
        Color32 pixel = pixels[index];
        pixel.r = value;
        pixels[index] = pixel;
        textureDirty = true;
        dirtyTextureChunks.Add(new Vector2Int(x / TextureUploadChunkSize, pixelY / TextureUploadChunkSize));
    }

    void UploadLightingTexture()
    {
        if (!textureDirty || !texture) return;
        int minChunkX = int.MaxValue, minChunkY = int.MaxValue, maxChunkX = -1, maxChunkY = -1;
        foreach (var chunk in dirtyTextureChunks)
        {
            minChunkX = Mathf.Min(minChunkX, chunk.x); minChunkY = Mathf.Min(minChunkY, chunk.y);
            maxChunkX = Mathf.Max(maxChunkX, chunk.x); maxChunkY = Mathf.Max(maxChunkY, chunk.y);
        }
        int left = minChunkX * TextureUploadChunkSize, bottom = minChunkY * TextureUploadChunkSize;
        int copyWidth = maxChunkX < 0 ? 0 : Mathf.Min(width, (maxChunkX + 1) * TextureUploadChunkSize) - left;
        int copyHeight = maxChunkY < 0 ? 0 : Mathf.Min(height, (maxChunkY + 1) * TextureUploadChunkSize) - bottom;
        long affectedPixels = (long)copyWidth * copyHeight;
        int patchWidth = copyWidth > 0 ? Mathf.NextPowerOfTwo(copyWidth) : 0;
        int patchHeight = copyHeight > 0 ? Mathf.NextPowerOfTwo(copyHeight) : 0;
        long transferPixels = affectedPixels + (long)patchWidth * patchHeight;
        if (fullTextureUpload ||
            (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0 ||
            copyWidth <= 0 || copyHeight <= 0 ||
            affectedPixels * 3 >= (long)width * height ||
            transferPixels * 4 >= (long)width * height * 3)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            textureDirty = false;
            fullTextureUpload = false;
            dirtyTextureChunks.Clear();
            return;
        }

        if (!uploadPatch || uploadPatch.width < patchWidth || uploadPatch.height < patchHeight)
        {
            if (uploadPatch) Destroy(uploadPatch);
            uploadPatch = new Texture2D(patchWidth, patchHeight,
                TextureFormat.RGBA32, false, true)
            { name = "Daylight mask update", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave };
            uploadPatchPixels = new Color32[patchWidth * patchHeight];
        }
        try
        {
            Array.Clear(uploadPatchPixels, 0, uploadPatchPixels.Length);
            for (int row = 0; row < copyHeight; row++)
                Array.Copy(pixels, (bottom + row) * width + left, uploadPatchPixels, row * uploadPatch.width, copyWidth);
            uploadPatch.SetPixels32(uploadPatchPixels);
            uploadPatch.Apply(false, false);
            Graphics.CopyTexture(uploadPatch, 0, 0, 0, 0, copyWidth, copyHeight,
                texture, 0, 0, left, bottom);
        }
        catch (UnityException)
        {
            // Preserve the same pixel values on graphics backends that reject partial copies.
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }
        textureDirty = false;
        fullTextureUpload = false;
        dirtyTextureChunks.Clear();
    }

    public float GetBrightness(Vector3Int cell)
    {
        if (GameplayTestSettings.GlobalLighting) return 1f;
        if (!lightingEnabled) return Mathf.Max(ambientBrightness, daylightStrength);
        int x = cell.x + width / 2, y = -cell.y;
        float mapBrightness = cell.y > 0
            ? Mathf.Max(ambientBrightness, daylightStrength)
            : field == null || x < 0 || x >= width || y >= height
            ? ambientBrightness
            : Mathf.Max(ambientBrightness, GridDaylight.VisibleLight(field[x, y]));
        float brightness = Mathf.Max(mapBrightness, torchField != null ? torchField.Get(x, y) : 0f);
        Vector2 worldPosition = tiles ? tiles.GetCellCenterWorld(cell) : Vector2.zero;
        if (headlamp && headlamp.isActiveAndEnabled && headlamp.intensity > 0 && tiles)
            brightness = Mathf.Max(brightness, GetHeadlampBrightness(worldPosition));
        return brightness;
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
        if (torchLight) Destroy(torchLight.gameObject);
        if (torchLightSprite) Destroy(torchLightSprite);
        if (torchLightTexture) Destroy(torchLightTexture);
        if (overlay) { overlay.SetActive(false); Destroy(overlay); }
        if (texture) Destroy(texture);
        if (material) Destroy(material);
        if (mesh) Destroy(mesh);
        if (uploadPatch) Destroy(uploadPatch);
        field = null;
        torchField = null;
        torchLight = null;
        torchLightSprite = null;
        torchLightTexture = null;
        torchLightPixels = null;
        torchSources.Clear();
        torchFieldDirty = true;
        pixels = null;
        overlay = null;
        texture = null;
        material = null;
        mesh = null;
        uploadPatch = null;
        textureDirty = fullTextureUpload = false;
        dirtyTextureChunks.Clear();
        changed.Clear();
    }
}
