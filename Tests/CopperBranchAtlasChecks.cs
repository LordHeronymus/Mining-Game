using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Colour-based readability audit at gameplay size for all 54 authored cells.
// This measures copper pigment, not alpha: every source is a complete earth tile.
public static class CopperBranchAtlasChecks
{
    public static string Main()
    {
        var atlas=new Texture2D(2,2); var report=new StringBuilder();
        try {
            atlas.LoadImage(File.ReadAllBytes("Assets/Resources/OreVeins/CopperEarthTiles/Combined.png"));
            if(atlas.width!=4050 || atlas.height!=2700)throw new Exception("Unexpected atlas dimensions");
            var sums=new double[3];int checks=0;
            for(int motif=0;motif<6;motif++)for(int phase=0;phase<9;phase++) {
                int count=0,minX=80,maxX=-1,minY=80,maxY=-1;
                for(int y=0;y<80;y++)for(int x=0;x<80;x++) {
                    var c=atlas.GetPixelBilinear((phase*450+16+(x+.5f)*418/80)/4050f,
                        (motif*450+16+(y+.5f)*418/80)/2700f);
                    if(c.r-c.b>40/255f && (c.r-c.g)/(c.r-c.b)>.65f) {
                        count++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    }
                }
                float fraction=count/6400f;sums[motif/2]+=fraction/18;
                if(fraction<.02f || maxX-minX<24 || maxY-minY<24)
                    throw new Exception($"Hidden/narrow copper: motif {motif} phase {phase} area {fraction:P1}, span {maxX-minX}/{maxY-minY}");
                checks++;report.AppendLine($"PASS motif {motif} phase {phase}: copper {fraction:P1}, span {maxX-minX}/{maxY-minY}px at 80px/cell");
            }
            if(sums[1]<sums[0]*1.3 || sums[2]<sums[1]*1.25)
                throw new Exception("Richness steps not sufficiently distinct: "+string.Join(", ",sums.Select(x=>x.ToString("P1"))));
            checks+=2;report.AppendLine("PASS average visible copper Small/Medium/Large: "+string.Join(" / ",sums.Select(x=>x.ToString("P1"))));
            report.AppendLine($"ALL PASSED: {checks} checks");return report.ToString();
        }
        catch(Exception e) {report.AppendLine("FAIL "+e);throw;}
        finally {File.WriteAllText("Temp/CopperBranchAtlasChecks.txt",report.ToString());UnityEngine.Object.DestroyImmediate(atlas);}
    }
}
