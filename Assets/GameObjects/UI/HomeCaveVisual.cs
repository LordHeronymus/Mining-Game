using UnityEngine;
using UnityEngine.UI;

public sealed class HomeCaveVisual : MonoBehaviour
{
    Material material;
    RawImage image;
    RectTransform picture;
    Vector2 parallax;
    int lastWaterImpact;
    public float AnimationTime { get; private set; }
    void OnEnable() => lastWaterImpact = Mathf.FloorToInt((Time.unscaledTime - 6.2f) / 9.7f);
    void Awake()
    {
        var texture = Resources.Load<Texture2D>("Homescreen/CaveLake");
        picture = HomeUi.Rect("Cave Picture", transform, Vector2.zero, new Vector2(1920, 1080));
        image = picture.gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
        var shader = Shader.Find("Tiefenhall/HomeCave");
        if (shader) { material = new Material(shader); image.material = material; }
        var motes = HomeUi.Rect("Cave Dust", picture, Vector2.zero, new Vector2(1920, 1080));
        motes.gameObject.AddComponent<HomeCaveMotes>(); Fit();
    }
    void Fit()
    {
        if (!picture) return;
        var size = ((RectTransform)transform).rect.size;
        float cover = Mathf.Max(size.x / 1920f, size.y / 1080f);
        picture.localScale = Vector3.one * cover;
    }
    void OnRectTransformDimensionsChange() => Fit();
    void Update()
    {
        if (!material) return;
        Vector2 mouse = Application.isFocused ? new Vector2(Input.mousePosition.x / Mathf.Max(1, Screen.width) - .5f,
            Input.mousePosition.y / Mathf.Max(1, Screen.height) - .5f) : Vector2.zero;
        parallax = Vector2.Lerp(parallax, mouse, 1 - Mathf.Exp(-2f * Time.unscaledDeltaTime));
        AnimationTime = Time.unscaledTime;
        // Same contact time and cycle as the lake shader; the three rings belong to one drop.
        int impact = Mathf.FloorToInt((AnimationTime - 6.2f) / 9.7f);
        if (impact > lastWaterImpact) LoadingAudio.PlayHomeWaterdrop();
        lastWaterImpact = impact;
        material.SetFloat("_SceneTime", AnimationTime);
        material.SetVector("_Parallax", new Vector4(parallax.x * .012f, parallax.y * .009f, 0, 0));
    }
    void OnDestroy() { if (material) Destroy(material); }
}

public sealed class HomeCaveMotes : MaskableGraphic
{
    Material dustMaterial;
    protected override void Awake()
    {
        base.Awake(); raycastTarget = false;
        var shader = Shader.Find("Tiefenhall/HomeDust");
        if (shader) { dustMaterial = new Material(shader); material = dustMaterial; }
    }
    void Update() => SetVerticesDirty();
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); float time = Time.unscaledTime;
        for (int i = 0; i < 36; i++)
        {
            float seed = Mathf.Repeat(Mathf.Sin(i * 78.233f + 1) * 43758.54f, 1);
            float x = -330 + seed * 1230 + Mathf.Sin(time * .11f + i) * 16;
            float y = -260 + Mathf.Repeat(seed * 647 + time * (2.5f + i % 4), 680);
            float fade = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(-260, 420, y));
            float radius = 1.1f + seed * 1.6f;
            Color32 color = new Color(1, .85f + seed * .1f, .6f + seed * .3f, fade * .24f);
            int index = vh.currentVertCount;
            vh.AddVert(new Vector3(x - radius, y - radius), color, new Vector2(0, 0));
            vh.AddVert(new Vector3(x - radius, y + radius), color, new Vector2(0, 1));
            vh.AddVert(new Vector3(x + radius, y + radius), color, new Vector2(1, 1));
            vh.AddVert(new Vector3(x + radius, y - radius), color, new Vector2(1, 0));
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
    }
    protected override void OnDestroy() { base.OnDestroy(); if (dustMaterial) Destroy(dustMaterial); }
}
