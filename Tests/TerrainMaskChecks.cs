using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class TerrainMaskChecks
{
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<UniformStoneAppearance>();
        var sourceMap=source.GetComponent<MapGenerator>();
        var previous=SceneManager.GetActiveScene();
        var scene=Application.isPlaying?SceneManager.CreateScene("Embedded rubble checks"):
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var lightStates=Array.ConvertAll(lights,l=>l.enabled);
        var root=new GameObject("Embedded rubble render check");SceneManager.MoveGameObjectToScene(root,scene);
        var occupancy=new Texture2D(9,9,TextureFormat.RGBA32,false,true);
        var target=new RenderTexture(1000,1000,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(1000,1000,TextureFormat.RGBA32,false);
        var oldTarget=RenderTexture.active;
        try
        {
            foreach(var l in lights)l.enabled=false;
            GameObject Child(string name,params Type[] types)
            {
                var go=new GameObject(name,types);go.transform.SetParent(root.transform,false);go.layer=31;return go;
            }
            var grid=Child("Grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=Vector3.one*1.1f;
            var goMap=Child("Terrain",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));
            goMap.transform.SetParent(grid.transform,false);
            var map=goMap.GetComponent<MapGenerator>();map.enabled=false;map.registry=sourceMap.registry;map.seed=137;
            var tileRenderer=goMap.GetComponent<TilemapRenderer>();tileRenderer.enabled=false;tileRenderer.sortingOrder=10;
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
            var terrain=new List<SpriteRenderer>();
            var mask=new Color32[81];
            for(int y=0;y<9;y++)for(int x=0;x<9;x++)
            {
                bool hole=x>=3&&x<=5&&y>=2&&y<=6 || x==2&&y>=4&&y<=5;
                mask[y*9+x]=new Color32((byte)(hole?0:255),255,0,0);
                if(hole)continue;
                map.Terrain.SetTile(new Vector3Int(x,y,0),sourceMap.uniformTestTile);
                var go=Child("Tile",typeof(SpriteRenderer));go.transform.position=new Vector3((x+.5f)*1.1f,(y+.5f)*1.1f,0);
                go.transform.localScale=Vector3.one*2.2f;
                var r=go.GetComponent<SpriteRenderer>();r.sprite=sourceMap.uniformTestTile.sprite;
                r.sharedMaterial=material;r.sortingOrder=10;terrain.Add(r);
            }
            occupancy.SetPixels32(mask);occupancy.Apply();
            var props=new MaterialPropertyBlock();
            props.SetTexture("_TestStoneTex",source.texture);props.SetTexture("_SurfaceDirtTex",source.dirtTexture);
            props.SetTexture("_LayerOneTex",source.layerOneTexture);props.SetTexture("_LayerThreeTex",source.layerThreeTexture);
            props.SetTexture("_TestOccupancy",occupancy);props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,9,9));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            var light=Child("Light",typeof(Light2D)).GetComponent<Light2D>();
            light.lightType=Light2D.LightType.Global;light.intensity=.85f;light.color=new Color(1,.88f,.72f);
            var camera=Child("Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(4.7f,4.95f,-10);camera.orthographic=true;camera.orthographicSize=3.85f;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=false;camera.targetTexture=target;
            Color32[] Render()
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,1000),0,0);pixels.Apply();return pixels.GetPixels32();
            }
            camera.backgroundColor=new Color(.14f,.065f,.027f,1);
            Render();File.WriteAllBytes("Assets/Design/TerrainMasks-before.png",pixels.EncodeToPNG());
            props.SetFloat("_UseTerrainMasks",1);
            props.SetVector("_TerrainEdgeTuning",new Vector4(1.5f,1,1,0));
            props.SetTexture("_TerrainEdgeMasks",Resources.Load<Texture2DArray>("TerrainEdgeMasks"));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            var before=Render();
            const string preview="Assets/Design/TerrainMasks-after.png";
            File.WriteAllBytes(preview,pixels.EncodeToPNG());AssetDatabase.ImportAsset(preview);
            int[] parameterDifferences=new int[3];
            for(int axis=0;axis<3;axis++)
            {
                var tuning=new Vector4(1.5f,1,1,0);tuning[axis]=0;
                props.SetVector("_TerrainEdgeTuning",tuning);
                foreach(var r in terrain)r.SetPropertyBlock(props);
                var changed=Render();
                for(int i=0;i<changed.Length;i++)if(!changed[i].Equals(before[i]))parameterDifferences[axis]++;
                if(parameterDifferences[axis]<10)throw new Exception("Ineffective edge parameter: "+axis);
            }
            props.SetVector("_TerrainEdgeTuning",new Vector4(1.5f,1,1,0));
            // The real failure mode: enabled masking with no bound array.
            var missing=new Texture2DArray(1,1,1,TextureFormat.RHalf,false);
            props.SetTexture("_TerrainEdgeMasks",missing);
            foreach(var r in terrain)r.SetPropertyBlock(props);
            camera.backgroundColor=Color.clear;Render();
            var screen=camera.WorldToScreenPoint(new Vector3(2.5f*1.1f,2.5f*1.1f,0));
            float fallbackAlpha=pixels.GetPixel((int)screen.x,(int)screen.y).a;
            if(fallbackAlpha<.99f)throw new Exception("Missing mask makes solid terrain transparent");
            props.SetTexture("_TerrainEdgeMasks",Resources.Load<Texture2DArray>("TerrainEdgeMasks"));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            UnityEngine.Object.DestroyImmediate(missing);
            camera.backgroundColor=new Color(.14f,.065f,.027f,1);
            // Put the top row at logical Y=0 and inspect its entire skyline.
            var oldCameraPosition=camera.transform.position;
            camera.transform.position=new Vector3(4.95f,9,-10);
            props.SetVector("_UniformStone",new Vector4(1.1f,3,0,8.8f));
            props.SetVector("_TestBounds",new Vector4(0,-8,9,9));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            camera.backgroundColor=Color.clear;Render();
            for(float x=1.5f;x<8.4f;x+=.025f)
            {
                var below=camera.WorldToScreenPoint(new Vector3(x,9.87f,0));
                var above=camera.WorldToScreenPoint(new Vector3(x,9.93f,0));
                if(pixels.GetPixel((int)below.x,(int)below.y).a<.99f||pixels.GetPixel((int)above.x,(int)above.y).a>.01f)
                    throw new Exception("Original surface is not straight at "+x);
            }
            camera.transform.position=oldCameraPosition;
            props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,9,9));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            camera.backgroundColor=new Color(.14f,.065f,.027f,1);
            // Change occupancy and remove just this sprite, like the terrain tilemap.
            mask[3*9+2].r=0;occupancy.SetPixels32(mask);occupancy.Apply();
            foreach(var r in terrain)if(Vector2.Distance(r.transform.position,new Vector2(2.5f*1.1f,3.5f*1.1f))<.01f)r.enabled=false;
            var after=Render();int changes=0;for(int i=0;i<after.Length;i++)if(!after[i].Equals(before[i]))changes++;
            if(changes<100)throw new Exception("Occupancy changes did not update masks");
            camera.backgroundColor=Color.clear;light.intensity=0;var dark=Render();
            foreach(var c in dark)if(c.r>1||c.g>1||c.b>1)throw new Exception("Terrain emits light");
            if(root.GetComponentsInChildren<Collider2D>().Length>0)throw new Exception("Added collision");
            if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Shader error");
            return new{passed=true,changedPixels=changes,parameterDifferences,fallbackAlpha,preview};
        }
        finally
        {
            RenderTexture.active=oldTarget;
            UnityEngine.Object.DestroyImmediate(root);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=lightStates[i];
            SceneManager.SetActiveScene(previous);
            if(Application.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);
            UnityEngine.Object.DestroyImmediate(occupancy);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}






