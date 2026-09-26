using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class InventoryCellDrag : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public InventoryUI owner;
    public Func<ItemSO> item;
    bool dragging;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.clickCount != 2 ||
            !owner || item == null) return;
        owner.TryUseMedkit(item());
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !owner || item == null || !item()) return;
        dragging = true;
        owner.BeginItemDrag(item(), eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging) owner.MoveDraggedIcon(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;
        owner.EndItemDrag(eventData.position);
    }

    void OnDisable()
    {
        if (dragging && owner) owner.CancelItemDrag();
        dragging = false;
    }
}
