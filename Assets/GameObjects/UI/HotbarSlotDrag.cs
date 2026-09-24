using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HotbarSlotDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    const float HoldDuration = .5f;

    public CompactHud hud;
    public int slotIndex;

    bool pressed;
    bool dragging;
    bool suppressClick;
    float pressedAt;
    Vector2 pointerPosition;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !hud || !hud.CanReorderSlot(slotIndex)) return;
        pressed = true;
        dragging = false;
        pointerPosition = eventData.position;
        pressedAt = Time.unscaledTime;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) pointerPosition = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !pressed) return;
        pointerPosition = eventData.position;
        pressed = false;
        if (!dragging) return;
        hud.EndHotbarDrag(slotIndex, pointerPosition);
        dragging = false;
        suppressClick = true;
    }

    public bool ConsumeClick()
    {
        bool value = suppressClick;
        suppressClick = false;
        return value;
    }

    void Update()
    {
        if (!pressed || dragging || Time.unscaledTime - pressedAt < HoldDuration) return;
        dragging = hud && hud.BeginHotbarDrag(slotIndex, pointerPosition);
    }

    void OnDisable()
    {
        if (dragging && hud) hud.CancelHotbarDrag();
        pressed = dragging = false;
    }
}
