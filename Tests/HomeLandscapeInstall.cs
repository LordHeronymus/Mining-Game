using UnityEditor;
using UnityEngine;
using System.IO;
public static class HomeLandscapeInstall {
 public static object Main() {
  const string path="Assets/Resources/Homescreen/Layer1.png";
  File.Copy("Design/Tiefenhall/SurfaceBackgroundPrototypes/Stylized-v2/V1-WarmSky-EmptyTunnel.png",path,true);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);
  importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
  importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;importer.wrapMode=TextureWrapMode.Clamp;
  importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
  return "Layer1 imported; original CaveLake retained";
 }
}

