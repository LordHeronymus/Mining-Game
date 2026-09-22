using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Variant A: a shallow status strip and eight independent item slots.
public sealed class CompactHud : MonoBehaviour
{
    public Sprite stripSprite, slotSprite, selectedSprite, badgeSprite, barSprite, heartSprite, boltSprite, coinSprite;
    public TMP_FontAsset font;
    public Material fontMaterial;
    public EnergyManager energy;
    public PlayerMovement player;
    public MapGenerator map;
    public ItemSO[] slots = new ItemSO[8];
    public int SelectedSlot { get; private set; }
    public ItemSO SelectedItem => slots != null && SelectedSlot < slots.Length ? slots[SelectedSlot] : null;
    public int DepthMeters { get; private set; }
    RectTransform top, hotbar;
    CanvasGroup visibility;
    Image energyFill;
    TextMeshProUGUI energyValue, moneyValue, pointsValue, depthValue;
    readonly Image[] icons = new Image[8], selections = new Image[8];
    readonly TextMeshProUGUI[] counts = new TextMeshProUGUI[8];
    readonly Button[] buttons = new Button[8];
    InventoryManager inventory;
    PlayerLadder ladder;
    bool wasBuilding;
    int lastMoney = int.MinValue, lastPoints = int.MinValue, lastDepth = int.MinValue, lastEnergy = int.MinValue, lastMax = int.MinValue;
    static readonly Color Cream = new Color32(255,245,229,255);
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    void Awake()
    {
        if (slots == null) slots = new ItemSO[8];
        Array.Resize(ref slots, 8);
        visibility = gameObject.AddComponent<CanvasGroup>();
        ladder = player ? player.GetComponent<PlayerLadder>() : null;
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
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i))) SelectSlot(i);
        if (ladder && ladder.BuildMode != wasBuilding)
        {
            wasBuilding = ladder.BuildMode;
            if (wasBuilding)
                for (int i = 0; i < 8; i++) if (slots[i] && slots[i].item == Item.Ladder) { SelectedSlot = i; RefreshItems(); break; }
        }
    }
    void LateUpdate()
    {
        var stats = StatsManager.Instance;
        if (stats)
        {
            if (stats.Money != lastMoney) { lastMoney = stats.Money; moneyValue.text = Format(stats.Money); }
            if (stats.Points != lastPoints) { lastPoints = stats.Points; pointsValue.text = "Punkte  " + Format(stats.Points); }
        }
        if (energy && energy.stats)
        {
            float max = Mathf.Max(1, energy.stats.MaxEnergy);
            energyFill.fillAmount = Mathf.Clamp01(energy.energy / max);
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
        if (GameplayInputBlocker.IsBlocked || index < 0 || index >= 8) return false;
        SelectedSlot = index;
        if (ladder) ladder.SetBuildMode(SelectedItem && SelectedItem.item == Item.Ladder);
        wasBuilding = ladder && ladder.BuildMode;
        RefreshItems(); return true;
    }
    public void AssignSlot(int index, ItemSO item)
    {
        if (index < 0 || index >= 8) throw new ArgumentOutOfRangeException(nameof(index));
        slots[index] = item;
        if (SelectedSlot == index && ladder) ladder.SetBuildMode(item && item.item == Item.Ladder);
        RefreshItems();
    }
    static string Format(int value) => value < 1000000 ? value.ToString("N0", German) : ShopMoneyFormatter.Format(value);
    void RefreshItems()
    {
        for (int i = 0; i < 8; i++)
        {
            if (!icons[i]) continue;
            var item = slots[i]; int amount = item && inventory ? inventory.GetCount(item) : 0;
            icons[i].sprite = item ? item.icon : null; icons[i].enabled = item;
            icons[i].color = amount > 0 ? Color.white : new Color(1,1,1,.3f);
            counts[i].text = item ? ShopMoneyFormatter.Format(amount) : "";
            var badge = (RectTransform)counts[i].transform.parent;
            badge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Ceil(counts[i].GetPreferredValues(counts[i].text).x) + 8f);
            counts[i].transform.parent.gameObject.SetActive(item);
            selections[i].enabled = SelectedSlot == i;
        }
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
    Image Bar(string name, float y, Sprite icon, Color tint, string value, out TextMeshProUGUI text)
    {
        var symbol=Image(name+" Icon",top,20,y-2,20,20,icon,Color.white); symbol.preserveAspect=true;
        Image(name+" Track",top,48,y,232,17,badgeSprite,new Color(.32f,.25f,.2f));
        var fill=Image(name+" Fill",top,51,y+3,226,11,barSprite,tint);
        fill.type=UnityEngine.UI.Image.Type.Filled; fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
        text=Text(name+" Value",top,value,150,y-2,120,21,14,TextAlignmentOptions.MidlineRight);
        return fill;
    }
    void Build()
    {
        top=Rect("Status Strip",transform,0,12,880,56); top.anchorMin=top.anchorMax=top.pivot=new Vector2(.5f,1);
        Image("Wood Frame",top,0,0,880,56,stripSprite,Color.white).raycastTarget=true;
        Bar("Health",11,heartSprite,new Color(.92f,.055f,.075f),"100 / 100",out _);
        energyFill=Bar("Energy",30,boltSprite,new Color(1,.68f,.035f),"",out energyValue);
        foreach(float x in new[]{304f,486f,690f}) Image("Divider",top,x,12,2,32,null,new Color(.68f,.43f,.22f,.7f));
        var coin=Image("Coin",top,332,14,28,28,coinSprite,Color.white); coin.preserveAspect=true;
        moneyValue=Text("Money",top,"",368,8,100,40,21);
        pointsValue=Text("Points",top,"",502,8,172,40,20);
        depthValue=Text("Depth",top,"",704,8,154,40,20);
        hotbar=Rect("Hotbar",transform,0,0,570,66); hotbar.anchorMin=hotbar.anchorMax=hotbar.pivot=new Vector2(.5f,0);
        for(int i=0;i<8;i++)
        {
            int index=i; var bg=Image("Slot "+(i+1),hotbar,i*72,0,66,66,slotSprite,Color.white); bg.raycastTarget=true;
            buttons[i]=bg.gameObject.AddComponent<Button>(); buttons[i].targetGraphic=bg;
            buttons[i].navigation=new Navigation {mode=Navigation.Mode.None}; buttons[i].onClick.AddListener(()=>SelectSlot(index));
            selections[i]=Image("Selection",bg.transform,0,0,66,66,selectedSprite,Color.white);
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
