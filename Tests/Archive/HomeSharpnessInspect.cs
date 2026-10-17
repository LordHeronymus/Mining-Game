using UnityEditor;
using UnityEngine;
public static class HomeSharpnessInspect {
 public static object Main() {
 var path="Assets/Resources/Homescreen/Layer1.png";
 var importer=(TextureImporter)AssetImporter.GetAtPath(path);
 importer.GetSourceTextureWidthAndHeight(out int w,out int h);
 var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 var visual=Object.FindFirstObjectByType<HomeCaveVisual>();
 return new { source=new {w,h}, imported=new {texture.width,texture.height,format=texture.format.ToString(),mips=texture.mipmapCount}, importer.maxTextureSize,compression=importer.textureCompression.ToString(),defaults=importer.GetDefaultPlatformTextureSettings(),standalone=importer.GetPlatformTextureSettings("Standalone"),screen=new {Screen.width,Screen.height},play=Application.isPlaying,layer=visual?visual.Layer:0};
 }
}
