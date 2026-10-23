using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public static class ChallengeArt
{
    static readonly Dictionary<int, Sprite> sprites = new();
    // Atlas is authored on a 1536×1024 canvas; coordinates below are top-left.
    static readonly Rect[] rects = {
        new(65,45,470,325), new(580,20,395,350), new(1060,12,425,360),
        new(75,400,450,315), new(585,385,430,330), new(1130,360,295,360),
        new(77,805,575,94), new(703,715,262,253), new(1208,719,181,246)
    };
    static Material wood;
    static Sprite artifact;
    static ItemCatalog catalog;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() {
        foreach (var sprite in sprites.Values) if (sprite) UnityEngine.Object.Destroy(sprite);
        sprites.Clear(); if (wood) UnityEngine.Object.Destroy(wood);
        artifact = null; catalog = null;
    }
    public static Sprite Sprite(int index) {
        if (index < 0 || index >= rects.Length) return null;
        if (sprites.TryGetValue(index, out var cached) && cached) return cached;
        var texture = Resources.Load<Texture2D>("Progression/ChallengeAtlas");
        if (!texture) return null;
        var r = rects[index]; float sx = texture.width / 1536f, sy = texture.height / 1024f;
        var border = index == 6 ? new Vector4(76*sx, 30*sy, 76*sx, 30*sy) : Vector4.zero;
        cached = UnityEngine.Sprite.Create(texture, new Rect(r.x*sx, (1024-r.y-r.height)*sy, r.width*sx, r.height*sy),
            new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect, border);
        cached.name = "Challenge Art " + index; sprites[index] = cached; return cached;
    }
    // Catalog icon keys: "art:N" (this atlas), "item:<Item>" (ItemCatalog icon), "artifact" (sun medallion); null when unknown.
    public static Sprite Icon(string key) {
        if (string.IsNullOrEmpty(key)) return null;
        if (key == "artifact") {
            if (!artifact) foreach (var sprite in Resources.LoadAll<Sprite>("ArtifactPointMedallions"))
                if (sprite.name == "ArtifactPoint_Sun") { artifact = sprite; break; }
            return artifact ? artifact : null;
        }
        if (key.StartsWith("art:", StringComparison.Ordinal)) {
            // 6 (track frame) is not an icon.
            if (!int.TryParse(key.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int part) || part == 6) return null;
            var sprite = Sprite(part); return sprite ? sprite : null;
        }
        if (!key.StartsWith("item:", StringComparison.Ordinal) || !Enum.TryParse(key.Substring(5), out Item item)
            || !Enum.IsDefined(typeof(Item), item)) return null;
        if (!catalog) catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (catalog && catalog.items != null)
            foreach (var entry in catalog.items) if (entry && entry.item == item && entry.icon) return entry.icon;
        return null;
    }
    public static void DarkWood(Image image) {
        if (!wood) {
            var sprite=HomeUi.Sprite("SelectionCardNormal"); var r=sprite.rect; var t=sprite.texture;
            wood=new Material(Resources.Load<Shader>("Progression/UpgradeCardWood")) { hideFlags=HideFlags.DontSave };
            wood.SetVector("_WoodUvRect",new Vector4(r.x/t.width,r.y/t.height,r.width/t.width,r.height/t.height));
            wood.SetColor("_WoodTone",new Color(.48f,.52f,.5f,1));
        }
        image.material=wood;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChallengeGoldFill : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r=rectTransform.rect;
        // A soft vertical metal gradient, independent of progress width.
        Color32[] tones={new Color32(155,65,0,255),new Color32(255,178,12,255),new Color32(255,228,107,255)};
        for(int row=0;row<3;row++) {
            float y=Mathf.Lerp(r.yMin,r.yMax,row*.5f);
            vh.AddVert(new Vector3(r.xMin,y),tones[row],Vector2.zero);
            vh.AddVert(new Vector3(r.xMax,y),tones[row],Vector2.zero);
        }
        for(int row=0;row<2;row++) {int n=row*2;vh.AddTriangle(n,n+2,n+1);vh.AddTriangle(n+1,n+2,n+3);}
    }
}

// Tier colour of a challenge card: a soft light along the inner wood lip and a faceted gem on each upper corner plate.
// It replaces the former flat colour lines; the frame itself stays untinted, so the brass and wood keep their colours.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChallengeTierRim : MaskableGraphic
{
    const float Glow=26, TopAlpha=.42f, SideAlpha=.3f, BottomAlpha=.18f, Gem=11;
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r=rectTransform.rect;
        if(r.width<=4*Glow||r.height<=4*Glow)return;
        Color32 c=color;
        // Mitred bands fading inwards; the light falls from the top like the lamp glow on the panel.
        Band(vh,new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax-Glow,r.yMax-Glow),new Vector2(r.xMin+Glow,r.yMax-Glow),c,TopAlpha,TopAlpha);
        Band(vh,new Vector2(r.xMax,r.yMin),new Vector2(r.xMin,r.yMin),new Vector2(r.xMin+Glow,r.yMin+Glow),new Vector2(r.xMax-Glow,r.yMin+Glow),c,BottomAlpha,BottomAlpha);
        Band(vh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMin+Glow,r.yMax-Glow),new Vector2(r.xMin+Glow,r.yMin+Glow),c,BottomAlpha,TopAlpha);
        Band(vh,new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax-Glow,r.yMin+Glow),new Vector2(r.xMax-Glow,r.yMax-Glow),c,TopAlpha,BottomAlpha);
        ChallengeGem.Draw(vh,new Vector2(r.xMin+2,r.yMax-2),Gem,color);
        ChallengeGem.Draw(vh,new Vector2(r.xMax-2,r.yMax-2),Gem,color);
    }
    // a→b is the outer edge, c→d the inner one (c below b, d below a); the outer edge carries alpha, the inner none.
    static void Band(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color32 tone,float alphaA,float alphaB) {
        int n=vh.currentVertCount;
        Color32 clear=tone;clear.a=0;
        Color32 ta=tone;ta.a=(byte)(255*alphaA);Color32 tb=tone;tb.a=(byte)(255*alphaB);
        vh.AddVert(a,ta,Vector2.zero);vh.AddVert(b,tb,Vector2.zero);vh.AddVert(c,clear,Vector2.zero);vh.AddVert(d,clear,Vector2.zero);
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n+2,n+3,n);
    }
}

