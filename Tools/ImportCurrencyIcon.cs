using System;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class ImportCurrencyIcon
{
    public static object Main()
    {
        const string source = "Assets/Design/Tiefenhall/Coin-v3/Coin.png";
        var texture = new Texture2D(2, 2);
        ImageConversion.LoadImage(texture, File.ReadAllBytes(source));
        int left=texture.width, bottom=texture.height, right=0, top=0;
        var pixels=texture.GetPixels32();
        for(int y=0;y<texture.height;y++) for(int x=0;x<texture.width;x++)
            if(pixels[y*texture.width+x].a>2)
            { left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y); }
        if(left>=right || bottom>=top) throw new Exception("Coin has no visible pixels");
        var bounds=new Rect(left,bottom,right-left+1,top-bottom+1);
        UnityEngine.Object.DestroyImmediate(texture);
        foreach(var path in new[]{"Assets/Resources/GameOverCoin.png","Assets/AB Sprites/Gold Coin.png"})
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            var factory=new SpriteDataProviderFactories(); factory.Init();
            var provider=factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var rects=provider.GetSpriteRects();
            if(rects.Length!=1) throw new Exception("Expected one existing currency sprite: "+path);
            File.Copy(source,path,true);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.mipmapEnabled=false;
            importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;
            importer.filterMode=FilterMode.Bilinear;
            rects[0].rect=bounds; rects[0].pivot=new Vector2(.5f,.5f); rects[0].alignment=SpriteAlignment.Center;
            provider.SetSpriteRects(rects); provider.Apply();
            importer.SaveAndReimport();
        }
        return "Updated both currency textures, preserved sprite identifiers; bounds="+bounds;
    }
}
