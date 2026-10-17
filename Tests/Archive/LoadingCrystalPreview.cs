using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class LoadingCrystalPreview
{
    public static object Main()
    {
        LoadingScreen.Show();
        var screen = Object.FindFirstObjectByType<LoadingScreen>();
        screen.enabled = false;
        screen.transform.Find("Progress").GetComponent<Image>().fillAmount = .66f;
        foreach (var text in screen.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (text.gameObject.name == "0 %") text.text = "66 %";
            if (text.gameObject.name == "Spielszene laden") text.text = "Erzadern verteilen";
        }
        new GameObject("Loading animation preview recorder").AddComponent<LoadingCrystalRecorder>();
        return "Recording a complete 1.5 second animation cycle into Temp/LoadingCrystalFrames.";
    }
}

public sealed class LoadingCrystalRecorder : MonoBehaviour
{
    IEnumerator Start()
    {
        var visual = Object.FindFirstObjectByType<LoadingCrystalVisual>();
        Directory.CreateDirectory("Temp/LoadingCrystalFrames");
        for (int frame = 0; frame < 45; frame++)
        {
            visual.Animate(frame / 30f);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            var capture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes($"Temp/LoadingCrystalFrames/{frame:D3}.png", capture.EncodeToPNG());
            Destroy(capture);
        }
        File.WriteAllText("Temp/LoadingCrystalPreview.txt", "45 frames captured, 1.5-second cycle at 30 fps.");
        Destroy(gameObject);
    }
}
