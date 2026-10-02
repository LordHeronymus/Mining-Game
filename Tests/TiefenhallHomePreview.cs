using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class TiefenhallHomePreview
{
    public static object Main()
    {
        new GameObject("Home Animation Recorder").AddComponent<TiefenhallHomeRecorder>();
        return "Recording the cave lake, droplet rings, crystal light and parallax into Temp/TiefenhallHomeFrames.";
    }
}
public sealed class TiefenhallHomeRecorder : MonoBehaviour
{
    IEnumerator Start()
    {
        var visual = FindFirstObjectByType<HomeCaveVisual>(); if (!visual) { Destroy(gameObject); yield break; }
        visual.enabled = false;
        var material = visual.GetComponentInChildren<RawImage>().material;
        var captureType = System.AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x => x != null);
        var encode = captureType.GetMethod("EncodeScreenToPng", BindingFlags.Static | BindingFlags.NonPublic);
        Directory.CreateDirectory("Temp/TiefenhallHomeFrames");
        for (int frame = 0; frame < 72; frame++)
        {
            float time = 5.5f + frame / 24f;
            material.SetFloat("_SceneTime", time);
            material.SetVector("_Parallax", new Vector4(Mathf.Sin(frame / 71f * Mathf.PI * 2) * .003f, 0, 0, 0));
            yield return new WaitForEndOfFrame();
            File.WriteAllBytes($"Temp/TiefenhallHomeFrames/{frame:D3}.png", (byte[])encode.Invoke(null, new object[] { 1280, 720 }));
        }
        visual.enabled = true;
        File.WriteAllText("Temp/TiefenhallHomePreview.txt", "72 frames, 3 seconds at 24 fps. Runtime shader and screen-space UI captured from Unity.");
        Destroy(gameObject);
    }
}
