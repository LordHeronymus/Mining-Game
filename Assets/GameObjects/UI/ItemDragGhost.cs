using UnityEngine;
using UnityEngine.UI;

// A dragged item looks exactly like the slot icon it was picked up from: same sprite, size, world scale
// (including the ItemIconLayout flips) and rotation. The point where the icon was grabbed stays under the cursor.
public static class ItemDragGhost
{
    // Copies the source icon onto the ghost and returns the grab offset for Move.
    public static Vector3 Begin(Image ghost, Image source, Vector2 screenPosition, Camera eventCamera)
    {
        if (!ghost || !source) return Vector3.zero;
        var rect = ghost.rectTransform;
        var from = source.rectTransform;
        ghost.sprite = source.sprite;
        ghost.type = source.type;
        ghost.preserveAspect = source.preserveAspect;
        ghost.raycastTarget = false;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = from.pivot;
        rect.sizeDelta = from.rect.size;
        rect.rotation = from.rotation;
        var parentScale = rect.parent ? rect.parent.lossyScale : Vector3.one;
        var scale = from.lossyScale;
        rect.localScale = new Vector3(Divide(scale.x, parentScale.x), Divide(scale.y, parentScale.y), 1f);
        return Pointer(rect, screenPosition, eventCamera, out var pointer) ? from.position - pointer : Vector3.zero;
    }

    public static void Move(Image ghost, Vector3 grabOffset, Vector2 screenPosition, Camera eventCamera)
    {
        if (ghost && Pointer(ghost.rectTransform, screenPosition, eventCamera, out var pointer))
            ghost.rectTransform.position = pointer + grabOffset;
    }

    static float Divide(float value, float by) => Mathf.Abs(by) > 1e-6f ? value / by : value;

    static bool Pointer(RectTransform rect, Vector2 screenPosition, Camera eventCamera, out Vector3 world)
    {
        world = Vector3.zero;
        return rect.parent is RectTransform parent &&
               RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPosition, eventCamera, out world);
    }
}
