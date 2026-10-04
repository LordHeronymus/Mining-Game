using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Four hotbar-style status modules, a fixed pickaxe slot and eight item slots.
public sealed class CompactHud : MonoBehaviour
{
    public Sprite stripSprite, slotSprite, selectedSprite, badgeSprite, barSprite, heartSprite, boltSprite, coinSprite, pickaxeSprite;
    public TMP_FontAsset font;
    public Material fontMaterial;
    public EnergyManager energy;
    public PlayerMovement player;
    public MapGenerator map;
    public ItemSO[] slots = new ItemSO[8];
    public int SelectedSlot { get; private set; }
    public ItemSO SelectedItem => slots != null && SelectedSlot > 0 && SelectedSlot <= slots.Length &&
        IsKnownHotbarItem(slots[SelectedSlot - 1]) ? slots[SelectedSlot - 1] : null;
    public int DepthMeters { get; private set; }
    const string HotbarIconVerticalOffsetKey = "workbench.hotbarIconVerticalOffset";
    public static float HotbarIconVerticalOffset
    {
        get => GpsSettings.Preferences.hotbarVerticalOffset;
        set
        {
            float next = Mathf.Clamp(value, -48f, 48f);
            if (Mathf.Approximately(next, HotbarIconVerticalOffset)) return;
            var node=GpsSettings.GetValue("preferences","hotbarVerticalOffset"); node.number=next;
            GpsSettings.SetValue("preferences",node,out _);
            UnityEngine.Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
        }
    }
    Sprite statusFrame;
    void OnDestroy() { if (statusFrame) Destroy(statusFrame); }
    RectTransform top, hotbar;
    Transform hotbarOverlayParent;
    InventoryUI inventoryPanel;
    CanvasGroup visibility;
    HudGemBar healthFill, energyFill;
    HealthDamageOverlay damageOverlay;
    StatsManager observedStats;
    float observedHealth, damagePulse, damagePulseTime, healthFlashTime;
    float heartbeatPulseTime, lastHeartbeatPlaybackTime = -1f;
    bool criticalHealth;
    const float DamageFadeSeconds = 1.4f;
    const float HealthFlashSeconds = .55f;
    const float HeartbeatFadeSeconds = .32f;
    TextMeshProUGUI healthValue, energyValue, moneyValue, pointsValue, depthValue;
    readonly Image[] icons = new Image[8], selections = new Image[9];
    readonly TextMeshProUGUI[] counts = new TextMeshProUGUI[8];
    readonly Button[] buttons = new Button[8];
    readonly HotbarSlotDrag[] slotDrags = new HotbarSlotDrag[8];
    InventoryManager inventory;
    PlayerLadder ladder;
    WorkbenchPanel workbench;
    Image dragIcon;
    Image pickaxeIcon;
    int draggedSlot = -1;
    bool wasBuilding;
    int lastMoney = int.MinValue, lastPoints = int.MinValue, lastDepth = int.MinValue,
        lastHealth = int.MinValue, lastMaxHealth = int.MinValue, lastEnergy = int.MinValue, lastMax = int.MinValue;
    static readonly Color Cream = new Color32(255,245,229,255);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    void Awake()
    {
        if (slots == null) slots = new ItemSO[8];
        Array.Resize(ref slots, 8);
        LoadSlotLayout();
        if (RemoveIneligibleSlots()) SaveSlotLayout();
        AddStartingResourcesToSlots();
        visibility = gameObject.AddComponent<CanvasGroup>();
        ladder = player ? player.GetComponent<PlayerLadder>() : null;
        workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        Build(); BuildDamageOverlay(); RefreshItems();
        var canvas = GetComponentInParent<Canvas>();
        hotbarOverlayParent = canvas ? canvas.rootCanvas.transform : null;
    }
    void OnDisable()
    {
        if (hotbar && hotbar.parent != transform)
        {
            hotbar.SetParent(transform, false);
            Fit();
        }
        if (inventory) inventory.OnInventoryChanged -= RefreshItems;
        inventory = null;
        if (observedStats) observedStats.OnHealthChanged -= OnHealthChanged;
        observedStats = null;
    }
    void Update()
    {
        if (inventory != InventoryManager.Instance)
        {
            if (inventory) inventory.OnInventoryChanged -= RefreshItems;
            inventory = InventoryManager.Instance;
            if (inventory) inventory.OnInventoryChanged += RefreshItems;
            RefreshItems();
        }
        if (!inventoryPanel) inventoryPanel = UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        bool inventoryOpen = inventoryPanel && inventoryPanel.IsOpen && !GameOverPanel.IsOpen;
        bool blocked = GameplayInputBlocker.IsBlocked;
        SetInventoryHotbarVisible(inventoryOpen);
        visibility.alpha = blocked && !GameplayDebugPanel.IsOpen && !GameOverPanel.IsOpen && !inventoryOpen ? 0 : 1;
        visibility.blocksRaycasts = visibility.interactable = !blocked || inventoryOpen;
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        bool typing = selected && selected.GetComponent<TMP_InputField>();
        if (!typing && (!blocked || inventoryOpen) && GameBindings.Down(GameAction.UseMedkit))
            StatsManager.Instance?.TryUseMedkit();
        if (blocked || typing) return;
        for (int i = 0; i < 8; i++)
            if (GameBindings.Down((GameAction)((int)GameAction.Slot1 + i))) SelectSlot(i + 1);
        if (GameBindings.Down(GameAction.PreviousSlot)) SelectSlot((SelectedSlot + 8) % 9);
        if (GameBindings.Down(GameAction.NextSlot)) SelectSlot((SelectedSlot + 1) % 9);
        if (ladder && ladder.BuildMode != wasBuilding)
        {
            wasBuilding = ladder.BuildMode;
            if (wasBuilding)
                for (int i = 0; i < 8; i++) if (IsKnownHotbarItem(slots[i]) && (slots[i].item == Item.Ladder || slots[i].item == Item.IronLadder)) { SelectedSlot = i + 1; RefreshItems(); break; }
        }
    }
    void LateUpdate()
    {
        if (draggedSlot > 0) MoveDraggedIcon(Input.mousePosition);
        var stats = StatsManager.Instance;
        if (observedStats != stats)
        {
            if (observedStats) observedStats.OnHealthChanged -= OnHealthChanged;
            observedStats = stats;
            if (observedStats)
            {
                observedHealth = observedStats.Health;
                observedStats.OnHealthChanged += OnHealthChanged;
            }
        }
        if (stats)
        {
            if (stats.Money != lastMoney) { lastMoney = stats.Money; moneyValue.text = ShopMoneyFormatter.Format(stats.Money); }
            if (stats.Points != lastPoints) { lastPoints = stats.Points; pointsValue.text = Format(stats.Points); }
            healthFill.FillAmount = Mathf.Clamp01(stats.Health / Mathf.Max(1f, stats.MaxHealth));
            int currentHealth = Mathf.CeilToInt(stats.Health), maximumHealth = Mathf.CeilToInt(stats.MaxHealth);
            if (currentHealth != lastHealth || maximumHealth != lastMaxHealth)
            {
                lastHealth = currentHealth; lastMaxHealth = maximumHealth;
                healthValue.text = Format(currentHealth) + " / " + Format(maximumHealth);
            }
        }
        damagePulseTime = Mathf.Max(0f, damagePulseTime - Time.unscaledDeltaTime);
        healthFlashTime = Mathf.Max(0f, healthFlashTime - Time.unscaledDeltaTime);
        heartbeatPulseTime = Mathf.Max(0f, heartbeatPulseTime - Time.unscaledDeltaTime);
        float healthFraction = stats ? Mathf.Clamp01(stats.Health / Mathf.Max(1f, stats.MaxHealth)) : 1f;
        if (GameOverPanel.IsOpen || GameVictoryPanel.IsOpen || (stats && stats.Health <= 0f))
        {
            criticalHealth = false;
            damagePulseTime = healthFlashTime = heartbeatPulseTime = 0f;
            lastHeartbeatPlaybackTime = -1f;
            if (damageOverlay) damageOverlay.Strength = 0f;
        }
        else
        {
            criticalHealth = stats && healthFraction <= StatsManager.LowHealthHeartbeatThresholdFraction;
            float critical = criticalHealth ? Mathf.Lerp(.42f, .24f,
                healthFraction / StatsManager.LowHealthHeartbeatThresholdFraction) : 0f;
            UpdateHeartbeatPulse(stats);
            float pulse = damagePulse * Mathf.Pow(damagePulseTime / DamageFadeSeconds, 2f);
            float directStrength = Mathf.Lerp(.48f, .95f, 1f - healthFraction);
            float heartbeat = critical + .5f * Mathf.Max(0f, directStrength - critical) *
                Mathf.Pow(heartbeatPulseTime / HeartbeatFadeSeconds, 2f);
            if (damageOverlay) damageOverlay.Strength = Mathf.Max(critical, pulse, heartbeat) *
                (stats ? Mathf.Clamp01(stats.bloodEdgeIntensity) : 1f) * 2f;
        }
        float flash = healthFlashTime / HealthFlashSeconds;
        if (healthFill) healthFill.FlashAmount = flash;
        if (healthValue) healthValue.color = Color.Lerp(Cream, Color.white, flash);
        if (energy && energy.stats)
        {
            float max = Mathf.Max(1, energy.stats.MaxEnergy);
            energyFill.FillAmount = Mathf.Clamp01(energy.energy / max);
            int current = Mathf.CeilToInt(energy.energy), maximum = Mathf.CeilToInt(max);
            if (current != lastEnergy || maximum != lastMax)
            { lastEnergy = current; lastMax = maximum; energyValue.text = Format(current) + " / " + Format(maximum); }
        }
        // One world unit corresponds to one metre; tile width is 0.5 world units.
        float surface = map && map.Terrain ? map.Terrain.CellToWorld(Vector3Int.up).y : 0;
        DepthMeters = player ? Mathf.Max(0, Mathf.FloorToInt(surface - player.transform.position.y)) : 0;
        if (DepthMeters != lastDepth) { lastDepth = DepthMeters; depthValue.text = Format(DepthMeters) + " m"; }
    }
    void UpdateHeartbeatPulse(StatsManager stats)
    {
        var audio = AudioManager.Instance;
        if (!criticalHealth || !stats || !audio ||
            !audio.TryGetLowHealthHeartbeatPlayback(out float position, out _))
        {
            lastHeartbeatPlaybackTime = -1f;
            heartbeatPulseTime = 0f;
            return;
        }

        float interval = stats.HeartbeatIntervalSeconds;
        float offset = Mathf.Repeat(stats.heartbeatFlashOffsetSeconds, interval);
        bool crossed = lastHeartbeatPlaybackTime < 0f || position < lastHeartbeatPlaybackTime
            ? BeatCrossed(-.0001f, position, interval, offset)
            : BeatCrossed(lastHeartbeatPlaybackTime, position, interval, offset);
        if (crossed) heartbeatPulseTime = HeartbeatFadeSeconds;
        lastHeartbeatPlaybackTime = position;
    }

