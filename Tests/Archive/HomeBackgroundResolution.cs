using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
public static class HomeBackgroundResolution {
 public static object Main() {
  var results=new List<object>();
  foreach(var path in new[]{"Assets/Resources/Homescreen/Layer1.png","Assets/Resources/Homescreen/CaveLake.png"}) {
   var importer=(TextureImporter)AssetImporter.GetAtPath(path);
   importer.maxTextureSize=8192;
   importer.textureCompression=TextureImporterCompression.Uncompressed;
   importer.crunchedCompression=false;
   importer.mipmapEnabled=false;
   importer.npotScale=TextureImporterNPOTScale.None;
   var defaults=importer.GetDefaultPlatformTextureSettings();
   defaults.maxTextureSize=8192;defaults.textureCompression=TextureImporterCompression.Uncompressed;
   defaults.crunchedCompression=false;importer.SetPlatformTextureSettings(defaults);
   foreach(var platform in new[]{"Standalone","WebGL","Android","iPhone"}) {
    var settings=importer.GetPlatformTextureSettings(platform);
    settings.name=platform;settings.maxTextureSize=8192;
    settings.textureCompression=TextureImporterCompression.Uncompressed;
    settings.crunchedCompression=false;
    if(settings.overridden)settings.format=TextureImporterFormat.RGBA32;
    importer.SetPlatformTextureSettings(settings);
   }
   importer.SaveAndReimport();
   var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   importer.GetSourceTextureWidthAndHeight(out var width,out var height);
   if(texture.width!=width||texture.height!=height)throw new System.Exception("Unexpected downscaling: "+path);
   if(importer.maxTextureSize!=8192||importer.textureCompression!=TextureImporterCompression.Uncompressed)
    throw new System.Exception("Import settings were not retained: "+path);
   results.Add(new {path,sourceWidth=width,sourceHeight=height,importedWidth=texture.width,importedHeight=texture.height,maxSize=importer.maxTextureSize,format=texture.format.ToString(),compression=importer.textureCompression.ToString()});
  }
  var report=Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented);
  File.WriteAllText("Temp/HomeBackgroundResolution.txt",report);
  return results;
 }
}
