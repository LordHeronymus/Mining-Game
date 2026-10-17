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

public static class CopperConnectedFissureChecks
{
    static string Output;
    static readonly List<string> passed=new();
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);passed.Add("PASS "+message);}
    public static string Main(int testLayer=1)
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Output="Design/OreVeins/ConnectedCopper/Implemented";passed.Clear();Directory.CreateDirectory(Output);
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
                if(metalOverride)block.SetTexture("_CopperFissureMetal",metalOverride); block.SetFloat("_CopperEarthTilesEnabled",enabled?1:0);block.SetFloat("_CopperRockTilesEnabled",enabled?1:0);
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
            void CheckConnectedPixels(string label,int expected)
            {
                metalOverride=Texture2D.blackTexture;var black=Render(null);
                metalOverride=Texture2D.whiteTexture;var white=Render(null);metalOverride=null;
                var maskPixels=new bool[width*height];
                for(int n=0;n<maskPixels.Length;n++)maskPixels[n]=Math.Abs(white[n].r-black[n].r)+Math.Abs(white[n].g-black[n].g)+Math.Abs(white[n].b-black[n].b)>15;
                int components=0,holes=0;var seen=new bool[maskPixels.Length];var queue=new Queue<int>();
                for(int n=0;n<maskPixels.Length;n++)if(!seen[n]){
                    bool metal=maskPixels[n],border=false;int size=0;queue.Enqueue(n);seen[n]=true;
                    while(queue.Count>0){int current=queue.Dequeue();int x=current%width,y=current/width;size++;
                        border|=x==0||x==width-1||y==0||y==height-1;
                        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                            if(dx==0&&dy==0 || !metal&&dx!=0&&dy!=0)continue;
                            int xx=x+dx,yy=y+dy;if(xx<0||xx>=width||yy<0||yy>=height)continue;int next=yy*width+xx;
                            if(seen[next]||maskPixels[next]!=metal)continue;seen[next]=true;queue.Enqueue(next);
                        }
                    }
                    if(metal && size>12)components++;
                    if(!metal && !border && size>40)holes++;
                }
                Check(components==expected,label+": actual rendered copper components="+components+", expected="+expected);
                Check(holes==0,label+": no rendered copper loops enclosing earth clods, holes="+holes);
            }
            var cells=new List<Vector3Int>();
            // A cardinally connected long trunk with short asymmetric branches.
            for(int x=2;x<=13;x++){
                int y=9-(x-2)/2;cells.Add(new Vector3Int(x,y,0));
                if(x%2==0 && x>2)cells.Add(new Vector3Int(x,y+1,0));
            }
            cells.AddRange(new[]{new Vector3Int(5,7,0),new Vector3Int(5,6,0),new Vector3Int(8,7,0),new Vector3Int(8,8,0),new Vector3Int(11,4,0),new Vector3Int(11,3,0)});
            cells=cells.Distinct().ToList();
            var variants=new[]{copper.smallOre[0],copper.mediumOre[0],copper.richOre[0]};
            for(int n=0;n<cells.Count;n++)ores.SetTile(cells[n],variants[n%3]);
            ApplyHost(1);
            Color32[] baseline=null;
            for(int q=0;q<=2;q++){
                QualitySettings.globalTextureMipmapLimit=q;
                groundOnly=true;var bare=Render(null);groundOnly=false;
                for(int r=0;r<3;r++){
                    foreach(var cell in cells)ores.SetTile(cell,variants[r]);
                    var actual=Render(q==1?new[]{"Small","Medium","Large"}[r]:null);
                    if(r==0)baseline=actual;else Check(actual.SequenceEqual(baseline),"Quality "+q+": richness "+r+" is pixel-identical to Small");
                }
                metalOverride=Texture2D.blackTexture;var black=Render(null);
                metalOverride=Texture2D.whiteTexture;var white=Render(null);metalOverride=null;
                int soil=0,changed=0,metal=0,worst=0;
                foreach(var cell in cells)for(int y=0;y<80;y++)for(int x=0;x<80;x++){
                    int i=(cell.y*80+y)*width+cell.x*80+x;
                    if(!black[i].Equals(white[i])){metal++;continue;}
                    soil++;if(!baseline[i].Equals(bare[i]))changed++;
                    worst=Math.Max(worst,Math.Abs(baseline[i].r-bare[i].r)+Math.Abs(baseline[i].g-bare[i].g)+Math.Abs(baseline[i].b-bare[i].b));
                }
                Check(soil>50000 && changed==0,"Quality "+q+": exact original earth outside metal, samples="+soil+", changed="+changed+", worst="+worst);
                Check(metal>1500,"Quality "+q+": substantial visible vein="+metal);
            }
            QualitySettings.globalTextureMipmapLimit=1;
            CheckConnectedPixels("Long vein",1);
            var before=Render("Whole-vein");
            camera.orthographicSize=3.3f;camera.transform.position=new Vector3(8.25f,6.6f,-10);Render("Closeup");
            camera.orthographicSize=6.6f;camera.transform.position=new Vector3(8.8f,6.6f,-10);
            using(var field=new OreVeinField()){
                field.Prepare(map,camera);var links=cells.ToDictionary(c=>c,c=>field.CopperLinksAt(c));
                var steps=new[]{Vector3Int.right,Vector3Int.up,Vector3Int.left,Vector3Int.down};
                int edgeCount=0;
                foreach(var cell in cells)for(int side=0;side<4;side++)if((links[cell]&(1<<side))!=0){
                    Check(links.ContainsKey(cell+steps[side]) && (links[cell+steps[side]]&(1<<((side+2)%4)))!=0,"Reciprocal ore connection "+cell+"/"+side);edgeCount++;
                }
                Check(edgeCount/2==cells.Count-1,"Long connected deposit has exactly n-1 edges, no loops");
                var mined=cells[7];ores.SetTile(mined,null);field.Invalidate();field.Prepare(map,camera);
                foreach(var cell in cells.Where(c=>c!=mined))Check(field.CopperLinksAt(cell)==links[cell],"Mining preserves route "+cell);
                var after=Render("Mined");
                foreach(var cell in cells.Where(c=>c!=mined)){
                    bool equal=true;for(int y=2;y<78;y++)for(int x=2;x<78;x++){
                        int i=(cell.y*80+y)*width+cell.x*80+x;equal&=before[i].Equals(after[i]);}
                    Check(equal,"Mining preserves rendered neighbouring cell "+cell);
                }
                var saved=JsonUtility.ToJson(new PlanSnapshot{plans=visual.CopperPlans.Capture()});
                visual.RestoreCopperPlans(JsonUtility.FromJson<PlanSnapshot>(saved).plans);
                Check(after.SequenceEqual(Render(null)),"Saved original deposit restores identical mined route");
                ores.SetTile(mined,copper.richOre[0]);
                Check(before.SequenceEqual(Render(null)),"Restored cell returns identical visual");
                camera.transform.position+=Vector3.right*2.2f;field.Prepare(map,camera);
                foreach(var cell in cells)Check(field.CopperLinksAt(cell)==links[cell],"Camera scroll preserves route "+cell);
                camera.transform.position-=Vector3.right*2.2f;
            }
            // Dark and coloured lights must still affect solid, non-emissive metal.
            lamp.intensity=0;var dark=Render("Dark");
            Check(dark.Max(c=>Math.Max(c.r,Math.Max(c.g,c.b)))<5,"Copper disappears without incident light");
            lamp.intensity=1;lamp.color=new Color(.2f,.4f,1);Render("Blue-light");lamp.color=Color.white;
            // Recreate in negative coordinates, independently of the camera buffer.
            var offset=new Vector3Int(-21,-15,0);terrain.ClearAllTiles();ores.ClearAllTiles();visual.CopperPlans.Clear();
            for(int y=0;y<12;y++)for(int x=0;x<16;x++)terrain.SetTile(new Vector3Int(x,y,0)+offset,source.surfaceDirtTile);
            props.SetVector("_TestBounds",new Vector4(offset.x,offset.y,16,12));
            foreach(var cell in cells)ores.SetTile(cell+offset,copper.smallOre[0]);
            camera.transform.position+=new Vector3(offset.x*1.1f,offset.y*1.1f,0);ApplyHost(1);
            var negative=Render("Negative-coordinates");
            foreach(var cell in cells)ores.SetTile(cell+offset,copper.richOre[0]);
            Check(negative.SequenceEqual(Render(null)),"Negative coordinates retain identical richness visuals");
            CheckConnectedPixels("Negative coordinates",1);
            // Dense ore groups must form a tree too, not outline every dirt clod.
            ores.ClearAllTiles();visual.CopperPlans.Clear();
            for(int y=3;y<8;y++)for(int x=4;x<10;x++)ores.SetTile(new Vector3Int(x,y,0)+offset,copper.richOre[0]);
            Render("Dense-group");CheckConnectedPixels("Dense group",1);
            ores.ClearAllTiles();visual.CopperPlans.Clear();
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)ores.SetTile(new Vector3Int(2+x*4,2+y*4,0)+offset,copper.smallOre[0]);
            Render("Isolated-phases");CheckConnectedPixels("Nine isolated original-earth phases",9);
            Check(!ShaderUtil.ShaderHasError(material.shader),"Production shader compiled without errors");
            File.WriteAllText("Temp/CopperConnectedFissureChecks.txt",string.Join("\n",passed)+"\nALL PASSED");
            return passed.Count+" checks passed";
        }
        catch(Exception e){File.WriteAllText("Temp/CopperConnectedFissureChecks.txt",string.Join("\n",passed)+"\nFAIL "+e);throw;}
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

[Serializable] public sealed class PlanSnapshot { public CopperDepositState[] plans; }
