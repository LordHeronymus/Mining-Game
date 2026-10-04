using UnityEngine;
using UnityEngine.UI;

// Lightweight UI geometry for animated light: no per-frame textures or particle objects.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class LevelUpGlowGraphic : MaskableGraphic
{
    public bool front;
    Material glowMaterial;
    float seconds, impact=.16f;
    protected override void Awake(){base.Awake();var shader=Resources.Load<Shader>("Progression/LevelUpGlow");if(shader){glowMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material=glowMaterial;}}
    protected override void OnDestroy(){base.OnDestroy();if(glowMaterial)Destroy(glowMaterial);}
    public void Animate(float time,float peak){seconds=time;impact=peak;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper v)
    {
        v.Clear();float pulse=Mathf.Exp(-Mathf.Max(0,seconds-impact)*3.8f);
        if(!front)
        {
            Ring(v,151,220,new Color(1,.61f,.13f,.12f+.17f*pulse));
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI*2/24+.13f*Mathf.Sin(i*2.1f);
                var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var n=new Vector2(-d.y,d.x);
                Triangle(v,d*148+n*3,d*(181+24*pulse+13*Mathf.Sin(i*7)),d*148-n*3,new Color(1,.73f,.26f,.08f+.17f*pulse));
            }
        }
        // Elliptical orbit split around the medallion, with a bright travelling leading point.
        for(int i=0;i<96;i++)
        {
            float a=i*Mathf.PI*2/96;bool near=Mathf.Sin(a)<0;
            if(near!=front)continue;
            float head=seconds*2.8f;float trail=Mathf.Repeat(head-a,Mathf.PI*2);
            float brightness=.55f+.45f*Mathf.Exp(-trail*1.7f);
            Vector2 p=Orbit(a),q=Orbit(a+Mathf.PI*2/96);
            Stroke(v,p,q,42f,new Color(1,.47f,.04f,brightness*.10f));
            Stroke(v,p,q,22f,new Color(1,.61f,.06f,brightness*.22f));
            Stroke(v,p,q,10f,new Color(1,.82f,.27f,brightness*.5f));
            Stroke(v,p,q,3f,new Color(1,.98f,.76f,brightness));
        }
        if(front)
        {
            for(int i=0;i<23;i++)
            {
                float cycle=Mathf.Repeat(seconds*.63f+i*.173f,1);
                float a=i*2.39996f;
                float radius=148+cycle*(25+(i%4)*9);
                var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius+Vector2.up*cycle*13;
                float alpha=Mathf.Sin(cycle*Mathf.PI)*(.55f+.3f*pulse);
                Star(v,p,2f+(i%3)*1.1f,new Color(1,.85f,.35f,alpha));
            }
            float head=seconds*2.8f;if(Mathf.Sin(head)<0)Star(v,Orbit(head),4,new Color(1,.96f,.65f,1));
        }
    }
    static Vector2 Orbit(float a)
    {
        Vector2 p=new Vector2(Mathf.Cos(a)*202,Mathf.Sin(a)*83);
        float angle=.28f;return new Vector2(p.x*Mathf.Cos(angle)-p.y*Mathf.Sin(angle),p.x*Mathf.Sin(angle)+p.y*Mathf.Cos(angle));
    }
    static void Stroke(VertexHelper v,Vector2 a,Vector2 b,float width,Color c)
    {
        var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;int k=v.currentVertCount;
        v.AddVert(a+n,c,new Vector2(0,1));v.AddVert(b+n,c,new Vector2(1,1));v.AddVert(b-n,c,new Vector2(1,-1));v.AddVert(a-n,c,new Vector2(0,-1));
        v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);
    }
    static void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color tint)
    {int k=v.currentVertCount;v.AddVert(a,tint,Vector2.zero);var clear=tint;clear.a=0;v.AddVert(b,clear,Vector2.zero);v.AddVert(c,tint,Vector2.zero);v.AddTriangle(k,k+1,k+2);}
    static void Star(VertexHelper v,Vector2 p,float size,Color c)
    {
        Stroke(v,p+Vector2.left*size,p+Vector2.right*size,.9f,c);Stroke(v,p+Vector2.down*size,p+Vector2.up*size,.9f,c);
        int k=v.currentVertCount;v.AddVert(p+Vector2.left*size*.45f,c,Vector2.zero);v.AddVert(p+Vector2.up*size*.45f,c,Vector2.zero);v.AddVert(p+Vector2.right*size*.45f,c,Vector2.zero);v.AddVert(p+Vector2.down*size*.45f,c,Vector2.zero);v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);
    }
    static void Ring(VertexHelper v,float inner,float outer,Color c)
    {
        for(int i=0;i<80;i++)
        {
            float a=i*Mathf.PI*2/80,b=(i+1)*Mathf.PI*2/80;var da=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int k=v.currentVertCount;var clear=c;clear.a=0;
            v.AddVert(da*inner,c,Vector2.zero);v.AddVert(db*inner,c,Vector2.zero);v.AddVert(db*outer,clear,Vector2.zero);v.AddVert(da*outer,clear,Vector2.zero);v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);
        }
    }
}
