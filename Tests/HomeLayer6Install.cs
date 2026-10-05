using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System.Collections;
public static class HomeLayer6Install {
 public static object Main() {
  const string path="Assets/Resources/Homescreen/Layer6.png";
  File.Copy("Design/Tiefenhall/Layer6Prototypes/L6-V1-LavaChasm.png",path,true);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);
  importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
  importer.crunchedCompression=false;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;
  importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
  var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  return new {width=texture.width,height=texture.height,max=importer.maxTextureSize};
 }
 public static object Preview() {
  if(!Application.isPlaying||!MainMenuController.IsVisible)throw new System.Exception("MainMenu Play required");
  new GameObject("L6 Preview").AddComponent<HomeLayer6Preview>();return "Preview started";
 }
}
public sealed class HomeLayer6Preview:MonoBehaviour {
 IEnumerator Start() {
  var visual=Object.FindFirstObjectByType<HomeCaveVisual>();int previous=visual.Layer;
  visual.SetLayer(6);yield return new WaitForSecondsRealtime(1.2f);
  var active=visual.GetComponentsInChildren<RawImage>().FirstOrDefault(i=>i.texture==Resources.Load<Texture2D>("Homescreen/Layer6")&&i.color.a==1);
  bool valid=active&&!visual.WaterEffectsEnabled&&active.material.GetFloat("_CaveEffects")==0&&LoadingAudio.HomeLayer==6;
  File.WriteAllText("Temp/HomeLayer6Preview.txt",valid?"PASS: approved L6 texture visible; L3 effects disabled; audio layer 6\n":"FAILED L6 preview\n");
  yield return new WaitForEndOfFrame();
  var screenshot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Design/Tiefenhall/Layer6Prototypes/L6-implemented.png",screenshot.EncodeToPNG());Destroy(screenshot);
  visual.SetLayer(previous);Destroy(gameObject);
 }
}


