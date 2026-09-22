using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class FrayedTerrainChecks
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
            var rubble=goMap.AddComponent<TerrainEdgeRubble>();rubble.Apply(props,camera);
            Render();File.WriteAllBytes("Assets/Design/FrayedTerrain-before.png",pixels.EncodeToPNG());
            var oldVisual=goMap.transform.Find("Terrain edge rubble");UnityEngine.Object.DestroyImmediate(rubble);if(oldVisual)UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
            props.SetFloat("_FrayedInset",.18f);
            foreach(var r in terrain)r.SetPropertyBlock(props);
            var frayed=goMap.AddComponent<TerrainFrayedEdges>();frayed.Apply(props,camera);
            var mesh=goMap.transform.Find("Frayed terrain edges").GetComponent<MeshFilter>().sharedMesh;
            var meshRenderer=goMap.transform.Find("Frayed terrain edges").GetComponent<MeshRenderer>();
            if(mesh.vertexCount==0)throw new Exception("No frayed geometry");
            Render();
            const string preview="Assets/Design/FrayedTerrain-after.png";
            File.WriteAllBytes(preview,pixels.EncodeToPNG());AssetDatabase.ImportAsset(preview);
            int oldVertices=mesh.vertexCount;var previousVertices=mesh.vertices;
            map.Terrain.SetTile(new Vector3Int(2,3,0),null);typeof(TerrainFrayedEdges).GetMethod("Changed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frayed,new object[]{map.Terrain,new Tilemap.SyncTile[0]});frayed.Apply(props,camera);
            bool changed=mesh.vertexCount!=oldVertices;var currentVertices=mesh.vertices;for(int i=0;!changed&&i<currentVertices.Length;i++)changed=currentVertices[i]!=previousVertices[i];if(!changed)throw new Exception("Mining did not rebuild exposed edges");
            camera.backgroundColor=Color.clear;light.intensity=0;var dark=Render();
            foreach(var c in dark)if(c.r>1||c.g>1||c.b>1)throw new Exception("Terrain emits light");
            if(root.GetComponentsInChildren<Collider2D>().Length>0)throw new Exception("Decorative edges have collision");
            if(ShaderUtil.ShaderHasError(material.shader)||ShaderUtil.ShaderHasError(meshRenderer.sharedMaterial.shader))throw new Exception("Shader error");
            return new{passed=true,vertices=oldVertices,preview};
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




