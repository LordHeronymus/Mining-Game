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
using Object = UnityEngine.Object;

public static class CopperCarpetVisualChecks
{
    const string Output = "Assets/Design/OreVeins/CopperCarpet/Implemented";
    static readonly List<string> passed = new();
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); passed.Add(message); }

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        passed.Clear(); Directory.CreateDirectory(Output);
        var previous = SceneManager.GetActiveScene();
        var sourceScene = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool openedSource = !sourceScene.IsValid() || !sourceScene.isLoaded;
        if(openedSource) sourceScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        var sourceRoots=sourceScene.GetRootGameObjects();
        var rootStates=sourceRoots.Select(x=>x.activeSelf).ToArray();
        var source = sourceScene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MapGenerator>(true)).First();
        var appearance = source.GetComponent<UniformStoneAppearance>();
        var previousLights = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var lightStates = previousLights.Select(x=>x.enabled).ToArray();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var materials = new List<Material>();
        var generated = new List<Object>();
        var oldTarget = RenderTexture.active;
        Action<ScriptableRenderContext,Camera> overrideArt=null;
        try
        {
            foreach (var light in previousLights) light.enabled = false;
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Ore vein test grid",typeof(Grid));
            grid.GetComponent<Grid>().cellSize = new Vector3(1.1f,1.1f,0);
            var ground = new GameObject("Ore vein test terrain",typeof(Tilemap),typeof(TilemapRenderer));
            ground.transform.SetParent(grid.transform,false); ground.layer=31;
            var terrain=ground.GetComponent<Tilemap>();
            terrain.orientation=Tilemap.Orientation.Custom;
            terrain.orientationMatrix=Matrix4x4.Scale(new Vector3(2.2f,2.2f,1));
            var map=ground.AddComponent<MapGenerator>(); map.enabled=false;
            map.registry=source.registry; map.seed=74123; map.oreScale=source.oreScale;
            map.mapWidth=14; map.mapHeight=12;
            var overlay=map.EnsureOreOverlay(); overlay.gameObject.layer=31;
            var oreAppearance=ground.GetComponent<OreOverlayAppearance>();
            oreAppearance.OverlayMaterial=source.GetComponent<OreOverlayAppearance>().OverlayMaterial;
            var stoneMaterial=new Material(Shader.Find("Mining Game/Dirt Terrain Lit")); materials.Add(stoneMaterial);
            terrain.GetComponent<TilemapRenderer>().sharedMaterial=stoneMaterial;
            overlay.GetComponent<TilemapRenderer>().sortingOrder=1;
            var mask=new Texture2D(14,12,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            generated.Add(mask);
            var colors=new Color[14*12];
            var props=new MaterialPropertyBlock();
            props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,14,12));
            props.SetTexture("_TestStoneTex",appearance.texture);
            props.SetTexture("_SurfaceDirtTex",appearance.dirtTexture);
            props.SetTexture("_LayerOneTex",appearance.layerOneTexture);
            props.SetTexture("_LayerThreeTex",appearance.layerThreeTexture);
            props.SetTexture("_LayerFourTex",appearance.layerFourTexture);
            props.SetTexture("_TestOccupancy",mask);
            for(int y=0;y<12;y++) for(int x=0;x<14;x++) terrain.SetTile(new Vector3Int(x,y,0),source.uniformTestTile);
            var lamp=new GameObject("Test lamp",typeof(Light2D)).GetComponent<Light2D>();
            lamp.gameObject.layer=31;
            lamp.lightType=Light2D.LightType.Global; lamp.intensity=1;
            var camera=new GameObject("Test camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(7.7f,6.6f,-10);
            camera.orthographic=true; camera.orthographicSize=6.6f;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.allowHDR=false;
            var rt=new RenderTexture(1120,960,24,RenderTextureFormat.ARGB32); generated.Add(rt);
            var pixels=new Texture2D(1120,960,TextureFormat.RGBA32,false); generated.Add(pixels);
            camera.targetTexture=rt;
            void SetRock(int layer)
            {
                for(int y=0;y<12;y++) for(int x=0;x<14;x++)
                {
                    int l=layer<0 ? Mathf.Min(4,x/3) : layer;
                    colors[y*14+x]=new Color(1,l==0?1:0,l==1?1:0,l==3?.5f:l==4?1:0);
                }
                mask.SetPixels(colors);mask.Apply();
                terrain.GetComponent<TilemapRenderer>().SetPropertyBlock(props);
                oreAppearance.ApplyTerrain(props);
            }
            void Deposit(Block block,int offset=0)
            {
                var cells=new[]{new Vector2Int(7,10),new Vector2Int(7,9),new Vector2Int(6,8),new Vector2Int(7,8),
                    new Vector2Int(7,7),new Vector2Int(8,7),new Vector2Int(8,8),new Vector2Int(6,7),
                    new Vector2Int(6,6),new Vector2Int(6,5),new Vector2Int(5,5),new Vector2Int(4,5),
                    new Vector2Int(7,5),new Vector2Int(8,5),new Vector2Int(8,4),new Vector2Int(8,3),
                    new Vector2Int(8,2),new Vector2Int(7,3),new Vector2Int(6,3),new Vector2Int(6,2),new Vector2Int(6,1)};
                for(int n=0;n<cells.Length;n++)
                {
                    var group=block.GetOreVariants(n%5==0?OreRichness.Rich:n%3==0?OreRichness.Medium:OreRichness.Small);
                    overlay.SetTile(new Vector3Int(cells[n].x+offset,cells[n].y,0),group[n%group.Length]);
                }
            }
            float Render(string name)
            {
                terrain.RefreshAllTiles();overlay.RefreshAllTiles();
                oreAppearance.PrepareVeins(camera); camera.Render();RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();
                string destination=name.StartsWith("Copper",StringComparison.Ordinal)?Output:"Temp/CopperCarpetValidation";
                Directory.CreateDirectory(destination);
                File.WriteAllBytes(destination+"/"+name+".png",pixels.EncodeToPNG());
                return pixels.GetPixels().Average(c=>(c.r+c.g+c.b)/3);
            }
            var copper=map.registry.GetById(BlockType.CopperOre);
            bool legacy=false;
            overrideArt=(context,view)=>
            {
                if(view!=camera || !legacy)return;
                var p=new MaterialPropertyBlock();overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(p);
                p.SetFloat("_CopperCarpetEnabled",0);overlay.GetComponent<TilemapRenderer>().SetPropertyBlock(p);
                if(oreAppearance.CopperSurface){p.SetFloat("_CopperSurfacePass",1);oreAppearance.CopperSurface.SetPropertyBlock(p);}
            };
            RenderPipelineManager.beginCameraRendering+=overrideArt;
            Deposit(copper);
            for(int layer=0;layer<5;layer++)
            {
                SetRock(layer);Render("Copper-L"+(layer+1));var current=pixels.GetPixels32();
                legacy=true;Render("Copper-L"+(layer+1)+"-legacy");legacy=false;
                int changed=pixels.GetPixels32().Where((c,n)=>!c.Equals(current[n])).Count();
                Check(layer<2?changed>5000:changed==0,"Layer "+(layer+1)+": "+changed+" changed pixels (only L1/L2 enabled)");
            }
            SetRock(1);Render("Copper-shape");var shape=pixels.GetPixels32();
            var binding=new MaterialPropertyBlock();overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(binding);
            Check(binding.GetTexture("_CopperCarpet")==Resources.Load<Texture2D>("OreVeins/CopperCarpet/Copper"),"Approved full copper texture is bound");
            Check(oreAppearance.CopperPlans.Capture().Length==0,"Carpet needs no per-deposit geometry or save data");
            terrain.GetComponent<TilemapRenderer>().enabled=false;
            Render("Copper-mask");
            int inside=0,outside=0,missing=0;
            for(int y=0;y<960;y+=3)for(int x=0;x<1120;x+=3)
            {
                var world=camera.ScreenToWorldPoint(new Vector3(x+.5f,y+.5f,10));var cell=terrain.WorldToCell(world);
                var local=(world-terrain.CellToWorld(cell))/1.1f;
                if(local.x<.025f || local.x>.975f || local.y<.025f || local.y>.975f)continue;
                bool ore=overlay.HasTile(cell);var c=pixels.GetPixel(x,y);bool visible=c.r>.02f;
                if(ore){inside++;if(!visible)missing++;}else if(visible)outside++;
            }
            Check(inside>1000 && missing==0 && outside==0,"Every copper cell is filled, no copper outside ore cells: "+inside+" / "+missing+" / "+outside);
            terrain.GetComponent<TilemapRenderer>().enabled=true;
            // A large continuous carpet exposes seams/repetition that a thin vein could hide.
            for(int y=0;y<12;y++)for(int x=0;x<14;x++)overlay.SetTile(new Vector3Int(x,y,0),copper.smallOre[0]);
            Render("Copper-full-surface");var full=pixels.GetPixels32();
            double seamSum=0,innerSum=0;int seamCount=0,innerCount=0;
            for(int y=5;y<955;y++)for(int x=5;x<1115;x++)
            {
                var a=full[y*1120+x];var b=full[y*1120+x-1];
                double d=(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/765.0;
                if(x%80==0){seamSum+=d;seamCount++;}else{innerSum+=d;innerCount++;}
            }
            Check(seamSum/seamCount < innerSum/innerCount*1.65,"Cell borders have no abnormal seam contrast: "+seamSum/seamCount+" vs "+innerSum/innerCount);
            double repeatDifference=0;int repeats=0;
            for(int y=20;y<940;y+=3)for(int x=20;x<1020;x+=3)
            {var a=full[y*1120+x];var b=full[y*1120+x+80];repeatDifference+=Math.Abs(a.r-b.r);repeats++;}
            Check(repeatDifference/repeats>12,"Adjacent cells are not repeated texture copies: mean red difference "+repeatDifference/repeats);
            var viewPosition=camera.transform.position;
            camera.transform.position+=Vector3.right*1.1f;Render("Copper-scroll");var shifted=pixels.GetPixels32();
            double scrollDifference=0;int comparisons=0;
            for(int y=20;y<940;y+=3)for(int x=20;x<1000;x+=3)
            {scrollDifference+=Math.Abs(full[y*1120+x+80].r-shifted[y*1120+x].r);comparisons++;}
            Check(scrollDifference/comparisons<1,"World texture stays fixed while camera moves: "+scrollDifference/comparisons);
            camera.transform.position=viewPosition;
            var removed=new Vector3Int(7,6,0);Check(map.RemoveBlock(removed),"Mining removes a carpet cell");
            Render("Copper-mined");var mined=pixels.GetPixels32();
            bool stable=true;for(int y=40;y<900;y+=4)for(int x=40;x<1080;x+=4)
            {if(x>=559 && x<=641 && y>=479 && y<=561)continue;stable &= full[y*1120+x].Equals(mined[y*1120+x]);}
            Check(stable,"Mining does not rearrange the remaining copper texture");
            Check(pixels.GetPixel(600,520).r<.005f,"Mined opening contains no residual copper");
            terrain.SetTile(removed,source.uniformTestTile);overlay.SetTile(removed,copper.smallOre[0]);
            lamp.intensity=0;Check(Render("Copper-dark")<.002f,"Copper has no emission in darkness");
            lamp.intensity=1;lamp.color=Color.blue;Render("Copper-blue-light");
            Check(pixels.GetPixels32().All(c=>c.r<2 && c.g<2),"Copper follows the actual light colour");
            lamp.color=Color.white;overlay.ClearAllTiles();Deposit(map.registry.GetById(BlockType.DiamondOre));
            Render("Diamond");var diamond=pixels.GetPixels32();legacy=true;Render("Diamond-legacy");legacy=false;
            Check(pixels.GetPixels32().SequenceEqual(diamond),"Other ores remain unchanged");
            Check(!ShaderUtil.ShaderHasError(oreAppearance.OverlayMaterial.shader),"Shader compiled and rendered successfully");
            File.WriteAllLines("Temp/CopperCarpetVisualChecks.txt",passed);
            return "PASS "+passed.Count+" checks. Images: "+Output;
        }
        finally
        {
            if(overrideArt!=null)RenderPipelineManager.beginCameraRendering-=overrideArt;
            foreach(var view in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Camera>(true)))view.targetTexture=null;
            RenderTexture.active=oldTarget;
            EditorSceneManager.CloseScene(scene,true);
            for(int n=0;n<sourceRoots.Length;n++) if(sourceRoots[n])sourceRoots[n].SetActive(rootStates[n]);
            if(openedSource)EditorSceneManager.CloseScene(sourceScene,true);
            SceneManager.SetActiveScene(previous);
            for(int i=0;i<previousLights.Length;i++) if(previousLights[i])previousLights[i].enabled=lightStates[i];
            foreach(var obj in generated) if(obj)Object.DestroyImmediate(obj);
            foreach(var mat in materials) if(mat)Object.DestroyImmediate(mat);
        }
    }
}


