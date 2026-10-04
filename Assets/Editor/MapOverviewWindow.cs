using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public sealed class MapOverviewWindow : EditorWindow
{
    const int RowsPerUpdate = 16;
    static readonly Color32 EmptyColor = new Color32(23, 25, 29, 255);
    static readonly Color32 UnknownColor = new Color32(232, 58, 180, 255);

    [SerializeField] int previewSeed = 12345;
    [SerializeField] float zoom = 1f;
    [SerializeField] Rect view = new Rect(0, 0, 1, 1);

    MapGenerator map;
    Tilemap tilemap;
    Texture2D texture;
    Texture2D altarIcon;
    Color32[] pixels;
    BlockType[] types;
    readonly Dictionary<int, ArtifactTile> artifactCells = new();
    Block[] previewBlocks;
    bool[] previewCaves;
    UltroniumChamberLayout previewChamber;
    MapGenerationSampler sampler;
    readonly HashSet<int> changedCells = new HashSet<int>();
    int width;
    int height;
    int nextRow;
    int sourceSeed;
    bool live;
    bool building;
    bool recolorAfterBuild;
    bool pendingBuild = true;
    bool dragging;
    Vector2 mouseDown;
    Vector2 lastMouse;
    double nextRepaint;
    string error;

    [MenuItem("Mining Game/Map Overview")]
    public static void Open()
    {
        var windows = Resources.FindObjectsOfTypeAll<MapOverviewWindow>();
        var window = windows.Length > 0
            ? windows[0]
            : CreateWindow<MapOverviewWindow>("Map Overview", typeof(SceneView));
        window.minSize = new Vector2(320, 420);
        window.Show();
        window.Focus();
    }

    internal static void RefreshPalette()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<MapOverviewWindow>())
        {
            if (window.building) window.recolorAfterBuild = true;
            else window.RecolorTexture();
        }
    }

    void OnEnable()
    {
        EditorApplication.update += UpdateOverview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.projectChanged += OnProjectChanged;
        EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        Undo.undoRedoPerformed += RequestBuild;
        GpsSettings.Changed += OnGpsChanged;
        Tilemap.tilemapTileChanged += OnTilesChanged;
        TileMiner.OnBlockMined += OnBlockMined;
        pendingBuild = true;
    }

    void OnDisable()
    {
        EditorApplication.update -= UpdateOverview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.projectChanged -= OnProjectChanged;
        EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
        Undo.undoRedoPerformed -= RequestBuild;
        GpsSettings.Changed -= OnGpsChanged;
        Tilemap.tilemapTileChanged -= OnTilesChanged;
        TileMiner.OnBlockMined -= OnBlockMined;
        DisposeTexture();
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        GpsSettings.Changed -= OnGpsChanged;
        GpsSettings.Changed += OnGpsChanged;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            TileMiner.OnBlockMined -= OnBlockMined;
            TileMiner.OnBlockMined += OnBlockMined;
        }
        RequestBuild();
    }
    void OnProjectChanged() { if (!EditorApplication.isPlaying) RequestBuild(); }
    void OnGpsChanged() { if (live) recolorAfterBuild=true; else RequestBuild(); }
    void OnSceneChanged(Scene previous, Scene next) => RequestBuild();
    void RequestBuild() { pendingBuild = true; Repaint(); }

    static MapGenerator FindMap()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<MapGenerator>(true);
            if (found) return found;
        }
        return null;
    }

    void UpdateOverview()
    {
        if (EditorApplication.isPlaying && (LoadingProgress.Active || RunNavigation.IsTransitioning)) return;
        if (!map || !tilemap)
        {
            map = FindMap();
            tilemap = map ? map.GetComponent<Tilemap>() : null;
            pendingBuild = true;
        }

        if (!map || !tilemap || !map.registry || map.mapWidth <= 0 || map.mapHeight <= 0)
        {
            if (texture || building) { DisposeTexture(); building = false; Repaint(); }
            return;
        }

        // Show the placed map when available; otherwise use a calculated preview.
        bool shouldUseLive = HasLiveMap();
        int wantedSeed = shouldUseLive ? map.ActiveSeed : (map.randomizeSeed ? previewSeed : map.seed);
        if (shouldUseLive != live || wantedSeed != sourceSeed || width != (shouldUseLive ? map.GeneratedWidth : map.mapWidth) || height != (shouldUseLive ? map.GeneratedHeight : map.mapHeight))
            pendingBuild = true;

        if (pendingBuild)
            BeginBuild(shouldUseLive, wantedSeed);

        if (building)
        {
            BuildRows();
            return;
        }

        if (changedCells.Count > 0 && live && texture)
        {
            foreach (int index in changedCells)
            {
                int x = index % width;
                int y = index / width;
                var cell = new Vector3Int(x - width / 2, -y, 0);
                SetCell(x, y, map.GetBlockAt(cell));
            }
            changedCells.Clear();
            texture.Apply(false, false);
            Repaint();
        }

        if (EditorApplication.timeSinceStartup >= nextRepaint)
        {
            nextRepaint = EditorApplication.timeSinceStartup + 0.2;
            if (live && recolorAfterBuild) { recolorAfterBuild=false; RecolorTexture(); }
            if (live) Repaint(); // Player marker, without repainting every game frame.
        }
    }

    void BeginBuild(bool useLive, int seed)
    {
        pendingBuild = false;
        error = null;
        changedCells.Clear();
        artifactCells.Clear();
        DisposeTexture();
        live = useLive;
        sourceSeed = seed;
        width = useLive ? map.GeneratedWidth : map.mapWidth;
        height = useLive ? map.GeneratedHeight : map.mapHeight;
        nextRow = 0;
        pixels = new Color32[width * height];
        types = new BlockType[width * height];
        previewBlocks = useLive ? null : new Block[width * height];
        try
        {
            sampler = live ? null : new MapGenerationSampler(map.registry, seed, height, map.layers,
                map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
                map.useOreSettings ? map.oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
            previewChamber = !live && map.AltarChamber
                ? map.AltarChamber.ChooseLayout(sourceSeed, width, height) : default;
            previewCaves = live ? null : map.CreateCaveMask(sourceSeed, width, height, previewChamber);
            building = true;
        }
        catch (Exception ex)
        {
            sampler = null;
            building = false;
            error = ex.Message;
        }
        Repaint();
    }

    void BuildRows()
    {
        var chamber = live && map.AltarChamber ? map.AltarChamber.Layout : previewChamber;
        int end = Mathf.Min(height, nextRow + RowsPerUpdate);
        for (int y = nextRow; y < end; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Block block = live ? map.GetBlockAt(new Vector3Int(x - width / 2, -y, 0)) : sampler.GetBlock(x, y);
                var cell = new Vector3Int(x-width/2,-y,0);
                if(!live && chamber.IsReserved(cell)) block=chamber.IsOpen(cell)?null:sampler.GetBaseBlock(x,y);
                if (!live && previewCaves[y * width + x]) block = null;
                if (!live) previewBlocks[y * width + x] = block;
                SetCell(x, y, block);
            }
        }
        nextRow = end;
        if (nextRow < height) { Repaint(); return; }

        if (!live)
        {
            ConnectedOreVeins.Generate(previewBlocks, width, height, sourceSeed, sampler,
                map.GetMinimumVeinSize, (x, y) => previewCaves[y * width + x] ||
                    chamber.IsReserved(new Vector3Int(x - width / 2, -y, 0)));
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) SetCell(x, y, previewBlocks[y * width + x]);
        }

        if (!live)
        {
            var placedArtifacts = new Dictionary<ArtifactTile, List<Vector2Int>>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var block = previewBlocks[y * width + x];
                    if (block && !chamber.IsReserved(new Vector3Int(x-width/2,-y,0)) && !block.HasOreOverlays &&
                        !OreVeins.HasVeinInNeighborhood(previewBlocks, width, height, x, y))
                    {
                        var artifact = map.SelectArtifact(sourceSeed, x, y);
                        if (ArtifactPlacement.TryPlace(placedArtifacts, artifact, x, y,
                            map.artifactMinimumSameTypeDistance))
                            SetArtifact(x, y, artifact);
                    }
                }
        }

        texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            name = "Map Overview (editor only)",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        sampler = null;
        previewBlocks = null;
        previewCaves = null;
        building = false;
        if (recolorAfterBuild)
        {
            recolorAfterBuild = false;
            RecolorTexture();
        }
        Repaint();
    }

    void SetCell(int x, int y, Block block)
    {
        int index = y * width + x;
        BlockType type = block ? block.id : BlockType.Empty;
        types[index] = type;
        SetArtifact(x, y, live && block
            ? map.GetArtifactAt(new Vector3Int(x - width / 2, -y, 0)) : null);
        Color32 color = GetMapColor(type);
        pixels[(height - 1 - y) * width + x] = color;
        if (texture) texture.SetPixel(x, height - 1 - y, color);
    }

    Color32 GetMapColor(BlockType type)
    {
        if (map && map.mapOverviewColors != null)
        {
            int index = (int)type;
            if (index >= 0 && index < map.mapOverviewColors.Length)
                return map.mapOverviewColors[index];
        }
        return ColorFor(type);
    }

    void RecolorTexture()
    {
        if (!map || !texture || pixels == null || types == null) return;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[(height - 1 - y) * width + x] = GetMapColor(types[y * width + x]);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Repaint();
    }

    void SetArtifact(int x, int y, ArtifactTile artifact)
    {
        int index = y * width + x;
        if (artifact) artifactCells[index] = artifact;
        else artifactCells.Remove(index);
    }

    internal static Color32 ColorFor(BlockType type)
    {
        switch (type)
        {
            case BlockType.Dirt: return new Color32(196, 85, 28, 255);
            case BlockType.Stone: return new Color32(244, 123, 32, 255);
            case BlockType.StoneLayer2: return new Color32(211, 91, 38, 255);
            case BlockType.StoneLayer3: return new Color32(142, 63, 255, 255);
            case BlockType.StoneLayer4: return new Color32(49, 77, 255, 255);
            case BlockType.DiamondOre: return new Color32(0, 232, 255, 255);
            case BlockType.IronOre: return new Color32(61, 156, 255, 255);
            case BlockType.CopperOre: return new Color32(255, 87, 34, 255);
            case BlockType.SilverOre: return new Color32(231, 237, 245, 255);
            case BlockType.GoldOre: return new Color32(255, 211, 0, 255);
            case BlockType.PlatinumOre: return new Color32(255, 62, 234, 255);
            case BlockType.TitaniumOre: return new Color32(255, 255, 255, 255);
            case BlockType.TungstenOre: return new Color32(111, 160, 208, 255);
            case BlockType.OrangeGarnetOre: return new Color32(255, 112, 24, 255);
            case BlockType.MythrilOre: return new Color32(35, 205, 255, 255);
            case BlockType.EmeraldOre: return new Color32(28, 210, 72, 255);
            case BlockType.RubyOre: return new Color32(242, 20, 52, 255);
            case BlockType.Coal: return new Color32(9, 11, 16, 255);
            case BlockType.UltroniumOre: return new Color32(180, 0, 255, 255);
            case BlockType.Empty: return EmptyColor;
            default: return UnknownColor;
        }
    }

    void OnTilesChanged(Tilemap changedMap, Tilemap.SyncTile[] changes)
    {
        if (EditorApplication.isPlaying && (LoadingProgress.Active || RunNavigation.IsTransitioning))
        { pendingBuild = true; return; }
        if (!live || !map || (changedMap != tilemap && changedMap != map.OreOverlay) || !HasLiveMap() || changes == null) return;
        foreach (var change in changes)
            QueueCell(change.position);
    }

    void OnBlockMined(Vector2 position, int points)
    {
        if (!live || !tilemap) return;
        QueueCell(tilemap.WorldToCell(position));
    }

    void QueueCell(Vector3Int cell)
    {
        int x = cell.x + width / 2;
        int y = -cell.y;
        if (x >= 0 && x < width && y >= 0 && y < height)
            changedCells.Add(y * width + x);
    }

    bool HasLiveMap()
    {
        if (!map || !tilemap) return false;
        if (map.IsGenerated) return true;
        var bounds = tilemap.cellBounds;
        return bounds.size.x >= map.mapWidth && bounds.size.y >= map.mapHeight;
    }

    void DisposeTexture()
    {
        if (texture) DestroyImmediate(texture);
        texture = null;
        pixels = null;
        types = null;
        previewBlocks = null;
        sampler = null;
    }

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Map Overview", EditorStyles.boldLabel, GUILayout.Width(105));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Ganze Map", EditorStyles.toolbarButton)) { zoom = 1f; view = new Rect(0, 0, 1, 1); }
            if (GUILayout.Button("Aktualisieren", EditorStyles.toolbarButton)) RequestBuild();
        }

        if (!map || !map.registry)
        {
            EditorGUILayout.HelpBox("Keine Map mit Block Registry in der aktiven Szene gefunden.", MessageType.Info);
            return;
        }

        string mode = live ? "Live-Map" : "Vorschau";
        EditorGUILayout.LabelField(mode + "  •  " + width + " × " + height + " Blöcke  •  Seed " + sourceSeed);
        if (!live && map.randomizeSeed)
        {
            EditorGUI.BeginChangeCheck();
            previewSeed = EditorGUILayout.IntField("Vorschau-Seed", previewSeed);
            if (EditorGUI.EndChangeCheck()) RequestBuild();
        }

        if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
        if (building || !texture)
        {
            EditorGUILayout.HelpBox(building ? "Übersicht wird erstellt: " + Mathf.RoundToInt(100f * nextRow / height) + " %" : "Übersicht wird geladen.", MessageType.Info);
            return;
        }

        const float legendWidth = 95f;
        float top = GUILayoutUtility.GetLastRect().yMax + 8;
        var area = new Rect(12 + legendWidth, top, position.width - 24 - legendWidth, position.height - top - 32);
        if (area.width <= 0 || area.height <= 0) return;
        Vector2 center = view.center;
        view.size = ViewSize(area);
        view.center = center;
        ClampView();
        EditorGUI.DrawRect(new Rect(0, area.y - 2, position.width, position.height - area.y), new Color(0.12f, 0.13f, 0.15f));
        HandleInput(area);
        DrawMap(area);
        DrawArtifactMarkers(area);
        DrawAltarMarker(area);
        DrawPlayerMarker(area);
        DrawStatus(area, position.height - 24);
        DrawLegend(new Rect(12, top, legendWidth, area.height));
    }

    Vector2 ViewSize(Rect area)
    {
        float scale = Mathf.Min(area.width / width, area.height / height) * zoom;
        return new Vector2(area.width / (width * scale), area.height / (height * scale));
    }

    void ClampView()
    {
        // Center an axis while the map fits; allow panning once it exceeds the viewport.
        view.x = view.width >= 1f ? (1f - view.width) * 0.5f : Mathf.Clamp(view.x, 0f, 1f - view.width);
        view.y = view.height >= 1f ? (1f - view.height) * 0.5f : Mathf.Clamp(view.y, 0f, 1f - view.height);
    }

    void DrawMap(Rect area)
    {
        // Clip to the map's UV bounds so empty margins never stretch edge pixels.
        var uv = Rect.MinMaxRect(Mathf.Max(0f, view.xMin), Mathf.Max(0f, view.yMin),
            Mathf.Min(1f, view.xMax), Mathf.Min(1f, view.yMax));
        var visible = new Rect(area.x + (uv.x - view.x) / view.width * area.width,
            area.y + (view.yMax - uv.yMax) / view.height * area.height,
            uv.width / view.width * area.width, uv.height / view.height * area.height);
        GUI.DrawTextureWithTexCoords(visible, texture, uv, false);
    }

    void DrawAltarMarker(Rect area)
    {
        if (!map || !map.AltarChamber) return;
        var chamber = live ? map.AltarChamber.Layout : previewChamber;
        if (!chamber.valid) return;
        if (!altarIcon) altarIcon = Resources.Load<Texture2D>("UltroniumAltar/Altar");
        if (!altarIcon) return;

        float u = (chamber.origin.x + width / 2 + .5f) / width;
        float v = (height + chamber.origin.y) / (float)height;
        float sx = (u - view.x) / view.width * area.width;
        float sy = (view.yMax - v) / view.height * area.height;
        float cellPixels = area.width / (width * view.width);
        float iconWidth = Mathf.Clamp(cellPixels * 6.2f * Mathf.Clamp(map.altarSize, .25f, 3f), 36f, 96f);
        float iconHeight = iconWidth * altarIcon.height / altarIcon.width;
        var marker = new Rect(sx - iconWidth * .5f, sy - iconHeight, iconWidth, iconHeight);
        if (!marker.Overlaps(new Rect(0, 0, area.width, area.height))) return;

        GUI.BeginGroup(area);
        GUI.DrawTexture(marker, altarIcon, ScaleMode.StretchToFill, true);
        GUI.EndGroup();
    }

    void DrawArtifactMarkers(Rect area)
    {
        float cellPixels = Mathf.Min(area.width / width, area.height / height) * zoom;
        float iconScale = Mathf.Clamp(map.artifactOverviewIconScale, .25f, 8f);
        float iconSize = Mathf.Clamp(cellPixels * .85f, 5f, 22f) * iconScale;
        float inset = Mathf.Min(iconScale, iconSize * .16f);
        foreach (var entry in artifactCells)
        {
            var sprite = entry.Value ? entry.Value.sprite : null;
            if (!sprite || !sprite.texture) continue;
            int x = entry.Key % width;
            int y = entry.Key / width;
            float u = (x + .5f) / width;
            float v = (height - y - .5f) / height;
            float sx = area.x + (u - view.x) / view.width * area.width;
            float sy = area.y + (view.yMax - v) / view.height * area.height;
            if (!area.Contains(new Vector2(sx, sy))) continue;

            var marker = new Rect(sx - iconSize * .5f, sy - iconSize * .5f, iconSize, iconSize);
            var textureRect = sprite.textureRect;
            var uv = new Rect(textureRect.x / sprite.texture.width, textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width, textureRect.height / sprite.texture.height);
            float contentSize = marker.width - inset * 2f;
            float aspect = textureRect.width / Mathf.Max(1f, textureRect.height);
            float imageWidth = aspect >= 1f ? contentSize : contentSize * aspect;
            float imageHeight = aspect >= 1f ? contentSize / aspect : contentSize;
            var image = new Rect(sx - imageWidth * .5f, sy - imageHeight * .5f, imageWidth, imageHeight);
            GUI.DrawTextureWithTexCoords(image, sprite.texture, uv, true);
        }
    }

    void HandleInput(Rect imageRect)
    {
        Event e = Event.current;
        if (!imageRect.Contains(e.mousePosition) && !(dragging &&
            (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))) return;

        if (e.type == EventType.ScrollWheel)
        {
            float oldZoom = zoom;
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-e.delta.y * 0.11f), 1f, 32f);
            float fractionX = Mathf.Clamp01((e.mousePosition.x - imageRect.x) / imageRect.width);
            float fractionY = Mathf.Clamp01((e.mousePosition.y - imageRect.y) / imageRect.height);
            float anchorU = view.x + fractionX * view.width;
            float anchorV = view.y + (1f - fractionY) * view.height;
            Vector2 newSize = ViewSize(imageRect);
            view = new Rect(anchorU - fractionX * newSize.x,
                anchorV - (1f - fractionY) * newSize.y, newSize.x, newSize.y);
            ClampView();
            if (!Mathf.Approximately(oldZoom, zoom)) Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseDown && e.button == 0)
        {
            dragging = true;
            mouseDown = lastMouse = e.mousePosition;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging)
        {
            Vector2 delta = e.mousePosition - lastMouse;
            lastMouse = e.mousePosition;
            view.x -= delta.x / imageRect.width * view.width;
            view.y += delta.y / imageRect.height * view.height;
            ClampView();
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && dragging)
        {
            dragging = false;
            if (Vector2.Distance(mouseDown, e.mousePosition) < 4f)
            {
                int x, y;
                if (CellUnderMouse(e.mousePosition, imageRect, out x, out y))
                    FocusCell(x, y);
            }
            e.Use();
        }
        else if (e.type == EventType.MouseMove) Repaint();
    }

    void DrawStatus(Rect imageRect, float statusY)
    {
        string status = "Mausrad: Zoom  •  Ziehen: Verschieben  •  Klick: Scene-Ansicht";
        int x, y;
        if (CellUnderMouse(Event.current.mousePosition, imageRect, out x, out y))
        {
            status = "X " + (x - width / 2) + "  •  Tiefe " + y + "  •  " + types[y * width + x]
                + "  |  Mausrad: Zoom  •  Klick: Scene";
            if (artifactCells.TryGetValue(y * width + x, out var artifact) && artifact)
                status = "X " + (x - width / 2) + "  •  Tiefe " + y + "  •  " + artifact.displayName
                    + "  |  Mausrad: Zoom  •  Klick: Scene";
        }
        GUI.Label(new Rect(12, statusY, position.width - 24, 20), status, EditorStyles.miniLabel);
    }

    bool CellUnderMouse(Vector2 mouse, Rect imageRect, out int x, out int y)
    {
        float fx = (mouse.x - imageRect.x) / imageRect.width;
        float fy = (mouse.y - imageRect.y) / imageRect.height;
        x = Mathf.FloorToInt((view.x + fx * view.width) * width);
        y = Mathf.FloorToInt((1f - (view.y + (1f - fy) * view.height)) * height);
        return imageRect.Contains(mouse) && x >= 0 && x < width && y >= 0 && y < height;
    }

    void FocusCell(int x, int y)
    {
        if (!tilemap) return;
        Vector3 world = tilemap.GetCellCenterWorld(new Vector3Int(x - width / 2, -y, 0));
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (!sceneView) sceneView = GetWindow<SceneView>();
        sceneView.LookAt(world, Quaternion.identity, 18f, true, true);
        sceneView.Focus();
    }

    void DrawPlayerMarker(Rect imageRect)
    {
        if (!EditorApplication.isPlaying || !tilemap) return;
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        if (!player) return;
        Vector3Int cell = tilemap.WorldToCell(player.transform.position);
        float u = (cell.x + width / 2f + 0.5f) / width;
        float v = (height + cell.y - 0.5f) / height;
        float sx = imageRect.x + (u - view.x) / view.width * imageRect.width;
        float sy = imageRect.y + (1f - (v - view.y) / view.height) * imageRect.height;
        if (!imageRect.Contains(new Vector2(sx, sy))) return;
        EditorGUI.DrawRect(new Rect(sx - 5, sy - 1, 11, 3), map.mapOverviewPlayerColor);
        EditorGUI.DrawRect(new Rect(sx - 1, sy - 5, 3, 11), map.mapOverviewPlayerColor);
    }

    internal static readonly string[] LegendNames =
    {
        "Erde", "Übergang", "Stein", "Tiefstein 1", "Tiefstein 2", "Kohle", "Eisen", "Kupfer",
        "Silber", "Gold", "Platin", "Titan", "Wolfram", "Diamant", "Ultronium", "Orange Granat", "Mythril", "Smaragd", "Rubin", "Leer", "Spieler"
    };

    internal static readonly BlockType[] LegendTypes =
    {
        BlockType.Dirt, BlockType.Stone, BlockType.StoneLayer2, BlockType.StoneLayer3, BlockType.StoneLayer4,
        BlockType.Coal, BlockType.IronOre, BlockType.CopperOre, BlockType.SilverOre, BlockType.GoldOre,
        BlockType.PlatinumOre, BlockType.TitaniumOre, BlockType.TungstenOre, BlockType.DiamondOre,
        BlockType.UltroniumOre, BlockType.OrangeGarnetOre, BlockType.MythrilOre, BlockType.EmeraldOre, BlockType.RubyOre, BlockType.Empty
    };

    internal static Color LegendColorAt(int index, MapGenerator map = null)
    {
        if (index == LegendTypes.Length) return map ? map.mapOverviewPlayerColor : Color.cyan;
        if (map && map.mapOverviewColors != null)
        {
            int colorIndex = (int)LegendTypes[index];
            if (colorIndex >= 0 && colorIndex < map.mapOverviewColors.Length)
                return map.mapOverviewColors[colorIndex];
        }
        return ColorFor(LegendTypes[index]);
    }

    void DrawLegend(Rect area)
    {
        float x = area.x;
        for (int i = 0; i < LegendNames.Length; i++)
        {
            EditorGUI.DrawRect(new Rect(x, area.y + 2, 11, 11), LegendColorAt(i, map));
            GUI.Label(new Rect(x + 15, area.y, 65, 17), LegendNames[i], EditorStyles.miniLabel);
            area.y += 18;
        }
        var artifact = map && map.artifactSettings != null
            ? Array.Find(map.artifactSettings, setting => setting != null && setting.tile) : null;
        if (artifact != null && artifact.tile.sprite)
        {
            var sprite = artifact.tile.sprite;
            var source = sprite.textureRect;
            var uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                source.width / sprite.texture.width, source.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(new Rect(x, area.y + 2, 13, 13), sprite.texture, uv, true);
            GUI.Label(new Rect(x + 15, area.y, 75, 17), "Artefakt", EditorStyles.miniLabel);
        }
    }
}
