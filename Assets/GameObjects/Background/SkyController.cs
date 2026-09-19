using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(ParallaxLayer)), DefaultExecutionOrder(1100)]
public sealed class SkyController : MonoBehaviour
{
    public Sprite nightSky;
    [Min(0.01f), InspectorName("Night Sky Scale")] public float nightSkyScale = 1f;
    [Min(0.01f)] public float SkyFadeDuration = 3f;
    public bool automaticCycle = true;
    [Min(0.01f)] public float DayDuration = 120f;
    [Min(0.01f)] public float NightDuration = 60f;
    [SerializeField] bool isNight;
    float phaseElapsed;

    public bool IsNight => isNight;
    public float NightBlend { get; private set; }
    public bool TryGetNightRenderer(SpriteRenderer day, out SpriteRenderer night) => companions.TryGetValue(day, out night);

    ParallaxLayer sky;
    Sprite edgeSource, nightEdge;
    readonly Dictionary<SpriteRenderer, SpriteRenderer> companions = new Dictionary<SpriteRenderer, SpriteRenderer>();
    readonly List<SpriteRenderer> stale = new List<SpriteRenderer>();

    void OnEnable()
    {
        sky = GetComponent<ParallaxLayer>();
        NightBlend = isNight ? 1f : 0f;
        phaseElapsed = 0f;
    }

    public void SetNight(bool value)
    {
        if (isNight == value) return;
        isNight = value;
        phaseElapsed = 0f;
    }
    [ContextMenu("Nacht")] public void SwitchToNight() => SetNight(true);
    [ContextMenu("Tag")] public void SwitchToDay() => SetNight(false);

    void Update()
    {
        if (Application.isPlaying) AdvanceTime(Time.deltaTime);
        else NightBlend = isNight ? 1f : 0f;
    }

