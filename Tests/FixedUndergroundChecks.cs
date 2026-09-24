using System;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class FixedUndergroundChecks
{
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>();
        var root=new GameObject("Background checks");root.layer=31;
        var target=new RenderTexture(600,400,24);
        var pixels=new Texture2D(600,400,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;
        Material testMaterial=null;Texture2D red=null,blue=null;
        try
        {
            var bg=root.AddComponent<FixedUndergroundBackground>();bg.material=source.material;bg.map=source.map;bg.nearHills=source.nearHills;
            var camGo=new GameObject("Background test camera");camGo.transform.SetParent(root.transform);
            var camera=camGo.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=5;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.targetTexture=target;
            void Render(float x,float y)
            {
                camera.transform.position=new Vector3(x,y,-10);bg.Refresh(camera);camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,600,400),0,0);pixels.Apply();
            }
            Color At(Vector2 world){var p=camera.WorldToScreenPoint(world);return pixels.GetPixel((int)p.x,(int)p.y);}
            Render(0,-3);
            if(At(new Vector2(0,1.5f)).a>.01f||At(new Vector2(0,.5f)).a<.99f)throw new Exception("Y=1 clipping failed");
            int lipPixels=0;
            for(int x=-3;x<=3;x++)if(At(new Vector2(x,source.topY+.15f)).a>.2f)lipPixels++;
            if(lipPixels==0)throw new Exception("Upper soil lip is missing");
            Render(0,-10);var stationary=At(new Vector2(0,-10));
            File.WriteAllBytes("Assets/Design/Underground-Upper.png",pixels.EncodeToPNG());
            Render(1,-9);var shifted=At(new Vector2(0,-10));
            if(Vector4.Distance(stationary,shifted)>.015f)throw new Exception("Texture moves with camera");
            Render(0,1-(bg.FadeDepths.x+bg.FadeDepths.y)*.5f);
            File.WriteAllBytes("Assets/Design/Underground-Transition.png",pixels.EncodeToPNG());
            Render(0,-240);File.WriteAllBytes("Assets/Design/Underground-Lower.png",pixels.EncodeToPNG());
            var renderer=root.GetComponentInChildren<MeshRenderer>();
            if(renderer.sortingOrder!=source.nearHills.sortingOrder+1||renderer.sortingLayerID!=SortingLayer.NameToID("Background"))throw new Exception("Wrong sorting");
            if(root.GetComponentsInChildren<Collider2D>().Length!=0)throw new Exception("Background collider");
            if(ShaderUtil.ShaderHasError(source.material.shader))throw new Exception("Shader errors");
            testMaterial=new Material(source.material);red=new Texture2D(1,1);blue=new Texture2D(1,1);
            red.SetPixel(0,0,Color.red);red.Apply();blue.SetPixel(0,0,Color.blue);blue.Apply();
            testMaterial.SetTexture("_UpperTex",red);testMaterial.SetTexture("_LowerTex",blue);renderer.sharedMaterial=testMaterial;
            var fade=bg.FadeDepths;
            Render(0,1-fade.x+5);var upper=pixels.GetPixel(300,200);
            Render(0,1-fade.y-5);var lower=pixels.GetPixel(300,200);
            Render(0,1-(fade.x+fade.y)*.5f);var middle=pixels.GetPixel(300,200);
            if(upper.r<.99f||upper.b>.01f||lower.b<.99f||lower.r>.01f||Mathf.Abs(middle.r-middle.b)>.01f||middle.r<.45f)
                throw new Exception("Depth fade failed: "+upper+" / "+middle+" / "+lower);
            return new{passed=true,worldFixed=true,topY=source.topY,fadeStart=fade.x,fadeEnd=fade.y};
        }
        finally
        {
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pixels);if(testMaterial)UnityEngine.Object.DestroyImmediate(testMaterial);
            if(red)UnityEngine.Object.DestroyImmediate(red);if(blue)UnityEngine.Object.DestroyImmediate(blue);
            AssetDatabase.Refresh();
        }
    }
}
