using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class FixedUndergroundLightingChecks
{
    public static object Main()
    {
        var wall = UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>();
        var camera = Camera.main;
        var sun = UnityEngine.Object.FindFirstObjectByType<MoonlightController>().daylight;
        if (!wall || !camera || !sun) throw new Exception("Scene lighting references are missing");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameObjects/Background/Underground/FixedUnderground.shader");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Underground shader failed to compile");
        var cameraPosition = camera.transform.position;
        var cameraTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var rt = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(256,256,TextureFormat.RGBA32,false);
        float oldIntensity = sun.intensity;
        var headlamp = UnityEngine.Object.FindFirstObjectByType<MinerPlayerVisual>().headlamp;
        float oldHeadlamp = headlamp.intensity;
        int oldMask = camera.cullingMask;
        try
        {
            camera.transform.position = new Vector3(cameraPosition.x,-12,cameraPosition.z);
            camera.cullingMask = 1 << wall.gameObject.layer;
            camera.targetTexture = rt;
            wall.Refresh(camera);
            sun.intensity = 1;
            wall.Refresh(camera);
            float lit = Render("Temp/underground-lit.png");
            sun.intensity = 0;
            wall.Refresh(camera);
            float dark = Render("Temp/underground-unlit.png");
            if (lit < dark * 2 || dark > 0.015f)
                throw new Exception($"Background did not follow light: lit={lit}, dark={dark}");
            camera.transform.position = new Vector3(cameraPosition.x,headlamp.transform.position.y,cameraPosition.z);
            sun.intensity = 0;
            headlamp.intensity = 0;
            var mapLighting = wall.map.GetComponent<MapLighting>();
            mapLighting.SendMessage("UpdateHeadlamp");
            wall.Refresh(camera);
            Render("Temp/underground-lamp-off.png");
            float lampOff = LampEnergy();
            headlamp.intensity = oldHeadlamp;
            mapLighting.SendMessage("UpdateHeadlamp");
            wall.Refresh(camera);
            Render("Temp/underground-lamp-on.png");
            float lampOn = LampEnergy();
            if (lampOn <= lampOff * 1.5f)
                throw new Exception($"Headlamp did not brighten the background: off={lampOff}, on={lampOn}");
            return new {lit, dark, lampOff, lampOn, shaderValid = true};
        }
        finally
        {
            sun.intensity = oldIntensity;
            headlamp.intensity = oldHeadlamp;
            camera.targetTexture = cameraTarget;
            camera.cullingMask = oldMask;
            camera.transform.position = cameraPosition;
            RenderTexture.active = oldActive;
            wall.Refresh(camera);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(pixels);
        }

        float Render(string path)
        {
            camera.Render();
            RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0,0,256,256),0,0);
            pixels.Apply();
            System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());
            double sum=0;
            foreach (var p in pixels.GetPixels32()) sum += (p.r+p.g+p.b)/765.0;
            return (float)(sum / (256*256));
        }

        float LampEnergy()
        {
            double sum = 0;
            for (int y=85;y<150;y++)
                for (int x=75;x<175;x++)
                {
                    var p=pixels.GetPixel(x,y);
                    sum+=(p.r+p.g+p.b)/3.0;
                }
            return (float)(sum/(65*100));
        }
    }
}
