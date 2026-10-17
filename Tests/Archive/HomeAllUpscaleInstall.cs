using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
public static class HomeAllUpscaleInstall {
 public static object Main() {
 var results=new List<object>();
 foreach(string name in new[]{"Layer2","CaveLake","Layer4","Layer5","Layer6"}) {
 string path="Assets/Resources/Homescreen/"+name+".png";
 File.Copy("Design/Tiefenhall/HomeBackgroundSharpness/"+name+"-upscaled-x4.png",path,true);
 AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
 var importer=(TextureImporter)AssetImporter.GetAtPath(path);
 importer.maxTextureSize=8192;importer.textureCompression=TextureImporterCompression.Uncompressed;
 importer.crunchedCompression=false;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
 var settings=importer.GetDefaultPlatformTextureSettings();settings.maxTextureSize=8192;settings.textureCompression=TextureImporterCompression.Uncompressed;settings.crunchedCompression=false;importer.SetPlatformTextureSettings(settings);
 foreach(string platform in new[]{"Standalone","WebGL","Android","iPhone"}) {
 var s=importer.GetPlatformTextureSettings(platform);s.name=platform;s.maxTextureSize=8192;s.textureCompression=TextureImporterCompression.Uncompressed;s.crunchedCompression=false;if(s.overridden)s.format=TextureImporterFormat.RGB24;importer.SetPlatformTextureSettings(s);
 }
 importer.SaveAndReimport();var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 importer.GetSourceTextureWidthAndHeight(out int width,out int height);
 if(t.width!=width||t.height!=height||width<6000)throw new System.Exception("Upscaled texture reduced: "+name);
 results.Add(new{name,width,height,format=t.format.ToString(),max=importer.maxTextureSize});
 }
 File.WriteAllText("Temp/HomeAllUpscaleImport.json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));return results;
 }
}
