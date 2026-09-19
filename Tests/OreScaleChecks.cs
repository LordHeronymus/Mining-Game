using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class OreScaleChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static object Main()
    {
        Check(!Application.isPlaying, "Run scale checks in edit mode");
        AssetDatabase.ImportAsset("Assets/GameObjects/Map/OreOverlayLit.shader",ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameObjects/Map/OreOverlayLit.shader");
        Check(shader && !ShaderUtil.ShaderHasError(shader), "Ore shader failed to compile");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var enabled=new bool[lights.Length];
        var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);
        var oldTarget=RenderTexture.active;
        var material=new Material(shader);
        Texture2D pixels=null;
        try
        {
            for(int i=0;i<lights.Length;i++){enabled[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            var grid=new GameObject("Scale Test Grid",typeof(Grid));
            grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var go=new GameObject("Scale Test Map",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));
            go.transform.SetParent(grid.transform,false);go.layer=31;
            var map=go.GetComponent<MapGenerator>();map.enabled=false;
            var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
            map.registry=registry;
            var appearance=go.GetComponent<OreOverlayAppearance>();
            appearance.OverlayMaterial=material;
            map.EnsureOreOverlay();
            map.Terrain.SetTile(Vector3Int.zero,registry.GetById(BlockType.Stone).variants[0]);
            go.GetComponent<TilemapRenderer>().enabled=false;
            var overlay=map.OreOverlay;
            var ore=registry.GetById(BlockType.GoldOre).richOre[0];
            overlay.SetTile(Vector3Int.zero,ore);
            overlay.SetTileFlags(Vector3Int.zero,TileFlags.None);
            var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,0,90));
            overlay.SetTransformMatrix(Vector3Int.zero,rotation);
            var light=new GameObject("Scale Test Light",typeof(Light2D)).GetComponent<Light2D>();
            light.gameObject.layer=31;
            light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Scale Test Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(.25f,.25f,-10);
            camera.orthographic=true;camera.orthographicSize=.5f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.cullingMask=1<<31;camera.allowHDR=false;camera.targetTexture=target;
            var areas=new List<int>();
            foreach(float scale in new[]{.5f,1f,1.35f,2f,3f})
            {
                map.oreScale=scale;appearance.ApplyTo(overlay.GetComponent<TilemapRenderer>());
                var block=new MaterialPropertyBlock();overlay.GetComponent<TilemapRenderer>().GetPropertyBlock(block);
                Check(Mathf.Approximately(block.GetFloat("_OreScale"),scale),"Live scale did not reach renderer");
                int area=Render();areas.Add(area);
                Check(area>0,"Ore is invisible at scale "+scale);
                Check(overlay.GetTransformMatrix(Vector3Int.zero)==rotation && map.GetOreAt(Vector3Int.zero)==ore,
                    "Scale changed rotation or ore identity");
                if(scale==1||scale==1.35f)
                    System.IO.File.WriteAllBytes("Temp/ore-scale-"+(scale==1?"100":"135")+".png",pixels.EncodeToPNG());
            }
            Check(areas[2]>areas[1]*1.1f,"Scaling did not visibly increase ore coverage");
            Check(areas[0]<areas[1],"Shrinking did not reduce ore coverage");
            light.intensity=.2f;
            material.SetFloat("_ReflectionStrength",0);
            Render();float matte=Energy();
            System.IO.File.WriteAllBytes("Temp/ore-reflection-before.png",pixels.EncodeToPNG());
            material.SetFloat("_ReflectionStrength",2.5f);
            Render();float reflective=Energy();
            System.IO.File.WriteAllBytes("Temp/ore-reflection-after.png",pixels.EncodeToPNG());
            Check(reflective>matte*1.2f,"Reflection did not strengthen incident light: "+matte+" -> "+reflective);
            light.intensity=.05f;
            Render();Check(Energy()<reflective*.8f,"Reflections did not follow light intensity");
            light.color=Color.blue;
            Render();
            foreach(var c in pixels.GetPixels32())
                Check(c.r<2 && c.g<2,"Reflection did not inherit incident light color");
            light.color=Color.white;
            map.oreScale=1;
            appearance.ApplyTo(overlay.GetComponent<TilemapRenderer>());
            light.intensity=1;
            material.SetFloat("_ShimmerStrength",0);
            Render();var noHalo=pixels.GetPixels32();
            material.SetFloat("_ShimmerStrength",1.4f);
            Render();int haloPixels=0;
            var withHalo=pixels.GetPixels32();
            for(int p=0;p<withHalo.Length;p++)
                if(noHalo[p].r<2 && withHalo[p].r>8)haloPixels++;
            Check(haloPixels>100,"Missing shimmer outside the ore silhouette");
            System.IO.File.WriteAllBytes("Temp/ore-colored-shimmer.png",pixels.EncodeToPNG());
            light.intensity=10;
            Render();int goldPixels=0;
            foreach(var c in pixels.GetPixels32())
                if(c.r>150 && c.r>c.b*1.3f)goldPixels++;
            Check(goldPixels>1000,"Strong light washed out the gold hue");
            light.intensity=0;
            Check(Render()==0,"Ore is visible without illumination");
            Check(OreOverlayAppearance.ValidScale(float.NaN)==1 && OreOverlayAppearance.ValidScale(-1)==.5f &&
                OreOverlayAppearance.ValidScale(99)==3,"Invalid scales are not constrained");
            Check(!ShaderUtil.ShaderHasError(shader),"Shader failed during rendering");
            return new { passed=true, scales=new[]{.5f,1f,1.35f,2f,3f}, visiblePixels=areas,
                staysInsideTile=true, rotationPreserved=true, darkPixels=0,
                matteEnergy=matte, reflectiveEnergy=reflective, lightColorPreserved=true, haloPixels, goldPixels };

            float Energy()
            {
                float total=0;
                foreach(var c in pixels.GetPixels()) total+=c.r+c.g+c.b;
                return total;
            }

            int Render()
            {
                camera.Render();RenderTexture.active=target;
                if(!pixels)pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();
                var colors=pixels.GetPixels32();int count=0;
                for(int y=0;y<512;y++)for(int x=0;x<512;x++)
                {
                    var c=colors[y*512+x];if(c.r<8&&c.g<8&&c.b<8)continue;
                    count++;
                    Check(x>=127&&x<=384&&y>=127&&y<=384,"Ore escaped its tile at "+map.oreScale);
                }
                return count;
            }
        }
        finally
        {
            RenderTexture.active=oldTarget;
            EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=enabled[i];
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
