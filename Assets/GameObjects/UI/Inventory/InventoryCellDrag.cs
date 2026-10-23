using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Holding an inventory cell picks its item up (see ItemDragHold); InventoryUI moves the ghost to the cursor.
public sealed class InventoryCellDrag : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public InventoryUI owner;
    public Func<ItemSO> item;
    public Image icon;
    readonly ItemDragHold hold = new();

    public void OnPointerClick(PointerEventData eventData)
    {
        if (hold.ConsumeClick()) return;
        if (eventData.button != PointerEventData.InputButton.Left || eventData.clickCount != 2 ||
            !owner || item == null) return;
        owner.TryUseMedkit(item());
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !owner || item == null || !item()) return;
        hold.Down(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        hold.Pointer = eventData.position;
        if (hold.Dragging) owner.MoveDraggedIcon(eventData.position);
        else if (hold.ShouldStart(true)) Begin();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && hold.Up(eventData.position))
            owner.EndItemDrag(eventData.position);
    }

    void Update()
    {
        if (hold.ShouldStart(false)) Begin();
    }

    void Begin()
    {
        var current = item?.Invoke();
        if (!owner || !current) { hold.Cancel(); return; }
        hold.Dragging = true;
        owner.BeginItemDrag(current, hold.PressPosition, icon);
        owner.MoveDraggedIcon(hold.Pointer);
    }

    void OnDisable()
    {
        if (hold.Dragging && owner) owner.CancelItemDrag();
        hold.Cancel();
    }
}
