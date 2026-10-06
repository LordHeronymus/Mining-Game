using UnityEditor;
using UnityEngine;
using System.IO;
public static class HomeUpscaleInstall {
 public static object Main() {
 const string path="Assets/Resources/Homescreen/Layer1.png";
 File.Copy("Design/Tiefenhall/HomeBackgroundSharpness/Layer1-upscaled-x4.png",path,true);
 AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
 var importer=(TextureImporter)AssetImporter.GetAtPath(path);
 importer.maxTextureSize=8192;importer.textureCompression=TextureImporterCompression.Uncompressed;
 importer.crunchedCompression=false;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
 var settings=importer.GetDefaultPlatformTextureSettings();settings.maxTextureSize=8192;settings.textureCompression=TextureImporterCompression.Uncompressed;settings.crunchedCompression=false;importer.SetPlatformTextureSettings(settings);
 importer.SaveAndReimport();
 var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 if(t.width!=6688||t.height!=3764)throw new System.Exception("Upscaled texture was reduced: "+t.width+"x"+t.height);
 var result=new {t.width,t.height,format=t.format.ToString(),importer.maxTextureSize,compression=importer.textureCompression.ToString()};
 File.WriteAllText("Temp/HomeUpscaleImport.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
 return result;
 }
}
