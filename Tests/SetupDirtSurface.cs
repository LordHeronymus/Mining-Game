using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
public static class SetupDirtSurface
{
    const string Root="Assets/GameObjects/Map/Blocks";
    public static object Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return "NEEDS_EDIT_MODE";
        Folder(Root+"/Dirt");Folder(Root+"/Sprites/Dirt");
        var registry=AssetDatabase.LoadAssetAtPath<BlockRegistry>(Root+"/BlockRegistry.asset");
        var stone=registry.GetById(BlockType.Stone);
        var dirt=AssetDatabase.LoadAssetAtPath<Block>(Root+"/Dirt/Dirt.asset");
        if(!dirt)
        {
            dirt=ScriptableObject.CreateInstance<Block>();dirt.id=BlockType.Dirt;dirt.displayName="Erde";
            dirt.hardness=.6f;dirt.points=stone.points;dirt.digSound=SoundType.DigSoft;
            dirt.breakSound=SoundType.BreakRock;dirt.spawnWithNoise=false;dirt.MigrateGenerationSettings();
            AssetDatabase.CreateAsset(dirt,Root+"/Dirt/Dirt.asset");
        }
        var tiles=new TileBase[4];
        for(int i=0;i<4;i++)
        {
            string name="Dirt_Wurzeln_"+(i+1).ToString("00");
            string path=Root+"/Sprites/Dirt/"+name+".png";
            if(!File.Exists(path))
            {
                string source="Assets/ZZZ New Assets/"+name+".png";
                string guid=AssetDatabase.AssetPathToGUID(source);
                if(string.IsNullOrEmpty(guid))throw new Exception("Missing dirt image: "+source);
                string error=AssetDatabase.MoveAsset(source,path);if(error!="")throw new Exception(error);
                if(AssetDatabase.AssetPathToGUID(path)!=guid)throw new Exception("Dirt GUID changed");
            }
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(width!=height)throw new Exception("Dirt sprite is not square");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=width/.5f;importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;
            importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.alphaSource=TextureImporterAlphaSource.None;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Center;settings.spritePivot=new Vector2(.5f,.5f);
            settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteGenerateFallbackPhysicsShape=false;
            importer.SetTextureSettings(settings);importer.SaveAndReimport();
            string tilePath=Root+"/Dirt/"+name+".asset";
            var tile=AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if(!tile){tile=ScriptableObject.CreateInstance<Tile>();AssetDatabase.CreateAsset(tile,tilePath);}
            tile.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);tile.colliderType=Tile.ColliderType.Grid;
            tile.color=new Color(1,1,1,.5f);tile.flags=TileFlags.LockColor;tile.transform=Matrix4x4.identity;
            EditorUtility.SetDirty(tile);AssetDatabase.SaveAssetIfDirty(tile);tiles[i]=tile;
        }
        dirt.variants=tiles;EditorUtility.SetDirty(dirt);AssetDatabase.SaveAssetIfDirty(dirt);
        if(Array.IndexOf(registry.blocks,dirt)<0)
        {
            var data=new SerializedObject(registry);var list=data.FindProperty("blocks");int index=list.arraySize++;
            list.GetArrayElementAtIndex(index).objectReferenceValue=dirt;data.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(registry);
        }
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameObjects/Map/DirtTerrainLit.shader");
        if(!shader || ShaderUtil.ShaderHasError(shader))throw new Exception("Dirt shader compilation failed");
        const string matPath="Assets/GameObjects/Map/DirtTerrainLit.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,matPath);}
        material.SetTexture("_StoneTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Sprites/Stein_01_Ruhig.png"));
        material.SetTexture("_DirtTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Sprites/Dirt/Dirt_Wurzeln_01.png"));
        material.SetVector("_DirtSurface",Vector4.zero);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        int converted=0,holes=0;
        foreach(var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var appearance=map.GetComponent<DirtSurfaceAppearance>();
            if(!appearance)appearance=map.gameObject.AddComponent<DirtSurfaceAppearance>();
            appearance.TerrainMaterial=material;EditorUtility.SetDirty(appearance);
            map.EnsureOreOverlay();
            var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,map.oreDensityByDepth);
            var changes=new List<TileChangeData>();
            for(int y=0;y<Math.Min(map.GeneratedHeight,MapGenerationSampler.DirtEndDepth);y++)
                for(int x=0;x<map.GeneratedWidth;x++)
                {
                    var cell=new Vector3Int(x-map.GeneratedWidth/2,-y,0);
                    if(!map.Terrain.HasTile(cell)){holes++;continue;}
                    if(y<MapGenerationSampler.SurfaceStoneRows)map.OreOverlay?.SetTile(cell,null);
                    var chosen=sampler.GetBaseBlock(x,y);
                    var variants=chosen.variants;
                    var tile=variants[OreVeins.Hash(map.ActiveSeed,x,y,0x1234u)%(uint)variants.Length];
                    changes.Add(new TileChangeData(cell,tile,chosen.id==BlockType.Dirt ? new Color(1,1,1,.5f) : Color.white,Matrix4x4.identity));converted++;
                }
            map.Terrain.SetTiles(changes.ToArray(),true);
            map.Terrain.RefreshAllTiles();appearance.Apply();EditorUtility.SetDirty(map.Terrain);
            if(map.OreOverlay)EditorUtility.SetDirty(map.OreOverlay);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if(!EditorSceneManager.SaveScene(map.gameObject.scene))throw new Exception("Could not save dirt surface");
        }
        return new{success=true,variants=4,pureDirtRows=20,transitionRows=8,converted,preservedHoles=holes};
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
}