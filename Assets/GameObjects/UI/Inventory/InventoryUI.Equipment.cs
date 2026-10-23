using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Inventory tab "Ausrüstung": everything that is equipped or unlocked, in the shop style
// (cards on the left like the ore list, detail with blueprint and stat boxes on the right).
public partial class InventoryUI
{
    public const int EquipmentFilter = 4;
    const float EquipmentCardWidth = 344, EquipmentCardHeight = 176;
    static readonly Color EquipmentMuted = new Color32(224, 207, 187, 255);
    static readonly Color EquipmentGold = new Color32(255, 199, 70, 255);
    static readonly Color Missing = new Color32(150, 132, 112, 255);
    static readonly Color MissingIcon = new Color(.38f, .38f, .38f, .55f);

    RectTransform equipmentRoot;
    readonly List<EquipmentCard> equipmentCards = new();
    List<EquipmentOverview.Entry> equipmentEntries = new();
    Image detailBlueprint, detailIcon;
    TextMeshProUGUI detailName, detailArea, detailNote;
    readonly TextMeshProUGUI[] detailStatLabels = new TextMeshProUGUI[3], detailStatValues = new TextMeshProUGUI[3];
    TMP_FontAsset titleFont;
    Material titleMaterial;
    Sprite shopFrame;
    public int SelectedEquipment { get; private set; }
    public IReadOnlyList<EquipmentOverview.Entry> EquipmentEntries => equipmentEntries;

    sealed class EquipmentCard
    {
        public Image background, icon, badge;
        public TextMeshProUGUI name, area, badgeText;
        public WorkbenchGlyph check;
    }

    public void SelectEquipment(int index)
    {
        SelectedEquipment = Mathf.Clamp(index, 0, Mathf.Max(0, equipmentEntries.Count - 1));
        RefreshEquipment();
    }

    // Called from OnDestroy (InventoryUI.Delete.cs).
    void DestroyEquipmentAssets()
    {
        if (titleMaterial) Destroy(titleMaterial);
        if (shopFrame) Destroy(shopFrame);
    }

    void BuildEquipmentView()
    {
        titleFont = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont");
        if (titleFont)
        {
            titleMaterial = new Material(titleFont.material);
            titleMaterial.SetFloat("_OutlineWidth", .065f);
            titleMaterial.SetColor("_OutlineColor", new Color(.10f, .055f, .025f, 1));
        }
        // Same wooden frame as the shop cards, storage panels and stat boxes (Shop/BuyFrames-v2, "card" region).
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames-v2");
        if (sheet)
        {
            float sx = sheet.width / 1280f, sy = sheet.height / 1280f;
            shopFrame = Sprite.Create(sheet, new UnityEngine.Rect(40 * sx, (1280 - 110 - 325) * sy, 1175 * sx, 325 * sy),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 80 * sy, 100 * sx, 80 * sy));
            shopFrame.name = "Equipment Frame";
        }
        equipmentRoot = Rect("Equipment", layout, 222, 237, 1230, 613);
        for (int i = 0; i < 6; i++) equipmentCards.Add(CreateEquipmentCard(i));

