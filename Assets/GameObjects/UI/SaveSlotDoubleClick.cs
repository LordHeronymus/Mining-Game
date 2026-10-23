using System;
using UnityEngine;
using UnityEngine.EventSystems;

// A double click on a save slot card in the homescreen starts that run (see SaveSlotPanel).
public sealed class SaveSlotDoubleClick : MonoBehaviour, IPointerClickHandler
{
    public Action onDoubleClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.clickCount != 2) return;
        onDoubleClick?.Invoke();
    }
}
