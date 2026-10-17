using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System.Collections;
public static class HomeLayer4Install {
 public static object Main() {
  const string path="Assets/Resources/Homescreen/Layer4.png";
  File.Copy("Design/Tiefenhall/Layer4Prototypes/L4-V2-NaturalRock.png",path,true);
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
  new GameObject("L4 Preview").AddComponent<HomeLayer4Preview>();return "Preview started";
 }
}
public sealed class HomeLayer4Preview:MonoBehaviour {
 IEnumerator Start() {
  var visual=Object.FindFirstObjectByType<HomeCaveVisual>();int previous=visual.Layer;
  visual.SetLayer(4);yield return new WaitForSecondsRealtime(1.2f);
  var active=visual.GetComponentsInChildren<RawImage>().FirstOrDefault(i=>i.texture==Resources.Load<Texture2D>("Homescreen/Layer4")&&i.color.a==1);
  bool valid=active&&!visual.WaterEffectsEnabled&&active.material.GetFloat("_CaveEffects")==0&&LoadingAudio.HomeLayer==4;
  File.WriteAllText("Temp/HomeLayer4Preview.txt",valid?"PASS: approved L4 texture visible; L3 effects disabled; audio layer 4\n":"FAILED L4 preview\n");
  yield return new WaitForEndOfFrame();
  var screenshot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Design/Tiefenhall/Layer4Prototypes/L4-implemented.png",screenshot.EncodeToPNG());Destroy(screenshot);
  visual.SetLayer(previous);Destroy(gameObject);
 }
}