        var detail = Frame("Equipment Detail", equipmentRoot, 738, 1, 492, 612, 5);
        detailBlueprint = Panel("Blueprint", detail.transform, 96, 16, 300, 248, null);
        detailBlueprint.preserveAspect = true;
        CenterPivot(detailBlueprint.rectTransform);
        detailIcon = Panel("Item Icon", detail.transform, 126, 50, 240, 180, null);
        detailIcon.preserveAspect = true;
        CenterPivot(detailIcon.rectTransform);
        detailName = Serif(detail.transform, "", 0, 270, 492, 52, 38, TextAlignmentOptions.Midline);
        detailArea = Serif(detail.transform, "", 0, 316, 492, 30, 22, TextAlignmentOptions.Midline);
        detailArea.color = EquipmentMuted;
        var line = Panel("Divider", detail.transform, 40, 353, 412, 2, null);
        line.color = new Color(EquipmentGold.r, EquipmentGold.g, EquipmentGold.b, .65f);
        for (int i = 0; i < 3; i++)
        {
            var box = Frame("Stat " + i, detail.transform, 26 + i * 150, 374, 140, 96, 5);
            detailStatLabels[i] = Serif(box.transform, "", 0, 10, 140, 32, 22, TextAlignmentOptions.Midline);
            detailStatLabels[i].color = EquipmentMuted;
            detailStatValues[i] = Serif(box.transform, "", 0, 44, 140, 42, 28, TextAlignmentOptions.Midline);
        }
        var plate = Panel("Next", detail.transform, 32, 498, 428, 66, HomeUi.Sprite("SaveButton"));
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = plate.sprite ? plate.sprite.rect.height / 66f : 1;
        detailNote = Serif(plate.transform, "", 60, 0, 308, 66, 26, TextAlignmentOptions.Midline);
        detailNote.enableAutoSizing = true; detailNote.fontSizeMin = 16; detailNote.fontSizeMax = 26;
        equipmentRoot.gameObject.SetActive(false);
    }

    EquipmentCard CreateEquipmentCard(int index)
    {
        float x = 8 + index % 2 * (EquipmentCardWidth + 20), y = 9 + index / 2 * (EquipmentCardHeight + 22);
        var button = Button("Equipment " + index, equipmentRoot, "", x, y, EquipmentCardWidth, EquipmentCardHeight,
            () => SelectEquipment(index));
        var card = new EquipmentCard { background = button.GetComponent<Image>() };
        var iconSize = ItemIconLayout.CellIconSize(new Vector2(176, 176));
        card.icon = Panel("Icon", button.transform, 0, 0, iconSize.x, iconSize.y, null);
        card.icon.preserveAspect = true;
        // Names end well inside the gold rim (card frame inset about 26 units).
        card.name = Serif(button.transform, "", 152, 30, 150, 42, 26);
        card.name.enableAutoSizing = true; card.name.fontSizeMin = 16; card.name.fontSizeMax = 26;
        card.area = Serif(button.transform, "", 152, 70, 150, 32, 20);
        card.badge = Frame("Badge", button.transform, EquipmentCardWidth - 106, EquipmentCardHeight - 52, 92, 38, 10);
        card.badgeText = Serif(card.badge.transform, "", 0, 0, 92, 38, 21, TextAlignmentOptions.Midline);
        card.check = Rect("Check", card.badge.transform, 31, 5, 30, 28).gameObject.AddComponent<WorkbenchGlyph>();
        card.check.shape = WorkbenchGlyph.Shape.Check; card.check.filled = true; card.check.color = EquipmentGold; card.check.raycastTarget = false;
        return card;
    }

    void RefreshEquipment()
    {
        if (!equipmentRoot) return;
        equipmentEntries = EquipmentOverview.Build(inventory);
        SelectedEquipment = Mathf.Clamp(SelectedEquipment, 0, Mathf.Max(0, equipmentEntries.Count - 1));
        for (int i = 0; i < equipmentCards.Count; i++)
        {
            var card = equipmentCards[i];
            var entry = i < equipmentEntries.Count ? equipmentEntries[i] : null;
            card.background.gameObject.SetActive(entry != null);
            if (entry == null) continue;
            GoldButtonFeedback.Select(card.background, i == SelectedEquipment);
            var shown = entry.Shown;
            card.icon.sprite = shown ? shown.icon : null;
            card.icon.enabled = card.icon.sprite;
            ItemIconLayout.Apply(shown, card.icon.rectTransform, new Vector2(82, -EquipmentCardHeight / 2));
            card.icon.color = entry.owned ? Color.white : MissingIcon;
            card.name.text = entry.name;
            card.name.color = entry.owned ? Cream : Missing;
            card.area.text = entry.area;
            card.area.color = entry.owned ? EquipmentMuted : Missing;
            bool check = entry.badge == EquipmentOverview.Check;
            card.check.gameObject.SetActive(check);
            card.badgeText.text = check ? "" : entry.badge;
            card.badgeText.color = entry.owned ? EquipmentGold : Missing;
            float width = check || entry.badge.Length <= 2 ? 50 : 92;
            card.badge.rectTransform.sizeDelta = new Vector2(width, 38);
            card.badge.rectTransform.anchoredPosition = new Vector2(EquipmentCardWidth - 14 - width, -(EquipmentCardHeight - 52));
            card.badgeText.rectTransform.sizeDelta = new Vector2(width, 38);
            card.check.rectTransform.anchoredPosition = new Vector2((width - 30) / 2, -5);
        }
        var selected = SelectedEquipment < equipmentEntries.Count ? equipmentEntries[SelectedEquipment] : null;
        var blueprint = selected != null && selected.owned && selected.item
            ? Resources.Load<Sprite>("Shop/" + selected.item.item + "Recipe") : null;
        detailBlueprint.sprite = blueprint;
        detailBlueprint.enabled = blueprint;
        var shownItem = selected?.Shown;
        detailIcon.sprite = !blueprint && shownItem ? shownItem.icon : null;
        detailIcon.enabled = detailIcon.sprite;
        detailIcon.color = selected != null && selected.owned ? Color.white : MissingIcon;
        detailName.text = selected?.name ?? "";
        detailArea.text = selected?.area ?? "";
        for (int i = 0; i < 3; i++)
        {
            bool has = selected != null && i < selected.stats.Length;
            detailStatLabels[i].transform.parent.gameObject.SetActive(has);
            detailStatLabels[i].text = has ? selected.stats[i].label : "";
            detailStatValues[i].text = has ? selected.stats[i].value : "";
        }
        detailNote.text = selected?.note ?? "";
    }

    // preserveAspect aligns the sprite by the pivot; a centred pivot keeps it in the middle of its box.
    static void CenterPivot(RectTransform rect)
    {
        rect.anchoredPosition += new Vector2(rect.sizeDelta.x * .5f, -rect.sizeDelta.y * .5f);
        rect.pivot = new Vector2(.5f, .5f);
    }

    Image Frame(string name, Transform parent, float x, float y, float w, float h, float pixelsPerUnit)
    {
        var image = Panel(name, parent, x, y, w, h, shopFrame);
        image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = pixelsPerUnit;
        HomeUi.StylePanelWood(image);
        return image;
    }

    TextMeshProUGUI Serif(Transform parent, string text, float x, float y, float w, float h, float size,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var label = Label(parent, text, x, y, w, h, size, align);
        if (titleFont) { label.font = titleFont; label.fontSharedMaterial = titleMaterial; }
        label.fontStyle = FontStyles.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
}

