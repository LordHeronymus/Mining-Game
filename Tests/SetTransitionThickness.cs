using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetTransitionThickness
{
    public static object Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return "NEEDS_EDIT_MODE";
        int maps=0,changed=0,holes=0;
        foreach(var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(!map.registry || !map.Terrain || !map.IsGenerated)continue;
            map.transitionThickness=15;
            var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,
                map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness,map.oreTransitionCurve,map.oreTransitionDepth, map.oreVeinSizeCurve, map.surfaceOreRampDepth, map.surfaceOreRampCurve);
            var edits=new List<TileChangeData>();
            void UpdateRows(int start,int end)
            {
                for(int y=start;y<Mathf.Min(end,map.GeneratedHeight);y++)for(int x=0;x<map.GeneratedWidth;x++)
                {
                    var cell=new Vector3Int(x-map.GeneratedWidth/2,-y,0);
                    if(!map.Terrain.HasTile(cell)){holes++;continue;}
                    var block=sampler.GetBaseBlock(x,y);
                    if(!block || block.variants==null || block.variants.Length==0)
                        throw new Exception("Missing block variant at depth "+y);
                    var tile=block.variants[OreVeins.Hash(map.ActiveSeed,x,y,0x1234u)%(uint)block.variants.Length];
                    float alpha=block.id==BlockType.Dirt?.5f:1f;
                    if(map.Terrain.GetTile(cell)==tile && Mathf.Abs(map.Terrain.GetColor(cell).a-alpha)<.01f)continue;
                    edits.Add(new TileChangeData(cell,tile,new Color(1,1,1,alpha),Matrix4x4.identity));
                }
            }
            UpdateRows(MapGenerationSampler.SurfaceDirtRows,sampler.DirtEndDepth);
            if(sampler.FirstStoneBoundary>0)
                UpdateRows(sampler.FirstStoneBoundary,sampler.FirstStoneBoundary+sampler.TransitionThickness);
            map.Terrain.SetTiles(edits.ToArray(),true);
            map.Terrain.RefreshAllTiles();
            map.GetComponent<DirtSurfaceAppearance>()?.Apply();
            EditorUtility.SetDirty(map);EditorUtility.SetDirty(map.Terrain);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if(!EditorSceneManager.SaveScene(map.gameObject.scene))throw new Exception("Could not save scene");
            maps++;changed+=edits.Count;
        }
        return new{success=true,maps,transitionThickness=15,changed,preservedHoles=holes};
    }
}
