using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Variant A: a shallow status strip, a fixed pickaxe slot and eight item slots.
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
        IsHotbarItem(slots[SelectedSlot - 1]) ? slots[SelectedSlot - 1] : null;
    public int DepthMeters { get; private set; }
    const string HotbarIconVerticalOffsetKey = "workbench.hotbarIconVerticalOffset";
    public static float HotbarIconVerticalOffset
    {
        get => PlayerPrefs.GetFloat(HotbarIconVerticalOffsetKey, 0f);
        set
        {
            float next = Mathf.Clamp(value, -48f, 48f);
            if (Mathf.Approximately(next, HotbarIconVerticalOffset)) return;
            PlayerPrefs.SetFloat(HotbarIconVerticalOffsetKey, next);
            PlayerPrefs.Save();
            UnityEngine.Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
        }
    }
    RectTransform top, hotbar;
    CanvasGroup visibility;
    HudGemBar energyFill;
    TextMeshProUGUI energyValue, moneyValue, pointsValue, depthValue;
    readonly Image[] icons = new Image[8], selections = new Image[9];
    readonly TextMeshProUGUI[] counts = new TextMeshProUGUI[8];
    readonly Button[] buttons = new Button[8];
    readonly HotbarSlotDrag[] slotDrags = new HotbarSlotDrag[8];
    InventoryManager inventory;
    PlayerLadder ladder;
    WorkbenchPanel workbench;
    Image dragIcon;
    RectTransform pickaxeIcon;
    int draggedSlot = -1;
    bool wasBuilding;
    int lastMoney = int.MinValue, lastPoints = int.MinValue, lastDepth = int.MinValue, lastEnergy = int.MinValue, lastMax = int.MinValue;
    static readonly Color Cream = new Color32(255,245,229,255);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    void Awake()
    {
        if (slots == null) slots = new ItemSO[8];
        Array.Resize(ref slots, 8);
        LoadSlotLayout();
        if (RemoveIneligibleSlots()) SaveSlotLayout();
        visibility = gameObject.AddComponent<CanvasGroup>();
        ladder = player ? player.GetComponent<PlayerLadder>() : null;
        workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        Build(); RefreshItems();
    }
    void OnDisable()
    {
        if (inventory) inventory.OnInventoryChanged -= RefreshItems;
        inventory = null;
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
        bool blocked = GameplayInputBlocker.IsBlocked;
        visibility.alpha = blocked ? 0 : 1;
        visibility.blocksRaycasts = visibility.interactable = !blocked;
        if (blocked) return;
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (selected && selected.GetComponent<TMP_InputField>()) return;
        for (int i = 0; i < 8; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i))) SelectSlot(i + 1);
        if (ladder && ladder.BuildMode != wasBuilding)
        {
            wasBuilding = ladder.BuildMode;
            if (wasBuilding)
                for (int i = 0; i < 8; i++) if (slots[i] && slots[i].item == Item.Ladder) { SelectedSlot = i + 1; RefreshItems(); break; }
        }
    }
    void LateUpdate()
    {
        if (draggedSlot > 0) MoveDraggedIcon(Input.mousePosition);
        var stats = StatsManager.Instance;
        if (stats)
        {
            if (stats.Money != lastMoney) { lastMoney = stats.Money; moneyValue.text = Format(stats.Money); }
            if (stats.Points != lastPoints) { lastPoints = stats.Points; pointsValue.text = "Punkte  " + Format(stats.Points); }
        }
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
        if (DepthMeters != lastDepth) { lastDepth = DepthMeters; depthValue.text = "Tiefe  " + Format(DepthMeters) + " m"; }
    }
    public bool SelectSlot(int index)
    {
        if (GameplayInputBlocker.IsBlocked || index < 0 || index >= 9) return false;
        SelectedSlot = index;
        if (ladder) ladder.SetBuildMode(SelectedItem && SelectedItem.item == Item.Ladder);
        wasBuilding = ladder && ladder.BuildMode;
        RefreshItems(); return true;
    }
    public void AssignSlot(int index, ItemSO item)
    {
        if (index < 1 || index > 8) throw new ArgumentOutOfRangeException(nameof(index));
        if (item && !IsHotbarItem(item)) throw new ArgumentException("Only tools and consumables can be assigned to the hotbar.", nameof(item));
        slots[index - 1] = item;
        if (SelectedSlot == index && ladder) ladder.SetBuildMode(item && item.item == Item.Ladder);
        RefreshItems();
    }
    public bool CanReorderSlot(int index) => !GameplayInputBlocker.IsBlocked && index >= 1 && index <= 8 &&
        IsHotbarItem(slots[index - 1]);
    public static bool IsHotbarItem(ItemSO item) => item &&
        (item.category == ItemCategory.Tool || item.category == ItemCategory.Consumable);
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
        if (ladder) ladder.SetBuildMode(SelectedItem && SelectedItem.item == Item.Ladder);
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
        selections[0].enabled = true;
        if (pickaxeIcon)
        {
            pickaxeIcon.anchorMin = pickaxeIcon.anchorMax = pickaxeIcon.pivot = new Vector2(.5f, .5f);
            pickaxeIcon.anchoredPosition = new Vector2(0f, HotbarIconVerticalOffset);
        }
        for (int i = 0; i < 8; i++)
        {
            if (!icons[i]) continue;
            var item = IsHotbarItem(slots[i]) ? slots[i] : null;
            int amount = item && inventory ? inventory.GetCount(item) : 0;
            icons[i].sprite = item ? item.icon : null; icons[i].enabled = item;
            ApplyHotbarIconLayout(item, icons[i].rectTransform);
            icons[i].color = amount > 0 ? Color.white : new Color(1,1,1,.3f);
            counts[i].text = item ? ShopMoneyFormatter.Format(amount) : "";
            var badge = (RectTransform)counts[i].transform.parent;
            badge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Ceil(counts[i].GetPreferredValues(counts[i].text).x) + 8f);
            counts[i].transform.parent.gameObject.SetActive(item);
            selections[i + 1].enabled = SelectedSlot == i + 1;
        }
    }
    public void RefreshHotbarIconLayouts() => RefreshItems();
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
        top.localScale = hotbar.localScale = Vector3.one * scale;
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
        var symbol=Image(name+" Icon",top,20,y-2,20,20,icon,Color.white); symbol.preserveAspect=true;
        var fill=Rect(name+" Fill",top,51,y,223.6f,17).gameObject.AddComponent<HudGemBar>();
        fill.GemColor=tint;
        fill.raycastTarget=false;
        text=Text(name+" Value",top,value,279.4f,y-2,79.3f,21,13,TextAlignmentOptions.MidlineRight);
        return fill;
    }
    void Build()
    {
        top=Rect("Status Strip",transform,0,12,971.2f,56); top.anchorMin=top.anchorMax=top.pivot=new Vector2(.5f,1);
        Image("Wood Frame",top,0,0,971.2f,56,stripSprite,Color.white).raycastTarget=true;
        Bar("Health",11,heartSprite,new Color(.92f,.055f,.075f),"100 / 100",out _);
        energyFill=Bar("Energy",30,boltSprite,new Color(1,.68f,.035f),"",out energyValue);
        foreach(float x in new[]{395.2f,577.2f,781.2f}) Image("Divider",top,x,12,2,32,null,new Color(.68f,.43f,.22f,.7f));
        var coin=Image("Coin",top,423.2f,14,28,28,coinSprite,Color.white); coin.preserveAspect=true;
        moneyValue=Text("Money",top,"",459.2f,8,100,40,21);
        pointsValue=Text("Points",top,"",593.2f,8,172,40,20);
        depthValue=Text("Depth",top,"",795.2f,8,154,40,20);
        hotbar=Rect("Hotbar",transform,0,0,658,66); hotbar.anchorMin=hotbar.anchorMax=hotbar.pivot=new Vector2(.5f,0);
        var pickaxe=Image("Slot 1",hotbar,0,0,66,66,slotSprite,Color.white); pickaxe.raycastTarget=true;
        var pickaxeButton=pickaxe.gameObject.AddComponent<Button>(); pickaxeButton.targetGraphic=pickaxe;
        pickaxeButton.navigation=new Navigation {mode=Navigation.Mode.None}; pickaxeButton.onClick.AddListener(()=>SelectSlot(0));
        selections[0]=Image("Selection",pickaxe.transform,0,0,66,66,selectedSprite,Color.white);
        var pickaxeImage=Image("Pickaxe",pickaxe.transform,9,9,48,48,pickaxeSprite,Color.white); pickaxeImage.preserveAspect=true;
        pickaxeIcon=pickaxeImage.rectTransform;
        for(int i=0;i<8;i++)
        {
            int index=i+1; var bg=Image("Slot "+(i+2),hotbar,86+i*72,0,66,66,slotSprite,Color.white); bg.raycastTarget=true;
            buttons[i]=bg.gameObject.AddComponent<Button>(); buttons[i].targetGraphic=bg;
            buttons[i].navigation=new Navigation {mode=Navigation.Mode.None};
            slotDrags[i] = bg.gameObject.AddComponent<HotbarSlotDrag>();
            slotDrags[i].hud = this; slotDrags[i].slotIndex = index;
            int slot = i;
            buttons[i].onClick.AddListener(()=> { if (!slotDrags[slot].ConsumeClick()) SelectSlot(index); });
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
