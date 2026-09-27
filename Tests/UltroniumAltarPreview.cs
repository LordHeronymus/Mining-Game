using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
public static class UltroniumAltarPreview
{
    public static async Task<string> Run(int amount=0)
    {
        if(!Application.isPlaying) throw new Exception("Play mode required");
        Application.runInBackground=true;
        var m=UnityEngine.Object.FindFirstObjectByType<MapGenerator>(); m.CompleteStreamingGeneration();
        bool fly=GameplayTestSettings.GetConfiguredMode(GameplayTestMode.Fly), noClip=GameplayTestSettings.GetConfiguredMode(GameplayTestMode.NoClip);
        GameplayTestSettings.SetMode(GameplayTestMode.Fly,false,out _); GameplayTestSettings.SetMode(GameplayTestMode.NoClip,false,out _);
        var a=m.AltarChamber; var p=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var c=Camera.main;
        foreach(var b in c.GetComponents<MonoBehaviour>()) if(b.GetType().Name.Contains("CameraFollow") || b.GetType().Name.Contains("Clamp")) b.enabled=false;
        p.transform.position=a.AltarPosition+new Vector3(-a.CellSize*2.8f,a.CellSize*.9f,0);
        p.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
        // Allow the streamed collision chunks to catch up to the preview teleport.
        await Task.Delay(350);
        p.transform.position=a.AltarPosition+new Vector3(-a.CellSize*2.8f,a.CellSize*.9f,0);
        p.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
        c.transform.position=a.AltarPosition+new Vector3(0,a.CellSize*3.5f,-10); c.orthographicSize=a.CellSize*7.5f;
        var debug=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>(FindObjectsInactive.Include); if(debug)debug.gameObject.SetActive(false);
        var state=a.CaptureState(); state.deposited=amount; state.victoryShown=false; a.RestoreState(state);
        await Task.Delay(1500);
        Directory.CreateDirectory("Assets/Design/UltroniumAltar/Implemented");
        var rt=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
        var old=c.targetTexture; var active=RenderTexture.active;
        try
        {
            c.targetTexture=rt;c.Render();RenderTexture.active=rt;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
            File.WriteAllBytes("Assets/Design/UltroniumAltar/Implemented/Charge-"+amount+".png",image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }
        finally {c.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);
            GameplayTestSettings.SetMode(GameplayTestMode.Fly,fly,out _); GameplayTestSettings.SetMode(GameplayTestMode.NoClip,noClip,out _);}
        return "Captured charge "+amount+"; player="+p.transform.position+"; interactive="+a.CanInteract;
    }
}