// What the equipment tab shows: pickaxe, the three tools, and the carrying and energy upgrades.
public static class EquipmentOverview
{
    public const string Check = "✓";
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public sealed class Entry
    {
        public string key, name, area, badge, note;
        public ItemSO item, preview;
        public bool owned;
        public (string label, string value)[] stats;
        public ItemSO Shown => item ? item : preview;
    }

    public static List<Entry> Build(InventoryManager inventory)
    {
        var list = new List<Entry>();
        if (!inventory) return list;
        var settings = Resources.Load<UpgradeSettings>("UpgradeSettings");
        list.Add(Pickaxe(inventory, settings));
        list.Add(Tool(inventory, Item.Scythe, "Mäht hohes Gras", "Gras"));
        list.Add(Tool(inventory, Item.Axe, "Fällt Bäume", "Bäume"));
        list.Add(Tool(inventory, Item.PercussionHammer, "Zerschlägt Felsbrocken", "Felsen"));
        list.Add(Carrying(inventory, settings));
        list.Add(Energy(inventory, settings));
        return list;
    }

    static Entry Pickaxe(InventoryManager inventory, UpgradeSettings settings)
    {
        var pickaxe = inventory.EquippedPickaxe;
        int level = Mathf.Max(1, inventory.EquippedPickaxeLevel);
        int count = settings ? Mathf.Max(1, settings.PickaxeLevelCount) : 1;
        var next = settings && level < count ? settings.pickaxeLevels[level]?.pickaxe : null;
        return new Entry {
            key = "pickaxe", item = pickaxe, owned = pickaxe, area = "Spitzhacke",
            name = pickaxe ? pickaxe.displayName : "Spitzhacke", badge = "Stufe " + level,
            stats = new[] { ("Stufe", $"{level} von {count}"),
                ("Härte", "bis " + Number(inventory.EquippedPickaxeMaximumHardness)),
                ("Abbau", "×" + Number(inventory.EquippedPickaxeProgressMultiplier)) },
            note = next ? "Nächste: " + next.displayName : "Beste Spitzhacke" };
    }

