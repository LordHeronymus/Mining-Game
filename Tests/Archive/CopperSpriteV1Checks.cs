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

public static class CopperSpriteV1Checks
{
    static string Output;
    static readonly List<string> passed=new();
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);passed.Add("PASS "+message);}
    public static string Main(bool baselineOnly=false)
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Output="Design/OreVeins/CopperL1SpriteV1/Implemented";passed.Clear();Directory.CreateDirectory(Output);
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
            const int width=1280,height=960;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);disposables.Add(rt);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);disposables.Add(image);camera.targetTexture=rt;
            bool enabled=true, groundOnly=false; Texture metalOverride=null;
            overrides=(context,view)=>
            {
                if(view!=camera)return;
                var block=new MaterialPropertyBlock();ores.GetComponent<TilemapRenderer>().GetPropertyBlock(block);
                if(metalOverride)block.SetTexture("_CopperL1Sprite",metalOverride); block.SetFloat("_CopperEarthTilesEnabled",enabled?1:0);block.SetFloat("_CopperRockTilesEnabled",enabled?1:0);
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
            material.SetFloat("_ReflectionStrength",0);material.SetFloat("_ShimmerStrength",0);
            var copper=map.registry.GetById(BlockType.CopperOre);
            var variants=new[]{copper.smallOre[0],copper.mediumOre[0],copper.richOre[0]};
            var cells=new List<Vector3Int>();
            for(int x=2;x<14;x++){int y=9-(x-2)/2;cells.Add(new Vector3Int(x,y,0));if(x%3==0)cells.Add(new Vector3Int(x,y-1,0));}
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],variants[n%3]);
            QualitySettings.globalTextureMipmapLimit=1;
            foreach(int layer in new[]{2,3,4,5}){
                ApplyHost(layer);var actual=Render(baselineOnly?"Baseline-L"+layer:"Scope-L"+layer);
                if(!baselineOnly){var saved=new Texture2D(2,2);disposables.Add(saved);saved.LoadImage(File.ReadAllBytes(Output+"/Baseline-L"+layer+".png"));Check(actual.SequenceEqual(saved.GetPixels32()),"L"+layer+" is pixel-identical to pre-change production rendering");}
            }
            ApplyHost(1);var iron=map.registry.GetById(BlockType.IronOre);
            foreach(var cell in cells)ores.SetTile(cell,iron.smallOre[0]);
            var other=Render(baselineOnly?"Baseline-Iron":"Scope-Iron");
            if(baselineOnly)return "Captured L2-L5 and iron baselines";
            var ironSaved=new Texture2D(2,2);disposables.Add(ironSaved);ironSaved.LoadImage(File.ReadAllBytes(Output+"/Baseline-Iron.png"));
            Check(other.SequenceEqual(ironSaved.GetPixels32()),"Other ore in L1 is pixel-identical to pre-change rendering");
            var art=new Texture2D(2,2,TextureFormat.RGBA32,true);disposables.Add(art);
            art.LoadImage(File.ReadAllBytes("Assets/Resources/OreVeins/CopperL1Sprite/Copper.png"));
            int opaque=0,transparent=0;foreach(var c in art.GetPixels32()){if(c.a>=250)opaque++;if(c.a==0)transparent++;}
            Check(opaque>art.width*art.height*.25 && transparent>art.width*art.height*.3,"Sprite has real transparent background and substantial near-opaque copper");
            foreach(var cell in cells)ores.SetTile(cell,copper.smallOre[0]);
            Color32[] baseline=null;
            for(int quality=0;quality<=2;quality++){
                QualitySettings.globalTextureMipmapLimit=quality;
                groundOnly=true;var bare=Render(null);groundOnly=false;
                for(int r=0;r<3;r++){
                    foreach(var cell in cells)ores.SetTile(cell,variants[r]);
                    var actual=Render(quality==1?new[]{"Small","Medium","Large"}[r]:null);
                    if(r==0)baseline=actual;else Check(actual.SequenceEqual(baseline),"Quality "+quality+": richness "+r+" has identical sprite and scale");
                }
                int unchanged=0,changedOutside=0,visible=0;
                foreach(var cell in cells)for(int y=0;y<80;y++)for(int x=0;x<80;x++){
                    int i=(cell.y*80+y)*width+cell.x*80+x;float u=((x+.5f)/80-.5f)/.9f+.5f,v=((y+.5f)/80-.5f)/.9f+.5f;
                    bool clear=true;for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)if(art.GetPixelBilinear(u+dx/80f,v+dy/80f).a>.001f)clear=false;
                    if(clear){unchanged++;if(!baseline[i].Equals(bare[i]))changedOutside++;}
                    if(!baseline[i].Equals(bare[i]))visible++;
                }
                Check(unchanged>25000 && changedOutside==0,"Quality "+quality+": original dirt unchanged outside sprite, samples="+unchanged+", changed="+changedOutside);
                Check(visible>12000,"Quality "+quality+": large ore remains clearly visible, pixels="+visible);
            }
            QualitySettings.globalTextureMipmapLimit=1;foreach(var cell in cells)ores.SetTile(cell,copper.smallOre[0]);
            var before=Render("Whole-vein");
            camera.orthographicSize=3.3f;camera.transform.position=new Vector3(8.25f,6.6f,-10);Render("Closeup");
            camera.orthographicSize=6.6f;camera.transform.position=new Vector3(8.8f,6.6f,-10);
            // Exact texture-colour comparison against the source at fully opaque
            // points catches the wrong resource, rotation, tint and UV scaling.
            double error=0;int samples=0;
            foreach(var cell in cells)for(int y=6;y<74;y++)for(int x=6;x<74;x++){
                float u=((x+.5f)/80-.5f)/.9f+.5f,v=((y+.5f)/80-.5f)/.9f+.5f;
                float lod=Mathf.Log(art.width/(80f*.9f),2);int level=Mathf.FloorToInt(lod);
                var expected=Color.Lerp(art.GetPixelBilinear(u,v,level),art.GetPixelBilinear(u,v,level+1),lod-level);if(expected.a<.98f)continue;
                var actual=(Color)before[(cell.y*80+y)*width+cell.x*80+x];
                error+=Math.Abs(actual.r-expected.r)+Math.Abs(actual.g-expected.g)+Math.Abs(actual.b-expected.b);samples++;
            }
            Check(samples>15000 && error/samples<.16,"Rendered art matches approved cutout orientation/colour/scale: samples="+samples+", mean RGB error="+(error/samples));
            foreach(float scale in new[]{.5f,1.35f,3f}){map.oreScale=scale;Check(before.SequenceEqual(Render(null)),"Global ore scale "+scale+" preserves fixed L1 sprite size");}map.oreScale=1;
            var mined=cells[6];ores.SetTile(mined,null);var after=Render("Ore-removed");
            groundOnly=true;var empty=Render(null);groundOnly=false;
            bool clean=true;for(int y=0;y<80;y++)for(int x=0;x<80;x++){int i=(mined.y*80+y)*width+mined.x*80+x;clean&=after[i].Equals(empty[i]);}
            Check(clean,"Removed ore leaves the exact original dirt without ghost sprites");
            foreach(var cell in cells.Where(c=>c!=mined)){bool same=true;for(int y=0;y<80;y++)for(int x=0;x<80;x++){int i=(cell.y*80+y)*width+cell.x*80+x;same&=before[i].Equals(after[i]);}Check(same,"Mining leaves neighbouring sprite unchanged "+cell);}
            ores.SetTile(mined,copper.smallOre[0]);Check(before.SequenceEqual(Render(null)),"Restored ore restores exact sprite");
            lamp.intensity=0;var dark=Render("Dark");Check(dark.Max(c=>Math.Max(c.r,Math.Max(c.g,c.b)))<5,"Copper obeys darkness without emission");
            lamp.intensity=1;lamp.color=new Color(.2f,.4f,1);Render("Blue-light");lamp.color=Color.white;
            var offset=new Vector3Int(-21,-15,0);terrain.ClearAllTiles();ores.ClearAllTiles();visual.CopperPlans.Clear();
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0)+offset,source.surfaceDirtTile);
            props.SetVector("_TestBounds",new Vector4(offset.x,offset.y,16,12));
            foreach(var cell in cells)ores.SetTile(cell+offset,copper.smallOre[0]);camera.transform.position+=new Vector3(offset.x*1.1f,offset.y*1.1f,0);ApplyHost(1);
            var negative=Render("Negative");foreach(var cell in cells)ores.SetTile(cell+offset,copper.richOre[0]);
            Check(negative.SequenceEqual(Render(null)),"Negative coordinates retain identical richness appearance");
            ores.ClearAllTiles();visual.CopperPlans.Clear();
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)ores.SetTile(new Vector3Int(2+x*4,2+y*4,0)+offset,copper.smallOre[0]);Render("Nine-earth-phases");
            camera.orthographicSize=1.1f;
            camera.transform.position=terrain.GetCellCenterWorld(new Vector3Int(6,6,0)+offset)+Vector3.back*10;
            Render("Single-group-closeup");
            Check(!ShaderUtil.ShaderHasError(material.shader),"Production shader compiles without errors");
            File.WriteAllText("Temp/CopperSpriteV1Checks.txt",string.Join("\n",passed)+"\nALL PASSED");
            return passed.Count+" render checks passed";
        }        catch(Exception e){File.WriteAllText("Temp/CopperSpriteV1Checks.txt",string.Join("\n",passed)+"\nFAIL "+e);throw;}
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



