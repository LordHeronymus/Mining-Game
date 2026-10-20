using UnityEngine;

// Keeps a panel at its UI-Größe = 100 % size although its root canvas follows PlayerSettings.UiScale.
// Used for the building overlays (shop, workbench, storage): the canvas scale factor grows with
// UiScale, so the panel is shrunk by 1/UiScale around its pivot and its stretched extent and offset
// are widened to match. Works for stretched and fixed-size rects.
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class UiScaleExempt : MonoBehaviour
{
    RectTransform rect;
    Vector2 baseSize, basePosition;
    Vector3 baseScale;
    bool captured;
    float appliedScale = -1;
    Vector2 appliedParentSize;

    public static void Attach(Component panel)
    {
        if (panel && !panel.GetComponent<UiScaleExempt>()) panel.gameObject.AddComponent<UiScaleExempt>();
    }

    void OnEnable() => Apply(PlayerSettings.UiScale);
    void LateUpdate() => Apply(PlayerSettings.UiScale);

    void Capture()
    {
        if (captured) return;
        rect = (RectTransform)transform;
        baseSize = rect.sizeDelta;
        basePosition = rect.anchoredPosition;
        baseScale = rect.localScale;
        captured = true;
    }

    public void Apply(float uiScale)
    {
        Capture();
        var parent = rect.parent as RectTransform;
        if (!parent) return;
        float scale = Mathf.Max(.01f, uiScale);
        Vector2 parentSize = parent.rect.size;
        if (Mathf.Approximately(scale, appliedScale) && parentSize == appliedParentSize) return;
        appliedScale = scale; appliedParentSize = parentSize;
        Vector2 stretch = rect.anchorMax - rect.anchorMin;
        rect.localScale = baseScale / scale;
        rect.anchoredPosition = basePosition / scale;
        rect.sizeDelta = baseSize + Vector2.Scale(parentSize * (scale - 1f), stretch);
    }

    void OnDisable()
    {
        if (!captured || !rect) return;
        rect.localScale = baseScale;
        rect.anchoredPosition = basePosition;
        rect.sizeDelta = baseSize;
        appliedScale = -1;
    }
}
