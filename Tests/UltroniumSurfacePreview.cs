using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class UltroniumSurfacePreview
{
    public static string Main(string stage)
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var previous=SceneManager.GetActiveScene();
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var enabled=new bool[lights.Length];
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var target=new RenderTexture(1400,800,24,RenderTextureFormat.ARGB32);
        var oldTarget=RenderTexture.active;
        var pixels=new Texture2D(1400,800,TextureFormat.RGBA32,false);
        var oreMat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/OreOverlayLit.mat"));
        var stoneMat=new Material(Shader.Find("Mining Game/Dirt Terrain Lit"));
        var occupancy=new Texture2D(7,4,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        try
        {
            for(int i=0;i<lights.Length;i++){enabled[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            var stone=map.uniformTestTile.sprite;
            stoneMat.shader=map.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().sharedMaterial.shader;
            var props=new MaterialPropertyBlock();map.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().GetPropertyBlock(props);
            for(int x=0;x<7;x++)for(int y=0;y<4;y++)occupancy.SetPixel(x,y,new Color(1,y==3?1:0,y==2?1:0,y==0?1:0));
            occupancy.Apply();props.SetTexture("_TestOccupancy",occupancy);
            props.SetVector("_UniformStone",new Vector4(1,3,-.5f,-.5f));props.SetVector("_TestBounds",new Vector4(0,0,7,4));
            props.SetFloat("_OreScale",map.oreScale);
            string[] names={"Ultronium","Ultronium","Ultronium","Ultronium","Ultronium","Ultronium","Ultronium"};
            for(int col=0;col<7;col++)for(int row=0;row<4;row++)
            {
                var block=AssetDatabase.LoadAssetAtPath<Block>("Assets/GameObjects/Map/Blocks/"+names[col]+".asset");
                var ore=col<2?block.smallOre[col]:col<4?block.mediumOre[col-2]:block.richOre[col-4];
                var bg=new GameObject("Stone",typeof(SpriteRenderer));bg.layer=31;
                bg.transform.position=new Vector3(col,3-row,0);bg.transform.localScale=Vector3.one*2;
                var br=bg.GetComponent<SpriteRenderer>();br.sprite=stone;br.sharedMaterial=stoneMat;br.SetPropertyBlock(props);
                var go=new GameObject(names[col],typeof(SpriteRenderer));go.layer=31;
                go.transform.position=bg.transform.position;go.transform.localScale=Vector3.one*2;
                var renderer=go.GetComponent<SpriteRenderer>();renderer.sprite=ore.sprite;renderer.sharedMaterial=oreMat;renderer.sortingOrder=1;
                renderer.color=ore.color; renderer.SetPropertyBlock(props);
            }
            var light=new GameObject("Test Light",typeof(Light2D)).GetComponent<Light2D>();
            light.gameObject.layer=31;light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Test Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(3f,1.5f,-10);camera.orthographic=true;camera.orthographicSize=2f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<31;
            camera.allowHDR=false;camera.targetTexture=target;
            Directory.CreateDirectory("Design/OreOverlaySheets");
            oreMat.SetFloat("_EmbeddingStrength",0);oreMat.SetFloat("_ShimmerStrength",1.4f);
            float before=Render("Before",1);
            var baseline=pixels.GetPixels();
            oreMat.SetFloat("_EmbeddingStrength",1);oreMat.SetFloat("_ShimmerStrength",.12f);
            float bright=Render("Bright",1);
            var embeddedPixels=pixels.GetPixels();
            float difference=0;
            for(int p=0;p<baseline.Length;p++)
                difference+=Mathf.Abs(baseline[p].r-embeddedPixels[p].r)
                    +Mathf.Abs(baseline[p].g-embeddedPixels[p].g)+Mathf.Abs(baseline[p].b-embeddedPixels[p].b);
            difference/=baseline.Length*3;
            float dim=Render("Dim",.1f),dark=Render("Dark",0);
            if(difference<.001f)throw new Exception("Embedding made no visible difference");
            if(bright<=dim)throw new Exception("Invalid light response: "+bright+" / "+dim+" / "+dark);
            if(ShaderUtil.ShaderHasError(oreMat.shader))throw new Exception("Shader compilation failed");
            return "Without embedding: "+before+"; embedded: "+bright+"; dim: "+dim+"; no light: "+dark+". Seven Ultronium variants, four host materials, actual terrain masks and world textures.";

            float Render(string suffix,float intensity)
            {
                light.intensity=intensity;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1400,800),0,0);pixels.Apply();
                File.WriteAllBytes("Design/OreOverlaySheets/Ultronium-"+stage+"-"+suffix+".png",pixels.EncodeToPNG());
                float sum=0;foreach(var c in pixels.GetPixels())sum+=c.r+c.g+c.b;
                return sum/(1400*800*3);
            }
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
            for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=enabled[i];
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(stoneMat);UnityEngine.Object.DestroyImmediate(occupancy);UnityEngine.Object.DestroyImmediate(oreMat);
        }
    }
}

