using System;
using System.IO;
using UnityEngine;

public static class CopperV3PrototypeChecks
{
    const string Root="Design/OreVeins/CopperL1OriginalDirt/V3/";
    static bool Metal(Color c)=>c.r>.35f && c.r-c.b>.16f && (c.r-c.g)/Mathf.Max(.001f,c.r-c.b)>.65f;
    public static string Main(bool validate=false)
    {
        var prototype=new Texture2D(2,2);var before=new Texture2D(2,2);var actual=new Texture2D(2,2);
        try {
            prototype.LoadImage(File.ReadAllBytes(Root+"Approved-prototype.png"));
            before.LoadImage(File.ReadAllBytes(Root+"Before-closeup.png"));
            actual.LoadImage(File.ReadAllBytes(Root+"Implemented/L1/L1-closeup.png"));
            double[] amounts=new double[3],brightness=new double[3],highlights=new double[3],widths=new double[3];
            var masks=new[]{new bool[307200],new bool[307200],new bool[307200]};
            var regions=new[]{new double[48],new double[48],new double[48]};
            var images=new[]{before,actual,prototype};
            // Identical normalized camera framing, resampled only for measurement.
            // Copper masks measure metal rather than brown dirt saturation.
            for(int n=0;n<3;n++)for(int y=0;y<480;y++)for(int x=0;x<640;x++) {
                var c=images[n].GetPixelBilinear((x+.5f)/640,(y+.5f)/480);
                if(!Metal(c))continue;
                masks[n][y*640+x]=true;regions[n][y/80*8+x/80]++;
                amounts[n]++;double l=c.r*.2126+c.g*.7152+c.b*.0722;brightness[n]+=l;if(l>.48)highlights[n]++;
            }
            // Chamfer distance estimates ribbon thickness on equally sized
            // captures, independently of the baker's native fissure masks.
            for(int n=0;n<3;n++){
                var distances=new float[307200];for(int i=0;i<distances.Length;i++)distances[i]=masks[n][i]?1000:0;
                for(int y=0;y<480;y++)for(int x=0;x<640;x++){
                    int i=y*640+x;if(!masks[n][i])continue;
                    if(x>0)distances[i]=Mathf.Min(distances[i],distances[i-1]+1);
                    if(y>0){distances[i]=Mathf.Min(distances[i],distances[i-640]+1);if(x>0)distances[i]=Mathf.Min(distances[i],distances[i-641]+1.4142f);if(x<639)distances[i]=Mathf.Min(distances[i],distances[i-639]+1.4142f);}
                }
                for(int y=479;y>=0;y--)for(int x=639;x>=0;x--){
                    int i=y*640+x;if(!masks[n][i])continue;
                    if(x<639)distances[i]=Mathf.Min(distances[i],distances[i+1]+1);
                    if(y<479){distances[i]=Mathf.Min(distances[i],distances[i+640]+1);if(x>0)distances[i]=Mathf.Min(distances[i],distances[i+639]+1.4142f);if(x<639)distances[i]=Mathf.Min(distances[i],distances[i+641]+1.4142f);}
                    widths[n]+=2*distances[i];
                }
                widths[n]/=amounts[n];
            }
            double covariance=0,varActual=0,varProto=0;
            for(int i=0;i<48;i++){double a=regions[1][i]-amounts[1]/48,p=regions[2][i]-amounts[2]/48;covariance+=a*p;varActual+=a*a;varProto+=p*p;}
            double correlation=covariance/Math.Sqrt(varActual*varProto);
            var richnessAmounts=new double[3];
            for(int n=0;n<3;n++){
                var t=new Texture2D(2,2);
                try{
                    t.LoadImage(File.ReadAllBytes(Root+"Implemented/L1/"+new[]{"Small","Medium","Large"}[n]+"-comparison.png"));
                    foreach(var c in t.GetPixels())if(Metal(c))richnessAmounts[n]++;
                    richnessAmounts[n]/=t.width*t.height;
                }finally{UnityEngine.Object.DestroyImmediate(t);}
            }
            string report=$"Copper exposure before / actual / approved V3: {amounts[0]/307200:P2} / {amounts[1]/307200:P2} / {amounts[2]/307200:P2}\n"+
                $"Actual/prototype area: {amounts[1]/amounts[2]:F3}; actual/before area: {amounts[1]/amounts[0]:F3}\n"+
                $"Metal luminance before / actual / prototype: {brightness[0]/amounts[0]:F3} / {brightness[1]/amounts[1]:F3} / {brightness[2]/amounts[2]:F3}\n"+
                $"Ribbon thickness before / actual / prototype: {widths[0]:F2} / {widths[1]:F2} / {widths[2]:F2}px\n"+
                $"Metal highlights actual / prototype: {highlights[1]/amounts[1]:P2} / {highlights[2]/amounts[2]:P2}; spatial distribution correlation: {correlation:F3}\n"+
                $"Actual equal-layout copper exposure Small / Medium / Large: {richnessAmounts[0]:P2} / {richnessAmounts[1]:P2} / {richnessAmounts[2]:P2}\n";
            File.WriteAllText("Temp/CopperV3PrototypeChecks.txt",report);
            if(validate && (amounts[1]/amounts[2]<.85 || amounts[1]/amounts[2]>1.15 || amounts[1]/amounts[0]<1.6))throw new Exception("V3 copper coverage still differs from approved prototype: "+report);
            if(validate && (widths[1]/widths[2]<.70 || widths[1]/widths[2]>1.40 || correlation<.80 || Math.Abs(brightness[1]/amounts[1]-brightness[2]/amounts[2])>.04 || Math.Abs(highlights[1]/amounts[1]-highlights[2]/amounts[2])>.06))throw new Exception("V3 ribbon structure or metal lighting differs from approved prototype: "+report);
            if(validate && !(richnessAmounts[0]<richnessAmounts[1] && richnessAmounts[1]<richnessAmounts[2]))throw new Exception("Richness is not readable in actual rendering: "+report);
            if(validate)File.AppendAllText("Temp/CopperV3PrototypeChecks.txt","ALL PASSED: coverage, width, distribution, luminance and highlights compared with approved V3; richness remains readable.\n");
            return report;
        } finally {UnityEngine.Object.DestroyImmediate(prototype);UnityEngine.Object.DestroyImmediate(before);UnityEngine.Object.DestroyImmediate(actual);}
    }
}
