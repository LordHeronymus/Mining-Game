using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
public static class TransitionThicknessChecks
{
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
 public static object Main()
 {
  Check(!Application.isPlaying,"Run in edit mode");
  var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
  var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
  var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
  try
  {
   SceneManager.SetActiveScene(scene);
   var grid=new GameObject("Thickness Grid",typeof(Grid));grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
   var go=new GameObject("Thickness Map",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));go.transform.SetParent(grid.transform,false);
   var map=go.GetComponent<MapGenerator>();map.enabled=false;map.registry=registry;map.mapWidth=48;map.mapHeight=105;map.seed=63415;map.randomizeSeed=false;
   map.layers=new[]{new MapLayer{name="Upper",startDepth=0,stone=registry.GetById(BlockType.Stone),ores=Array.Empty<BlockType>()},
    new MapLayer{name="Lower",startDepth=60,stone=registry.GetById(BlockType.StoneLayer2),ores=Array.Empty<BlockType>()}};
   go.GetComponent<DirtSurfaceAppearance>().TerrainMaterial=material;
   var properties=new MaterialPropertyBlock();
   foreach(int width in new[]{5,15,30})
   {
    map.transitionThickness=width;map.GenerateMap();
    var sampler=new MapGenerationSampler(registry,map.seed,map.mapHeight,map.layers,map.oreDensityCurve,map.oreDensityMultiplierPercent,width,map.oreTransitionCurve,map.oreTransitionDepth, map.oreVeinSizeCurve, map.surfaceOreRampDepth, map.surfaceOreRampCurve, map.surfaceOreVeinSizePercent);
    Check(sampler.DirtEndDepth==20+width,"Dirt width ignored");
    int dirt=0,upper=0;
    for(int y=0;y<map.mapHeight;y++)for(int x=0;x<map.mapWidth;x++)
    {
     var cell=new Vector3Int(x-map.mapWidth/2,-y,0);
     Check(registry.FromTile(map.Terrain.GetTile(cell))==sampler.GetBaseBlock(x,y),"Generated substrate disagrees with setting");
     if(y>=20 && y<20+width && sampler.IsDirtAt(x,y))dirt++;
     if(y>=60 && y<60+width && sampler.IsFirstLayerStoneAt(x,y))upper++;
     if(y>=20+width && y<60)Check(!sampler.IsDirtAt(x,y),"Dirt extends past selected width");
     if(y>=60+width)Check(!sampler.IsFirstLayerStoneAt(x,y),"Layer blend extends past selected width");
    }
    Check(dirt>0 && upper>0,"Transition has no material scatter");
    go.GetComponent<TilemapRenderer>().GetPropertyBlock(properties);
    Check(Mathf.Approximately(properties.GetVector("_DirtSurface").z,width) &&
     Mathf.Approximately(properties.GetVector("_DeepSurface").z,width),"Shader did not receive the selected width");
   }
   return new{passed=true,validatedWidths=new[]{5,15,30},sceneTilesPreserved=true};
  }
  finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(prior);}
 }
}
