using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System.Collections;
public static class HomeUpscalePreview {
 public static object Main() {
 if(!Application.isPlaying||!MainMenuController.IsVisible)throw new System.Exception("MainMenu Play required");
 new GameObject("Upscale Preview").AddComponent<HomeUpscalePreviewCapture>();return "Preview started";
 }
}
public sealed class HomeUpscalePreviewCapture:MonoBehaviour {
 IEnumerator Start() {
 var visual=Object.FindFirstObjectByType<HomeCaveVisual>();int previous=visual.Layer;
 visual.SetLayer(1);yield return new WaitForSecondsRealtime(1.2f);
 var image=visual.GetComponentsInChildren<RawImage>().FirstOrDefault(i=>i.color.a==1&&i.texture==Resources.Load<Texture2D>("Homescreen/Layer1"));
 bool valid=image&&image.texture.width==6688&&image.texture.height==3764&&!visual.WaterEffectsEnabled;
 File.WriteAllText("Temp/HomeUpscalePreview.txt",valid?"PASS: L1 visible at 6688x3764; L3 effects disabled; viewport "+Screen.width+"x"+Screen.height:"FAILED: resolution/display");
 yield return new WaitForEndOfFrame();var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
 File.WriteAllBytes("Design/Tiefenhall/HomeBackgroundSharpness/Layer1-upscaled-implemented.png",screenshot.EncodeToPNG());Destroy(screenshot);
 visual.SetLayer(previous);Destroy(gameObject);
 }
}
