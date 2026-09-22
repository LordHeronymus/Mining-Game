using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupLushMeadow
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        const string folder="Assets/GameObjects/Map/Blocks/Sprites/Grass/LushMeadow";
        const string path=folder+"/LushMeadow.png";
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        const string backup="Assets/_SceneBackups/BeforeLushMeadow_20260922.unity";
        if(!System.IO.File.Exists(backup))EditorSceneManager.SaveScene(map.gameObject.scene,backup,true);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Default;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=4096;
        importer.isReadable=true;
        importer.alphaIsTransparency=true;
        importer.mipmapEnabled=true;
        importer.filterMode=FilterMode.Trilinear;
        importer.wrapMode=TextureWrapMode.Clamp;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        int transparent=0,opaque=0;
        foreach(var pixel in texture.GetPixels32()){if(pixel.a==0)transparent++;if(pixel.a>240)opaque++;}
        if(transparent<texture.width*texture.height/4 || opaque<1000)throw new Exception("Grass alpha invalid");
        var tiles=new TileBase[6];
        float width=texture.width/6f;
        for(int i=0;i<6;i++)
        {
            string spritePath=folder+"/MeadowSprite_"+i+".asset";
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if(!sprite)
            {
                sprite=Sprite.Create(texture,new Rect(i*width,0,width,texture.height),new Vector2(.5f,.38f),width/.5f,0,SpriteMeshType.FullRect);
                sprite.name="MeadowSprite_"+i;
                AssetDatabase.CreateAsset(sprite,spritePath);
            }
            string tilePath=folder+"/MeadowTile_"+i+".asset";
            var tile=AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if(!tile){tile=ScriptableObject.CreateInstance<Tile>();AssetDatabase.CreateAsset(tile,tilePath);}
            tile.sprite=sprite;tile.colliderType=Tile.ColliderType.None;
            tile.transform=Matrix4x4.TRS(new Vector3(0,.25f,0),Quaternion.identity,new Vector3(1,.55f,1));
            EditorUtility.SetDirty(tile);tiles[i]=tile;
        }
        map.SetGrassVariants(tiles,true);
        map.grassYOffset=0;
        map.SyncGrassFromTerrain();
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(map.GrassOverlay);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return "Six continuous grass segments installed; transparent pixels="+transparent;
    }
}
