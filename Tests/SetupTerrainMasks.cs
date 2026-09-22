using System;
using UnityEditor;
using UnityEngine;

public static class SetupTerrainMasks
{
    const int Size=128;
    // Broad asymmetric lobes: centre, left span, right span, signed depth.
    // Every profile has the same value and zero slope at cell endpoints.
    const float Endpoint=.065f;
    static readonly float[][] Profiles={
        new[]{.38f,.37f,.55f,.105f, .81f,.15f,.18f,-.018f},
        new[]{.63f,.59f,.36f,.135f, .18f,.17f,.22f,-.020f},
        new[]{.49f,.48f,.50f,.075f, .81f,.15f,.18f,-.018f},
        new[]{.28f,.27f,.37f,.090f, .74f,.30f,.25f,.050f},
        new[]{.68f,.64f,.31f,.145f, .18f,.17f,.20f,-.020f},
        new[]{.40f,.39f,.58f,.120f, .82f,.15f,.17f,-.024f},
        new[]{.31f,.30f,.48f,.100f, .78f,.18f,.21f,-.023f},
        new[]{.56f,.53f,.43f,.080f, .20f,.19f,.22f,-.016f},
        new[]{.73f,.54f,.26f,.105f, .23f,.22f,.30f,.055f},
        new[]{.42f,.41f,.45f,.140f, .85f,.13f,.14f,-.018f},
        new[]{.51f,.50f,.48f,.095f, .19f,.18f,.22f,-.025f},
        new[]{.25f,.24f,.34f,.055f, .66f,.39f,.33f,.105f}
    };
    static float Lobe(float t,float centre,float left,float right,float amplitude)
    {
        float u=Mathf.Abs(t-centre)/(t<centre?left:right);
        if(u>=1)return 0;
        // Compact smooth bump, continuous slope at its tip and endpoints.
        float q=1-u*u;return amplitude*q*q;
    }
    static float Cut(float t,int variant)
    {
        var p=Profiles[variant];
        return Endpoint+Lobe(t,p[0],p[1],p[2],p[3])+Lobe(t,p[4],p[5],p[6],p[7]);
    }
    static float SmoothMin(float a,float b)
    {
        float h=Mathf.Max(.11f-Mathf.Abs(a-b),0)/.11f;
        return Mathf.Min(a,b)-h*h*.0275f;
    }
    public static object Main()
    {
        float minDepth=1,maxDepth=0;
        for(int v=0;v<Profiles.Length;v++)
        {
            if(Mathf.Abs(Cut(0,v)-Endpoint)>1e-6f||Mathf.Abs(Cut(1,v)-Endpoint)>1e-6f)
                throw new Exception("Mismatched mask endpoints");
            for(int i=0;i<=1000;i++)
            {
                float depth=Cut(i/1000f,v);minDepth=Mathf.Min(minDepth,depth);maxDepth=Mathf.Max(maxDepth,depth);
                if(depth<.025f||depth>.22f)throw new Exception("Contour exceeds rounded erosion bounds");
            }
        }
        const string path="Assets/Resources/TerrainEdgeMasks.asset";
        var generated=new Texture2DArray(Size,Size,Profiles.Length*16,TextureFormat.RHalf,false,true)
        {name="Terrain edge masks",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        float minimumCenter=1;
        for(int variant=0;variant<Profiles.Length;variant++)for(int mask=0;mask<16;mask++)
        {
            var pixels=new Color[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                float u=x/(float)(Size-1),v=y/(float)(Size-1),d=1;
                if((mask&1)!=0)d=SmoothMin(d,u-Cut(v,variant));
                if((mask&2)!=0)d=SmoothMin(d,1-u-Cut(v,(variant+3)%Profiles.Length));
                if((mask&4)!=0)d=SmoothMin(d,v-Cut(u,(variant+7)%Profiles.Length));
                if((mask&8)!=0)d=SmoothMin(d,1-v-Cut(u,(variant+10)%Profiles.Length));
                pixels[y*Size+x]=new Color(d,0,0,1);
                if(x==64&&y==64)minimumCenter=Mathf.Min(minimumCenter,d);
            }
            generated.SetPixels(pixels,variant*16+mask);
        }
        generated.Apply(false,false);
        if(minimumCenter<.20f)throw new Exception("Masks erode narrow tiles too much");
        var existing=AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        if(existing){EditorUtility.CopySerialized(generated,existing);UnityEngine.Object.DestroyImmediate(generated);EditorUtility.SetDirty(existing);}
        else AssetDatabase.CreateAsset(generated,path);
        AssetDatabase.SaveAssets();
        return new{path,masks=Profiles.Length*16,minimumCenter,minDepth,maxDepth};
    }
}



