using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Right click on an item opens a centred window: amount slider (1 to whole stock, number in the handle),
// "Abbrechen" and a red "N löschen" button. Slider frames are the shop's (BuyFrames-v2) like the sell slider.
public partial class InventoryUI
{
    public bool DeleteDialogOpen => deleteRoot && deleteRoot.gameObject.activeSelf;
    public ItemSO DeleteItem { get; private set; }
    public int DeleteAmount => DeleteItem ? Mathf.Clamp(deleteAmount, 1, Mathf.Max(1, inventory ? inventory.GetCount(DeleteItem) : 0)) : 0;
    public Slider DeleteSlider => deleteSlider;
    public Button DeleteConfirmButton => deleteConfirm;
    RectTransform deleteRoot;
    Image deleteIcon;
    TextMeshProUGUI deleteName, deleteInfo, deleteAmountText, deleteConfirmText;
    Slider deleteSlider;
    Button deleteConfirm;
    int deleteAmount = 1;
    readonly List<Sprite> deleteSprites = new();
    static readonly Color Muted = new Color32(214, 190, 150, 255);
    static readonly Color Gold = new Color32(214, 160, 60, 255);

    public void OpenDeleteDialog(ItemSO item)
    {
        if (!IsOpen || !item || !inventory || inventory.GetCount(item) <= 0) return;
        CancelItemDrag();
        SelectItem(item);
        DeleteItem = item; deleteAmount = 1;
        deleteRoot.gameObject.SetActive(true); deleteRoot.SetAsLastSibling();
        SyncDeleteDialog();
    }
    public void CloseDeleteDialog()
    {
        DeleteItem = null;
        if (deleteRoot) deleteRoot.gameObject.SetActive(false);
    }
    public void SetDeleteAmount(int amount) { deleteAmount = amount; SyncDeleteDialog(); }
    public void ConfirmDelete()
    {
        var item = DeleteItem; int amount = DeleteAmount;
        CloseDeleteDialog();
        if (item && amount > 0 && inventory) inventory.TryRemove(item, amount);
    }

    // Called from Refresh too, so a stock change while the window is open moves Max along.
    void SyncDeleteDialog()
    {
        if (!DeleteDialogOpen) return;
        int count = inventory && DeleteItem ? inventory.GetCount(DeleteItem) : 0;
        if (count <= 0) { CloseDeleteDialog(); return; }
        deleteAmount = Mathf.Clamp(deleteAmount, 1, count);
        // Setting maxValue can notify with the old value, so the amount is applied afterwards.
        deleteSlider.maxValue = Mathf.Max(1, count);
        deleteSlider.SetValueWithoutNotify(deleteAmount);
        deleteSlider.interactable = count > 1;
        deleteAmountText.text = deleteAmount.ToString();
        deleteIcon.sprite = DeleteItem.icon;
        deleteName.text = DeleteItem.displayName;
        string kind = Category(DeleteItem) == 1 ? "Erz" : Category(DeleteItem) == 2 ? "Material" : "Werkzeug";
        deleteInfo.text = $"{kind} · Bestand {Count(count)}";
        deleteConfirmText.text = $"{deleteAmount} löschen";
    }

    void BuildDeleteDialog()
    {
        deleteRoot = Rect("Delete Dialog", layout, 0, 0, 1672, 941);
        // Larger than the layout so the whole screen darkens, not only the inventory frame.
        var dim = Panel("Delete Dim", deleteRoot, -2000, -1500, 5672, 3941, null);
        dim.color = new Color(0, 0, 0, .68f); dim.raycastTarget = true;
        var dimButton = dim.gameObject.AddComponent<Button>(); dimButton.transition = Selectable.Transition.None;
        dimButton.navigation = new Navigation { mode = Navigation.Mode.None }; dimButton.onClick.AddListener(CloseDeleteDialog);
        // Opaque shop card frame (gold border, corner rivets), so nothing of the grid shows through.
        var frame = DeleteFrame(Resources.Load<Texture2D>("Shop/BuyFrames-v2"), "Delete Window", 40, 110, 1175, 325);
        // Between the filter tabs (bottom 222) and the info bar (top 754); the backing stops faint see-through.
        Panel("Delete Backing", deleteRoot, 530, 250, 612, 484, null).color = new Color32(46, 27, 14, 255);
        var window = Panel("Delete Window", deleteRoot, 516, 236, 640, 512, frame ? frame : rowSprite);
        window.raycastTarget = true; if (frame) window.pixelsPerUnitMultiplier = 2.2f;
        var w = window.transform;
        deleteIcon = Panel("Item Icon", w, 245, 28, 150, 100, null); deleteIcon.preserveAspect = true;
        deleteName = Label(w, "", 40, 132, 560, 48, 40, TextAlignmentOptions.Midline);
        deleteInfo = Label(w, "", 40, 178, 560, 32, 24, TextAlignmentOptions.Midline); deleteInfo.color = Muted;
        var line = Panel("Line", w, 90, 228, 460, 3, null); line.color = Gold;
        Label(w, "Wie viel löschen?", 40, 244, 560, 40, 28, TextAlignmentOptions.Midline);
        BuildDeleteSlider(w);
        var cancel = Button("Cancel", w, "Abbrechen", 56, 392, 250, 72, CloseDeleteDialog);
        cancel.GetComponent<Image>().sprite = actionSprite;
        deleteConfirm = Button("Delete Selected", w, "1 löschen", 330, 392, 254, 72, ConfirmDelete);
        deleteConfirmText = deleteConfirm.GetComponentInChildren<TextMeshProUGUI>();
        deleteConfirm.GetComponent<GoldButtonFeedback>().SetArtwork(
            HomeUi.Sprite("DeleteButton"), HomeUi.Sprite("DeleteActive"), HomeUi.Sprite("DeleteSelected"));
        deleteRoot.gameObject.SetActive(false);
    }

