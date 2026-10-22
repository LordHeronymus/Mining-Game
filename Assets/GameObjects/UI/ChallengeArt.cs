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

// Tier colour line on the inner wood lip of a challenge card. Only the four straight edges are drawn, so the
// bolted corner plates of the frame stay untouched; tinting the frame itself would recolour the whole card.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChallengeTierRim : MaskableGraphic
{
    const float Width=3, Feather=1.5f, Corner=20;
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r=rectTransform.rect;
        if(r.width<=2*Corner||r.height<=2*Corner)return;
        Edge(vh,r.xMin+Corner,r.yMax-Width,r.xMax-Corner,r.yMax,true);
        Edge(vh,r.xMin+Corner,r.yMin,r.xMax-Corner,r.yMin+Width,true);
        Edge(vh,r.xMin,r.yMin+Corner,r.xMin+Width,r.yMax-Corner,false);
        Edge(vh,r.xMax-Width,r.yMin+Corner,r.xMax,r.yMax-Corner,false);
    }
    // Solid core plus a short alpha fade on both long sides, so the thin line does not alias when the layout is scaled down.
    void Edge(VertexHelper vh,float x0,float y0,float x1,float y1,bool horizontal) {
        Color32 solid=color,clear=color;clear.a=0;
        if(horizontal){
            Quad(vh,x0,y0-Feather,x1,y0,clear,solid,true);Quad(vh,x0,y0,x1,y1,solid,solid,true);Quad(vh,x0,y1,x1,y1+Feather,solid,clear,true);
        } else {
            Quad(vh,x0-Feather,y0,x0,y1,clear,solid,false);Quad(vh,x0,y0,x1,y1,solid,solid,false);Quad(vh,x1,y0,x1+Feather,y1,solid,clear,false);
        }
    }
    // from/to run bottom→top for horizontal edges and left→right for vertical ones.
    static void Quad(VertexHelper vh,float x0,float y0,float x1,float y1,Color32 from,Color32 to,bool horizontal) {
        int n=vh.currentVertCount;
        vh.AddVert(new Vector3(x0,y0),from,Vector2.zero);
        vh.AddVert(new Vector3(x0,y1),horizontal?to:from,Vector2.zero);
        vh.AddVert(new Vector3(x1,y1),to,Vector2.zero);
        vh.AddVert(new Vector3(x1,y0),horizontal?from:to,Vector2.zero);
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n+2,n+3,n);
    }
}
