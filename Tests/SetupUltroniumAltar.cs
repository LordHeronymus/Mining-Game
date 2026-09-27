using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupUltroniumAltar
{
    public static string Run()
    {
        if(EditorApplication.isPlaying) throw new Exception("Edit mode required");
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if(!map) throw new Exception("No map");
        Directory.CreateDirectory("Assets/_SceneBackups");
        string backup="Assets/_SceneBackups/BeforeUltroniumAltar_20260927.unity";
        if(!File.Exists(backup)) EditorSceneManager.SaveScene(map.gameObject.scene,backup,true);
        foreach(string name in new[]{"Altar","ChamberWall","SanctuaryStone"})
        {
            string path="Assets/Resources/UltroniumAltar/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default; importer.alphaIsTransparency=name=="Altar";
            importer.sRGBTexture=true; importer.mipmapEnabled=false; importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.wrapMode=name=="SanctuaryStone"?TextureWrapMode.Mirror:TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Bilinear; importer.SaveAndReimport();
        }
        var altar=map.AltarChamber;
        if(!altar) altar=map.gameObject.AddComponent<UltroniumAltarChamber>();
        if(!altar.Layout.valid)
        {
            altar.PrepareGeneration(map.ActiveSeed,map.GeneratedWidth,map.GeneratedHeight);
            if(!altar.Layout.valid) throw new Exception("Map cannot fit altar chamber");
            var sampler=new MapGenerationSampler(map.registry,map.ActiveSeed,map.GeneratedHeight,map.layers,map.oreDensityCurve,
                map.oreDensityMultiplierPercent,map.transitionThickness,map.useOreSettings?map.oreSettings:null);
            foreach(var cell in altar.Layout.Bounds.allPositionsWithin)
            {
                if(!altar.Layout.IsReserved(cell)) continue;
                map.EnsureOreOverlay().SetTile(cell,null); map.EnsureArtifactOverlay().SetTile(cell,null);
                if(altar.Layout.IsOpen(cell)) map.Terrain.SetTile(cell,null);
                else
                {
                    var block=sampler.GetBaseBlock(cell.x+map.GeneratedWidth/2,-cell.y);
                    map.Terrain.SetTile(cell,block.variants[0]);
                }
            }
        }
        map.GetComponent<DirtSurfaceAppearance>()?.Apply();
        altar.RebuildVisuals();
        var camera=Camera.main;
        if(camera && camera.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var data)) data.renderPostProcessing=true;
        EditorUtility.SetDirty(altar); EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(map.gameObject.scene);
        return "Installed altar at "+altar.Layout.origin;
    }
}
