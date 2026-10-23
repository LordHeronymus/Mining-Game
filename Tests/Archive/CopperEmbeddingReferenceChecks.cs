using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

// Independent image-space comparison with the approved prototype, not the
// extraction or production shader. Align by copper bounds, preserving aspect.
public static class CopperEmbeddingReferenceChecks
{
    const string Folder="Design/OreVeins/CopperL1EmbeddingV2/";
    static bool Copper(Color c)=>c.r>.55f && c.r-c.b>.32f && c.r-c.g>.18f && (c.r-c.g)/Mathf.Max(.001f,c.r-c.b)>.63f;
    static Texture2D Load(string path){var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(path));t.wrapMode=TextureWrapMode.Clamp;return t;}
    static Rect Bounds(Texture2D t)
    {
        int x0=t.width,y0=t.height,x1=0,y1=0;
        for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)if(Copper(t.GetPixel(x,y))){x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
        float w=x1-x0,h=y1-y0;return new Rect(x0-w*.12f,y0-h*.18f,w*1.24f,h*1.36f);
    }
    static Color[] Aligned(Texture2D t,Rect r,int n)
    {
        float edge=Mathf.Max(r.width,r.height);
        var a=new Color[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++){
            float u=(r.center.x+((x+.5f)/n-.5f)*edge)/t.width,v=(r.center.y+((y+.5f)/n-.5f)*edge)/t.height;
            a[y*n+x]=u<0||u>1||v<0||v>1?new Color(.10f,.10f,.10f):t.GetPixelBilinear(u,v);
        }return a;
    }
    public static string Main()
    {
        var target=Load(Folder+"Approved.png");var old=Load(Folder+"Before.png");
        var actual=Load(Folder+"Implemented/Single-group-closeup.png");Texture2D board=null;
        try{
            const int n=128;var textures=new[]{target,old,actual};var rectangles=textures.Select(Bounds).ToArray();
            var a=textures.Select((t,i)=>Aligned(t,rectangles[i],n)).ToArray();
            var masks=a.Select(p=>p.Select(Copper).ToArray()).ToArray();
            float IoU(int i)=>Enumerable.Range(0,n*n).Count(k=>masks[0][k]&&masks[i][k])/(float)Enumerable.Range(0,n*n).Count(k=>masks[0][k]||masks[i][k]);
            float LowerEarth(int i)=>Enumerable.Range(0,n*n).Where(k=>k/n>n*.13f&&k/n<n*.38f&&k%n>n*.15f&&k%n<n*.85f).Average(k=>masks[i][k]?0f:1f);
            float oldScore=IoU(1),score=IoU(2),earth=LowerEarth(0),oldEarth=LowerEarth(1),newEarth=LowerEarth(2);
            float aspectError=Mathf.Abs(rectangles[0].width/rectangles[0].height-rectangles[2].width/rectangles[2].height);
            string report=$"Copper silhouette intersection/union (aligned): old={oldScore:F3}; new={score:F3}\nLower foreground earth fraction: target={earth:F3}; old={oldEarth:F3}; new={newEarth:F3}\nAspect-ratio absolute difference: {aspectError:F3}\n";
            // Equal framing for human review, without replacing production pixels.
            const int side=480;board=new Texture2D(side*3,side,TextureFormat.RGBA32,false);
            for(int j=0;j<3;j++){var p=Aligned(textures[j],rectangles[j],side);board.SetPixels(j*side,0,side,side,p);}board.Apply();
            File.WriteAllBytes(Folder+"Comparison-target-old-new.png",board.EncodeToPNG());
            bool pass=score>=.70f && score>oldScore && Mathf.Abs(newEarth-earth)<.10f && aspectError<.15f;
            report+=pass?"PASS reference geometry, foreground coverage and proportions\n":"FAIL reference agreement too weak; adjust and rerender\n";
            File.WriteAllText(Folder+"Reference-checks.txt",report);
            if(!pass)throw new Exception(report);return report;
        }finally{Object.DestroyImmediate(target);Object.DestroyImmediate(old);Object.DestroyImmediate(actual);if(board)Object.DestroyImmediate(board);}
    }
}
