using UnityEngine;
using TMPro;

public class Number : MonoBehaviour
{
    [SerializeField] TMP_Text tmp;
    [SerializeField] float riseDistance = .6f;
    [SerializeField] float riseDuration = .24f;
    [SerializeField] float fadeDuration = .45f;

    Vector3 origin, baseScale;
    float age, idleAge, pulseAge, lifetime;
    bool initialized;
    public long Total { get; private set; }
    public Vector2 Origin => origin;
    public bool CanAccumulate => initialized && idleAge < lifetime;

    public void Init(Color theme, float size, int amount, float duration)
    {
        origin = transform.position;
        baseScale = transform.localScale;
        lifetime = Mathf.Max(riseDuration + fadeDuration + .5f, duration);
        age = idleAge = pulseAge = 0f;
        Total = amount;
        initialized = true;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Normal;
        tmp.characterSpacing = -1f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.color = Color.white;
        Color top = Color.Lerp(new Color(1f, .96f, .83f), theme, .3f);
        Color bottom = Color.Lerp(new Color(1f, .76f, .40f), theme, .82f);
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(top, top, bottom, bottom);
        tmp.outlineColor = new Color(.19f, .075f, .025f, 1f);
        tmp.outlineWidth = .12f;
        var material = tmp.fontMaterial;
        material.EnableKeyword("OUTLINE_ON");
        material.SetFloat(ShaderUtilities.ID_FaceDilate, .18f);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(.075f, .027f, .008f, .7f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .6f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.65f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .16f);
        tmp.UpdateMeshPadding();
        RefreshText();
        Advance(0f);
    }

    public void Accumulate(int amount)
    {
        if (!CanAccumulate || amount <= 0) return;
        Total += amount;
        idleAge = pulseAge = 0f;
        RefreshText();
        tmp.alpha = 1f;
    }

    void RefreshText() => tmp.text = $"+{Total}";
    void Update() { if (initialized) Advance(Time.deltaTime); }

    void Advance(float delta)
    {
        age += delta;
        idleAge += delta;
        pulseAge += delta;
        float rise = Mathf.Clamp01(age / Mathf.Max(.01f, riseDuration));
        float easedRise = 1f - Mathf.Pow(1f - rise, 3f);
        float hover = rise >= 1f ? Mathf.Sin((age - riseDuration) * 3f) * .012f : 0f;
        transform.position = origin + Vector3.up * (riseDistance * easedRise + hover);
        float pop = Mathf.Clamp01(pulseAge / .28f);
        float scale = 1f + Mathf.Sin(pop * Mathf.PI) * .16f;
        if (age < .1f) scale *= Mathf.Lerp(.72f, 1f, age / .1f);
        transform.localScale = baseScale * scale;
        float fade = Mathf.Clamp01((lifetime - idleAge) / Mathf.Max(.01f, fadeDuration));
        tmp.alpha = Mathf.SmoothStep(0f, 1f, fade);
        if (idleAge >= lifetime)
        {
            initialized = false;
            Destroy(gameObject);
        }
    }
}
