using UnityEngine;
using UnityEngine.UI;

public sealed class HomeCaveVisual : MonoBehaviour
{
    readonly Material[] materials = new Material[2];
    readonly RawImage[] images = new RawImage[2];
    RectTransform picture;
    HomeCaveMotes motes;
    Vector2 parallax, pointerTarget;
    int current, lastWaterImpact;
    bool blending;
    public int Layer { get; private set; } = 1;
    public float AnimationTime { get; private set; }
    public bool WaterEffectsEnabled => Layer == 3;
    void OnEnable() => lastWaterImpact = Mathf.FloorToInt((Time.unscaledTime - 6.2f) / 9.7f);
    void Awake()
    {
        picture = HomeUi.Rect("Landscape Picture", transform, Vector2.zero, new Vector2(1920,1080));
        var shader = Shader.Find("Tiefenhall/HomeCave");
        for (int i=0; i<2; i++) {
            var rect=HomeUi.Rect("Landscape " + i,picture,Vector2.zero,new Vector2(1920,1080));
            images[i]=rect.gameObject.AddComponent<RawImage>(); images[i].raycastTarget=false;
            if(shader) { materials[i]=new Material(shader); images[i].material=materials[i]; }
            images[i].color=new Color(1,1,1,i==0 ? 1 : 0);
        }
        var dust=HomeUi.Rect("Cave Dust",picture,Vector2.zero,new Vector2(1920,1080));
        motes=dust.gameObject.AddComponent<HomeCaveMotes>();
        Apply(images[0],materials[0],1);motes.gameObject.SetActive(false);Fit();
    }
    static Texture2D Texture(int layer) => Resources.Load<Texture2D>("Homescreen/Layer"+layer) ??
        Resources.Load<Texture2D>(layer==1 ? "Homescreen/Layer1" : "Homescreen/CaveLake");
    static void Apply(RawImage image,Material material,int layer) {
        image.texture=Texture(layer);
        if(material) material.SetFloat("_CaveEffects",layer==3 ? 1 : 0);
    }
    public void SetLayer(int layer)
    {
        layer=Mathf.Max(1,layer); Layer=layer;
        LoadingAudio.SetHomeLayer(layer);
        var target=Texture(layer);
        if(images[current].texture==target) {
            if(materials[current])materials[current].SetFloat("_CaveEffects",layer==3 ? 1 : 0);
        } else {
            int next=1-current;
            // A quick selection reversal continues from the currently visible blend.
            float alpha=images[next].texture==target && blending ? 1-images[current].color.a : 0;
            Apply(images[next],materials[next],layer);
            images[next].color=new Color(1,1,1,alpha);
            images[current].color=Color.white;
            images[next].transform.SetAsLastSibling();
            current=next;blending=true;
        }
        motes.gameObject.SetActive(layer==3);
        motes.transform.SetAsLastSibling();
        lastWaterImpact=Mathf.FloorToInt((Time.unscaledTime-6.2f)/9.7f);
    }
    void Fit() {
        if(!picture)return;
        var size=((RectTransform)transform).rect.size;
        picture.localScale=Vector3.one*Mathf.Max(size.x/1920f,size.y/1080f);
    }
    void OnRectTransformDimensionsChange()=>Fit();
    void Update()
    {
        Vector2 mouse=Input.mousePosition;
        if(Application.isFocused && mouse.x>=0 && mouse.y>=0 && mouse.x<Screen.width && mouse.y<Screen.height)
            pointerTarget=new Vector2(mouse.x/Mathf.Max(1,Screen.width)-.5f,mouse.y/Mathf.Max(1,Screen.height)-.5f);
        parallax=Vector2.Lerp(parallax,pointerTarget,1-Mathf.Exp(-2f*Time.unscaledDeltaTime));
        AnimationTime=Time.unscaledTime;
        int impact=Mathf.FloorToInt((AnimationTime-6.2f)/9.7f);
        if(WaterEffectsEnabled && impact>lastWaterImpact)LoadingAudio.PlayHomeWaterdrop();
        lastWaterImpact=impact;
        foreach(var material in materials)if(material) {
            material.SetFloat("_SceneTime",AnimationTime);
            material.SetVector("_Parallax",new Vector4(parallax.x*.012f,parallax.y*.009f,0,0));
        }
        if(blending) {
            float alpha=Mathf.MoveTowards(images[current].color.a,1,Time.unscaledDeltaTime/.8f);
            images[current].color=new Color(1,1,1,alpha);
            if(alpha>=1) { images[1-current].color=new Color(1,1,1,0);blending=false; }
        }
    }
    void OnDestroy(){foreach(var material in materials)if(material)Destroy(material);}
}

[RequireComponent(typeof(CanvasRenderer))]
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

