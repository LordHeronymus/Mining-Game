using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class EditorMapGenerationChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}

    public static object Main()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Test requires Edit Mode");
        var previous=SceneManager.GetActiveScene();
        var source=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(source && source.registry,"Scene registry missing");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var grid=new GameObject("Editor Map Test Grid",typeof(Grid));
            grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var go=new GameObject("Editor Map Test",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));
            go.transform.SetParent(grid.transform,false);
            var map=go.GetComponent<MapGenerator>();
            map.registry=source.registry;map.mapWidth=32;map.mapHeight=80;map.seed=123456;map.randomizeSeed=false;
            var grassProperties=new SerializedObject(source).FindProperty("grassVariants");
            var grassVariants=new TileBase[grassProperties.arraySize];
            for(int i=0;i<grassVariants.Length;i++)
                grassVariants[i]=grassProperties.GetArrayElementAtIndex(i).objectReferenceValue as TileBase;
            Check(grassVariants.Length==4,"Grass variants missing from the scene");
            map.SetGrassVariants(grassVariants);
            map.grassYOffset=.12f;
            map.oreDensityCurve=AnimationCurve.Constant(0,1,1);
            map.oreDensityMultiplierPercent=100;
            map.oreTransitionCurve=AnimationCurve.Constant(0,1,1);
            map.oreTransitionDepth=1;
            var stone=source.registry.GetById(BlockType.Stone);
            var deep=source.registry.GetById(BlockType.StoneLayer2);
            map.layers=new[]{new MapLayer{name="First",startDepth=0,stone=stone,ores=new[]{BlockType.CopperOre}},
                new MapLayer{name="Second",startDepth=48,stone=deep,ores=new[]{BlockType.CopperOre}}};

            int fixedSeed=MapEditorGeneration.Generate(map);
            Check(fixedSeed==123456 && map.IsGenerated && map.ActiveSeed==fixedSeed,"Fixed-seed Edit Mode generation failed");
            Check(map.Terrain.GetUsedTilesCount()>0 && map.OreOverlay.GetUsedTilesCount()>0,
                "Terrain or ore overlay missing");
            int grassCells=0;
            for(int x=-map.mapWidth/2;x<map.mapWidth/2;x++)
                if(map.GrassOverlay.HasTile(new Vector3Int(x,0,0)))grassCells++;
            Check(grassCells==map.mapWidth &&
                !map.GrassOverlay.HasTile(new Vector3Int(-map.mapWidth/2,-1,0)) &&
                Mathf.Abs(map.GrassOverlay.transform.localPosition.y-.12f)<.0001f,
                "Grass is not limited to the top row or its offset is wrong");
            var bounds=map.Terrain.cellBounds;
            var terrain=map.Terrain.GetTilesBlock(bounds);
            var ores=map.OreOverlay.GetTilesBlock(bounds);
            var grass=map.GrassOverlay.GetTilesBlock(map.GrassOverlay.cellBounds);
            MapEditorGeneration.Generate(map);
            var repeatedTerrain=map.Terrain.GetTilesBlock(bounds);
            var repeatedOres=map.OreOverlay.GetTilesBlock(bounds);
            var repeatedGrass=map.GrassOverlay.GetTilesBlock(map.GrassOverlay.cellBounds);
            for(int i=0;i<terrain.Length;i++)
                Check(terrain[i]==repeatedTerrain[i] && ores[i]==repeatedOres[i],"Fixed seed changed a tile");
            for(int i=0;i<grass.Length;i++) Check(grass[i]==repeatedGrass[i],"Fixed seed changed grass");

            map.randomizeSeed=true;
            int randomSeed=MapEditorGeneration.Generate(map);
            Check(randomSeed!=0 && randomSeed!=map.seed && map.seed==123456 && map.ActiveSeed==randomSeed,
                "Random mode did not use a temporary random seed");
            Undo.PerformUndo();
            Check(map.IsGenerated && map.ActiveSeed==fixedSeed,"Undo did not restore map metadata");
            var restored=map.Terrain.GetTilesBlock(bounds);
            var restoredOres=map.OreOverlay.GetTilesBlock(bounds);
            var restoredGrass=map.GrassOverlay.GetTilesBlock(map.GrassOverlay.cellBounds);
            for(int i=0;i<terrain.Length;i++)
                Check(terrain[i]==restored[i] && ores[i]==restoredOres[i],"Undo did not restore both tilemaps");
            for(int i=0;i<grass.Length;i++) Check(grass[i]==restoredGrass[i],"Undo did not restore grass");
            var topCell=new Vector3Int(-map.mapWidth/2,0,0);
            Check(map.RemoveBlock(topCell) && !map.GrassOverlay.HasTile(topCell),"Mining left grass behind");
            return new{passed=true,terrainCells=terrain.Length,oreTiles=map.OreOverlay.GetUsedTilesCount(),fixedSeed,randomSeed};
        }
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(previous);
        }
    }
}