// Faceted gem in a thin brass setting: the tier marker on cards, in the HUD tracker and wherever a colour dot is needed.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChallengeGem : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r=rectTransform.rect;
        Draw(vh,r.center,Mathf.Min(r.width,r.height)*.5f-1,color);
    }
    static readonly Color32 Setting=new Color32(214,160,62,255), SettingDark=new Color32(92,56,20,255);
    public static void Draw(VertexHelper vh,Vector2 center,float half,Color tone) {
        // Setting: dark rim behind a brass diamond, then the stone with four shaded facets around an off-centre table.
        Diamond(vh,center,half+2.2f,SettingDark,SettingDark,SettingDark,SettingDark);
        Diamond(vh,center,half+.9f,Setting,Setting,Setting,Setting);
        Color light=Color.Lerp(tone,Color.white,.55f), mid=Color.Lerp(tone,Color.white,.12f);
        Color shade=Color.Lerp(tone,Color.black,.25f), dark=Color.Lerp(tone,Color.black,.5f);
        Vector2 top=center+new Vector2(0,half),right=center+new Vector2(half,0),bottom=center-new Vector2(0,half),left=center-new Vector2(half,0);
        Vector2 table=center+new Vector2(-half*.12f,half*.14f);
        Facet(vh,left,top,table,light);Facet(vh,top,right,table,mid);
        Facet(vh,right,bottom,table,dark);Facet(vh,bottom,left,table,shade);
        // Small sparkle on the lit facet.
        Diamond(vh,center+new Vector2(-half*.38f,half*.36f),Mathf.Max(1,half*.16f),Color.white,Color.white,Color.white,Color.white);
    }
    static void Facet(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color tone) {
        int n=vh.currentVertCount;
        vh.AddVert(a,tone,Vector2.zero);vh.AddVert(b,tone,Vector2.zero);vh.AddVert(c,Color.Lerp(tone,Color.white,.1f),Vector2.zero);
        vh.AddTriangle(n,n+1,n+2);
    }
    static void Diamond(VertexHelper vh,Vector2 c,float h,Color top,Color right,Color bottom,Color left) {
        int n=vh.currentVertCount;
        vh.AddVert(c+new Vector2(0,h),top,Vector2.zero);vh.AddVert(c+new Vector2(h,0),right,Vector2.zero);
        vh.AddVert(c-new Vector2(0,h),bottom,Vector2.zero);vh.AddVert(c-new Vector2(h,0),left,Vector2.zero);
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n+2,n+3,n);
    }
}

// Five-pointed star of the "verfolgen" toggle: gold when the challenge is followed in the HUD, an empty brass outline otherwise.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChallengeStar : MaskableGraphic
{
    bool filled;
    public bool Filled { get => filled; set { if (filled == value) return; filled = value; SetVerticesDirty(); } }
    static readonly Color32 Rim=new Color32(214,160,62,255), Hollow=new Color32(28,16,9,235);
    static readonly Color32 Deep=new Color32(196,110,12,255), Bright=new Color32(255,226,120,255);
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r=rectTransform.rect;
        float outer=Mathf.Min(r.width,r.height)*.5f;
        Vector2 c=r.center+new Vector2(0,-outer*.06f);
        Color tint=color;
        Star(vh,c,outer,Multiply(Rim,tint),Multiply(Rim,tint));
        if(filled) Star(vh,c,outer-2.4f,Multiply(Deep,tint),Multiply(Bright,tint));
        else Star(vh,c,outer-2.4f,Multiply(Hollow,tint),Multiply(Hollow,tint));
    }
    static Color Multiply(Color32 value,Color tint) { Color c=value; return new Color(c.r*tint.r,c.g*tint.g,c.b*tint.b,c.a*tint.a); }
    // Centre colour inside, tip colour at the points: a bright rim with a deeper heart reads as polished metal.
    static void Star(VertexHelper vh,Vector2 c,float outer,Color centre,Color tips) {
        if(outer<=0)return;
        float inner=outer*.47f;
        int n=vh.currentVertCount;
        vh.AddVert(c,centre,Vector2.zero);
        for(int i=0;i<10;i++) {
            float angle=Mathf.PI*.5f+i*Mathf.PI/5f, radius=i%2==0?outer:inner;
            vh.AddVert(c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,i%2==0?tips:Color.Lerp(centre,tips,.4f),Vector2.zero);
        }
        for(int i=0;i<10;i++) vh.AddTriangle(n,n+1+i,n+1+(i+1)%10);
    }
}
