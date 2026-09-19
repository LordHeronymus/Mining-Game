using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
public static class DirtBoundaryChecks
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static int Difference(Color32 a,Color32 b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
 public static object Main()
 {
  Check(!Application.isPlaying,"Run in edit mode");
  var previous=SceneManager.GetActiveScene();
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
  var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);var states=new bool[lights.Length];
  var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);var oldTarget=RenderTexture.active;
  Texture2D pixels=null,soil=null,rock=null;Material material=null;
  try
  {
   for(int i=0;i<lights.Length;i++){states[i]=lights[i].enabled;lights[i].enabled=false;}
   SceneManager.SetActiveScene(scene);
   var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
   var dirt=registry.GetById(BlockType.Dirt);var stone=registry.GetById(BlockType.Stone);
   material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat"));
   Texture2D Solid(Color c){var t=new Texture2D(2,2,TextureFormat.RGBA32,false);t.SetPixels(new[]{c,c,c,c});t.Apply();return t;}
   soil=Solid(new Color(.65f,.25f,.08f));rock=Solid(new Color(.3f,.4f,.5f));
   material.SetTexture("_DirtTex",soil);material.SetTexture("_StoneTex",rock);
   var grid=new GameObject("Boundary Check Grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
   var go=new GameObject("Boundary Check",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));go.layer=31;go.transform.SetParent(grid.transform,false);
   var map=go.GetComponent<MapGenerator>();map.enabled=false;map.registry=registry;map.mapWidth=36;map.mapHeight=40;map.seed=42319;
   var appearance=go.GetComponent<DirtSurfaceAppearance>();appearance.TerrainMaterial=material;
   map.GenerateMap();map.OreOverlay.GetComponent<TilemapRenderer>().enabled=false;
   var light=new GameObject("Boundary Light",typeof(Light2D)).GetComponent<Light2D>();light.gameObject.layer=31;light.lightType=Light2D.LightType.Global;light.intensity=1;
   var camera=new GameObject("Boundary Camera",typeof(Camera)).GetComponent<Camera>();camera.transform.position=new Vector3(0,-11.5f,-10);
   camera.orthographic=true;camera.orthographicSize=2;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
   camera.allowHDR=false;camera.targetTexture=target;
   Color32[] Render(){camera.Render();RenderTexture.active=target;if(!pixels)pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();return pixels.GetPixels32();}
   var before=Render();int maxJump=0,opaque=0;
   for(int y=0;y<512;y++)for(int x=0;x<512;x++){
    int i=y*512+x;if(before[i].a>250)opaque++;
    if(x>0)maxJump=Math.Max(maxJump,Difference(before[i],before[i-1]));
    if(y>0)maxJump=Math.Max(maxJump,Difference(before[i],before[i-512]));
   }
   Check(opaque==before.Length,"Boundary is not opaque");
   Check(maxJump<=8,"Hard boundary: adjacent pixel jump "+maxJump);
   for(int y=20;y<28;y++)for(int x=-18;x<18;x++){
    var cell=new Vector3Int(x,-y,0);bool wasDirt=registry.FromTile(map.Terrain.GetTile(cell))==dirt;
    map.Terrain.SetTile(new TileChangeData(cell,(wasDirt?stone:dirt).variants[0],new Color(1,1,1,wasDirt?1f:.5f),Matrix4x4.identity),true);
   }
   map.Terrain.RefreshAllTiles();var swapped=Render();int tileDependence=0;
   for(int i=0;i<before.Length;i++)tileDependence=Math.Max(tileDependence,Difference(before[i],swapped[i]));
   Check(tileDependence<=1,"Blend depends on tile type: "+tileDependence);
   appearance.Apply();var updated=Render();int changed=0;
   for(int i=0;i<before.Length;i++)if(Difference(before[i],updated[i])>10)changed++;
   Check(changed>10000,"Blend mask ignores actual tile distribution");
   Check(!ShaderUtil.ShaderHasError(material.shader),"Shader compilation failed");
   return new{passed=true,maxAdjacentPixelJump=maxJump,tileTypeDependence=tileDependence,maskChangedPixels=changed,opaquePixels=opaque};
  }
  finally{
   RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
   for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=states[i];
   if(pixels)UnityEngine.Object.DestroyImmediate(pixels);if(soil)UnityEngine.Object.DestroyImmediate(soil);if(rock)UnityEngine.Object.DestroyImmediate(rock);if(material)UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(target);
  }
 }
}
