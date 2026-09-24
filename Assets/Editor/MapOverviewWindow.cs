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
    static readonly Color32 EmptyColor = new Color32(26, 29, 33, 255);
    static readonly Color32 UnknownColor = new Color32(232, 58, 180, 255);

    [SerializeField] int previewSeed = 12345;
    [SerializeField] float zoom = 1f;
    [SerializeField] Rect view = new Rect(0, 0, 1, 1);

    MapGenerator map;
    Tilemap tilemap;
    Texture2D texture;
    Color32[] pixels;
    BlockType[] types;
    Block[] previewBlocks;
    MapGenerationSampler sampler;
    readonly HashSet<int> changedCells = new HashSet<int>();
    int width;
    int height;
    int nextRow;
    int sourceSeed;
    bool live;
    bool building;
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

    void OnEnable()
    {
        EditorApplication.update += UpdateOverview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.projectChanged += OnProjectChanged;
        EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        Undo.undoRedoPerformed += RequestBuild;
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
        Tilemap.tilemapTileChanged -= OnTilesChanged;
        TileMiner.OnBlockMined -= OnBlockMined;
        DisposeTexture();
    }

    void OnPlayModeChanged(PlayModeStateChange state) => RequestBuild();
    void OnProjectChanged() { if (!EditorApplication.isPlaying) RequestBuild(); }
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
            if (live) Repaint(); // Player marker, without repainting every game frame.
        }
    }

    void BeginBuild(bool useLive, int seed)
    {
        pendingBuild = false;
        error = null;
        changedCells.Clear();
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
                map.oreTransitionCurve, map.oreTransitionDepth, map.oreVeinSizeCurve,
                map.surfaceOreRampDepth, map.surfaceOreRampCurve, map.surfaceOreVeinSizePercent);
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
        int end = Mathf.Min(height, nextRow + RowsPerUpdate);
        for (int y = nextRow; y < end; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Block block = live ? map.GetBlockAt(new Vector3Int(x - width / 2, -y, 0)) : sampler.GetBlock(x, y);
                if (!live) previewBlocks[y * width + x] = block;
                SetCell(x, y, block);
            }
        }
        nextRow = end;
        if (nextRow < height) { Repaint(); return; }

        if (!live && map.minimumOreVeinSize > 1)
        {
            OreVeins.PruneSmallVeins(previewBlocks, width, height, map.minimumOreVeinSize, sampler.GetBaseBlock);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) SetCell(x, y, previewBlocks[y * width + x]);
        }

        texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
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
        building = false;
        Repaint();
    }

    void SetCell(int x, int y, Block block)
    {
        int index = y * width + x;
        BlockType type = block ? block.id : BlockType.Empty;
        types[index] = type;
        Color32 color = block ? ColorFor(type) : EmptyColor;
        pixels[(height - 1 - y) * width + x] = color;
        if (texture) texture.SetPixel(x, height - 1 - y, color);
    }

    static Color32 ColorFor(BlockType type)
    {
        switch (type)
        {
            case BlockType.Dirt: return new Color32(109, 72, 42, 255);
            case BlockType.Stone: return new Color32(105, 78, 60, 255);
            case BlockType.StoneLayer2: return new Color32(80, 86, 96, 255);
            case BlockType.StoneLayer3: return new Color32(67, 72, 82, 255);
            case BlockType.StoneLayer4: return new Color32(54, 58, 68, 255);
            case BlockType.DiamondOre: return new Color32(124, 231, 241, 255);
            case BlockType.IronOre: return new Color32(168, 179, 195, 255);
            case BlockType.CopperOre: return new Color32(211, 111, 69, 255);
            case BlockType.SilverOre: return new Color32(226, 228, 222, 255);
            case BlockType.GoldOre: return new Color32(246, 192, 58, 255);
            case BlockType.PlatinumOre: return new Color32(210, 204, 184, 255);
            case BlockType.Coal: return new Color32(38, 40, 45, 255);
            case BlockType.Empty: return EmptyColor;
            default: return UnknownColor;
        }
    }

    void OnTilesChanged(Tilemap changedMap, Tilemap.SyncTile[] changes)
    {
        if (!live || (changedMap != tilemap && changedMap != map.OreOverlay) || !HasLiveMap() || changes == null) return;
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
        EditorGUI.DrawRect(new Rect(sx - 5, sy - 1, 11, 3), Color.cyan);
        EditorGUI.DrawRect(new Rect(sx - 1, sy - 5, 3, 11), Color.cyan);
    }

    void DrawLegend(Rect area)
    {
        string[] names = { "Erde", "Übergang", "Stein", "Tiefstein 1", "Tiefstein 2", "Kohle", "Eisen", "Kupfer", "Silber", "Gold", "Platin", "Diamant", "Leer", "Spieler" };
        BlockType[] ids = { BlockType.Dirt, BlockType.Stone, BlockType.StoneLayer2, BlockType.StoneLayer3, BlockType.StoneLayer4,
            BlockType.Coal, BlockType.IronOre, BlockType.CopperOre,
            BlockType.SilverOre, BlockType.GoldOre, BlockType.PlatinumOre, BlockType.DiamondOre, BlockType.Empty };
        float x = area.x;
        for (int i = 0; i < names.Length; i++)
        {
            EditorGUI.DrawRect(new Rect(x, area.y + 2, 11, 11), i == ids.Length ? Color.cyan : ColorFor(ids[i]));
            GUI.Label(new Rect(x + 15, area.y, 65, 17), names[i], EditorStyles.miniLabel);
            area.y += 18;
        }
    }
}
