using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1250)]
public sealed class NightGlow : MonoBehaviour
{
    public SkyController sky;
    public SpriteRenderer source;
    public SpriteRenderer glow;
    public Light2D localLight;
    public Vector2 lightPosition = new Vector2(0.5f, 0.5f);
    public Color lightColor = new Color(0.12f, 0.75f, 1f);
    [Min(0f)] public float lightIntensity = 1.5f;
    [Min(0.1f)] public float lightRadius = 4.5f;
    [Min(0f)] public float glowIntensity = 2f;
    MaterialPropertyBlock properties;
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    void LateUpdate() => Refresh();
    public void Refresh()
    {
        float blend = sky && sky.isActiveAndEnabled ? sky.NightBlend : 0f;
        bool visible = source && source.enabled && source.gameObject.activeInHierarchy;
        if (glow && source)
        {
            glow.sprite = source.sprite;
            glow.flipX = source.flipX; glow.flipY = source.flipY;
            glow.sortingLayerID = source.sortingLayerID;
            glow.sortingOrder = source.sortingOrder + 1;
            glow.color = new Color(1f, 1f, 1f, blend * source.color.a);
            glow.enabled = visible && blend > 0f;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat(IntensityId, Mathf.Max(0f, glowIntensity));
            glow.SetPropertyBlock(properties);
        }
        if (localLight)
        {
            localLight.color = lightColor;
            localLight.intensity = visible ? Mathf.Max(0f, lightIntensity) * blend : 0f;
            localLight.pointLightOuterRadius = Mathf.Max(.1f, lightRadius);
            localLight.pointLightInnerRadius = lightRadius * .15f;
            if (source && source.sprite)
            {
                Vector2 point = lightPosition;
                if (source.flipX) point.x = 1f - point.x;
                if (source.flipY) point.y = 1f - point.y;
                Vector2 offset = (source.sprite.rect.size * point - source.sprite.pivot) / source.sprite.pixelsPerUnit;
                localLight.transform.position = source.transform.TransformPoint(offset);
            }
        }
    }
    void OnDisable()
    {
        if (glow) glow.enabled = false;
        if (localLight) localLight.intensity = 0f;
    }
}