    static Entry Tool(InventoryManager inventory, Item id, string use, string target)
    {
        var item = StartingResourcesSettings.Resolve((int)id);
        bool owned = inventory.IsPowerupUnlocked(item);
        return new Entry {
            key = id.ToString(), item = item, owned = owned, area = "Werkzeug",
            name = item ? item.displayName : id.ToString(), badge = owned ? Check : "–",
            stats = new[] { ("Status", owned ? "Dabei" : "Fehlt"), ("Für", target), ("Gewicht", "0 kg") },
            note = owned ? use : "Noch nicht gefunden" };
    }

    static Entry Carrying(InventoryManager inventory, UpgradeSettings settings)
    {
        int level = inventory.CarryingCapacityLevel;
        int count = settings ? settings.CarryingCapacityLevelCount : 1;
        var item = Upgrade(level, true);
        var next = level < count ? Upgrade(level + 1, true) : null;
        float bonus = MetaProgression.CurrentLoadout.carryCapacityBonus;
        return new Entry {
            key = "carrying", item = item, preview = StartingResourcesSettings.Resolve((int)global::Item.Backpack),
            owned = item, area = "Traglast", name = item ? item.displayName : "Kein Traglast-Teil", badge = "Stufe " + level,
            stats = new[] { ("Stufe", $"{level} von {count}"), ("Traglast", Number(inventory.CarryingCapacity) + " kg"),
                ("Höchstens", Number(inventory.MaximumWeight) + " kg") },
            note = next ? $"Nächste: {next.displayName} ({Number(settings.GetCarryingCapacity(level + 1) + bonus)} kg)"
                : level >= count ? "Höchste Stufe" : "" };
    }

    static Entry Energy(InventoryManager inventory, UpgradeSettings settings)
    {
        int level = inventory.EnergyCapacityLevel;
        int count = settings ? settings.EnergyCapacityLevelCount : 1;
        var item = Upgrade(level, false);
        var next = level < count ? Upgrade(level + 1, false) : null;
        return new Entry {
            key = "energy", item = item, preview = StartingResourcesSettings.Resolve((int)global::Item.CrystalPendant),
            owned = item, area = "Energie", name = item ? item.displayName : "Kein Energie-Teil", badge = "Stufe " + level,
            stats = new[] { ("Stufe", $"{level} von {count}"), ("Energie", "×" + Number(inventory.EnergyCapacityMultiplier)),
                ("Nächste", level < count && settings ? "×" + Number(settings.GetEnergyCapacityMultiplier(level + 1)) : "–") },
            note = next ? "Nächste: " + next.displayName : level >= count ? "Höchste Stufe" : "" };
    }

    // The powerup whose upgrade level matches, e.g. carrying level 2 = Rucksack. Level 1 has no item.
    static ItemSO Upgrade(int level, bool carrying)
    {
        if (level <= 1) return null;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!catalog || catalog.items == null) return null;
        foreach (var item in catalog.items)
            if (item && item.category == ItemCategory.Powerup &&
                (carrying ? item.carryingCapacityUpgradeLevel : item.energyCapacityUpgradeLevel) == level) return item;
        return null;
    }

    static string Number(float value) => value.ToString("0.##", German);
}