    static bool BeatCrossed(float previous, float current, float interval, float offset)
        => Mathf.FloorToInt((current - offset) / interval) >
           Mathf.FloorToInt((previous - offset) / interval);

    void OnHealthChanged(float health, float maximum)
    {
        if (health < observedHealth - .001f)
        {
            float lostFraction = 1f - Mathf.Clamp01(health / Mathf.Max(1f, maximum));
            damagePulse = Mathf.Lerp(.48f, .95f, lostFraction);
            damagePulseTime = DamageFadeSeconds;
            healthFlashTime = HealthFlashSeconds;
        }
        observedHealth = health;
    }
    void BuildDamageOverlay()
    {
        var overlay = Rect("Damage Overlay", transform, 0, 0, 0, 0);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlay.SetAsFirstSibling();
        damageOverlay = overlay.gameObject.AddComponent<HealthDamageOverlay>();
        damageOverlay.raycastTarget = false;
    }
    void ActivateHotbarSlot(int index)
    {
        if (index < 1 || index > 8) return;
        var item = slots[index - 1];
        if (item && item.item == Item.Medkit)
        {
            bool inventoryOpen = inventoryPanel && inventoryPanel.IsOpen && !GameOverPanel.IsOpen;
            if (GameplayInputBlocker.IsBlocked && !inventoryOpen) return;
            if (!GameplayInputBlocker.IsBlocked) SelectSlot(index);
            StatsManager.Instance?.TryUseMedkit(item);
            return;
        }
        SelectSlot(index);
    }
    public bool SelectSlot(int index)
    {
        if (GameplayInputBlocker.IsBlocked || index < 0 || index >= 9) return false;
        SelectedSlot = index;
        if (ladder) ladder.SetBuildMode(SelectedItem && (SelectedItem.item == Item.Ladder || SelectedItem.item == Item.IronLadder));
        wasBuilding = ladder && ladder.BuildMode;
        RefreshItems(); return true;
    }
    public void AssignSlot(int index, ItemSO item)
    {
        AssignSlotInternal(index, item, true);
    }
    public void RestoreRunSlots(int[] ids, int selected)
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i] = ids != null && i < ids.Length && ids[i] >= 0 ? StartingResourcesSettings.Resolve(ids[i]) : null;
        SelectedSlot = Mathf.Clamp(selected, 0, 8);
        if (ladder) ladder.SetBuildMode(SelectedItem && (SelectedItem.item == Item.Ladder || SelectedItem.item == Item.IronLadder));
        wasBuilding = ladder && ladder.BuildMode;
        RefreshItems();
    }
    void AssignSlotInternal(int index, ItemSO item, bool persist)
    {
        if (index < 1 || index > 8) throw new ArgumentOutOfRangeException(nameof(index));
        if (item && !IsHotbarItem(item)) throw new ArgumentException("Item cannot be used from the hotbar.", nameof(item));
        slots[index - 1] = item;
        if (SelectedSlot == index && ladder) ladder.SetBuildMode(IsKnownHotbarItem(item) && (item.item == Item.Ladder || item.item == Item.IronLadder));
        if (persist) SaveSlotLayout();
        RefreshItems();
    }
    public bool CanReorderSlot(int index) => !GameplayInputBlocker.IsBlocked && index >= 1 && index <= 8 &&
        IsKnownHotbarItem(slots[index - 1]);
    public static bool IsHotbarItem(ItemSO item) => item &&
        (item.item == Item.Torche || item.item == Item.LavaLamp || item.item == Item.Dynamite || item.item == Item.Ladder || item.item == Item.IronLadder ||
         item.item == Item.BridgePart || item.item == Item.Medkit);
    bool IsKnownHotbarItem(ItemSO item) => IsHotbarItem(item) && InventoryManager.Instance &&
        (InventoryManager.Instance.GetCount(item) > 0 || InventoryManager.Instance.WasOwnedThisRun(item));
    public bool IsOverHotbar(Vector2 screenPosition) => hotbar &&
        RectTransformUtility.RectangleContainsScreenPoint(hotbar, screenPosition, HotbarEventCamera());
    public bool TryGetAssignableSlotAt(Vector2 screenPosition, out int index)
    {
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] && RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)buttons[i].transform, screenPosition, HotbarEventCamera()))
            {
                index = i + 1;
                return true;
            }
        index = -1;
        return false;
    }
    Camera HotbarEventCamera()
    {
        var canvas = GetComponentInParent<Canvas>();
        return canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera : null;
    }
    public bool BeginHotbarDrag(int index, Vector2 screenPosition)
    {
        if (!CanReorderSlot(index)) return false;
        draggedSlot = index;
        if (!dragIcon)
        {
            dragIcon = Image("Dragged Item", transform, 0, 0, 58, 58, null, Color.white);
            dragIcon.rectTransform.anchorMin = dragIcon.rectTransform.anchorMax = dragIcon.rectTransform.pivot = new Vector2(.5f, .5f);
            dragIcon.preserveAspect = true;
        }
        dragIcon.sprite = slots[index - 1].icon;
        dragIcon.color = new Color(1f, 1f, 1f, .9f);
        dragIcon.gameObject.SetActive(true);
        dragIcon.transform.SetAsLastSibling();
        MoveDraggedIcon(screenPosition);
        icons[index - 1].color = new Color(1f, 1f, 1f, .28f);
        return true;
    }
    public void EndHotbarDrag(int sourceIndex, Vector2 screenPosition)
    {
        if (draggedSlot != sourceIndex) { CancelHotbarDrag(); return; }
        var eventData = new PointerEventData(EventSystem.current) { position = screenPosition };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, hits);
        var target = hits.Select(hit => hit.gameObject.GetComponentInParent<HotbarSlotDrag>())
            .FirstOrDefault(slot => slot && slot.hud == this);
        if (target && target.slotIndex != sourceIndex) SwapSlots(sourceIndex, target.slotIndex);
        CancelHotbarDrag();
    }
    public void CancelHotbarDrag()
    {
        draggedSlot = -1;
        if (dragIcon) dragIcon.gameObject.SetActive(false);
        RefreshItems();
    }
    void SwapSlots(int first, int second)
    {
        if (first < 1 || first > 8 || second < 1 || second > 8 || first == second) return;
        (slots[first - 1], slots[second - 1]) = (slots[second - 1], slots[first - 1]);
        if (SelectedSlot == first) SelectedSlot = second;
        else if (SelectedSlot == second) SelectedSlot = first;
        if (ladder) ladder.SetBuildMode(SelectedItem && (SelectedItem.item == Item.Ladder || SelectedItem.item == Item.IronLadder));
        wasBuilding = ladder && ladder.BuildMode;
        SaveSlotLayout();
        RefreshItems();
    }
    void MoveDraggedIcon(Vector2 screenPosition)
    {
        if (!dragIcon || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
            screenPosition, null, out var local)) return;
        dragIcon.rectTransform.anchoredPosition = local;
    }
    const string SlotLayoutVersion = "CompactHud.SlotLayoutVersion";
    const string SlotLayoutPrefix = "CompactHud.Slot.";
    public void AddStartingResourcesToSlots()
    {
        bool changed = false;
        foreach (var resource in StartingResourcesSettings.Load().items)
        {
            if (resource.amount <= 0) continue;
            var item = StartingResourcesSettings.Resolve(resource.itemId);
            if (!IsHotbarItem(item) || Array.Exists(slots, slot => slot && slot.item == item.item)) continue;
            int freeSlot = Array.FindIndex(slots, slot => !slot);
            if (freeSlot < 0) break;
            slots[freeSlot] = item;
            changed = true;
        }
        if (changed) SaveSlotLayout();
    }
    void LoadSlotLayout()
    {
        if (PlayerPrefs.GetInt(SlotLayoutVersion, 0) != 1) return;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!catalog) return;
        for (int i = 0; i < slots.Length; i++)
        {
            int id = PlayerPrefs.GetInt(SlotLayoutPrefix + i, int.MinValue);
            slots[i] = id == int.MinValue ? null : Array.Find(catalog.items,
                item => item && (int)item.item == id && IsHotbarItem(item));
        }
    }
    bool RemoveIneligibleSlots()
    {
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] && !IsHotbarItem(slots[i])) { slots[i] = null; changed = true; }
        return changed;
    }
    void SaveSlotLayout()
    {
        for (int i = 0; i < slots.Length; i++)
            PlayerPrefs.SetInt(SlotLayoutPrefix + i, IsHotbarItem(slots[i]) ? (int)slots[i].item : int.MinValue);
        PlayerPrefs.SetInt(SlotLayoutVersion, 1);
        PlayerPrefs.Save();
    }
    static string Format(int value) => value < 1000000 ? value.ToString("N0", German) : ShopMoneyFormatter.Format(value);
    void RefreshItems()
    {
        if (SelectedSlot > 0 && !IsKnownHotbarItem(slots[SelectedSlot - 1]))
        {
            SelectedSlot = 0;
            if (ladder) ladder.SetBuildMode(false);
            wasBuilding = false;
        }
        selections[0].enabled = false;
        GoldButtonFeedback.Select(selections[0].transform.parent.GetComponent<Image>(), SelectedSlot == 0);
        var equippedPickaxe = InventoryManager.Instance ? InventoryManager.Instance.EquippedPickaxe : null;
        if (pickaxeIcon)
        {
            pickaxeIcon.sprite = equippedPickaxe && equippedPickaxe.icon ? equippedPickaxe.icon : pickaxeSprite;
            var pickaxeRect = pickaxeIcon.rectTransform;
            pickaxeRect.anchorMin = pickaxeRect.anchorMax = pickaxeRect.pivot = new Vector2(.5f, .5f);
            pickaxeRect.anchoredPosition = new Vector2(0f, HotbarIconVerticalOffset);
        }
        for (int i = 0; i < 8; i++)
        {
            if (!icons[i]) continue;
            var item = IsKnownHotbarItem(slots[i]) ? slots[i] : null;
            int amount = item ? InventoryManager.Instance.GetCount(item) : 0;
            icons[i].sprite = item ? item.icon : null; icons[i].enabled = item;
            ApplyHotbarIconLayout(item, icons[i].rectTransform);
            icons[i].color = amount > 0 ? Color.white : new Color(1f, 1f, 1f, .3f);
            counts[i].text = item ? ShopMoneyFormatter.Format(amount) : "";
            var badge = (RectTransform)counts[i].transform.parent;
            badge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Ceil(counts[i].GetPreferredValues(counts[i].text).x) + 8f);
            counts[i].transform.parent.gameObject.SetActive(item);
            selections[i + 1].enabled = false;
            GoldButtonFeedback.Select(selections[i + 1].transform.parent.GetComponent<Image>(), SelectedSlot == i + 1);
        }
    }
    public void RefreshHotbarIconLayouts() => RefreshItems();
    public void SetInventoryHotbarVisible(bool visible)
    {
        if (!hotbar) return;
        if (visible && hotbarOverlayParent && hotbar.parent != hotbarOverlayParent)
        {
            hotbar.SetParent(hotbarOverlayParent, false);
            hotbar.SetAsLastSibling();
            Fit();
        }
        else if (!visible && hotbar.parent != transform)
        {
            hotbar.SetParent(transform, false);
            Fit();
        }
    }
    void ApplyHotbarIconLayout(ItemSO item, RectTransform icon)
    {
        var recipe = item && workbench ? workbench.FindRecipeForOutput(item) : null;
        var settings = recipe ? recipe.CardIconLayout :
            new CraftingRecipe.RecipeIconLayout { scale = Vector2.one };
        icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(.5f, .5f);
        icon.anchoredPosition = new Vector2(settings.offset.x * (48f / 134f),
            settings.offset.y * (48f / 106f) + HotbarIconVerticalOffset);
        icon.localScale = new Vector3(settings.scale.x * (settings.flipX ? -1f : 1f),
            settings.scale.y * (settings.flipY ? -1f : 1f), 1f);
    }
    void OnRectTransformDimensionsChange() => Fit();
    void Fit()
    {
        if (!top || !hotbar) return;
        var rect = ((RectTransform)transform).rect;
        float scale = Mathf.Min(rect.width / 1920f, rect.height / 1080f);
        hotbar.localScale = Vector3.one * scale;
        // Match the approved compact HUD to the hotbar width, preserving its artwork proportions.
        top.localScale = Vector3.one * (scale * hotbar.rect.width / top.rect.width);
        top.anchoredPosition = new Vector2(0, -12 * scale);
        hotbar.anchoredPosition = new Vector2(0, 16 * scale);
    }
    RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false); r.gameObject.layer = gameObject.layer;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(w,h); return r;
    }
    Image Image(string name, Transform parent, float x, float y, float w, float h, Sprite sprite, Color color)
    {
        var im = Rect(name,parent,x,y,w,h).gameObject.AddComponent<Image>();
        im.sprite = sprite; im.color = color; im.raycastTarget = false;
        if (sprite && sprite.border.sqrMagnitude > 0) im.type = UnityEngine.UI.Image.Type.Sliced;
        return im;
    }
    TextMeshProUGUI Text(string name, Transform parent, string value, float x, float y, float w, float h, float size, TextAlignmentOptions align = TextAlignmentOptions.Midline)
    {
        var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        t.font=font; t.fontSharedMaterial=fontMaterial; t.fontStyle=FontStyles.Bold; t.color=Cream;
        t.text=value; t.fontSize=size; t.alignment=align; t.textWrappingMode=TextWrappingModes.NoWrap; t.raycastTarget=false;
        t.enableAutoSizing=true; t.fontSizeMin=size*.72f; t.fontSizeMax=size;
        return t;
    }
    HudGemBar Bar(string name, float y, Sprite icon, Color tint, string value, out TextMeshProUGUI text)
    {
        var symbol=Image(name+" Icon",top,20,y+2,36,36,icon,Color.white); symbol.preserveAspect=true;
        var fill=Rect(name+" Fill",top,64,y,378,38).gameObject.AddComponent<HudGemBar>();
        fill.GemColor=tint;
        fill.DecorativeEndCaps=false;
        fill.raycastTarget=false;
        text=Text(name+" Value",top,value,330,y,92,24,14,TextAlignmentOptions.MidlineRight);
        text.gameObject.SetActive(false);
        return fill;
    }
    void StatusModule(string name, float x, float width)
    {
        var source=HomeUi.Sprite("SelectionCardNormal");
        if (!statusFrame)
        {
            var rect=source.rect; rect.x+=32; rect.y+=32; rect.width-=64; rect.height-=64;
            statusFrame=Sprite.Create(source.texture,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(160,160,160,160));
            statusFrame.name="HUD Protected Corners";
        }
        var sprite=statusFrame;
        var panel=Image(name,top,x,0,width,116,sprite,Color.white);
        panel.type=UnityEngine.UI.Image.Type.Sliced;
        panel.pixelsPerUnitMultiplier=sprite.rect.height/80f;
        panel.raycastTarget=true;
    }
    TextMeshProUGUI StatusText(string name, string value, float x, float y, float width, float height, float size)
    {
        var text=Text(name,top,value,x,y,width,height,size,TextAlignmentOptions.Center);
        var titleFont=Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if(titleFont) { text.font=titleFont; text.fontSharedMaterial=titleFont.material; }
        text.fontStyle=FontStyles.Normal;
        return text;
    }
    void Build()
    {
        top=Rect("Status Strip",transform,0,12,971.2f,116); top.anchorMin=top.anchorMax=top.pivot=new Vector2(.5f,1);
        StatusModule("Vitals Module",0,462);
        StatusModule("Money Module",468,198);
        StatusModule("Points Module",672,159);
        StatusModule("Depth Module",837,134.2f);
        healthFill=Bar("Health",16,heartSprite,new Color(.92f,.055f,.075f),"100 / 100",out healthValue);
        energyFill=Bar("Energy",62,boltSprite,new Color(1,.68f,.035f),"",out energyValue);
        var coin=Image("Coin",top,484,29,58,58,coinSprite,Color.white); coin.preserveAspect=true;
        moneyValue=StatusText("Money","",549,27,104,62,36);
        StatusText("Points Label","Punkte",682,19,139,32,27);
        pointsValue=StatusText("Points","",682,49,139,48,37);
        StatusText("Depth Label","Tiefe",847,19,114.2f,32,27);
        depthValue=StatusText("Depth","",847,49,114.2f,48,37);
        hotbar=Rect("Hotbar",transform,0,0,658,66); hotbar.anchorMin=hotbar.anchorMax=hotbar.pivot=new Vector2(.5f,0);
        var pickaxe=Image("Slot 1",hotbar,0,0,66,66,slotSprite,Color.white); pickaxe.raycastTarget=true;
        var pickaxeButton=pickaxe.gameObject.AddComponent<Button>(); pickaxeButton.targetGraphic=pickaxe;
        GoldButtonFeedback.Apply(pickaxeButton, slotSprite, selectedSprite);
        pickaxeButton.navigation=new Navigation {mode=Navigation.Mode.None}; pickaxeButton.onClick.AddListener(()=>SelectSlot(0));
        selections[0]=Image("Selection",pickaxe.transform,0,0,66,66,selectedSprite,Color.white);
        var pickaxeImage=Image("Pickaxe",pickaxe.transform,9,9,48,48,pickaxeSprite,Color.white); pickaxeImage.preserveAspect=true;
        pickaxeIcon=pickaxeImage;
        for(int i=0;i<8;i++)
        {
            int index=i+1; var bg=Image("Slot "+(i+2),hotbar,86+i*72,0,66,66,slotSprite,Color.white); bg.raycastTarget=true;
            buttons[i]=bg.gameObject.AddComponent<Button>(); buttons[i].targetGraphic=bg;
            GoldButtonFeedback.Apply(buttons[i], slotSprite, selectedSprite);
            buttons[i].navigation=new Navigation {mode=Navigation.Mode.None};
            slotDrags[i] = bg.gameObject.AddComponent<HotbarSlotDrag>();
            slotDrags[i].hud = this; slotDrags[i].slotIndex = index;
            int slot = i;
            buttons[i].onClick.AddListener(()=> { if (!slotDrags[slot].ConsumeClick()) ActivateHotbarSlot(index); });
            selections[i+1]=Image("Selection",bg.transform,0,0,66,66,selectedSprite,Color.white);
            icons[i]=Image("Item",bg.transform,9,9,48,48,null,Color.white); icons[i].preserveAspect=true;
            Text("Key",bg.transform,(i+1).ToString(),7,5,15,17,15);
            var badge=Image("Count Badge",bg.transform,20,46,40,16,badgeSprite,Color.white);
            badge.rectTransform.anchorMin=badge.rectTransform.anchorMax=badge.rectTransform.pivot=new Vector2(1,0);
            badge.rectTransform.anchoredPosition=new Vector2(-6,4);
            counts[i]=Text("Count",badge.transform,"",0,0,32,16,13,TextAlignmentOptions.Midline);
            counts[i].enableAutoSizing=false;
            counts[i].rectTransform.anchorMin=Vector2.zero;
            counts[i].rectTransform.anchorMax=Vector2.one;
            counts[i].rectTransform.offsetMin=new Vector2(4,0);
            counts[i].rectTransform.offsetMax=new Vector2(-4,0);
        }
        Fit();
    }
}
