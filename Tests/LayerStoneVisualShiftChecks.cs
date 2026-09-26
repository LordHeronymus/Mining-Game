using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LayerStoneVisualShiftChecks
{
    public static object Main()
    {
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var appearance=map.GetComponent<UniformStoneAppearance>();
        const string root="Assets/GameObjects/Map/StoneTest/";
        var light=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Layer3/LightGrayStone.png");
        var former=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"WarmStone.png");
        var saved=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Layer3/DeepStone.png");
        if(!light||!former||!saved||appearance.texture!=light||appearance.layerThreeTexture!=former)
            throw new Exception("L3/L4/L5 texture order is incorrect");
        foreach(var path in new[]{root+"Layer3/LightGrayStone.png",root+"WarmStone.png"})
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null || importer.wrapModeU!=TextureWrapMode.Repeat ||
                importer.wrapModeV!=TextureWrapMode.Repeat)
                throw new Exception("Terrain texture must repeat on both axes: "+path);
        }
        if(map.uniformTestTile.block!=map.layers[2].stone ||
            map.uniformTestTile.sprite!=AssetDatabase.LoadAssetAtPath<Sprite>(root+"Layer3/LightGrayStone.png") ||
            map.layerThreeTile.block!=map.layers[3].stone ||
            map.layerThreeTile.sprite!=AssetDatabase.LoadAssetAtPath<Sprite>(root+"WarmStone.png"))
            throw new Exception("Layer tiles do not match their stones");
        if(Mathf.Abs(map.uniformTestTile.sprite.bounds.size.x-1f)>.01f ||
            Mathf.Abs(map.layerThreeTile.sprite.bounds.size.x-1f)>.01f)
            throw new Exception("Layer tile sprites extend beyond one cell");
        for(int i=0;i<map.layers[2].stone.variants.Length;i++)
        {
            var third=SpriteOf(map.layers[2].stone.variants[i]);
            var fourth=SpriteOf(map.layers[3].stone.variants[i]);
            if(third!=map.uniformTestTile.sprite ||
                !AssetDatabase.GetAssetPath(fourth).Contains("/Sprites/Stein_"))
                throw new Exception("Variant art did not shift at index "+i);
        }
        var future=AssetDatabase.LoadAssetAtPath<Block>(
            "Assets/GameObjects/Map/Blocks/LayerStones/Stone_Layer4.asset");
        foreach(var variant in future.variants)
            if(!AssetDatabase.GetAssetPath(SpriteOf(variant)).Contains("/DeepStone/Layer3/TS1_"))
                throw new Exception("Former L4 art is not prepared for L5");
        appearance.RefreshAppearance();
        var properties=new MaterialPropertyBlock();
        map.GetComponent<TilemapRenderer>().GetPropertyBlock(properties);
        if(properties.GetTexture("_TestStoneTex")!=light ||
            properties.GetTexture("_LayerThreeTex")!=former)
            throw new Exception("Terrain renderer uses the wrong layer textures");
        return new{passed=true,layer3=light.name,layer4=former.name,futureLayer5=saved.name};
    }

    static Sprite SpriteOf(TileBase tile)
        => new SerializedObject(tile).FindProperty("m_DefaultSprite").objectReferenceValue as Sprite;
}
