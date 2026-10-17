using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;

public static class InstallInventoryOreIcons
{
    const string Root = "Assets/GameObjects/Items/Sprites/";
    const string Archive = Root + "Archive/Ores_PreInventoryRedesign_20260921/";
    const string Generated = "C:/Users/jlang/.codex/generated_images/01a0be3e-ef8a-7542-a8dd-e0d1424ba253/";
    static readonly string[] Names = { "Coal", "Iron", "Copper", "Silver", "Gold", "Platinum" };
    static readonly string[] Sources = {
        "exec-1294595c-b3e2-4135-a934-8ef6d13a37a0.png", "exec-e1ed146f-a6b9-4858-927e-fdc5efc15692.png",
        "exec-e1472876-bb48-4bd3-9131-96a1e973b353.png", "exec-1b83abea-0641-4837-940e-4fcf667e0740.png",
        "exec-9a1ab435-0a3f-4a86-9343-a5a3c9586540.png", "exec-2b723965-ffd9-4fbf-bd56-b14b6b8ee76d.png"
    };
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0,path.LastIndexOf('/'));
        Folder(parent); AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
    }
    static string Hash(string path) { using(var hash=SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Use Edit Mode");
        for(int i=0;i<Names.Length;i++)
        {
            if (!File.Exists(Generated+Sources[i])) throw new Exception("Missing source "+Names[i]);
            string current=Root+"Ores/"+Names[i]+"_ItemIcon.png";
            if (!File.Exists(current)) throw new Exception("Missing current icon "+Names[i]);
            if (File.Exists(Archive+Names[i]+"_ItemIcon.png") && Hash(current)!=Hash(Generated+Sources[i]))
                throw new Exception("Archive already exists but current file differs: "+Names[i]);
        }
        Folder(Archive.TrimEnd('/'));
        var report = new List<string>();
        for(int i=0;i<Names.Length;i++)
        {
            string name=Names[i], current=Root+"Ores/"+name+"_ItemIcon.png", archived=Archive+name+"_ItemIcon.png";
            if (!File.Exists(archived))
            {
                string guid=AssetDatabase.AssetPathToGUID(current), hash=Hash(current);
                string error=AssetDatabase.MoveAsset(current,archived);
                if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                if (guid!=AssetDatabase.AssetPathToGUID(archived) || hash!=Hash(archived)) throw new Exception("Archive integrity failed");
                File.Copy(Generated+Sources[i],current);
            }
            AssetDatabase.ImportAsset(current,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(current);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit=100; importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.mipmapEnabled=false;
            importer.filterMode=FilterMode.Bilinear; importer.wrapMode=TextureWrapMode.Clamp; importer.maxTextureSize=2048;
            var settings=new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType=SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            var source=new Texture2D(2,2); source.LoadImage(File.ReadAllBytes(current));
            var pixels=source.GetPixels32(); int w=source.width,h=source.height,minX=w,minY=h,maxX=-1,maxY=-1,transparent=0;
            for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            {
                if(pixels[y*w+x].a<=8) { transparent++; continue; }
                minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
            }
            if(transparent<w*h/10 || maxX<minX) throw new Exception("Missing transparent padding: "+name);
            var bounds=new Rect(Math.Max(0,minX-8),Math.Max(0,minY-8),Math.Min(w,maxX+9)-Math.Max(0,minX-8),Math.Min(h,maxY+9)-Math.Max(0,minY-8));
            UnityEngine.Object.DestroyImmediate(source);
            var factories=new SpriteDataProviderFactories(); factories.Init();
            var provider=factories.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var old=provider.GetSpriteRects().FirstOrDefault();
            var rect=new SpriteRect {name=name+"_ItemIcon",rect=bounds,pivot=new Vector2(.5f,.5f),alignment=SpriteAlignment.Center,spriteID=old?.spriteID??GUID.Generate()};
            provider.SetSpriteRects(new[]{rect});
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(new[]{new SpriteNameFileIdPair(rect.name,rect.spriteID)});
            provider.Apply(); importer.SaveAndReimport();
            var item=AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Ores/"+name+".asset");
            Undo.RecordObject(item,"Replace ore item icon");
            item.icon=AssetDatabase.LoadAllAssetsAtPath(current).OfType<Sprite>().Single(); EditorUtility.SetDirty(item);
            report.Add(name+": original archived with GUID and pixels intact; new icon "+bounds.width+"x"+bounds.height);
        }
        AssetDatabase.SaveAssets(); return report;
    }
}
