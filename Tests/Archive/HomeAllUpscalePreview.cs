using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System.Collections;
public static class HomeAllUpscalePreview {
 public static object Main() {
 if(!Application.isPlaying||!MainMenuController.IsVisible)throw new System.Exception("MainMenu Play required");
 new GameObject("All Upscale Preview").AddComponent<HomeAllUpscalePreviewCapture>();return "Preview started";
 }
}
public sealed class HomeAllUpscalePreviewCapture:MonoBehaviour {
 IEnumerator Start() {
 var visual=Object.FindFirstObjectByType<HomeCaveVisual>();int previous=visual.Layer;string report="";
 for(int layer=1;layer<=6;layer++) {
 visual.SetLayer(layer);yield return new WaitForSecondsRealtime(1.2f);
 string resource=layer==3?"CaveLake":"Layer"+layer;
 var texture=Resources.Load<Texture2D>("Homescreen/"+resource);
 var image=visual.GetComponentsInChildren<RawImage>().FirstOrDefault(i=>i.color.a==1&&i.texture==texture);
 bool valid=image&&texture.width>=6000&&visual.WaterEffectsEnabled==(layer==3)&&image.material.GetFloat("_CaveEffects")== (layer==3?1:0)&&LoadingAudio.HomeLayer==layer;
 report+=(valid?"PASS":"FAILED")+": L"+layer+" "+texture.width+"x"+texture.height+" effects="+visual.WaterEffectsEnabled+"\n";
 yield return new WaitForEndOfFrame();var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
 File.WriteAllBytes("Design/Tiefenhall/HomeBackgroundSharpness/Layer"+layer+"-upscaled-implemented.png",screenshot.EncodeToPNG());Destroy(screenshot);
 }
 File.WriteAllText("Temp/HomeAllUpscalePreview.txt",report);visual.SetLayer(previous);Destroy(gameObject);
 }
}
