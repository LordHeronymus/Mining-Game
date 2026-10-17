using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupLayerOneRock
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        const string folder="Assets/GameObjects/Map/StoneTest/Layer1";
        const string path=folder+"/ClayStone.png";
        const string backup="Assets/_SceneBackups/BeforeLayerOneRock_20260922.unity";
        if(!System.IO.File.Exists(backup))EditorSceneManager.SaveScene(map.gameObject.scene,backup,true);
        AssetDatabase.Refresh();
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.GetSourceTextureWidthAndHeight(out int width,out _);
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;
        importer.spritePixelsPerUnit=width/.5f;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.wrapMode=TextureWrapMode.Repeat;
        importer.mipmapEnabled=true;
        importer.filterMode=FilterMode.Trilinear;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
        settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        var tile=AssetDatabase.LoadAssetAtPath<StoneTestTile>(folder+"/ClayStone.asset");
        if(!tile){tile=ScriptableObject.CreateInstance<StoneTestTile>();AssetDatabase.CreateAsset(tile,folder+"/ClayStone.asset");}
        tile.block=map.registry.GetById(BlockType.Stone);
        tile.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        tile.colliderType=Tile.ColliderType.Grid;
        map.layerOneTile=tile;
        var appearance=map.GetComponent<UniformStoneAppearance>();
        appearance.layerOneTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,
            map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness);
        var bounds=new BoundsInt(-map.GeneratedWidth/2,1-map.GeneratedHeight,0,map.GeneratedWidth,map.GeneratedHeight,1);
        var terrain=map.Terrain.GetTilesBlock(bounds);
        int dirt=0,layer1=0,holes=0;
        for(int i=0;i<terrain.Length;i++)
        {
            if(!terrain[i]){holes++;continue;}
            int x=i%map.GeneratedWidth,depth=-(bounds.yMin+i/map.GeneratedWidth);
            var block=sampler.GetBaseBlock(x,depth);
            if(block.id==BlockType.Dirt){terrain[i]=map.surfaceDirtTile;dirt++;}
            else if(block==tile.block){terrain[i]=tile;layer1++;}
            else if(map.layerThreeTile && block==map.layerThreeTile.block)terrain[i]=map.layerThreeTile;
            else terrain[i]=map.uniformTestTile;
        }
        map.Terrain.SetTilesBlock(bounds,terrain);
        appearance.enabled=false;appearance.enabled=true;
        EditorUtility.SetDirty(tile);EditorUtility.SetDirty(map);EditorUtility.SetDirty(appearance);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(map.gameObject.scene);
        return "Dirt="+dirt+"; L1="+layer1+"; preserved holes="+holes+"; pure dirt rows="+MapGenerationSampler.SurfaceDirtRows;
    }
}
