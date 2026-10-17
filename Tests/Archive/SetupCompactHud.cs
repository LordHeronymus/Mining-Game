using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupCompactHud
{
    const string Folder="Assets/UI/HUD";
    public static string Main()
    {
        if(Application.isPlaying) throw new Exception("Use Edit Mode");
        if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/UI","HUD");
        MakeSymbol("Heart",0); MakeSymbol("Energy",1); MakeSymbol("Bar",2);
        string stripPath=Folder+"/StatusStrip.asset";
        var strip=AssetDatabase.LoadAssetAtPath<Sprite>(stripPath);
        if(!strip)
        {
            string sourcePath=Folder+"/StatusStrip.png"; AssetDatabase.ImportAsset(sourcePath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(sourcePath);
            importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.SaveAndReimport();
            var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes(sourcePath));
            var pixels=source.GetPixels32();int w=source.width,h=source.height,x0=w,y0=h,x1=0,y1=0;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[y*w+x].a>16){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
            UnityEngine.Object.DestroyImmediate(source);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            strip=Sprite.Create(texture,new Rect(x0,y0,x1-x0+1,y1-y0+1),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
            strip.name="Status Frame"; AssetDatabase.CreateAsset(strip,stripPath);
        }
        var info=UnityEngine.Object.FindFirstObjectByType<InfoPanel>(FindObjectsInactive.Include);
        Undo.RegisterFullObjectHierarchyUndo(info.gameObject,"Compact HUD");
        foreach(string name in new[]{"Points","Energy"})
        {
            var child=info.transform.Find(name); if(!child)continue;
            // Retain legacy text/slider targets used by managers; hide their rendered UI.
            var group=child.GetComponent<CanvasGroup>(); if(!group)group=child.gameObject.AddComponent<CanvasGroup>();
            group.alpha=0; group.interactable=group.blocksRaycasts=false;
        }
        var existing=info.transform.Find("Compact HUD");
        var root=existing?existing.gameObject:new GameObject("Compact HUD",typeof(RectTransform));
        root.transform.SetParent(info.transform,false); root.layer=LayerMask.NameToLayer("UI");
        var rect=(RectTransform)root.transform; rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
        rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;
        var hud=root.GetComponent<CompactHud>();if(!hud)hud=root.AddComponent<CompactHud>();
        var inventory=UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        hud.font=inventory.font;hud.fontMaterial=inventory.fontMaterial;
        hud.stripSprite=strip;hud.slotSprite=inventory.slotSprite;hud.selectedSprite=inventory.selectionSprite;hud.badgeSprite=inventory.badgeSprite;
        hud.heartSprite=AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/Heart.png");
        hud.boltSprite=AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/Energy.png");
        hud.barSprite=AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/Bar.png");
        hud.coinSprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AB Sprites/Gold Coin.png");
        string pickaxePath=Folder+"/Pickaxe.png";
        AssetDatabase.ImportAsset(pickaxePath);
        var pickaxeImporter=(TextureImporter)AssetImporter.GetAtPath(pickaxePath);
        pickaxeImporter.textureType=TextureImporterType.Sprite;
        pickaxeImporter.spriteImportMode=SpriteImportMode.Single;
        pickaxeImporter.alphaIsTransparency=true;
        pickaxeImporter.mipmapEnabled=false;
        pickaxeImporter.textureCompression=TextureImporterCompression.Uncompressed;
        pickaxeImporter.SaveAndReimport();
        hud.pickaxeSprite=AssetDatabase.LoadAssetAtPath<Sprite>(pickaxePath);
        hud.energy=UnityEngine.Object.FindFirstObjectByType<EnergyManager>();
        hud.player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        hud.map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        hud.slots=new ItemSO[8];
        string[] names={"Tools/Torche","Tools/Dynamite","Tools/Ladder","Tools/BridgePart","Materials/Rope","Materials/Wood"};
        for(int i=0;i<names.Length;i++)hud.slots[i]=AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/"+names[i]+".asset");
        EditorUtility.SetDirty(hud);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(info.gameObject.scene);EditorSceneManager.SaveScene(info.gameObject.scene);
        return "Compact HUD installed with a separate pickaxe slot 1 and item slots 2–9.";
    }
    static void MakeSymbol(string name,int kind)
    {
        const int n=128;var texture=new Texture2D(n,n,TextureFormat.RGBA32,false);var pixels=new Color[n*n];
        Vector2[] bolt={new Vector2(.58f,.98f),new Vector2(.15f,.44f),new Vector2(.44f,.44f),new Vector2(.3f,.02f),new Vector2(.87f,.62f),new Vector2(.58f,.62f)};
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            float u=(x+.5f)/n,v=(y+.5f)/n;bool inside;
            if(kind==0)
            {
                float a=(u-.5f)*2.5f,b=(v-.48f)*2.5f;
                float q=a*a+b*b-1;inside=q*q*q-a*a*b*b*b<=0;
            }
            else if(kind==1)
            {
                inside=false;
                for(int i=0,j=bolt.Length-1;i<bolt.Length;j=i++)
                    if((bolt[i].y>v)!=(bolt[j].y>v)&&u<(bolt[j].x-bolt[i].x)*(v-bolt[i].y)/(bolt[j].y-bolt[i].y)+bolt[i].x)inside=!inside;
            }
            else inside=true;
            Color c=kind==0?Color.Lerp(new Color(.62f,.015f,.035f),new Color(1,.2f,.22f),v):kind==1?Color.Lerp(new Color(.95f,.43f,.015f),new Color(1,.9f,.3f),v):Color.Lerp(new Color(.68f,.68f,.68f),Color.white,v);
            if(kind==0&&v>.62f)c=Color.Lerp(c,Color.white,.25f*Mathf.Clamp01(1-Mathf.Abs(u-.34f)*8));
            c.a=inside?1:0;pixels[y*n+x]=c;
        }
        texture.SetPixels(pixels);texture.Apply();string path=Folder+"/"+name+".png";
        File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
}
