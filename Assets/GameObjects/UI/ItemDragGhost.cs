using UnityEngine;
using UnityEngine.UI;

// A dragged item looks exactly like the slot icon it was picked up from: same sprite, size, world scale
// (including the ItemIconLayout flips) and rotation. The point where the icon was grabbed stays under the cursor.
// Entering drag mode is marked by a short pop: the item jumps up, then settles slightly enlarged with a drop shadow
// while it is carried. Strength, carry size and duration come from GPS (UI → Item ziehen); a third of the duration
// is the jump, the rest the settle.
public sealed class ItemDragGhost : MonoBehaviour
{
    // How long an item must be held before drag mode starts (storage, inventory, hotbar); 0 = as soon as it moves.
    // The GPS field keeps its old hotbar name so existing profiles keep their value.
    public static float HoldSeconds => Mathf.Max(0f, GpsSettings.Preferences.hotbarHoldDuration);
    public static float PopScale => Mathf.Max(1f, GpsSettings.Preferences.dragPopScale);
    public static float CarryScale => Mathf.Max(.1f, GpsSettings.Preferences.dragCarryScale);
    static float PopSeconds => Mathf.Max(0f, GpsSettings.Preferences.dragPopDuration) / 3f;
    static float SettleSeconds => PopSeconds * 2f;
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
        float pop = PopSeconds, settleSeconds = SettleSeconds;
        if (pop <= 0f) return CarryScale;
        if (seconds <= 0f) return 1f;
        if (seconds < pop)
        {
            float t = seconds / pop;
            return Mathf.LerpUnclamped(1f, PopScale, 1f - (1f - t) * (1f - t));
        }
        float settle = Mathf.Clamp01((seconds - pop) / settleSeconds);
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

// Press-and-hold to pick up an item, shared by storage, inventory and hotbar slots. Drag mode starts once the item
// was held for ItemDragGhost.HoldSeconds (wherever the pointer is by then), or with no hold time as soon as it moves.
// The ghost is grabbed at the press position. A click that ends a drag is swallowed. The hold is measured in real
// time: frame time would jump after a slow frame and could count the hold as done right after the press.
public sealed class ItemDragHold
{
    bool pressed, suppressClick;
    float pressedAt;
    public bool Dragging;
    public Vector2 PressPosition, Pointer;

    // Every press starts a new click: a drag released over another object never gets its click, so the swallow must
    // not wait for a later click. Called for any button, also where the press does not pick anything up.
    public void Press() => suppressClick = false;

    public void Down(Vector2 position)
    {
        pressed = true; Dragging = suppressClick = false;
        PressPosition = Pointer = position;
        pressedAt = Time.realtimeSinceStartup;
    }

    public bool ShouldStart(bool moved)
    {
        if (!pressed || Dragging) return false;
        float hold = ItemDragGhost.HoldSeconds;
        return hold <= 0f ? moved : Time.realtimeSinceStartup - pressedAt >= hold;
    }

    // Returns whether a drag was running, which the caller then drops at the release position.
    public bool Up(Vector2 position)
    {
        Pointer = position;
        bool wasDragging = Dragging;
        pressed = Dragging = false;
        if (wasDragging) suppressClick = true;
        return wasDragging;
    }

    public bool ConsumeClick()
    {
        bool value = suppressClick;
        suppressClick = false;
        return value;
    }

    public void Cancel() => pressed = Dragging = false;
}
