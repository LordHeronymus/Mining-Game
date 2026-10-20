using UnityEngine;
using UnityEngine.UI;

// A dragged item looks exactly like the slot icon it was picked up from: same sprite, size, world scale
// (including the ItemIconLayout flips) and rotation. The point where the icon was grabbed stays under the cursor.
// Entering drag mode is marked by a short pop: the item jumps up, then settles slightly enlarged with a drop shadow
// while it is carried.
public sealed class ItemDragGhost : MonoBehaviour
{
    public const float PopScale = 1.28f, CarryScale = 1.12f, PopSeconds = .08f, SettleSeconds = .16f;
    const float ShadowDistance = 5f;
    // A slow frame (e.g. loading or a screenshot) advances the pop by at most this much, so it is never skipped.
    const float MaxStep = 1f / 30f;
    static readonly Color ShadowColor = new Color(0f, 0f, 0f, .45f);

    Vector3 baseScale, grabOffset, pointer;
    float elapsed;
    int beganFrame;

    // Current size factor of the pop (1 at the start, CarryScale once settled).
    public float Factor { get; private set; } = 1f;

    // Copies the source icon onto the ghost, starts the pop and returns the grab offset for Move.
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
        var offset = Pointer(rect, screenPosition, eventCamera, out var pointer) ? from.position - pointer : Vector3.zero;

        var shadow = ghost.GetComponent<Shadow>();
        if (!shadow) shadow = ghost.gameObject.AddComponent<Shadow>();
        shadow.effectColor = ShadowColor;
        // The shadow offset is local, so mirrored icons need the mirrored offset to keep it bottom right.
        shadow.effectDistance = new Vector2(ShadowDistance * Mathf.Sign(rect.localScale.x), -ShadowDistance * Mathf.Sign(rect.localScale.y));

        var state = ghost.GetComponent<ItemDragGhost>();
        if (!state) state = ghost.gameObject.AddComponent<ItemDragGhost>();
        state.baseScale = rect.localScale;
        state.grabOffset = offset;
        state.pointer = from.position - offset;
        state.elapsed = 0f;
        state.beganFrame = Time.frameCount;
        state.Apply();
        return offset;
    }

    public static void Move(Image ghost, Vector3 grabOffset, Vector2 screenPosition, Camera eventCamera)
    {
        if (!ghost || !Pointer(ghost.rectTransform, screenPosition, eventCamera, out var pointer)) return;
        var state = ghost.GetComponent<ItemDragGhost>();
        if (state) { state.pointer = pointer; state.Apply(); }
        else ghost.rectTransform.position = pointer + grabOffset;
    }

    // Quick ease-out up to PopScale, then a smooth settle down to CarryScale.
    public static float Lift(float seconds)
    {
        if (seconds <= 0f) return 1f;
        if (seconds < PopSeconds)
        {
            float t = seconds / PopSeconds;
            return Mathf.LerpUnclamped(1f, PopScale, 1f - (1f - t) * (1f - t));
        }
        float settle = Mathf.Clamp01((seconds - PopSeconds) / SettleSeconds);
        return Mathf.Lerp(PopScale, CarryScale, Mathf.SmoothStep(0f, 1f, settle));
    }

    void LateUpdate()
    {
        if (Time.frameCount != beganFrame) elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
        Apply();
    }

    // Scales around the grab point, so the item stays under the cursor where it was picked up.
    void Apply()
    {
        Factor = Lift(elapsed);
        var rect = (RectTransform)transform;
        rect.localScale = new Vector3(baseScale.x * Factor, baseScale.y * Factor, 1f);
        rect.position = pointer + grabOffset * Factor;
    }

    static float Divide(float value, float by) => Mathf.Abs(by) > 1e-6f ? value / by : value;

    static bool Pointer(RectTransform rect, Vector2 screenPosition, Camera eventCamera, out Vector3 world)
    {
        world = Vector3.zero;
        return rect.parent is RectTransform parent &&
               RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPosition, eventCamera, out world);
    }
}
