using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public static class OreVeinVisualChecks
{
    const string Output = "Assets/Design/OreVeins/Implemented";
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
        try
        {
            foreach (var light in previousLights) light.enabled = false;
            foreach (var root in sourceRoots) root.SetActive(false);
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
                File.WriteAllBytes(Output+"/"+name+".png",pixels.EncodeToPNG());
                return pixels.GetPixels().Average(c=>(c.r+c.g+c.b)/3);
            }
            var granat=map.registry.GetById(BlockType.OrangeGarnetOre);
            var diamond=map.registry.GetById(BlockType.DiamondOre);
            SetRock(1);Deposit(granat);Render("Granat");
            var binding=new MaterialPropertyBlock();overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(binding);
            Check(binding.GetTexture("_VeinRelief")==Resources.Load<Texture2D>("OreVeins/RockFissure"),"Painted fissure relief bound to renderer");
            var firstLookup=binding.GetTexture("_VeinCells");
            var secondCamera=new GameObject("Second test view",typeof(Camera)).GetComponent<Camera>();
            secondCamera.CopyFrom(camera);secondCamera.transform.position=camera.transform.position+Vector3.left*60;
            oreAppearance.PrepareVeins(secondCamera);
            overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(binding);
            Check(firstLookup && binding.GetTexture("_VeinCells") && binding.GetTexture("_VeinCells")!=firstLookup,
                "Game and scene cameras have separate cached lookups");
            oreAppearance.PrepareVeins(camera);overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(binding);
            Check(binding.GetTexture("_VeinCells")==firstLookup,"Returning to first camera reuses its own lookup");
            Object.DestroyImmediate(secondCamera.gameObject);
            float zoom=camera.orthographicSize;camera.orthographicSize=1000;
            oreAppearance.PrepareVeins(camera);overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(binding);
            Check(binding.GetFloat("_VeinEnabled")==0,"Huge overview uses bounded-memory sprite fallback");
            camera.orthographicSize=zoom;oreAppearance.PrepareVeins(camera);
            using(var field=new OreVeinField())
            {
                Check(field.Prepare(map,camera),"Camera field initialized");
                Check(field.TypeAt(new Vector3Int(7,8,0))==(int)BlockType.OrangeGarnetOre,"Ore identity encoded at negative-safe world coordinates");
                int revision=field.Revision;field.Prepare(map,camera);
                Check(field.Revision==revision,"Stationary camera does not rebuild metadata");
                Check(field.Texture.width<=64 && field.Texture.height<=64,"Lookup scales with view, not world dimensions");
                var oldPosition=camera.transform.position;
                var negativeCell=new Vector3Int(-22,-31,0);
                terrain.SetTile(negativeCell,source.uniformTestTile);
                overlay.SetTile(negativeCell,diamond.smallOre[0]);
                camera.transform.position=terrain.GetCellCenterWorld(negativeCell)+Vector3.back*10;
                Check(field.Prepare(map,camera) && field.TypeAt(negativeCell)==(int)BlockType.DiamondOre,
                    "Camera scrolling rebuilds correct metadata at negative coordinates");
                terrain.SetTile(negativeCell,null);overlay.SetTile(negativeCell,null);
                field.Invalidate();field.Prepare(map,camera);
                Check(field.TypeAt(negativeCell)==-1,"Empty space cannot retain a stale ore type");
                // Even a legacy/orphan overlay must never draw a vein in empty terrain.
                overlay.SetTile(negativeCell,diamond.richOre[0]);field.Invalidate();field.Prepare(map,camera);
                Check(field.TypeAt(negativeCell)==-1,"Orphan overlays are excluded from connection lookup");
                overlay.SetTile(negativeCell,null);camera.transform.position=oldPosition;
            }
            var removed=new Vector3Int(7,8,0);
            Check(map.RemoveBlock(removed),"Mining removes a connected junction");
            oreAppearance.PrepareVeins(camera);
            var activeProps=new MaterialPropertyBlock();overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(activeProps);
            var fieldTex=(Texture2D)activeProps.GetTexture("_VeinCells");var b=activeProps.GetVector("_VeinBounds");
            Check(fieldTex.GetPixel(removed.x-(int)b.x,removed.y-(int)b.y).r==0,"Removed junction immediately clears GPU neighbours");
            Check(!overlay.HasTile(removed) && !terrain.HasTile(removed),"Mining preserves synchronized terrain and ore removal");
            Render("Granat-mined");
            terrain.SetTile(removed,source.uniformTestTile);overlay.ClearAllTiles();Deposit(diamond);SetRock(1);Render("Diamond");
            overlay.ClearAllTiles();
            for(int x=0;x<14;x++) overlay.SetTile(new Vector3Int(x,5,0),diamond.GetOreVariants((OreRichness)(x%3))[0]);
            SetRock(-1);Render("Diamond-all-strata");
            Check(activeProps.GetTexture("_LayerFourTex")==appearance.layerFourTexture,"Deepest host texture is passed to ore lips");
            foreach(var block in map.registry.blocks.Where(x=>x && x.HasOreOverlays))
            {
                overlay.ClearAllTiles();Deposit(block);SetRock(1);
                Render("Type-"+block.id);
                Check(overlay.GetTile<OreTile>(new Vector3Int(7,10,0)).block==block,"Rendered "+block.id+" without changing identity");
            }
            overlay.ClearAllTiles();Deposit(diamond);SetRock(1);
            lamp.intensity=1;float bright=Render("Diamond-bright");
            lamp.intensity=.1f;float dim=Render("Diamond-dim");
            lamp.intensity=0;float dark=Render("Diamond-dark");
            Check(bright>dim && dim>dark && dark<.002f,"Diamonds and mineral seams remain light-dependent: "+bright+" / "+dim+" / "+dark);
            lamp.intensity=1;lamp.color=Color.blue;Render("Diamond-blue-light");
            Check(pixels.GetPixels32().All(c=>c.r<2 && c.g<2),"Connected diamond reflections inherit incident light color");
            overlay.ClearAllTiles();Deposit(granat);Render("Granat-blue-light");
            Check(pixels.GetPixels32().All(c=>c.r<2 && c.g<2),"Garnet fissures inherit incident light color");
            lamp.color=Color.white;overlay.ClearAllTiles();
            var orphan=new Vector3Int(7,6,0);terrain.SetTile(orphan,null);overlay.SetTile(orphan,diamond.richOre[0]);
            terrain.GetComponent<TilemapRenderer>().enabled=false;
            Check(Render("Orphan-overlay")<.00001f,"Orphan overlay emits no visible pixels without host terrain");
            Check(!ShaderUtil.ShaderHasError(oreAppearance.OverlayMaterial.shader),"Ore shader compiled and rendered successfully");
            File.WriteAllLines("Temp/OreVeinVisualChecks.txt",passed);
            return "PASS "+passed.Count+" checks. Images: "+Output;
        }
        finally
        {
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
