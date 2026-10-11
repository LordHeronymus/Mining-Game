using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;

public static class CopperOriginalDirtVisualChecks
{
    static string Output;
    static readonly List<string> passed=new();
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);passed.Add("PASS "+message);}
    public static string Main(int testLayer=1)
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        Output="Assets/Design/OreVeins/CopperL1OriginalDirt/Implemented/L"+testLayer;passed.Clear();Directory.CreateDirectory(Output);
        var previous=SceneManager.GetActiveScene();
        var sourceScene=SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool opened=!sourceScene.IsValid() || !sourceScene.isLoaded;
        if(opened)sourceScene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
        var source=sourceScene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MapGenerator>(true)).First();
        var host=source.GetComponent<UniformStoneAppearance>();
        var lights=Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var states=lights.Select(x=>x.enabled).ToArray();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var disposables=new List<Object>();var oldTarget=RenderTexture.active;
        bool oldAsync=ShaderUtil.allowAsyncCompilation;
        int oldQuality=QualitySettings.globalTextureMipmapLimit;
        Action<ScriptableRenderContext,Camera> overrides=null;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            foreach(var light in lights)light.enabled=false;
            SceneManager.SetActiveScene(scene);
            var grid=new GameObject("Copper tile fixture",typeof(Grid));
            grid.GetComponent<Grid>().cellSize=new Vector3(1.1f,1.1f,0);
            var root=new GameObject("Earth tile fixture",typeof(Tilemap),typeof(TilemapRenderer));
            root.transform.SetParent(grid.transform,false);root.layer=31;
            var terrain=root.GetComponent<Tilemap>();terrain.orientation=Tilemap.Orientation.Custom;
            terrain.orientationMatrix=Matrix4x4.Scale(new Vector3(2.2f,2.2f,1));
            var map=root.AddComponent<MapGenerator>();map.enabled=false;
            map.registry=source.registry;map.seed=74123;map.oreScale=1;
            map.mapWidth=16;map.mapHeight=12;
            var ores=map.EnsureOreOverlay();ores.gameObject.layer=31;
            var visual=root.GetComponent<OreOverlayAppearance>();
            var material=new Material(source.GetComponent<OreOverlayAppearance>().OverlayMaterial);disposables.Add(material);
            visual.OverlayMaterial=material;
            var groundMaterial=new Material(Shader.Find("Mining Game/Dirt Terrain Lit"));disposables.Add(groundMaterial);
            terrain.GetComponent<TilemapRenderer>().sharedMaterial=groundMaterial;
            ores.GetComponent<TilemapRenderer>().sortingOrder=1;
            var mask=new Texture2D(16,12,TextureFormat.RGBA32,false,true)
                {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};disposables.Add(mask);
            var pixels=Enumerable.Repeat(new Color(1,1,0,0),192).ToArray();
            var props=new MaterialPropertyBlock();
            props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,16,12));
            props.SetTexture("_TestOccupancy",mask);
            props.SetTexture("_TestStoneTex",host.texture);props.SetTexture("_SurfaceDirtTex",host.dirtTexture);
            props.SetTexture("_LayerOneTex",host.layerOneTexture);props.SetTexture("_LayerThreeTex",host.layerThreeTexture);
            props.SetTexture("_LayerFourTex",host.layerFourTexture);
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0),source.surfaceDirtTile);
            void ApplyHost(int layer)
            {
                for(int n=0;n<pixels.Length;n++)pixels[n]=new Color(1,layer==1?1:0,layer==2?1:0,layer==4?128f/255:layer==5?1:0);
                mask.SetPixels(pixels);mask.Apply();terrain.GetComponent<TilemapRenderer>().SetPropertyBlock(props);
                visual.ApplyTerrain(props);
            }
            var lightObject=new GameObject("Copper tile light",typeof(Light2D));var lamp=lightObject.GetComponent<Light2D>();
            lightObject.layer=31;
            lamp.lightType=Light2D.LightType.Global;lamp.intensity=1;
            var camera=new GameObject("Copper tile camera",typeof(Camera)).GetComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=6.6f;camera.transform.position=new Vector3(8.8f,6.6f,-10);
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;
            const int width=1280,height=960,cellPixels=80;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);disposables.Add(rt);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);disposables.Add(image);camera.targetTexture=rt;
            bool enabled=true, groundOnly=false;
            overrides=(context,view)=>
            {
                if(view!=camera)return;
                var block=new MaterialPropertyBlock();ores.GetComponent<TilemapRenderer>().GetPropertyBlock(block);
                block.SetFloat("_CopperEarthTilesEnabled",enabled?1:0);block.SetFloat("_CopperRockTilesEnabled",enabled?1:0);
                if(visual.CopperSurface){block.SetFloat("_CopperSurfacePass",1);visual.CopperSurface.SetPropertyBlock(block);visual.CopperSurface.enabled=!groundOnly;}
            };
            RenderPipelineManager.beginCameraRendering+=overrides;
            Color32[] Render(string name)
            {
                terrain.RefreshAllTiles();ores.RefreshAllTiles();
                visual.ApplyTo(ores.GetComponent<TilemapRenderer>());visual.PrepareVeins(camera);camera.Render();
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                if(name!=null)File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
                return image.GetPixels32();
            }
            var copper=map.registry.GetById(BlockType.CopperOre);
            var sheets=new Texture2D[6];
            var names=new[]{"Small-1","Small-2","Medium-1","Medium-2","Large-1","Large-2"};
            string sourceFolder=testLayer==1?"Assets/Design/OreVeins/CopperL1OriginalDirt/Sources":
                "Assets/Design/OreVeins/CopperAllLayers/Sources/"+new[]{"Clay","Gray","Warm","Deep"}[testLayer-2];
            for(int m=0;m<6;m++){
                sheets[m]=new Texture2D(2,2,TextureFormat.RGBA32,true);disposables.Add(sheets[m]);
                sheets[m].LoadImage(File.ReadAllBytes(sourceFolder+"/"+names[m]+".png"));sheets[m].Apply(true,false);
            }
            var cells=new List<Vector3Int>();
            for(int x=2;x<14;x++)
            {
                int y=9-(x-2)*6/11;cells.Add(new Vector3Int(x,y,0));
                if(x>5 && x<11)cells.Add(new Vector3Int(x,y-1,0));
            }
            cells.AddRange(new[]{new Vector3Int(7,7,0),new Vector3Int(7,8,0),new Vector3Int(6,9,0),
                new Vector3Int(10,3,0),new Vector3Int(9,2,0)});
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
            ApplyHost(testLayer);var actual=Render("Whole-vein-L1"); groundOnly=true;var ground=Render("Ground-reference");groundOnly=false;
            // Capture identical cell positions for a meaningful richness comparison.
            var originalTiles=cells.Select(cell=>ores.GetTile(cell)).ToArray();
            var richnessTiles=new[]{copper.smallOre[0],copper.mediumOre[0],copper.richOre[0]};
            var originalDirt=new Texture2D(2,2,TextureFormat.RGBA32,true);disposables.Add(originalDirt);
            originalDirt.LoadImage(File.ReadAllBytes("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png"));originalDirt.Apply(true,false);
            void CheckOriginalSoil(Color32[] rendered,string phase){
                int soil=0,borderSoil=0;double error=0,borderError=0;int worst=0;
                foreach(var cell in cells)for(int y=1;y<79;y++)for(int x=1;x<79;x++){
                    int px=(cell.x%3+3)%3,py=(cell.y%3+3)%3;
                    float u=(px+(x+.5f)/80)/3,v=(py+(y+.5f)/80)/3;
                    var hostPixel=originalDirt.GetPixelBilinear(u,v,2);
                    // Independent soil samples must agree with the original
                    // reference in ALL six images and a surrounding footprint.
                    bool untouched=true;
                    foreach(var sheet in sheets){
                        foreach(var d in new[]{Vector2.zero,Vector2.left,Vector2.right,Vector2.up,Vector2.down}){
                            float su=u+d.x/240,sv=v+d.y/240;
                            var a=sheet.GetPixelBilinear(su,sv,2);var b=originalDirt.GetPixelBilinear(su,sv,2);
                            if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>.006f){untouched=false;break;}
                        }
                        if(!untouched)break;
                    }
                    if(!untouched)continue;
                    int i=(cell.y*80+y)*width+cell.x*80+x;var c=rendered[i];var g=ground[i];
                    int delta=Math.Abs(c.r-g.r)+Math.Abs(c.g-g.g)+Math.Abs(c.b-g.b);
                    error+=delta;soil++;worst=Math.Max(worst,delta);
                    if(x<6 || x>=74 || y<6 || y>=74){borderSoil++;borderError+=delta;}
                }
                Check(soil>20000 && error/soil<3,phase+": original dirt geometry and pixels match actual ground, samples="+soil+", mean RGB delta="+(error/soil).ToString("F3")+", worst="+worst);
                Check(borderSoil>2000 && borderError/borderSoil<3,phase+": original soil at copper-cell boundaries matches actual neighbouring ground, samples="+borderSoil+", mean RGB delta="+(borderError/borderSoil).ToString("F3"));
            }
            CheckOriginalSoil(actual,"Mixed richness");
            void CheckEarthTone(Color32[] rendered,string richness)
            {
                if(testLayer!=1)return;
                var earthSum=Vector3.zero;var hostSum=Vector3.zero;int count=0;
                foreach(var cell in cells)for(int y=4;y<76;y++)for(int x=4;x<76;x++){
                    int i=(cell.y*80+y)*width+cell.x*80+x;
                    Color c=rendered[i],reference=ground[i];
                    Color.RGBToHSV(c,out float h,out float s,out float v);
                    Color.RGBToHSV(reference,out float th,out float ts,out float tv);
                    float chroma=Mathf.Max(.001f,c.r-c.b);
                    // The independent render comparison excludes metal, mixed
                    // metal-edge pixels and neutral grey pebbles.
                    if((c.r-c.g)/chroma>.61f || s<.30f || ts<.30f)continue;
                    earthSum+=new Vector3(h,s,v);hostSum+=new Vector3(th,ts,tv);count++;
                }
                var delta=earthSum/count-hostSum/count;
                Check(count>10000 && Mathf.Abs(delta.x)<.006f && Mathf.Abs(delta.y)<.025f && Mathf.Abs(delta.z)<.025f,
                    richness+": rendered copper-cell dirt matches ordinary dirt HSV, delta="+delta.ToString("F4")+
                    ", actual="+(earthSum/count).ToString("F4")+", host="+(hostSum/count).ToString("F4")+", samples="+count);
            }
            CheckEarthTone(actual,"Mixed richness");
            // Independently compare rendered complete cells against the authored
            // sheets, including the former 20%-wide replacement band.
            bool IsCopper(Color c)=>c.r-c.b>40/255f && (c.r-c.g)/Mathf.Max(.001f,c.r-c.b)>.65f;
            void CheckArtwork(Color32[] rendered,Vector3Int offset,string phase)
            {
                int expectedBorder=0,retainedBorder=0,expectedInner=0,retainedInner=0;
                double allError=0;int samples=0;
                var orientationErrors=new double[8];
                for(int n=0;n<cells.Count;n++){
                    var cell=cells[n];var world=cell+offset;
                    int group=Array.IndexOf(richnessTiles,originalTiles[n]);
                    int px=(world.x%3+3)%3,py=(world.y%3+3)%3;
                    double bestError=double.MaxValue;int best=group*2;
                    // Independently score all quarter turns and mirrored turns
                    // against the existing artwork. No shader hash is reproduced.
                    for(int t=0;t<8;t++){
                        double orientationBest=double.MaxValue;
                        for(int m=group*2;m<group*2+2;m++){
                            double error=0;
                            for(int y=4;y<76;y+=3)for(int x=4;x<76;x+=3){
                                float u=(x+.5f)/80,v=(y+.5f)/80;
                                if(t>=4)u=1-u;
                                for(int turn=0;turn<t%4;turn++){float old=u;u=v;v=1-old;}
                                Color a=rendered[(cell.y*80+y)*width+cell.x*80+x];
                                Color b=sheets[m].GetPixelBilinear((px+u)/3,(py+v)/3,2);
                                error+=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                            }
                            orientationBest=Math.Min(orientationBest,error);
                        }
                        orientationErrors[t]+=orientationBest;
                    }
                    for(int m=group*2;m<group*2+2;m++){
                        double error=0;
                        for(int y=4;y<76;y+=3)for(int x=4;x<76;x+=3){
                            Color a=rendered[(cell.y*80+y)*width+cell.x*80+x];
                            Color b=sheets[m].GetPixelBilinear((px+(x+.5f)/80)/3,(py+(y+.5f)/80)/3,2);
                            error+=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                        }
                        if(error<bestError){bestError=error;best=m;}
                    }
                    for(int y=2;y<78;y++)for(int x=2;x<78;x++){
                        Color a=rendered[(cell.y*80+y)*width+cell.x*80+x];
                        Color b=sheets[best].GetPixelBilinear((px+(x+.5f)/80)/3,(py+(y+.5f)/80)/3,2);
                        // L1 intentionally remaps only the soil palette. Compare
                        // original metal colour there; orientation scoring below
                        // still covers the whole source image, including its soil.
                        allError+=(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))*255;samples++;
                        if(!IsCopper(b))continue;
                        if(x<16 || x>=64 || y<16 || y>=64){expectedBorder++;if(IsCopper(a))retainedBorder++;}
                        else {expectedInner++;if(IsCopper(a))retainedInner++;}
                    }
                }
                // CPU source mips, production compression/filtering and copper's
                // colour grade differ. Allow ten byte levels per channel on average;
                // separately require the peripheral copper to survive the render.
                // L1's intended soil colour remapping is validated independently
                // against the actual ground; copper colour uses the prior GPU render.
                Check(allError/samples<30,phase+": complete cells match baked original-dirt artwork, mean RGB error="+(allError/samples).ToString("F2"));
                Check(expectedBorder>80 && retainedBorder>=expectedBorder*.75,
                    phase+": authored peripheral copper retained "+retainedBorder+"/"+expectedBorder);
                Check(expectedInner>80 && retainedInner>=expectedInner*.75,
                    phase+": authored interior copper retained "+retainedInner+"/"+expectedInner);
                double incorrect=orientationErrors.Skip(1).Min();
                Check(orientationErrors[0]<incorrect*.85,phase+": original orientation matches better than any rotation or reflection ("+
                    orientationErrors[0].ToString("F1")+" vs "+incorrect.ToString("F1")+")");
            }
            CheckArtwork(actual,Vector3Int.zero,"Normal coordinates");
            for(int richness=0;richness<3;richness++) {
                foreach(var cell in cells)ores.SetTile(cell,richnessTiles[richness]);
                string label=new[]{"Small","Medium","Large"}[richness];
                var comparison=Render(label+"-comparison");CheckEarthTone(comparison,label);CheckOriginalSoil(comparison,label);
            }
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],originalTiles[n]);
            Render(null);
            Check(UnityEditor.ShaderUtil.GetShaderMessages(material.shader).All(x=>x.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Production shader compiles");
            var bound=new MaterialPropertyBlock();ores.GetComponent<TilemapRenderer>().GetPropertyBlock(bound);
            Check(bound.GetFloat("_CopperEarthTilesEnabled")==1 && bound.GetTexture("_CopperEarthCombined"),"Real renderer binds complete earth/copper atlas");
            terrain.GetComponent<TilemapRenderer>().enabled=false;
            // The approved branching art intentionally has soil between its bands.
            // Validate actual copper colour and spread, not an opaque central disk.
            foreach(var cell in cells) {
                int visible=0,minX=80,maxX=-1,minY=80,maxY=-1;
                for(int y=0;y<80;y++)for(int x=0;x<80;x++) {
                    var c=actual[(cell.y*80+y)*width+cell.x*80+x];
                    if(c.r-c.b>40 && (c.r-c.g)/(float)(c.r-c.b)>.65f) {
                        visible++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    }
                }
                Check(visible>=(testLayer==1?128:80) && maxX-minX>=24 && maxY-minY>=24,
                    "Visible copper bands span at least 30% of cell in both axes "+cell+": "+visible+" pixels");
            }
            var silhouette=Render("Tile-silhouettes");
            foreach(var cell in cells)
            {
                int count=0,core=0,hidden=0;
                for(int y=0;y<cellPixels;y++)for(int x=0;x<cellPixels;x++)
                {
                    var c=silhouette[(cell.y*cellPixels+y)*width+cell.x*cellPixels+x];
                    if(c.r+c.g+c.b>12)count++;
                    if(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(40,40))<=12)
                    {core++;if(c.r+c.g+c.b<=12)hidden++;}
                }
                Check(hidden==0,"Terrain centre remains opaque "+cell);
                Check(count==cellPixels*cellPixels,"One complete opaque earth/copper tile "+cell);
            }
            foreach(float size in new[]{.5f,1.35f,3f})
            {
                map.oreScale=size;
                var scaled=Render(null);int hidden=0,edge=0;
                foreach(var cell in cells)for(int y=0;y<80;y++)for(int x=0;x<80;x++)
                {
                    var c=scaled[(cell.y*80+y)*width+cell.x*80+x];
                    bool opaque=c.r+c.g+c.b>12;
                    if(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(40,40))<=12 && !opaque)hidden++;
                    if((x<2 || x>=78 || y<2 || y>=78) && !c.Equals(silhouette[(cell.y*80+y)*width+cell.x*80+x]))edge++;
                }
                Check(hidden==0,"Terrain centres remain opaque at ore size "+size);
                Check(edge==0,"Fixed baked earth borders at ore size "+size);
            }
            map.oreScale=1;
            terrain.GetComponent<TilemapRenderer>().enabled=true;
            var stable=Render(null);Check(actual.SequenceEqual(stable),"Repeated rendering is stable");
            camera.transform.position+=new Vector3(1.1f,0,0);var moved=Render(null);
            int differences=0;for(int y=0;y<height;y++)for(int x=80;x<width;x++)
                if(!actual[y*width+x].Equals(moved[y*width+x-80]))differences++;
            Check(differences<2500,"Copper artwork stays fixed in world coordinates");
            camera.transform.position-=new Vector3(1.1f,0,0);
            // Move the fixture to negative cell coordinates and compare the baked
            // inner earth corners with the original world-space ground material.
            var offset=new Vector3Int(-5,-7,0);
            terrain.ClearAllTiles();ores.ClearAllTiles();
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0)+offset,source.surfaceDirtTile);
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n]+offset,n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
            props.SetVector("_TestBounds",new Vector4(-5,-7,16,12));ApplyHost(testLayer);
            camera.transform.position+=new Vector3(-5.5f,-7.7f,0);
            var negative=Render("Negative-coordinates");groundOnly=true;var negativeGround=Render(null);groundOnly=false;
            CheckArtwork(negative,offset,"Negative coordinates");
            foreach(int quality in new[]{0,2}) {
                QualitySettings.globalTextureMipmapLimit=quality;
                var q=Render(null);groundOnly=true;var qGround=Render(null);groundOnly=false;
                CheckArtwork(q,offset,"Texture quality "+quality);
            }
            QualitySettings.globalTextureMipmapLimit=oldQuality;
            camera.transform.position-=new Vector3(-5.5f,-7.7f,0);
            terrain.ClearAllTiles();ores.ClearAllTiles();
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0),source.surfaceDirtTile);
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
            props.SetVector("_TestBounds",new Vector4(0,0,16,12));ApplyHost(testLayer);
            var removed=cells[7];var oldOre=ores.GetTile(removed);terrain.SetTile(removed,null);ores.SetTile(removed,null);
            pixels[removed.y*16+removed.x]=Color.clear;mask.SetPixels(pixels);mask.Apply();visual.ApplyTerrain(props);
            terrain.GetComponent<TilemapRenderer>().enabled=false;var mined=Render("Mined-silhouette");
            int remnants=0;for(int y=0;y<80;y++)for(int x=0;x<80;x++)
                {var c=mined[(removed.y*80+y)*width+removed.x*80+x];if(c.r+c.g+c.b>12)remnants++;}
            Check(remnants==0,"Mining removes every ore pixel in the mined cell");
            var neighbour=cells[8];int changes=0;for(int y=0;y<80;y++)for(int x=0;x<80;x++)
                if(!mined[(neighbour.y*80+y)*width+neighbour.x*80+x].Equals(silhouette[(neighbour.y*80+y)*width+neighbour.x*80+x]))changes++;
            Check(changes<80*80*.3,"Mining only changes neighbour terrain cut and bevel");
            var edgeMasks=Resources.Load<Texture2DArray>("TerrainEdgeMasks");
            Check(edgeMasks,"Production terrain edge masks exist");
            props.SetFloat("_UseTerrainMasks",1);props.SetTexture("_TerrainEdgeMasks",edgeMasks);
            props.SetVector("_TerrainEdgeTuning",new Vector4(1,1,1,0));ApplyHost(testLayer);
            // ApplyHost replaces material channels only; restore the missing cell occupancy.
            pixels[removed.y*16+removed.x]=Color.clear;mask.SetPixels(pixels);mask.Apply();visual.ApplyTerrain(props);
            var masked=Render("Masked-mined-silhouette");
            int squareCorners=0,coreLoss=0;
            for(int y=0;y<80;y++)for(int x=0;x<80;x++) {
                var c=masked[(removed.y*80+y)*width+removed.x*80+x];if(c.r+c.g+c.b>12)squareCorners++;
                var b=masked[(neighbour.y*80+y)*width+neighbour.x*80+x];
                if(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(40,40))<=12 && b.r+b.g+b.b<=12)coreLoss++;
            }
            Check(squareCorners==0 && coreLoss==0,"Production masked mining edge leaves no copper in the hole and preserves neighbour core");
            props.SetFloat("_UseTerrainMasks",0);
            terrain.SetTile(removed,source.surfaceDirtTile);ores.SetTile(removed,oldOre);ApplyHost(testLayer);
            terrain.GetComponent<TilemapRenderer>().enabled=true;
            camera.orthographicSize=3.3f;camera.transform.position=new Vector3(8.25f,6.6f,-10);
            Render("L1-closeup");
            camera.orthographicSize=6.6f;camera.transform.position=new Vector3(8.8f,6.6f,-10);
            lamp.intensity=0;var dark=Render("Darkness");Check(dark.All(c=>c.r+c.g+c.b<8),"Copper has no emission in total darkness");
            lamp.intensity=1;lamp.color=new Color(.3f,.6f,1);var tinted=Render("Blue-light");
            Check(!actual.SequenceEqual(tinted),"Copper reacts to real light colour");lamp.color=Color.white;
            Check(bound.GetFloat("_CopperRockTilesEnabled")==1 && bound.GetTexture("_CopperRockCombined") is Texture2DArray, "Real renderer binds four complete rock/copper families");
            // Material transitions may filter terrain edges, but the interior of
            // each copper cell must retain its centre-selected artwork family.
            var families=new Color32[5][];
            for(int h=1;h<=5;h++){ApplyHost(h);families[h-1]=Render(null);}
            Check(!families[0].SequenceEqual(families[1]) && !families[1].SequenceEqual(families[2])
                && !families[2].SequenceEqual(families[3]) && !families[3].SequenceEqual(families[4]),
                "All five host families render distinct rock artwork");
            mask.filterMode=FilterMode.Bilinear;
            for(int n=0;n<pixels.Length;n++){
                int h=Math.Min(5,n%16/3+1);
                pixels[n]=new Color(1,h==1?1:0,h==2?1:0,h==4?128f/255:h==5?1:0);
            }
            mask.SetPixels(pixels);mask.Apply();visual.ApplyTerrain(props);
            var mixed=Render("Mixed-host-transition");int switched=0;
            foreach(var cell in cells)for(int y=24;y<56;y++)for(int x=24;x<56;x++){
                int index=(cell.y*80+y)*width+cell.x*80+x;
                var a=mixed[index];var b=families[Math.Min(4,cell.x/3)][index];
                if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>3)switched++;
            }
            Check(switched==0,"Bilinear layer transitions preserve each cell's own host artwork");
            mask.filterMode=FilterMode.Point;
            ApplyHost(testLayer);
            const System.Reflection.BindingFlags privateMember=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var cursorRoot=new GameObject("Real cursor fixture",typeof(Tilemap),typeof(TilemapRenderer));
            cursorRoot.transform.SetParent(grid.transform,false);cursorRoot.layer=31;
            var cursorMap=cursorRoot.GetComponent<Tilemap>();cursorMap.orientation=terrain.orientation;
            cursorMap.orientationMatrix=terrain.orientationMatrix;
            var cursorRenderer=cursorRoot.GetComponent<TilemapRenderer>();cursorRenderer.sortingOrder=1;
            var miner=new GameObject("Real tile miner fixture",typeof(BoxCollider2D)).AddComponent<TileMiner>();
            void Set(string name,object value)=>typeof(TileMiner).GetField(name,privateMember).SetValue(miner,value);
            Set("tilemap",terrain);Set("highlightMap",cursorMap);Set("map",map);
            miner.cursorPulseAlphaRange=Vector2.one;miner.cursorPulseSize=1;miner.cursorGlowStrength=1;
            typeof(TileMiner).GetMethod("CreateHighlightTiles",privateMember).Invoke(miner,null);
            foreach(string name in new[]{"normalHighlightTile","smartHighlightTile"}){
                var tile=(Tile)typeof(TileMiner).GetField(name,privateMember).GetValue(miner);
                disposables.Add(tile.sprite.texture);disposables.Add(tile.sprite);disposables.Add(tile);
            }
            disposables.Add((Material)typeof(TileMiner).GetField("highlightMaterial",privateMember).GetValue(miner));
            var selected=cells[7];
            for(int mode=0;mode<2;mode++){
                Set("smartCursor",mode==1);
                typeof(TileMiner).GetMethod("ShowHighlight",privateMember).Invoke(miner,new object[]{selected,selected});
                var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;
                camera.transform.position=terrain.GetCellCenterWorld(selected)+Vector3.back*10;camera.orthographicSize=2.2f;
                cursorRenderer.enabled=false;var withoutCursor=Render(null);
                groundOnly=true;var bareGround=Render(null);groundOnly=false;
                cursorRenderer.enabled=true;
                var marked=Render(mode==0?"Cursor-Normal-closeup":"Cursor-Smart-closeup");
                Check(cursorRenderer.sortingLayerID==visual.CopperSurface.sortingLayerID &&
                    cursorRenderer.sortingOrder>visual.CopperSurface.sortingOrder,
                    "Real "+(mode==0?"normal":"smart")+" cursor sorts above copper surface");
                groundOnly=true;var referenceCursor=Render(null);groundOnly=false;
                var expected=new int[4];var visible=new int[4];
                // At the user's normal close-up scale each cell spans 240 pixels.
                for(int y=-3;y<243;y++)for(int x=-3;x<243;x++){
                    int index=(height/2-120+y)*width+width/2-120+x;
                    var gold=referenceCursor[index];var bare=bareGround[index];
                    int change=Math.Abs(gold.r-bare.r)+Math.Abs(gold.g-bare.g)+Math.Abs(gold.b-bare.b);
                    // Check the luminous stroke core; halo contrast legitimately
                    // changes with the soil colour beneath its transparent pixels.
                    if(change<30 || gold.r<200 || gold.g<140 || gold.r-gold.b<45)continue;
                    int side=x<4?0:x>=236?1:y<4?2:y>=236?3:-1;if(side<0)continue;
                    expected[side]++;
                    var a=marked[index];var b=withoutCursor[index];
                    int shown=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                    if(shown>=20 && a.r>=195 && a.g>=130 && a.r-a.b>=40)visible[side]++;
                }
                for(int side=0;side<4;side++)Check(expected[side]>8 && visible[side]>=expected[side]*.85,
                    "Cursor mode "+mode+" visible over copper on side "+side+": "+visible[side]+"/"+expected[side]);
                camera.transform.position=oldPosition;camera.orthographicSize=oldSize;
                cursorRenderer.enabled=false;
            }
            var iron=map.registry.GetById(BlockType.IronOre);ores.ClearAllTiles();
            foreach(var cell in cells)ores.SetTile(cell,iron.richOre[0]);
            enabled=true;var other=Render("Iron-unchanged");enabled=false;var otherOld=Render(null);
            Check(other.SequenceEqual(otherOld),"Other ore types retain their existing representation");
            File.WriteAllText("Temp/CopperOriginalDirtVisualChecks-L"+testLayer+".txt",string.Join("\n",passed)+"\nALL PASSED\n");
            return $"{passed.Count} render checks passed";
        }
        catch(Exception e){File.WriteAllText("Temp/CopperOriginalDirtVisualChecks-L"+testLayer+".txt",string.Join("\n",passed)+"\nFAIL "+e);throw;}
        finally
        {
            if(overrides!=null)RenderPipelineManager.beginCameraRendering-=overrides;
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);
            foreach(var obj in disposables)if(obj)Object.DestroyImmediate(obj);
            if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(opened)EditorSceneManager.CloseScene(sourceScene,true);
            for(int n=0;n<lights.Length;n++)if(lights[n])lights[n].enabled=states[n];
            ShaderUtil.allowAsyncCompilation=oldAsync;
            QualitySettings.globalTextureMipmapLimit=oldQuality;
        }
    }
}
