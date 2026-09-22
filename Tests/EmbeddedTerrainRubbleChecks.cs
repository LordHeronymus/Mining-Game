using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class EmbeddedTerrainRubbleChecks
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
            var appearance=goMap.AddComponent<UniformStoneAppearance>();appearance.enabled=false;
            appearance.useTerrainMasks=true;appearance.edgeDepth=source.edgeDepth;
            appearance.edgeIrregularity=source.edgeIrregularity;appearance.edgeRounding=source.edgeRounding;
            var props=new MaterialPropertyBlock();
            props.SetTexture("_TestStoneTex",source.texture);props.SetTexture("_SurfaceDirtTex",source.dirtTexture);
            props.SetTexture("_LayerOneTex",source.layerOneTexture);props.SetTexture("_LayerThreeTex",source.layerThreeTexture);
            props.SetTexture("_TestOccupancy",occupancy);props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,9,9));
            props.SetFloat("_UseTerrainMasks",1);
            props.SetTexture("_TerrainEdgeMasks",Resources.Load<Texture2DArray>("TerrainEdgeMasks"));
            props.SetVector("_TerrainEdgeTuning",new Vector4(appearance.edgeDepth,appearance.edgeIrregularity,appearance.edgeRounding,0));
            foreach(var r in terrain)r.SetPropertyBlock(props);
            var light=Child("Light",typeof(Light2D)).GetComponent<Light2D>();
            light.lightType=Light2D.LightType.Global;light.intensity=.85f;light.color=new Color(1,.88f,.72f);
            var camera=Child("Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(4.7f,4.95f,-10);camera.orthographic=true;camera.orthographicSize=3.85f;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=false;camera.targetTexture=target;
            var rubble=goMap.GetComponent<TerrainEdgeRubble>();if(!rubble)rubble=goMap.AddComponent<TerrainEdgeRubble>();
            rubble.enabled=true;rubble.Apply(props,camera);
            var mesh=goMap.GetComponentInChildren<MeshFilter>().sharedMesh;
            var meshRenderer=goMap.GetComponentInChildren<MeshRenderer>();
            if(meshRenderer.sortingOrder>=tileRenderer.sortingOrder)throw new Exception("Rubble is not behind terrain");
            if(mesh.vertexCount==0||mesh.vertexCount%13!=0)throw new Exception("Missing rubble geometry");
            var vertices=mesh.vertices;float smallest=float.MaxValue,largest=0;
            for(int i=0;i<vertices.Length;i+=13)
            {
                float radius=0;for(int j=1;j<13;j++)radius=Mathf.Max(radius,Vector3.Distance(vertices[i],vertices[i+j]));
                smallest=Mathf.Min(smallest,radius);largest=Mathf.Max(largest,radius);
            }
            if(largest/smallest<2)throw new Exception("Large and small rubble sizes are not distinct");
            Color32[] Render()
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,1000),0,0);pixels.Apply();return pixels.GetPixels32();
            }
            camera.backgroundColor=new Color(.14f,.065f,.027f,1);Render();
            const string preview="Assets/Design/EmbeddedTerrainCrumbs-check.png";
            File.WriteAllBytes(preview,pixels.EncodeToPNG());AssetDatabase.ImportAsset(preview);
            camera.backgroundColor=Color.clear;
            meshRenderer.enabled=false;var terrainPixels=Render();
            foreach(var r in terrain)r.enabled=false;
            meshRenderer.enabled=true;var crumbPixels=Render();
            int crumbArea=0,buriedArea=0;
            for(int i=0;i<crumbPixels.Length;i++)if(crumbPixels[i].a>128)
            {crumbArea++;if(terrainPixels[i].a>128)buriedArea++;}
            float buried=(float)buriedArea/Mathf.Max(1,crumbArea);
            if(crumbArea<100||buried<.30f||buried>.70f)throw new Exception("Unexpected covered fraction: "+buried);
            foreach(var r in terrain)r.enabled=true;
            light.intensity=0;var dark=Render();
            foreach(var c in dark)if(c.r>1||c.g>1||c.b>1)throw new Exception("Terrain/rubble emits light");
            if(root.GetComponentsInChildren<Collider2D>().Length>0)throw new Exception("Decorative rubble has a collider");
            if(ShaderUtil.ShaderHasError(material.shader)||ShaderUtil.ShaderHasError(meshRenderer.sharedMaterial.shader))throw new Exception("Shader error");
            int originalCount=mesh.vertexCount;
            appearance.rubbleAmount=0;rubble.Apply(props,camera);
            if(mesh.vertexCount!=0)throw new Exception("Zero amount must remove all crumbs");
            appearance.rubbleAmount=1;rubble.Apply(props,camera);int sparse=mesh.vertexCount;
            appearance.rubbleAmount=8;rubble.Apply(props,camera);
            if(mesh.vertexCount<=sparse)throw new Exception("Amount does not increase density");
            appearance.rubbleMinSize=appearance.rubbleMaxSize=.2f;rubble.Apply(props,camera);
            var fixedSize=mesh.vertices;
            for(int i=0;i<fixedSize.Length;i+=13)
                if(Mathf.Abs(Vector3.Distance(fixedSize[i+4],fixedSize[i+10])-.2f*1.1f*.85f)>.001f)
                    throw new Exception("Fixed diameter does not match size settings");
            appearance.rubbleProtrusionPercent=0;rubble.Apply(props,camera);var buriedVertices=mesh.vertices;
            appearance.rubbleProtrusionPercent=100;rubble.Apply(props,camera);var exposedVertices=mesh.vertices;
            for(int i=0;i<buriedVertices.Length;i+=13)
                if(Mathf.Abs(Vector3.Distance(buriedVertices[i],exposedVertices[i])-.2f*1.1f*.85f)>.001f)
                    throw new Exception("Protrusion does not move the full normal diameter");
            return new{passed=true,stones=originalCount/13,buriedFraction=buried,sizeRatio=largest/smallest,preview};
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
