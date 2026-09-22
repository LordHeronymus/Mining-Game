using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupDeepStone
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        const string folder="Assets/GameObjects/Map/StoneTest/Layer3";
        const string path=folder+"/DeepStone.png";
        const string backup="Assets/_SceneBackups/BeforeDeepStone_20260922.unity";
        if(!System.IO.File.Exists(backup))EditorSceneManager.SaveScene(map.gameObject.scene,backup,true);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.GetSourceTextureWidthAndHeight(out int width,out _);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.spritePixelsPerUnit=width/.5f;importer.npotScale=TextureImporterNPOTScale.None;
        importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;
        importer.filterMode=FilterMode.Trilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
        settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);importer.SaveAndReimport();
        var tile=AssetDatabase.LoadAssetAtPath<StoneTestTile>(folder+"/DeepStone.asset");
        if(!tile){tile=ScriptableObject.CreateInstance<StoneTestTile>();AssetDatabase.CreateAsset(tile,folder+"/DeepStone.asset");}
        tile.block=map.registry.GetById(BlockType.StoneLayer3);
        tile.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);tile.colliderType=Tile.ColliderType.Grid;
        map.layerThreeTile=tile;
        var appearance=map.GetComponent<UniformStoneAppearance>();
        appearance.layerThreeTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,
            map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness);
        var bounds=new BoundsInt(-map.GeneratedWidth/2,1-map.GeneratedHeight,0,map.GeneratedWidth,map.GeneratedHeight,1);
        var terrain=map.Terrain.GetTilesBlock(bounds);
        int deep=0,holes=0;
        for(int i=0;i<terrain.Length;i++)
        {
            if(!terrain[i]){holes++;continue;}
            var block=sampler.GetBaseBlock(i%map.GeneratedWidth,-(bounds.yMin+i/map.GeneratedWidth));
            if(block==tile.block){terrain[i]=tile;deep++;}
        }
        map.Terrain.SetTilesBlock(bounds,terrain);
        appearance.enabled=false;appearance.enabled=true;
        EditorUtility.SetDirty(tile);EditorUtility.SetDirty(map);EditorUtility.SetDirty(appearance);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(map.gameObject.scene);
        return "Deep stone cells="+deep+"; preserved holes="+holes;
    }
}
