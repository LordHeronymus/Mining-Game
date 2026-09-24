using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class LayerTransitionChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static int Difference(Color32 a,Color32 b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
    public static object Main()
    {
        Check(!Application.isPlaying,"Run in edit mode");
        var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
        var upper=registry.GetById(BlockType.Stone);var lower=registry.GetById(BlockType.StoneLayer2);
        Check(upper && upper.variants.Length==4 && lower && lower.variants.Length==6,"Layer stone variants missing");
        for(int i=0;i<4;i++){
            var path="Assets/GameObjects/Map/Blocks/Sprites/TransitionStone/Erde_Fels_"+(i+1).ToString("00")+".png";
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Check(sprite && Mathf.Abs(sprite.bounds.size.x-.5f)<.001f && importer.mipmapEnabled &&
                importer.filterMode==FilterMode.Trilinear && importer.textureCompression==TextureImporterCompression.Uncompressed,
                "Transition sprite import incorrect: "+path);
        }
        var materialAsset=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
        Check(materialAsset && !ShaderUtil.ShaderHasError(materialAsset.shader),"Terrain shader invalid");
        Check(materialAsset.GetTexture("_StoneTex") && materialAsset.GetTexture("_DeepStoneTex"),"Layer textures not configured");
        var source=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(source && source.layers.Length>1 && source.layers[0].stone==upper && source.layers[1].stone==lower,
            "Scene layer stones incorrect");
        var preview=new MapGenerationSampler(source.registry,source.ActiveSeed,source.GeneratedHeight,source.layers,source.oreDensityCurve,source.oreDensityMultiplierPercent,source.transitionThickness,source.oreTransitionCurve,source.oreTransitionDepth, source.oreVeinSizeCurve, source.surfaceOreRampDepth, source.surfaceOreRampCurve, source.surfaceOreVeinSizePercent);
        int checkedCells=0;
        for(int y=296;y<Math.Min(source.layers[1].startDepth+source.transitionThickness+2,source.GeneratedHeight);y++)for(int x=0;x<source.GeneratedWidth;x++){
            var cell=new Vector3Int(x-source.GeneratedWidth/2,-y,0);
            var tile=source.Terrain.GetTile(cell);
            if(!tile)continue;
            Check(source.registry.FromTile(tile)==preview.GetBaseBlock(x,y),"Scene preview substrate incorrect at "+cell);
            checkedCells++;
        }
        Check(checkedCells>1000,"Scene transition not available for validation");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);var states=new bool[lights.Length];
        var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);var oldTarget=RenderTexture.active;
        Texture2D pixels=null,brown=null,gray=null;Material material=null;
        try
        {
            for(int i=0;i<lights.Length;i++){states[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            material=new Material(materialAsset);
            Texture2D Solid(Color c){var t=new Texture2D(2,2,TextureFormat.RGBA32,false);t.SetPixels(new[]{c,c,c,c});t.Apply();return t;}
            brown=Solid(new Color(.65f,.25f,.08f));gray=Solid(new Color(.3f,.4f,.5f));
            material.SetTexture("_StoneTex",brown);material.SetTexture("_DeepStoneTex",gray);
            var grid=new GameObject("Layer Boundary Grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var go=new GameObject("Layer Boundary",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));go.layer=31;go.transform.SetParent(grid.transform,false);
            var map=go.GetComponent<MapGenerator>();map.enabled=false;map.registry=registry;map.mapWidth=36;map.mapHeight=72;map.seed=42319;map.randomizeSeed=false;
            map.layers=new[]{new MapLayer{name="Upper",startDepth=0,stone=upper,ores=Array.Empty<BlockType>()},
                new MapLayer{name="Lower",startDepth=40,stone=lower,ores=Array.Empty<BlockType>()}};
            var appearance=go.GetComponent<DirtSurfaceAppearance>();appearance.TerrainMaterial=material;
            map.GenerateMap();map.OreOverlay.GetComponent<TilemapRenderer>().enabled=false;
            var variantProperties=new MaterialPropertyBlock();go.GetComponent<TilemapRenderer>().GetPropertyBlock(variantProperties);
            var variantCounts=variantProperties.GetVector("_VariantCounts");
            Check(variantCounts.y==upper.variants.Length && variantCounts.z==lower.variants.Length,
                "Layer variant arrays missing: "+variantCounts);
            int checkedPairs=0;
            var previousBlocks=new Block[map.mapWidth];var previousSprites=new Sprite[map.mapWidth];
            var currentBlocks=new Block[map.mapWidth];var currentSprites=new Sprite[map.mapWidth];
            for(int y=0;y<map.mapHeight;y++)
            {
                for(int x=0;x<map.mapWidth;x++)
                {
                    var tile=map.Terrain.GetTile(new Vector3Int(x-map.mapWidth/2,-y,0));
                    var block=registry.FromTile(tile);
                    var data=new UnityEngine.Tilemaps.TileData();tile.GetTileData(Vector3Int.zero,null,ref data);
                    currentBlocks[x]=block;currentSprites[x]=data.sprite;
                    if(x>0 && block==currentBlocks[x-1] && data.sprite)
                    {checkedPairs++;Check(data.sprite!=currentSprites[x-1],"Repeated visual variant horizontally");}
                    if(y>0 && block==previousBlocks[x] && data.sprite)
                    {checkedPairs++;Check(data.sprite!=previousSprites[x],"Repeated visual variant vertically");}
                }
                (previousBlocks,currentBlocks)=(currentBlocks,previousBlocks);
                (previousSprites,currentSprites)=(currentSprites,previousSprites);
            }
            Check(checkedPairs>1000,"Too few terrain pairs checked");
            var sampler=new MapGenerationSampler(registry,map.seed,map.mapHeight,map.layers,map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness,map.oreTransitionCurve,map.oreTransitionDepth, map.oreVeinSizeCurve, map.surfaceOreRampDepth, map.surfaceOreRampCurve, map.surfaceOreVeinSizePercent);
            int[] shares=new int[map.transitionThickness];
            for(int y=40;y<40+map.transitionThickness;y++)for(int x=0;x<1024;x++)if(sampler.IsFirstLayerStoneAt(x,y))shares[y-40]++;
            for(int i=0;i<shares.Length;i++)Check(shares[i]>0 && shares[i]<1024,"Hard layer boundary row "+i);
            Check(shares[0]>shares[shares.Length-1],"Upper stone does not decrease with depth");
            var light=new GameObject("Layer Boundary Light",typeof(Light2D)).GetComponent<Light2D>();light.gameObject.layer=31;
            light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Layer Boundary Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,-23.75f,-10);camera.orthographic=true;camera.orthographicSize=5;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.allowHDR=false;camera.targetTexture=target;
            Color32[] Render(){camera.Render();RenderTexture.active=target;if(!pixels)pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();return pixels.GetPixels32();}
            bool CoreRow(int py){float worldY=-23.75f+((py+.5f)/512f*2f-1f)*5f;float depth=1-worldY/.5f;
                return depth>=40.2f && depth<=54.8f;}
            var before=Render();int maxJump=0,opaque=0;
            for(int y=1;y<511;y++)if(CoreRow(y)&&CoreRow(y-1))for(int x=1;x<511;x++){
                int i=y*512+x;if(before[i].a>250)opaque++;
                maxJump=Math.Max(maxJump,Difference(before[i],before[i-1]));
                maxJump=Math.Max(maxJump,Difference(before[i],before[i-512]));
            }
            Check(opaque>120000,"Layer transition is translucent");
            Check(maxJump<=80,"Hard layer boundary, adjacent pixel jump "+maxJump);
            for(int y=40;y<40+map.transitionThickness;y++)for(int x=-18;x<18;x++){
                var cell=new Vector3Int(x,-y,0);bool wasUpper=registry.FromTile(map.Terrain.GetTile(cell))==upper;
                map.Terrain.SetTile(new TileChangeData(cell,(wasUpper?lower:upper).variants[0],Color.white,Matrix4x4.identity),true);
            }
            map.Terrain.RefreshAllTiles();var swapped=Render();int tileDependence=0;
            for(int y=0;y<512;y++)if(CoreRow(y))for(int x=0;x<512;x++){
                int i=y*512+x;tileDependence=Math.Max(tileDependence,Difference(before[i],swapped[i]));
            }
            Check(tileDependence<=1,"Blend depends on tile type: "+tileDependence);
            appearance.Apply();var updated=Render();int changed=0;
            for(int y=0;y<512;y++)if(CoreRow(y))for(int x=0;x<512;x++)if(Difference(before[y*512+x],updated[y*512+x])>10)changed++;
            Check(changed>10000,"Layer mask ignores the actual block distribution");
            light.intensity=0;var dark=Render();int lit=0;
            for(int y=0;y<512;y++)if(CoreRow(y))for(int x=0;x<512;x++){var p=dark[y*512+x];if(p.r>8||p.g>8||p.b>8)lit++;}
            Check(lit==0,"Layer stones emit light");
            return new{passed=true,previewCells=checkedCells,upperCountsPer1024=shares,maxAdjacentPixelJump=maxJump,
                tileTypeDependence=tileDependence,maskChangedPixels=changed,opaquePixels=opaque,darkPixels=lit,variantCounts,checkedPairs};
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=states[i];
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);if(brown)UnityEngine.Object.DestroyImmediate(brown);
            if(gray)UnityEngine.Object.DestroyImmediate(gray);if(material)UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
