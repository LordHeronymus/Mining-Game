using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class TerrainCollisionChecks
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
            foreach(var existingCollider in goMap.GetComponents<Collider2D>())existingCollider.enabled=false;
            var shapes=new TerrainCollisionShape(source,map.Terrain);
            try
            {
                map.Terrain.orientation=sourceMap.Terrain.orientation;
                map.Terrain.orientationMatrix=sourceMap.Terrain.orientationMatrix;
                var physics=Child("Collision",typeof(Tilemap),typeof(TilemapCollider2D));
                physics.transform.SetParent(grid.transform,false);
                var tm=physics.GetComponent<Tilemap>();tm.orientation=map.Terrain.orientation;tm.orientationMatrix=map.Terrain.orientationMatrix;
                var paths=new List<Vector2[]>();
                foreach(var cell in map.Terrain.cellBounds.allPositionsWithin)
                {
                    var tile=map.Terrain.GetTile(cell);if(!tile)continue;
                    var outline=shapes.Get(cell,tile);
                    if(outline==null){tm.SetTile(cell,tile);continue;}
                    var path=new Vector2[outline.Length];
                    for(int i=0;i<path.Length;i++)path[i]=map.Terrain.CellToWorld(cell)+Vector3.Scale(outline[i],map.Terrain.layoutGrid.cellSize);
                    paths.Add(path);
                }
                var body=physics.AddComponent<Rigidbody2D>();body.bodyType=RigidbodyType2D.Static;
                var composite=physics.AddComponent<CompositeCollider2D>();composite.geometryType=CompositeCollider2D.GeometryType.Polygons;composite.vertexDistance=.001f;
                physics.GetComponent<TilemapCollider2D>().compositeOperation=Collider2D.CompositeOperation.Merge;
                var polygon=physics.AddComponent<PolygonCollider2D>();polygon.compositeOperation=Collider2D.CompositeOperation.Merge;polygon.pathCount=paths.Count;
                for(int i=0;i<paths.Count;i++)polygon.SetPath(i,paths[i]);
                physics.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();Physics2D.SyncTransforms();
                props.SetVector("_TerrainEdgeTuning",new Vector4(source.edgeDepth,source.edgeIrregularity,source.edgeRounding,0));
                foreach(var r in terrain)r.SetPropertyBlock(props);
                camera.backgroundColor=Color.clear;Render();
                int samples=0;float lowest=10,highest=-10;
                for(float x=3.45f;x<6.4f;x+=.08f)
                {
                    var hit=Physics2D.Raycast(new Vector2(x,3.0f),Vector2.down,2,1<<31);
                    if(!hit)throw new Exception("Missing floor collider at "+x);
                    var under=camera.WorldToScreenPoint(hit.point-Vector2.up*.035f);
                    var over=camera.WorldToScreenPoint(hit.point+Vector2.up*.035f);
                    float below=pixels.GetPixel((int)under.x,(int)under.y).a,above=pixels.GetPixel((int)over.x,(int)over.y).a;
                    if(below<.8f||above>.2f)throw new Exception("Physics/render mismatch at "+hit.point+" alpha below/above "+below+"/"+above+" cpu="+shapes.Distance(new Vector2(hit.point.x/1.1f-3,hit.point.y/1.1f-1),shapes.Neighbours(new Vector3Int(3,1,0)),TerrainCollisionShape.Variant(new Vector3Int(3,1,0)),false)+" collider="+hit.collider.name+" bits="+shapes.Neighbours(new Vector3Int(3,1,0))+" tuning="+source.edgeDepth);
                    lowest=Mathf.Min(lowest,hit.point.y);highest=Mathf.Max(highest,hit.point.y);samples++;
                }
                if(highest-lowest<.025f)throw new Exception("Collision still flat");
                return new{passed=true,samples,lowest,highest};
            }
            finally{shapes.Dispose();}
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















