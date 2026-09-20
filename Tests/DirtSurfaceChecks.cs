using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
public static class DirtSurfaceChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Main()
    {
        Check(!Application.isPlaying,"Run in edit mode");
        var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
        var dirt=registry.GetById(BlockType.Dirt);Check(dirt && dirt.variants.Length==4,"Four dirt variants missing");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
        Check(material && !ShaderUtil.ShaderHasError(material.shader),"Terrain lighting shader invalid");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);var states=new bool[lights.Length];
        var target=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);var oldTarget=RenderTexture.active;Texture2D pixels=null;
        try
        {
            for(int i=0;i<lights.Length;i++){states[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            var grid=new GameObject("Dirt Check Grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var go=new GameObject("Dirt Check",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));go.layer=31;go.transform.SetParent(grid.transform,false);
            var map=go.GetComponent<MapGenerator>();map.enabled=false;map.registry=registry;map.mapWidth=36;map.mapHeight=40;map.seed=42319;map.randomizeSeed=false;
            map.oreDensityCurve=AnimationCurve.Constant(0,1,1);map.oreDensityMultiplierPercent=100;
            go.GetComponent<DirtSurfaceAppearance>().TerrainMaterial=material;
            map.GenerateMap();map.OreOverlay.GetComponent<TilemapRenderer>().enabled=false;
            var sampler=new MapGenerationSampler(registry,map.seed,40,null,map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness);
            var expected=new Block[map.mapWidth*map.mapHeight];
            for(int y=0;y<map.mapHeight;y++)for(int x=0;x<map.mapWidth;x++)
                expected[y*map.mapWidth+x]=sampler.GetBlock(x,y);
            OreVeins.PruneSmallVeins(expected,map.mapWidth,map.mapHeight,map.minimumOreVeinSize,sampler.GetBaseBlock);
            int[] variants=new int[4];
            for(int y=0;y<sampler.DirtEndDepth;y++)for(int x=0;x<36;x++)
            {
                var cell=new Vector3Int(x-18,-y,0);
                var chosen=map.GetBlockAt(cell);
                Check(chosen==expected[y*map.mapWidth+x],"Surface map and sampler disagree");
                Check((map.GetOreAt(cell)!=null)==expected[y*map.mapWidth+x].HasOreOverlays,
                    "Ore overlay disagrees with the pruned map at "+cell);
                chosen=registry.FromTile(map.Terrain.GetTile(cell));
                Check(chosen==sampler.GetBaseBlock(x,y),"Ore replaced its dirt/stone substrate");
                Check(y<20 ? chosen==dirt : chosen==dirt || chosen.IsStone,"Invalid surface block type");
                Check(Mathf.Abs(map.Terrain.GetColor(cell).a-(chosen==dirt?.5f:1f))<.01f,"Surface tile shader tag invalid");
                if(chosen==dirt)
                {
                    var tile=map.Terrain.GetTile<Tile>(cell);
                    Check(tile.colliderType==Tile.ColliderType.Grid && Mathf.Abs(tile.color.a-.5f)<.01f,"Dirt collision or opacity invalid");
                    variants[Array.IndexOf(dirt.variants,tile)]++;
                }
            }
            var shares=new int[map.transitionThickness];int changedBySeed=0;
            var repeat=new MapGenerationSampler(registry,map.seed,40);
            var other=new MapGenerationSampler(registry,map.seed+1,40);
            for(int y=20;y<sampler.DirtEndDepth;y++)for(int x=0;x<1024;x++)
            {
                bool earth=sampler.IsDirtAt(x,y);
                if(earth)shares[y-20]++;
                Check(earth==repeat.IsDirtAt(x,y),"Boundary is not deterministic");
                if(earth!=other.IsDirtAt(x,y))changedBySeed++;
            }
            for(int row=0;row<map.transitionThickness;row++)
            {
                Check(shares[row]>0 && shares[row]<1024,"Transition row is a hard uniform cut");
                if(row>0)Check(shares[row]<shares[row-1],"Dirt share did not decrease with depth");
            }
            Check(changedBySeed>100,"Boundary ignores world seed");
            foreach(int count in variants)Check(count>0,"Dirt variant never used");
            var saved=map.Terrain.GetTilesBlock(map.Terrain.cellBounds);map.GenerateMap();
            var again=map.Terrain.GetTilesBlock(map.Terrain.cellBounds);for(int i=0;i<saved.Length;i++)Check(saved[i]==again[i],"Unstable dirt generation");
            var hole=new Vector3Int(0,-7,0);Check(map.RemoveBlock(hole)&&!map.GetBlockAt(hole)&&!map.GetOreAt(hole),"Dirt mining failed");
            var write=typeof(LastPlayedMap).GetMethod("WriteLayer",BindingFlags.Static|BindingFlags.NonPublic);
            var read=typeof(LastPlayedMap).GetMethod("ReadLayer",BindingFlags.Static|BindingFlags.NonPublic);
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))write.Invoke(null,new object[]{writer,map.Terrain});
                map.Terrain.ClearAllTiles();stream.Position=0;
                using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))read.Invoke(null,new object[]{reader,map.Terrain});
            }
            Check(!map.Terrain.HasTile(hole)&&registry.FromTile(map.Terrain.GetTile(new Vector3Int(1,-7,0)))==dirt,"Dirt save/restore failed");
            var light=new GameObject("Dirt Check Light",typeof(Light2D)).GetComponent<Light2D>();light.gameObject.layer=31;light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Dirt Check Camera",typeof(Camera)).GetComponent<Camera>();camera.transform.position=new Vector3(0,-9.5f,-10);
            camera.orthographic=true;camera.orthographicSize=10;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.allowHDR=false;camera.targetTexture=target;
            void Render(){camera.Render();RenderTexture.active=target;if(!pixels)pixels=new Texture2D(768,768,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,768,768),0,0);pixels.Apply();}
            Render();File.WriteAllBytes("Temp/DirtSurfacePreview.png",pixels.EncodeToPNG());
            int opaque=0;foreach(var p in pixels.GetPixels32())if(p.a>250)opaque++;
            Check(opaque>450000,"Terrain contains translucent tiles");
            light.intensity=0;Render();int bright=0;foreach(var p in pixels.GetPixels32())if(p.r>8||p.g>8||p.b>8)bright++;
            Check(bright==0,"Dirt or transition is self illuminated");
            return new{passed=true,pureDirtRows=20,transitionRows=map.transitionThickness,variants,dirtCountsPer1024Cells=shares,changedBySeed,opaquePixels=opaque,darkPixels=bright,mining=true,persistence=true};
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=states[i];
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
