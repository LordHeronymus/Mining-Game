using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class PlayerMapPanel : MonoBehaviour
{
    static readonly Color32 UnknownColor = new Color32(8, 10, 15, 255);
    static readonly Color32 EmptyColor = new Color32(29, 32, 38, 255);
    static readonly Color Cream = new Color32(255, 241, 218, 255);
    static readonly Color Gold = new Color32(240, 171, 79, 255);

    const float InitialCellSize = 18f;
    const float MinimumCellSize = 2f;
    const float MaximumCellSize = 48f;

    CanvasGroup visibility;
    CanvasGroup panelOpacity;
    RectTransform layout;
    RectTransform viewport;
    RectTransform playerMarker;
    RawImage mapImage;
    Texture2D mapTexture;
    Color32[] pixels;
    PlayerMapDiscovery discovery;
    MapGenerator map;
    PlayerMovement player;
    Camera worldCamera;
    TMP_FontAsset font;
    Material fontMaterial;
    Sprite rowSprite;
    Sprite actionSprite;
    float cellSize = InitialCellSize;
    float centerX;
    float centerDepth;
    float previousTimeScale = 1f;
    Vector3 lastPointer;
    bool dragging;
    bool textureDirty;
    bool open;

    public bool IsOpen => open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Create();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindFirstObjectByType<PlayerMapPanel>(FindObjectsInactive.Include)) return;
        var canvas = GameObject.Find("ScreenCanvas");
        if (!canvas || !canvas.GetComponent<Canvas>()) return;
        var root = new GameObject("PlayerMapPanel", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        root.AddComponent<PlayerMapPanel>();
    }

    void Awake()
    {
        visibility = gameObject.AddComponent<CanvasGroup>();
        var workbench = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (workbench)
        {
            font = workbench.font;
            fontMaterial = workbench.fontMaterial;
            rowSprite = workbench.rowSprite;
            actionSprite = workbench.actionSprite;
        }
        if (!font) font = TMP_Settings.defaultFontAsset;
        Build();
        SetVisible(false);
        ResolveWorld();
    }

    void OnEnable() => ResolveWorld();

    void OnDisable()
    {
        Close();
        if (discovery)
        {
            discovery.CellChanged -= OnCellChanged;
            discovery.MapReset -= OnMapReset;
        }
        discovery = null;
        if (mapTexture) Destroy(mapTexture);
        mapTexture = null;
        pixels = null;
    }

    void OnDestroy()
    {
        if (mapTexture) Destroy(mapTexture);
    }

    void OnRectTransformDimensionsChange() => Fit();

    void Update()
    {
        if (!discovery || !map || !player || !worldCamera) ResolveWorld();

        if (GameBindings.Down(GameAction.Map))
        {
            if (open) Close();
            else if (!GameplayInputBlocker.IsBlocked && map && map.IsGenerated &&
                !map.IsGenerationStreaming && discovery &&
                !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen)
                Open();
        }
        else if (open && (Input.GetKeyDown(KeyCode.Escape) || GameBindings.Down(GameAction.Settings))) Close();

        if (!open) return;
        if (panelOpacity && discovery) panelOpacity.alpha = Mathf.Clamp01(discovery.panelAlpha);
        HandleMapInput();
        RefreshView();
        if (textureDirty && mapTexture)
        {
            mapTexture.Apply(false, false);
            textureDirty = false;
        }
    }

    void ResolveWorld()
    {
        var nextMap = FindFirstObjectByType<MapGenerator>();
        if (nextMap != map)
        {
            map = nextMap;
            SetDiscovery(map ? map.GetComponent<PlayerMapDiscovery>() : null);
        }
        else if (map && !discovery) SetDiscovery(map.GetComponent<PlayerMapDiscovery>());
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (!worldCamera) worldCamera = Camera.main;
    }

    void SetDiscovery(PlayerMapDiscovery next)
    {
        if (discovery == next) return;
        if (discovery)
        {
            discovery.CellChanged -= OnCellChanged;
            discovery.MapReset -= OnMapReset;
        }
        discovery = next;
        if (discovery)
        {
            discovery.CellChanged += OnCellChanged;
            discovery.MapReset += OnMapReset;
        }
        RebuildTexture();
    }

    void Open()
    {
        if (!mapTexture) RebuildTexture();
        if (!mapTexture) return;
        open = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (panelOpacity) panelOpacity.alpha = Mathf.Clamp01(discovery.panelAlpha);
        transform.SetAsLastSibling();
        SetVisible(true);
        GameplayInputBlocker.SetBlocked(this, true);
        InfoPanel.Instance?.ShowPanel(false);
        cellSize = InitialCellSize;
        CenterOnPlayer();
        RefreshView();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        dragging = false;
        SetVisible(false);
        GameplayInputBlocker.SetBlocked(this, false);
        Time.timeScale = previousTimeScale;
        InfoPanel.Instance?.ShowPanel(true);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    void SetVisible(bool value)
    {
        if (!visibility) return;
        visibility.alpha = value ? 1f : 0f;
        visibility.interactable = visibility.blocksRaycasts = value;
    }

    void CenterOnPlayer()
    {
        if (!map || !player) return;
        var cell = map.Terrain.WorldToCell(player.transform.position);
        centerX = cell.x + map.GeneratedWidth / 2f + .5f;
        centerDepth = Mathf.Clamp(-cell.y + .5f, .5f, discovery.Height - .5f);
        ClampView();
    }

    void HandleMapInput()
    {
        if (!viewport || !discovery || discovery.Width <= 0) return;
        var canvas = GetComponentInParent<Canvas>();
        var uiCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            viewport, Input.mousePosition, uiCamera, out var pointer) && viewport.rect.Contains(pointer);

        if (inside && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
        {
            float oldSize = cellSize;
            cellSize = Mathf.Clamp(cellSize * Mathf.Pow(1.18f, Input.mouseScrollDelta.y),
                MinimumCellSize, MaximumCellSize);
            float pointerX = pointer.x / viewport.rect.width - .5f;
            float pointerY = -.5f - pointer.y / viewport.rect.height;
            centerX += pointerX * viewport.rect.width * (1f / oldSize - 1f / cellSize);
            centerDepth += pointerY * viewport.rect.height * (1f / oldSize - 1f / cellSize);
            ClampView();
        }

        if (Input.GetMouseButtonDown(0) && inside)
        {
            dragging = true;
            lastPointer = Input.mousePosition;
        }
        if (!Input.GetMouseButton(0)) dragging = false;
        if (!dragging) return;
        Vector3 delta = Input.mousePosition - lastPointer;
        lastPointer = Input.mousePosition;
        float scale = Mathf.Max(.01f, layout.localScale.x);
        centerX -= delta.x / (cellSize * scale);
        centerDepth += delta.y / (cellSize * scale);
        ClampView();
    }

    void ClampView()
    {
        if (!discovery || !viewport || discovery.Width <= 0 || discovery.Height <= 0) return;
        float halfWidth = Mathf.Min(discovery.Width, viewport.rect.width / cellSize) * .5f;
        float halfHeight = Mathf.Min(discovery.Height, viewport.rect.height / cellSize) * .5f;
        centerX = Mathf.Clamp(centerX, halfWidth, discovery.Width - halfWidth);
        centerDepth = Mathf.Clamp(centerDepth, halfHeight, discovery.Height - halfHeight);
    }

    void RefreshView()
    {
        if (!mapImage || !discovery || discovery.Width <= 0 || discovery.Height <= 0) return;
        ClampView();
        float viewWidth = Mathf.Min(discovery.Width, viewport.rect.width / cellSize);
        float viewHeight = Mathf.Min(discovery.Height, viewport.rect.height / cellSize);
        float left = centerX - viewWidth * .5f;
        float top = centerDepth - viewHeight * .5f;
        mapImage.uvRect = new Rect(left / discovery.Width,
            1f - (top + viewHeight) / discovery.Height,
            viewWidth / discovery.Width, viewHeight / discovery.Height);

        if (!playerMarker || !player || !map) return;
        var cell = map.Terrain.WorldToCell(player.transform.position);
        float mapX = cell.x + discovery.Width / 2f + .5f;
        float mapDepth = Mathf.Clamp(-cell.y + .5f, .5f, discovery.Height - .5f);
        float x = (mapX - left) * viewport.rect.width / viewWidth;
        float y = (mapDepth - top) * viewport.rect.height / viewHeight;
        bool markerVisible = x >= 0f && x <= viewport.rect.width && y >= 0f && y <= viewport.rect.height;
        playerMarker.gameObject.SetActive(markerVisible);
        if (markerVisible)
            playerMarker.anchoredPosition = new Vector2(
                Mathf.Clamp(x, 10f, viewport.rect.width - 10f),
                -Mathf.Clamp(y, 10f, viewport.rect.height - 10f));
    }

    void OnCellChanged(int x, int depth)
    {
        if (!mapTexture || !discovery || x < 0 || depth < 0 || x >= discovery.Width || depth >= discovery.Height) return;
        int index = (discovery.Height - 1 - depth) * discovery.Width + x;
        pixels[index] = SnapshotColor(x, depth);
        mapTexture.SetPixel(x, discovery.Height - 1 - depth, pixels[index]);
        textureDirty = true;
    }

    void OnMapReset() => RebuildTexture();

    void RebuildTexture()
    {
        if (mapTexture) Destroy(mapTexture);
        mapTexture = null;
        pixels = null;
        if (!discovery || discovery.Width <= 0 || discovery.Height <= 0) return;
        int width = discovery.Width, height = discovery.Height;
        pixels = new Color32[width * height];
        for (int depth = 0; depth < height; depth++)
            for (int x = 0; x < width; x++)
                pixels[(height - 1 - depth) * width + x] = SnapshotColor(x, depth);
        mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "Discovered map"
        };
        mapTexture.SetPixels32(pixels);
        mapTexture.Apply(false, false);
        if (mapImage) mapImage.texture = mapTexture;
        textureDirty = false;
    }

    Color32 SnapshotColor(int x, int depth)
    {
        if (!discovery.TryGetSnapshot(x, depth, out var type, out var artifact)) return UnknownColor;
        if (artifact) return new Color32(255, 207, 126, 255);
        if (map && map.mapOverviewColors != null)
        {
            int index = (int)type;
            if (index >= 0 && index < map.mapOverviewColors.Length) return map.mapOverviewColors[index];
        }
        return type switch
        {
            BlockType.Dirt => new Color32(196, 85, 28, 255),
            BlockType.Stone => new Color32(244, 123, 32, 255),
            BlockType.StoneLayer2 => new Color32(211, 91, 38, 255),
            BlockType.StoneLayer3 => new Color32(142, 63, 255, 255),
            BlockType.StoneLayer4 => new Color32(49, 77, 255, 255),
            BlockType.DiamondOre => new Color32(0, 232, 255, 255),
            BlockType.IronOre => new Color32(61, 156, 255, 255),
            BlockType.CopperOre => new Color32(255, 87, 34, 255),
            BlockType.SilverOre => new Color32(231, 237, 245, 255),
            BlockType.GoldOre => new Color32(255, 211, 0, 255),
            BlockType.PlatinumOre => new Color32(255, 62, 234, 255),
            BlockType.TitaniumOre => new Color32(255, 255, 255, 255),
            BlockType.TungstenOre => new Color32(111, 160, 208, 255),
            BlockType.OrangeGarnetOre => new Color32(255, 112, 24, 255),
            BlockType.MythrilOre => new Color32(35, 205, 255, 255),
            BlockType.EmeraldOre => new Color32(28, 210, 72, 255),
            BlockType.RubyOre => new Color32(242, 20, 52, 255),
            BlockType.Coal => new Color32(9, 11, 16, 255),
            BlockType.UltroniumOre => new Color32(180, 0, 255, 255),
            BlockType.Empty => EmptyColor,
            _ => UnknownColor
        };
    }

    void Build()
    {
        var scrim = Image("Scrim", transform, new Color(0f, 0f, 0f, .3f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        layout = Rect("Layout", transform, 0, 0, 1640, 960);
        layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(.5f, .5f);
        layout.anchoredPosition = Vector2.zero;
        panelOpacity = layout.gameObject.AddComponent<CanvasGroup>();
        var frame = Image("Panel", layout, new Color32(33, 23, 19, 255));
        Place(frame.rectTransform, 120, 56, 1400, 848);
        frame.sprite = rowSprite;
        frame.raycastTarget = true;
        var inner = Image("Kartenfläche", layout, new Color32(12, 14, 20, 255));
        Place(inner.rectTransform, 166, 158, 1308, 680);
        inner.raycastTarget = true;
        viewport = inner.rectTransform;
        viewport.gameObject.AddComponent<RectMask2D>();

        mapImage = Rect("Entdeckte Karte", viewport, 0, 0, 1308, 680).gameObject.AddComponent<RawImage>();
        Stretch(mapImage.rectTransform);
        mapImage.color = Color.white;
        mapImage.raycastTarget = true;
        mapImage.texture = Texture2D.blackTexture;

        playerMarker = Label(viewport, "●", 0, 0, 20, 20, 20, TextAlignmentOptions.Center).rectTransform;
        playerMarker.pivot = new Vector2(.5f, .5f);
        playerMarker.GetComponent<TextMeshProUGUI>().color = new Color32(84, 239, 244, 255);
        Label(layout, "Karte", 180, 73, 650, 70, 46, TextAlignmentOptions.Left);
        Button(layout, "×", 1382, 77, 78, 64, Close);
        Button(layout, "Spieler", 1210, 847, 250, 44, CenterOnPlayer);
        Fit();
    }

    void Fit()
    {
        if (!layout) return;
        Vector2 size = ((RectTransform)transform).rect.size;
        layout.localScale = Vector3.one * Mathf.Min(size.x / 1640f, size.y / 960f);
    }

    static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Image Image(string name, Transform parent, Color color)
    {
        var image = Rect(name, parent, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    TextMeshProUGUI Label(Transform parent, string value, float x, float y, float width, float height,
        float size, TextAlignmentOptions alignment)
    {
        var label = Rect("Label", parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        if (fontMaterial) label.fontSharedMaterial = fontMaterial;
        label.text = value;
        label.color = Cream;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = size;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    void Button(Transform parent, string value, float x, float y, float width, float height, Action action)
    {
        var image = Image(value, parent, new Color32(69, 43, 27, 255));
        Place(image.rectTransform, x, y, width, height);
        image.sprite = actionSprite;
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => action());
        var label = Label(image.transform, value, 0, 0, width, height,
            value == "×" ? 44 : 24, TextAlignmentOptions.Center);
        label.color = Gold;
    }
}