    // Durations are the fully visible phases; fades take their own additional time.
    public void AdvanceTime(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) return;
        if (!automaticCycle) { AdvanceFade(deltaTime); return; }
        float fade = ValidDuration(SkyFadeDuration, 3f);
        float day = ValidDuration(DayDuration, 120f);
        float night = ValidDuration(NightDuration, 60f);
        // Whole cycles end in exactly the same state, including partway through a fade.
        deltaTime %= day + night + 2f * fade;
        while (deltaTime > 0f)
        {
            float target = isNight ? 1f : 0f;
            if (NightBlend != target)
            {
                float remaining = Mathf.Abs(target - NightBlend) * fade;
                if (deltaTime < remaining) { AdvanceFade(deltaTime); return; }
                NightBlend = target;
                deltaTime -= remaining;
            }
            float holdRemaining = Mathf.Max(0f, (isNight ? night : day) - phaseElapsed);
            if (deltaTime < holdRemaining) { phaseElapsed += deltaTime; return; }
            deltaTime -= holdRemaining;
            SetNight(!isNight);
        }
    }

    static float ValidDuration(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0.01f, value);

    // A reversal continues from the current alpha at the same rate, without a jump.
    public void AdvanceFade(float deltaTime)
    {
        float duration = ValidDuration(SkyFadeDuration, 3f);
        NightBlend = Mathf.MoveTowards(NightBlend, isNight ? 1f : 0f, Mathf.Max(0f, deltaTime) / duration);
    }

    void LateUpdate() => ApplySky();

    public void Refresh()
    {
        if (!sky) sky = GetComponent<ParallaxLayer>();
        sky.Refresh();
        ApplySky();
    }

    void ApplySky()
    {
        if (!sky) return;
        stale.Clear();
        foreach (var pair in companions)
            if (!pair.Key || !pair.Value) stale.Add(pair.Key);
        foreach (var key in stale) companions.Remove(key);

        SpriteRenderer nightPanorama = null;
        foreach (SpriteRenderer day in sky.Renderers)
        {
            if (!day) continue;
            if (!nightSky || !sky.isActiveAndEnabled || !day.enabled || !day.sprite)
            {
                if (companions.TryGetValue(day, out var hidden) && hidden) hidden.enabled = false;
                continue;
            }
            if (!companions.TryGetValue(day, out var night) || !night)
            {
                var child = new GameObject("Night sky (generated)");
                child.hideFlags = HideFlags.HideAndDontSave;
                child.transform.SetParent(day.transform, false);
                night = child.AddComponent<SpriteRenderer>();
                companions[day] = night;
            }
            bool bottomEdge = sky.IsBottomEdge(day.sprite);
            Sprite sprite = bottomEdge ? GetNightEdge() : nightSky;
            night.sprite = sprite;
            MatchRectangle(day, night);
            if (!bottomEdge)
            {
                ScaleAroundCenter(night, nightSkyScale);
                nightPanorama = night;
            }
            else if (nightPanorama)
            {
                // Keep the continuation attached to the resized panorama, down to the same camera edge.
                Bounds panorama = nightPanorama.bounds;
                float bottom = Mathf.Min(day.bounds.min.y, panorama.min.y);
                Vector3 parentScale = day.transform.lossyScale;
                night.transform.localScale = new Vector3(
                    panorama.size.x * sprite.pixelsPerUnit / sprite.rect.width / parentScale.x,
                    (panorama.min.y - bottom) * sprite.pixelsPerUnit / sprite.rect.height / parentScale.y, 1f);
                night.transform.position = new Vector3(panorama.center.x, panorama.min.y, day.transform.position.z);
            }
            night.sharedMaterial = day.sharedMaterial;
            night.sortingLayerID = day.sortingLayerID;
            night.sortingOrder = day.sortingOrder + 1;
            night.gameObject.layer = day.gameObject.layer;
            // Read the unfaded color freshly supplied by ParallaxLayer each frame.
            Color color = day.color;
            Color nightColor = color;
            nightColor.a *= NightBlend;
            night.color = nightColor;
            color.a *= 1f - NightBlend;
            day.color = color;
            night.enabled = true;
        }
    }

    public static void ScaleAroundCenter(SpriteRenderer renderer, float scale)
    {
        scale = ValidDuration(scale, 1f);
        Sprite sprite = renderer.sprite;
        Vector3 localCenter = (sprite.rect.size * 0.5f - sprite.pivot) / sprite.pixelsPerUnit;
        Vector3 center = renderer.transform.localPosition + Vector3.Scale(localCenter, renderer.transform.localScale);
        renderer.transform.localScale = Vector3.Scale(renderer.transform.localScale, new Vector3(scale, scale, 1f));
        renderer.transform.localPosition = center - Vector3.Scale(localCenter, renderer.transform.localScale);
    }

    public static void MatchRectangle(SpriteRenderer day, SpriteRenderer night)
    {
        Sprite source = day.sprite, target = night.sprite;
        Vector2 sourceSize = source.rect.size / source.pixelsPerUnit;
        Vector2 targetSize = target.rect.size / target.pixelsPerUnit;
        Vector3 ratio = new Vector3(sourceSize.x / targetSize.x, sourceSize.y / targetSize.y, 1f);
        night.transform.localRotation = Quaternion.identity;
        night.transform.localScale = ratio;
        // Account for different pivots as well as resolution, PPU and aspect ratio.
        night.transform.localPosition = Vector3.Scale(target.pivot / target.pixelsPerUnit, ratio)
            - (Vector3)(source.pivot / source.pixelsPerUnit);
    }

    Sprite GetNightEdge()
    {
        if (nightEdge && edgeSource == nightSky) return nightEdge;
        ReleaseEdge();
        Rect rect = nightSky.textureRect;
        rect.height = 1f;
        nightEdge = Sprite.Create(nightSky.texture, rect, new Vector2(0.5f, 1f),
            nightSky.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        nightEdge.hideFlags = HideFlags.HideAndDontSave;
        edgeSource = nightSky;
        return nightEdge;
    }

    void OnDisable()
    {
        foreach (var night in companions.Values)
            if (night) { night.enabled = false; Dispose(night.gameObject); }
        companions.Clear();
        ReleaseEdge();
        if (sky) sky.Refresh();
    }

    void ReleaseEdge()
    {
        if (nightEdge) Dispose(nightEdge);
        nightEdge = null;
        edgeSource = null;
    }

    static void Dispose(Object item)
    {
        if (Application.isPlaying) Destroy(item);
        else DestroyImmediate(item);
    }
}
