using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class OrganicTerrainEdgeChecks
{
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var disabled=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var states=Array.ConvertAll(disabled,l=>l.enabled);
        const int width=9,height=10;
        var occupancy=new Texture2D(width,height,TextureFormat.RGBA32,false,true)
        {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var target=new RenderTexture(900,900,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(900,900,TextureFormat.RGBA32,false);
        var oldTarget=RenderTexture.active;
        try
        {
            for(int i=0;i<disabled.Length;i++)disabled[i].enabled=false;
            SceneManager.SetActiveScene(scene);
            var source=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            var look=source.GetComponent<UniformStoneAppearance>();
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
            var sprite=source.uniformTestTile.sprite;
            var grid=new GameObject("Rubble test grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=Vector3.one*1.1f;
            var mapObject=new GameObject("Rubble test map",typeof(UnityEngine.Tilemaps.Tilemap),typeof(UnityEngine.Tilemaps.TilemapRenderer),typeof(MapGenerator));
            mapObject.layer=31;mapObject.transform.SetParent(grid.transform,false);
            var testMap=mapObject.GetComponent<MapGenerator>();testMap.enabled=false;testMap.registry=source.registry;
            var testRenderer=mapObject.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();testRenderer.sharedMaterial=material;testRenderer.enabled=false;
            var rubble=mapObject.AddComponent<TerrainEdgeRubble>();
            var mask=new Color32[width*height];
            int solids=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                bool shaft=(x==4||x==5)&&y>=2;
                bool pocket=x==3&&y>=4&&y<=6;
                bool solid=!shaft&&!pocket;
                mask[y*width+x]=new Color32((byte)(solid?255:0),(byte)(y>=8?255:0),(byte)(y>=5&&y<8?255:0),(byte)(y<3?255:0));
                if(!solid)continue;
                testMap.Terrain.SetTile(new Vector3Int(x,y,0),source.uniformTestTile);
                solids++;
                var go=new GameObject("Terrain "+x+","+y,typeof(SpriteRenderer));go.layer=31;
                go.transform.position=new Vector3((x+.5f)*1.1f,(y+.5f)*1.1f,0);
                go.transform.localScale=Vector3.one*2.2f;
                var renderer=go.GetComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
            }
            occupancy.SetPixels32(mask);occupancy.Apply(false,false);
            var props=new MaterialPropertyBlock();
            props.SetTexture("_TestStoneTex",look.texture);
            props.SetTexture("_SurfaceDirtTex",look.dirtTexture);
            props.SetTexture("_LayerOneTex",look.layerOneTexture);
            props.SetTexture("_LayerThreeTex",look.layerThreeTexture);
            props.SetTexture("_TestOccupancy",occupancy);
            props.SetVector("_UniformStone",new Vector4(1.1f,3,0,0));
            props.SetVector("_TestBounds",new Vector4(0,0,width,height));
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if(renderer.gameObject.scene==scene)renderer.SetPropertyBlock(props);

            var light=new GameObject("Light",typeof(Light2D)).GetComponent<Light2D>();light.gameObject.layer=31;
            light.lightType=Light2D.LightType.Global;light.intensity=.78f;light.color=new Color(1,.83f,.62f);
            var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();camera.transform.position=new Vector3(4.95f,5.5f,-10);
            camera.orthographic=true;camera.orthographicSize=5.5f;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.075f,.035f,.02f);camera.cullingMask=1<<31;camera.allowHDR=false;camera.targetTexture=target;
            rubble.Apply(props,camera);
            var crumbMesh=mapObject.GetComponentInChildren<MeshFilter>().sharedMesh;
            int beforeCount=crumbMesh.vertexCount;
            if(beforeCount<100)throw new Exception("No projecting rubble generated");
            camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,900,900),0,0);pixels.Apply();
            Directory.CreateDirectory("Assets/Design");File.WriteAllBytes("Assets/Design/OrganicTerrainEdges-detail.png",pixels.EncodeToPNG());
            int alpha=0;foreach(var c in pixels.GetPixels32())if(c.a>8)alpha++;
            if(alpha<250000)throw new Exception("Terrain render unexpectedly sparse: "+alpha);
            var shader=material.shader;if(ShaderUtil.ShaderHasError(shader))throw new Exception("Terrain shader failed");
            AssetDatabase.ImportAsset("Assets/Design/OrganicTerrainEdges-detail.png");
            light.intensity=0;camera.backgroundColor=Color.black;camera.Render();RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,900,900),0,0);pixels.Apply();
            foreach(var color in pixels.GetPixels32())if(color.r>1||color.g>1||color.b>1)throw new Exception("Rubble emits light in darkness");
            if(mapObject.GetComponentsInChildren<Collider2D>().Length>0)throw new Exception("Decorative rubble added collision");
            return new {passed=true,solidCells=solids,rubbleVertices=beforeCount,darkPixels=0,collidersUnchanged=true,preview="Assets/Design/OrganicTerrainEdges-detail.png"};
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<disabled.Length;i++)if(disabled[i])disabled[i].enabled=states[i];
            UnityEngine.Object.DestroyImmediate(occupancy);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
