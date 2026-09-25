using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FixedUndergroundChecks
{
    public static object Main()
    {
        var background=UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>();
        if(!background||!background.map||background.map.layers.Length!=4)
            throw new Exception("Four configured background layers are required");
        background.Refresh();
        var field=typeof(FixedUndergroundBackground).GetField("meshRenderer",
            System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var renderer=(MeshRenderer)field.GetValue(background);
        if(!renderer||!renderer.gameObject.activeInHierarchy)
            throw new Exception("Fixed background renderer is inactive");
        var properties=new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        for(int i=0;i<4;i++)
            if(properties.GetTexture("_Layer"+i+"Tex")!=background.map.layers[i].backgroundSprite.texture)
                throw new Exception("Wrong background texture for layer "+(i+1));
        var starts=properties.GetVector("_LayerStarts");
        float cellHeight=background.map.Terrain.transform.TransformVector(
            Vector3.up*background.map.Terrain.layoutGrid.cellSize.y).magnitude;
        if(Mathf.Abs(properties.GetFloat("_LayerFadeWorld")-
            background.fadeDepthBlocks*cellHeight)>.001f)
            throw new Exception("Background fade width is not shared by all layer boundaries");
        for(int i=1;i<4;i++)
            if(Mathf.Abs(starts[i-1]-background.map.layers[i].startDepth*cellHeight)>.001f)
                throw new Exception("Wrong world boundary for layer "+(i+1));
        background.nearHills.Refresh();
        if(background.nearHills.Renderers.Any(r=>r&&r.enabled&&
            background.map.layers.Any(l=>l.backgroundSprite==r.sprite)))
            throw new Exception("NearHills still draws an underground layer");
        if(renderer.sortingOrder<=background.nearHills.sortingOrder)
            throw new Exception("Underground does not cover NearHills");
        if(ShaderUtil.ShaderHasError(background.material.shader))
            throw new Exception("Background shader has errors");
        return new{passed=true,layers=4,worldFixed=true,duplicateUnderground=false};
    }
}
