using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HotbarSlotDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler
{
    const string HoldDurationKey = "hotbar.dragHoldDuration";
    public static float HoldDuration
    {
        get => ItemDragGhost.HoldSeconds;
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            var node=GpsSettings.GetValue("preferences","hotbarHoldDuration"); node.number=Mathf.Clamp(value,0,2);
            GpsSettings.SetValue("preferences",node,out _);
        }
    }

    public CompactHud hud;
    public int slotIndex;

    readonly ItemDragHold hold = new();

    public void OnPointerDown(PointerEventData eventData)
    {
        hold.Press();
        if (eventData.button != PointerEventData.InputButton.Left || !hud || !hud.CanReorderSlot(slotIndex)) return;
        hold.Down(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        hold.Pointer = eventData.position;
        if (hold.ShouldStart(true)) Begin();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && hold.Up(eventData.position))
            hud.EndHotbarDrag(slotIndex, eventData.position);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right && hud) hud.ClearHotbarSlot(slotIndex);
    }

    public bool ConsumeClick() => hold.ConsumeClick();

    void Update()
    {
        if (hold.ShouldStart(false)) Begin();
    }

    // The ghost is grabbed at the press position; CompactHud moves it to the cursor every frame.
    void Begin()
    {
        hold.Dragging = hud && hud.BeginHotbarDrag(slotIndex, hold.PressPosition);
        if (!hold.Dragging) hold.Cancel();
    }

    void OnDisable()
    {
        if (hold.Dragging && hud) hud.CancelHotbarDrag();
        hold.Cancel();
    }
}
