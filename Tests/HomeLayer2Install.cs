using UnityEditor;
using UnityEngine;
using System.IO;
public static class HomeLayer2Install {
 public static object Main() {
  const string path="Assets/Resources/Homescreen/Layer2.png";
  File.Copy("Design/Tiefenhall/Layer2Prototypes/L2-V2-NaturalRock-A.png",path,true);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);
  importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
  importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;
  importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
  var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  return new {width=texture.width,height=texture.height,max=importer.maxTextureSize};
 }
}
