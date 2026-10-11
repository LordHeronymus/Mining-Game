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

public static class CopperCombinedVisualChecks
{
    const string Output="Assets/Design/OreVeins/CopperL1Branches/Implemented";
    static readonly List<string> passed=new();
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);passed.Add("PASS "+message);}
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        passed.Clear();Directory.CreateDirectory(Output);
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
                for(int n=0;n<pixels.Length;n++)pixels[n]=new Color(1,layer==1?1:0,layer==2?1:0,0);
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
                block.SetFloat("_CopperEarthTilesEnabled",enabled?1:0);
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
            var cells=new List<Vector3Int>();
            for(int x=2;x<14;x++)
            {
                int y=9-(x-2)*6/11;cells.Add(new Vector3Int(x,y,0));
                if(x>5 && x<11)cells.Add(new Vector3Int(x,y-1,0));
            }
            cells.AddRange(new[]{new Vector3Int(7,7,0),new Vector3Int(7,8,0),new Vector3Int(6,9,0),
                new Vector3Int(10,3,0),new Vector3Int(9,2,0)});
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
            ApplyHost(1);var actual=Render("Whole-vein-L1"); groundOnly=true;var ground=Render(null);groundOnly=false;
            // Capture identical cell positions for a meaningful richness comparison.
            var originalTiles=cells.Select(cell=>ores.GetTile(cell)).ToArray();
            var richnessTiles=new[]{copper.smallOre[0],copper.mediumOre[0],copper.richOre[0]};
            for(int richness=0;richness<3;richness++) {
                foreach(var cell in cells)ores.SetTile(cell,richnessTiles[richness]);
                Render(new[]{"Small","Medium","Large"}[richness]+"-comparison");
            }
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],originalTiles[n]);
            Render(null);
            Check(UnityEditor.ShaderUtil.GetShaderMessages(material.shader).All(x=>x.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Production shader compiles");
            var bound=new MaterialPropertyBlock();ores.GetComponent<TilemapRenderer>().GetPropertyBlock(bound);
            Check(bound.GetFloat("_CopperEarthTilesEnabled")==1 && bound.GetTexture("_CopperEarthCombined"),"Real renderer binds complete earth/copper atlas");
            terrain.GetComponent<TilemapRenderer>().enabled=false;
            int seams=0; foreach(var cell in cells)for(int y=0;y<80;y++)for(int x=0;x<80;x++) {
                int i=(cell.y*80+y)*width+cell.x*80+x;var a=actual[i];var b=ground[i];
                int d=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                if((x<2 || x>=78 || y<2 || y>=78) && d>3)seams++;
            } Check(seams==0,"Copper cell earth borders match the surrounding terrain");
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
                Check(visible>=80*80*.02f && maxX-minX>=24 && maxY-minY>=24,
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
            props.SetVector("_TestBounds",new Vector4(-5,-7,16,12));ApplyHost(1);
            camera.transform.position+=new Vector3(-5.5f,-7.7f,0);
            var negative=Render("Negative-coordinates");groundOnly=true;var negativeGround=Render(null);groundOnly=false;
            int phaseErrors=0,phaseTotal=0,phaseMax=0;var phaseDetails=new List<string>();
            foreach(var cell in cells)foreach(int x in new[]{6,73})foreach(int y in new[]{6,73})
            {
                int i=(cell.y*80+y)*width+cell.x*80+x;var a=negative[i];var b=negativeGround[i];
                int d=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                phaseTotal+=d;phaseMax=Math.Max(phaseMax,d);if(d>6){phaseErrors++;if(phaseDetails.Count<6)phaseDetails.Add(cell+" "+x+","+y+" "+a+" / "+b);}
            }
            // Separately generated mip pyramids can differ by a few byte levels;
            // a wrong earth phase produces much larger colour/feature differences.
            Check(phaseMax<=24 && phaseTotal<cells.Count*4*6,"Baked earth phases match terrain at negative coordinates: average="+(phaseTotal/(cells.Count*4f))+", max="+phaseMax);
            foreach(int quality in new[]{0,2}) {
                QualitySettings.globalTextureMipmapLimit=quality;
                var q=Render(null);groundOnly=true;var qGround=Render(null);groundOnly=false;
                int sum=0,max=0;
                foreach(var cell in cells)foreach(int x in new[]{6,73})foreach(int y in new[]{6,73}) {
                    int i=(cell.y*80+y)*width+cell.x*80+x;var a=q[i];var b=qGround[i];
                    int d=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);sum+=d;max=Math.Max(max,d);
                }
                Check(max<=24 && sum<cells.Count*4*6,"Atlas phase and earth filtering stay correct at texture quality "+quality+": average="+(sum/(cells.Count*4f))+", max="+max);
            }
            QualitySettings.globalTextureMipmapLimit=oldQuality;
            camera.transform.position-=new Vector3(-5.5f,-7.7f,0);
            terrain.ClearAllTiles();ores.ClearAllTiles();
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0),source.surfaceDirtTile);
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
            props.SetVector("_TestBounds",new Vector4(0,0,16,12));ApplyHost(1);
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
            props.SetVector("_TerrainEdgeTuning",new Vector4(1,1,1,0));ApplyHost(1);
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
            terrain.SetTile(removed,source.surfaceDirtTile);ores.SetTile(removed,oldOre);ApplyHost(1);
            terrain.GetComponent<TilemapRenderer>().enabled=true;
            camera.orthographicSize=3.3f;camera.transform.position=new Vector3(8.25f,6.6f,-10);
            Render("L1-closeup");
            camera.orthographicSize=6.6f;camera.transform.position=new Vector3(8.8f,6.6f,-10);
            lamp.intensity=0;var dark=Render("Darkness");Check(dark.All(c=>c.r+c.g+c.b<8),"Copper has no emission in total darkness");
            lamp.intensity=1;lamp.color=new Color(.3f,.6f,1);var tinted=Render("Blue-light");
            Check(!actual.SequenceEqual(tinted),"Copper reacts to real light colour");lamp.color=Color.white;
            ApplyHost(2);enabled=true;var l2=Render("L2-unchanged");enabled=false;var l2Old=Render(null);
            Check(l2.SequenceEqual(l2Old),"Brown L2 retains its existing representation");
            ApplyHost(1);var iron=map.registry.GetById(BlockType.IronOre);ores.ClearAllTiles();
            foreach(var cell in cells)ores.SetTile(cell,iron.richOre[0]);
            enabled=true;var other=Render("Iron-unchanged");enabled=false;var otherOld=Render(null);
            Check(other.SequenceEqual(otherOld),"Other ore types retain their existing representation");
            File.WriteAllText("Temp/CopperCombinedVisualChecks.txt",string.Join("\n",passed)+"\nALL PASSED\n");
            return $"{passed.Count} render checks passed";
        }
        catch(Exception e){File.WriteAllText("Temp/CopperCombinedVisualChecks.txt",string.Join("\n",passed)+"\nFAIL "+e);throw;}
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
