using UnityEngine;

public static class RoundHudArt
{
    static Texture2D atlas;
    static Sprite heart, bolt;
    public static Texture2D Atlas => atlas ? atlas : atlas=Resources.Load<Texture2D>("HUDRoundGems/Atlas");
    // Coordinates refer to the approved extraction at 2048 x 685, from its top edge.
    public static Rect Uv(float x, float y, float w, float h) => new Rect(x/2048f,1f-(y+h)/685f,w/2048f,h/685f);
    public static Rect Red => Uv(219,106,205,212);
    public static Rect Gold => Uv(219,335,205,214);
    public static Rect Empty => Uv(1720,335,205,214);
    public static Rect Link => Uv(417,197,23,28);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (heart) Object.Destroy(heart);
        if (bolt) Object.Destroy(bolt);
        heart=bolt=null; atlas=null;
    }
    static Sprite Make(Rect uv)
    {
        var tex=Atlas;
        return Sprite.Create(tex,new Rect(uv.x*tex.width,uv.y*tex.height,uv.width*tex.width,uv.height*tex.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
    }
    public static Sprite Heart => heart ? heart : heart=Make(Uv(48,145,169,150));
    public static Sprite Bolt => bolt ? bolt : bolt=Make(Uv(66,334,135,205));
}

