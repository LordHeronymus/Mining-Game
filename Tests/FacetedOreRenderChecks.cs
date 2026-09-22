using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class FacetedOreRenderChecks
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var previous=SceneManager.GetActiveScene();
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var enabled=new bool[lights.Length];
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var target=new RenderTexture(1200,600,24,RenderTextureFormat.ARGB32);
        var oldTarget=RenderTexture.active;
        var pixels=new Texture2D(1200,600,TextureFormat.RGBA32,false);
        var oreMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/OreOverlayLit.mat");
        var stoneMat=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        try
        {
            for(int i=0;i<lights.Length;i++){enabled[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            var stone=map.uniformTestTile.sprite;
            string[] names={"Gold","Copper","Iron","Silver","Platinum","Coal"};
            for(int col=0;col<6;col++)for(int row=0;row<3;row++)
            {
                var block=AssetDatabase.LoadAssetAtPath<Block>("Assets/GameObjects/Map/Blocks/"+names[col]+".asset");
                var ore=row==0?block.smallOre[0]:row==1?block.mediumOre[0]:block.richOre[0];
                var bg=new GameObject("Stone",typeof(SpriteRenderer));bg.layer=31;
                bg.transform.position=new Vector3(col,2-row,0);bg.transform.localScale=Vector3.one*2;
                var br=bg.GetComponent<SpriteRenderer>();br.sprite=stone;br.sharedMaterial=stoneMat;
                var go=new GameObject(names[col],typeof(SpriteRenderer));go.layer=31;
                go.transform.position=bg.transform.position;go.transform.localScale=Vector3.one*2;
                var renderer=go.GetComponent<SpriteRenderer>();renderer.sprite=ore.sprite;renderer.sharedMaterial=oreMat;renderer.sortingOrder=1;
                var props=new MaterialPropertyBlock();props.SetFloat("_OreScale",map.oreScale);renderer.SetPropertyBlock(props);
            }
            var light=new GameObject("Test Light",typeof(Light2D)).GetComponent<Light2D>();
            light.gameObject.layer=31;light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Test Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(2.5f,1,-10);camera.orthographic=true;camera.orthographicSize=1.5f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<31;
            camera.allowHDR=false;camera.targetTexture=target;
            Directory.CreateDirectory("Design/OreOverlaySheets");
            float bright=Render("Bright",1),dim=Render("Dim",.1f),dark=Render("Dark",0);
            if(bright<=dim||dark>.001f)throw new Exception("Invalid light response: "+bright+" / "+dim+" / "+dark);
            if(ShaderUtil.ShaderHasError(oreMat.shader))throw new Exception("Shader compilation failed");
            return "Brightness: "+bright+"; dim: "+dim+"; no light: "+dark+". Actual material rendered across all six ores and three richness levels.";

            float Render(string suffix,float intensity)
            {
                light.intensity=intensity;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1200,600),0,0);pixels.Apply();
                File.WriteAllBytes("Design/OreOverlaySheets/Implemented-"+suffix+".png",pixels.EncodeToPNG());
                float sum=0;foreach(var c in pixels.GetPixels())sum+=c.r+c.g+c.b;
                return sum/(1200*600*3);
            }
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=enabled[i];
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(stoneMat);
        }
    }
}
