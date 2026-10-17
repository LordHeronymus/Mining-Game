using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class RedistributeTerrainVariants
{
    public static object Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Run in Edit Mode");
        int maps=0,changed=0,holes=0,adjacentSame=0;
        foreach(var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            if(!map.registry || map.GeneratedWidth<=0 || map.GeneratedHeight<=0)continue;
            int width=map.GeneratedWidth;
            var previousBlocks=new Block[width];var currentBlocks=new Block[width];
            var previousIndices=new int[width];var currentIndices=new int[width];
            var changes=new List<TileChangeData>();
            var variantGroups=new Dictionary<Block,int[]>();
            var terrain=map.Terrain;
            for(int y=0;y<map.GeneratedHeight;y++)
            {
                for(int x=0;x<width;x++)
                {
                    var cell=new Vector3Int(x-width/2,-y,0);
                    var tile=terrain.GetTile(cell);
                    if(!tile){holes++;continue;}
                    var block=map.registry.FromTile(tile);
                    if(!block || block.variants==null || block.variants.Length==0)continue;
                    int left=x>0 && currentBlocks[x-1]==block?currentIndices[x-1]:-1;
                    int above=previousBlocks[x]==block?previousIndices[x]:-1;
                    if(!variantGroups.TryGetValue(block,out var groups))
                    {groups=TerrainVariantSelector.VisualGroups(block.variants);variantGroups.Add(block,groups);}
                    int index=TerrainVariantSelector.Choose(map.ActiveSeed,x,y,groups,left,above);
                    int current=Array.IndexOf(block.variants,tile);
                    if(current==left && left>=0)adjacentSame++;
                    if(current==above && above>=0)adjacentSame++;
                    currentBlocks[x]=block;currentIndices[x]=index;
                    if(tile==block.variants[index])continue;
                    changes.Add(new TileChangeData(cell,block.variants[index],terrain.GetColor(cell),terrain.GetTransformMatrix(cell)));
                }
                (previousBlocks,currentBlocks)=(currentBlocks,previousBlocks);
                (previousIndices,currentIndices)=(currentIndices,previousIndices);
                Array.Clear(currentBlocks,0,currentBlocks.Length);
            }
            if(changes.Count>0)terrain.SetTiles(changes.ToArray(),true);
            map.GetComponent<DirtSurfaceAppearance>()?.Apply();
            if(changes.Count>0)EditorSceneManager.SaveScene(map.gameObject.scene);
            maps++;changed+=changes.Count;
        }
        return new{maps,changed,holes,adjacentSame};
    }
}