    // Same build as the shop sell slider: "1" left, "Max" right, the handle shows the chosen amount.
    void BuildDeleteSlider(Transform parent)
    {
        var sheet = Resources.Load<Texture2D>("Shop/BuyFrames-v2");
        Sprite card = DeleteFrame(sheet, "Delete Rail", 40, 110, 1175, 325),
            selected = DeleteFrame(sheet, "Delete Handle", 35, 490, 1185, 340),
            action = DeleteFrame(sheet, "Delete Fill", 38, 890, 1178, 275);
        var row = Rect("Delete Amount", parent, 30, 292, 580, 76);
        Label(row, "1", 8, 0, 40, 76, 32).color = Muted;
        var max = Label(row, "Max", 500, 0, 76, 76, 32, TextAlignmentOptions.MidlineRight); max.color = Muted;
        var area = Rect("Amount Slider", row, 56, 0, 440, 76);
        var hit = area.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        var rail = Panel("Rail", area, 0, 26, 440, 24, card); rail.pixelsPerUnitMultiplier = 12; rail.raycastTarget = true;
        var slide = Rect("Handle Slide Area", area, 40, 0, 360, 76);
        var fill = Panel("Fill", Rect("Fill Area", area, 40, 30, 360, 16), 0, 0, 360, 16, action);
        fill.pixelsPerUnitMultiplier = 14;
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
        fill.rectTransform.offsetMin = new Vector2(-36, 0); fill.rectTransform.offsetMax = Vector2.zero;
        var handle = Panel("Handle", slide, 0, 0, 76, 56, selected);
        handle.pixelsPerUnitMultiplier = 10; handle.raycastTarget = true;
        handle.rectTransform.pivot = Vector2.one * .5f; handle.rectTransform.sizeDelta = new Vector2(76, -20);
        deleteAmountText = Label(handle.transform, "1", 0, 0, 76, 56, 32, TextAlignmentOptions.Midline);
        deleteAmountText.rectTransform.anchorMin = Vector2.zero; deleteAmountText.rectTransform.anchorMax = Vector2.one;
        deleteAmountText.rectTransform.offsetMin = deleteAmountText.rectTransform.offsetMax = Vector2.zero;
        slide.SetAsLastSibling(); // the handle and its number stay above the fill
        deleteSlider = area.gameObject.AddComponent<Slider>();
        deleteSlider.wholeNumbers = true; deleteSlider.minValue = 1; deleteSlider.maxValue = 1;
        deleteSlider.fillRect = fill.rectTransform; deleteSlider.handleRect = handle.rectTransform;
        deleteSlider.targetGraphic = handle; deleteSlider.navigation = new Navigation { mode = Navigation.Mode.None };
        deleteSlider.onValueChanged.AddListener(value => SetDeleteAmount(Mathf.RoundToInt(value)));
    }

    Sprite DeleteFrame(Texture2D sheet, string name, float x, float top, float width, float height)
    {
        if (!sheet) return null;
        float sx = sheet.width / 1280f, sy = sheet.height / 1280f;
        var sprite = Sprite.Create(sheet, new UnityEngine.Rect(x * sx, (1280 - top - height) * sy, width * sx, height * sy),
            Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, new Vector4(100 * sx, 80 * sy, 100 * sx, 80 * sy));
        sprite.name = name; deleteSprites.Add(sprite); return sprite;
    }

    void OnDestroy()
    {
        foreach (var sprite in deleteSprites) if (sprite) Destroy(sprite);
        DestroyEquipmentAssets();
    }
}
