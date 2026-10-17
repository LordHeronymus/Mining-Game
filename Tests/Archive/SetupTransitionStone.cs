using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupTransitionStone
{
    const string Root = "Assets/GameObjects/Map/Blocks";
    public static object Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return "NEEDS_EDIT_MODE";
        Folder(Root+"/TransitionStone");Folder(Root+"/Sprites/TransitionStone");
        var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>(Root+"/BlockRegistry.asset");
        var upper=registry.GetById(BlockType.Stone);
        var lower=registry.GetById(BlockType.StoneLayer2);
        if(!upper || !lower)throw new Exception("First and second layer stones are required");
        var tiles=new TileBase[4];
        for(int i=0;i<tiles.Length;i++)
        {
            string name="Erde_Fels_"+(i+1).ToString("00");
            string source="Assets/ZZZ New Assets/"+name+".png";
            string path=Root+"/Sprites/TransitionStone/"+name+".png";
            if(!AssetDatabase.LoadAssetAtPath<Texture2D>(path))
            {
                string guid=AssetDatabase.AssetPathToGUID(source);
                if(string.IsNullOrEmpty(guid))throw new Exception("Missing sprite: "+source);
                string error=AssetDatabase.MoveAsset(source,path);
                if(error!="")throw new Exception(error);
                if(AssetDatabase.AssetPathToGUID(path)!=guid)throw new Exception("Sprite GUID changed: "+name);
            }
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(width!=height)throw new Exception("Transition sprite is not square: "+name);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=width/.5f;importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;
            importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.alphaSource=TextureImporterAlphaSource.None;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Center;settings.spritePivot=new Vector2(.5f,.5f);
            settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteGenerateFallbackPhysicsShape=false;
            importer.SetTextureSettings(settings);importer.SaveAndReimport();
            string tilePath=Root+"/TransitionStone/"+name+".asset";
            var tile=AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if(!tile){tile=ScriptableObject.CreateInstance<Tile>();AssetDatabase.CreateAsset(tile,tilePath);}
            tile.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);tile.colliderType=Tile.ColliderType.Grid;
            tile.color=Color.white;tile.flags=TileFlags.LockColor;tile.transform=Matrix4x4.identity;
            EditorUtility.SetDirty(tile);AssetDatabase.SaveAssetIfDirty(tile);tiles[i]=tile;
        }
        upper.variants=tiles;upper.displayName="Übergangsgestein";
        lower.displayName="Stein";
        EditorUtility.SetDirty(upper);EditorUtility.SetDirty(lower);
        AssetDatabase.SaveAssetIfDirty(upper);AssetDatabase.SaveAssetIfDirty(lower);
        typeof(BlockRegistry).GetMethod("BuildIndex",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(registry,null);
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/DirtTerrainLit.mat");
        if(!material || ShaderUtil.ShaderHasError(material.shader))throw new Exception("Terrain shader invalid");
        material.SetTexture("_StoneTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Sprites/TransitionStone/Erde_Fels_01.png"));
        material.SetTexture("_DeepStoneTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Sprites/Stein_01_Ruhig.png"));
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        int converted=0,holes=0,maps=0;
        foreach(var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(!map.registry || !map.Terrain.layoutGrid)continue;
            var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness);
            int boundary=sampler.FirstStoneBoundary;
            int end=Mathf.Min(map.GeneratedHeight,boundary>0?boundary+sampler.TransitionThickness:map.GeneratedHeight);
            var changes=new List<TileChangeData>();
            for(int y=0;y<end;y++)for(int x=0;x<map.GeneratedWidth;x++)
            {
                var cell=new Vector3Int(x-map.GeneratedWidth/2,-y,0);
                if(!map.Terrain.HasTile(cell)){holes++;continue;}
                var block=sampler.GetBaseBlock(x,y);
                if(block!=upper && block!=lower)continue;
                var variants=block.variants;
                var tile=variants[OreVeins.Hash(map.ActiveSeed,x,y,0x1234u)%(uint)variants.Length];
                if(map.Terrain.GetTile(cell)==tile)continue;
                changes.Add(new TileChangeData(cell,tile,Color.white,Matrix4x4.identity));
            }
            map.Terrain.SetTiles(changes.ToArray(),true);
            map.Terrain.RefreshAllTiles();
            var appearance=map.GetComponent<DirtSurfaceAppearance>();
            if(appearance){appearance.TerrainMaterial=material;appearance.Apply();EditorUtility.SetDirty(appearance);}
            EditorUtility.SetDirty(map.Terrain);
            if(map.OreOverlay)EditorUtility.SetDirty(map.OreOverlay);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if(!EditorSceneManager.SaveScene(map.gameObject.scene))throw new Exception("Could not save map scene");
            converted+=changes.Count;maps++;
        }
        return new{success=true,variants=tiles.Length,converted,preservedHoles=holes,maps};
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));
        AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
}
