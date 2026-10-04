using System.Collections.Generic;
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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() {
        foreach (var sprite in sprites.Values) if (sprite) Object.Destroy(sprite);
        sprites.Clear(); if (wood) Object.Destroy(wood);
    }
    public static Sprite Sprite(int index) {
        if (sprites.TryGetValue(index, out var cached) && cached) return cached;
        var texture = Resources.Load<Texture2D>("Progression/ChallengeAtlas");
        if (!texture) return null;
        var r = rects[index]; float sx = texture.width / 1536f, sy = texture.height / 1024f;
        var border = index == 6 ? new Vector4(76*sx, 30*sy, 76*sx, 30*sy) : Vector4.zero;
        cached = UnityEngine.Sprite.Create(texture, new Rect(r.x*sx, (1024-r.y-r.height)*sy, r.width*sx, r.height*sy),
            new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect, border);
        cached.name = "Challenge Art " + index; sprites[index] = cached; return cached;
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
