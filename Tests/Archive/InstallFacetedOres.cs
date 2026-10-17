using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class InstallFacetedOres
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        const string root="Assets/GameObjects/Map/Blocks";
        int count=0;
        foreach(string name in new[]{"Gold","Copper","Iron","Silver","Platinum","Coal"})
        {
            string sheetPath="Design/OreOverlaySheets/"+name+".png";
            var sheet=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!sheet.LoadImage(File.ReadAllBytes(sheetPath)))throw new Exception(sheetPath);
            int side=sheet.width/3;
            if(sheet.width!=sheet.height)throw new Exception("Expected square sheet");
            var block=AssetDatabase.LoadAssetAtPath<Block>(root+"/"+name+".asset");
            var tiles=block.smallOre.Concat(block.mediumOre).Concat(block.richOre).ToArray();
            for(int index=0;index<7;index++)
            {
                int row=index<2?0:index<4?1:2;
                int col=index<2?index:index<4?index-2:index-4;
                var pixels=sheet.GetPixels(col*side,sheet.height-(row+1)*side,side,side);
                int transparent=pixels.Count(c=>c.a<.01f);
                if(transparent<pixels.Length*.25f)throw new Exception("Missing transparency "+name);
                string folder=root+"/Sprites/Ores/Faceted/"+name;
                Directory.CreateDirectory(folder);
                string path=folder+"/"+name+"_"+(index+1).ToString("00")+"_"+(row==0?"small":row==1?"medium":"rich")+".png";
                var slice=new Texture2D(side,side,TextureFormat.RGBA32,false);
                slice.SetPixels(pixels);slice.Apply();File.WriteAllBytes(path,slice.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(slice);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=side/.5f;importer.npotScale=TextureImporterNPOTScale.None;
                importer.alphaIsTransparency=true;importer.mipmapEnabled=true;
                importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteAlignment=0;settings.spritePivot=new Vector2(.5f,.5f);
                importer.SetTextureSettings(settings);importer.SaveAndReimport();
                string old=AssetDatabase.GetAssetPath(tiles[index].sprite);
                if(!old.Contains("/Faceted/")&&!old.Contains("/Archive/"))
                {
                    string archive=root+"/Sprites/Ores/Archive/PreFaceted_20260922/"+name;
                    Directory.CreateDirectory(archive);AssetDatabase.Refresh();
                    string dest=archive+"/"+Path.GetFileName(old);
                    if(File.Exists(dest))throw new Exception("Archive already exists: "+dest);
                    string error=AssetDatabase.MoveAsset(old,dest);
                    if(!string.IsNullOrEmpty(error))throw new Exception(error);
                }
                tiles[index].sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(tiles[index]);count++;
            }
            UnityEngine.Object.DestroyImmediate(sheet);
        }
        AssetDatabase.SaveAssets();
        foreach(var map in UnityEngine.Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None))map.RefreshAllTiles();
        return count+" ore sprites installed; original sprite GUIDs archived; tile GUIDs, drops and settings preserved.";
    }
}
