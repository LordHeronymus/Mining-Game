using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public static class LoadingTwinklePreview
{
    public static object Main()
    {
        LoadingScreen.Show();
        var screen=Object.FindFirstObjectByType<LoadingScreen>();
        screen.enabled=false;
        screen.transform.Find("Progress").GetComponent<Image>().fillAmount=.66f;
        foreach(var text in screen.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if(text.gameObject.name=="0 %") text.text="66 %";
            if(text.gameObject.name=="Spielszene laden") text.text="Erzadern verteilen";
        }
        new GameObject("Loading twinkle preview").AddComponent<LoadingTwinkleCapture>();
        return "Recording asynchronous crystal glints.";
    }
}
public sealed class LoadingTwinkleCapture : MonoBehaviour
{
    IEnumerator Start()
    {
        var visual=Object.FindFirstObjectByType<LoadingCrystalVisual>();
        Directory.CreateDirectory("Temp/LoadingTwinkles");
        for(int frame=0;frame<80;frame++)
        {
            visual.Animate(frame/10f+.35f);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            var capture=ScreenCapture.CaptureScreenshotAsTexture();
            if(frame==0) File.WriteAllBytes("Assets/Design/LoadingCrystal/Implemented-twinkles.png",capture.EncodeToPNG());
            var target=RenderTexture.GetTemporary(960,540);
            Graphics.Blit(capture,target);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var small=new Texture2D(960,540,TextureFormat.RGB24,false);
            small.ReadPixels(new Rect(0,0,960,540),0,0);small.Apply();
            File.WriteAllBytes($"Temp/LoadingTwinkles/{frame:D3}.png",small.EncodeToPNG());
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            Destroy(small);Destroy(capture);
        }
        var screen=Object.FindFirstObjectByType<LoadingScreen>();
        GameplayInputBlocker.SetBlocked(screen,false);
        LoadingProgress.Dismiss();
        screen.enabled=true;screen.gameObject.SetActive(false);Time.timeScale=1;
        File.WriteAllText("Temp/LoadingTwinkles/complete.txt","80 frames recorded.");
        Destroy(gameObject);
    }
}