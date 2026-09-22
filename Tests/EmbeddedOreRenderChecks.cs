using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class EmbeddedOreRenderChecks
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var previous=SceneManager.GetActiveScene();
        var lights=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var enabled=new bool[lights.Length];
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var target=new RenderTexture(1200,800,24,RenderTextureFormat.ARGB32);
        var oldTarget=RenderTexture.active;
        var pixels=new Texture2D(1200,800,TextureFormat.RGBA32,false);
        var oreMat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/OreOverlayLit.mat"));
        var stoneMat=new Material(Shader.Find("Mining Game/Dirt Terrain Lit"));
        var occupancy=new Texture2D(6,4,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        try
        {
            for(int i=0;i<lights.Length;i++){enabled[i]=lights[i].enabled;lights[i].enabled=false;}
            SceneManager.SetActiveScene(scene);
            var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            var stone=map.uniformTestTile.sprite;
            stoneMat.shader=map.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().sharedMaterial.shader;
            var props=new MaterialPropertyBlock();map.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().GetPropertyBlock(props);
            for(int x=0;x<6;x++)for(int y=0;y<4;y++)occupancy.SetPixel(x,y,new Color(1,y==3?1:0,y==2?1:0,y==0?1:0));
            occupancy.Apply();props.SetTexture("_TestOccupancy",occupancy);
            props.SetVector("_UniformStone",new Vector4(1,3,-.5f,-.5f));props.SetVector("_TestBounds",new Vector4(0,0,6,4));
            props.SetFloat("_OreScale",map.oreScale);
            string[] names={"Gold","Copper","Iron","Silver","Platinum","Coal"};
            for(int col=0;col<6;col++)for(int row=0;row<4;row++)
            {
                var block=AssetDatabase.LoadAssetAtPath<Block>("Assets/GameObjects/Map/Blocks/"+names[col]+".asset");
                var ore=row==0?block.smallOre[0]:row==1?block.mediumOre[0]:block.richOre[0];
                var bg=new GameObject("Stone",typeof(SpriteRenderer));bg.layer=31;
                bg.transform.position=new Vector3(col,3-row,0);bg.transform.localScale=Vector3.one*2;
                var br=bg.GetComponent<SpriteRenderer>();br.sprite=stone;br.sharedMaterial=stoneMat;br.SetPropertyBlock(props);
                var go=new GameObject(names[col],typeof(SpriteRenderer));go.layer=31;
                go.transform.position=bg.transform.position;go.transform.localScale=Vector3.one*2;
                var renderer=go.GetComponent<SpriteRenderer>();renderer.sprite=ore.sprite;renderer.sharedMaterial=oreMat;renderer.sortingOrder=1;
                renderer.SetPropertyBlock(props);
            }
            var light=new GameObject("Test Light",typeof(Light2D)).GetComponent<Light2D>();
            light.gameObject.layer=31;light.lightType=Light2D.LightType.Global;light.intensity=1;
            var camera=new GameObject("Test Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(2.5f,1.5f,-10);camera.orthographic=true;camera.orthographicSize=2f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<31;
            camera.allowHDR=false;camera.targetTexture=target;
            Directory.CreateDirectory("Design/OreOverlaySheets");
            oreMat.SetFloat("_EmbeddingStrength",0);oreMat.SetFloat("_ShimmerStrength",1.4f);
            float before=Render("Before",1);
            oreMat.SetFloat("_EmbeddingStrength",1);oreMat.SetFloat("_ShimmerStrength",.12f);
            float bright=Render("Bright",1),dim=Render("Dim",.1f),dark=Render("Dark",0);
            if(Mathf.Abs(before-bright)<.001f)throw new Exception("Embedding made no visible difference");
            if(bright<=dim||dark>.001f)throw new Exception("Invalid light response: "+bright+" / "+dim+" / "+dark);
            if(ShaderUtil.ShaderHasError(oreMat.shader))throw new Exception("Shader compilation failed");
            return "Before: "+before+"; embedded: "+bright+"; dim: "+dim+"; no light: "+dark+". Six ores, four host materials, actual terrain masks and world textures.";

            float Render(string suffix,float intensity)
            {
                light.intensity=intensity;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();
                File.WriteAllBytes("Design/OreOverlaySheets/Embedded-"+suffix+".png",pixels.EncodeToPNG());
                float sum=0;foreach(var c in pixels.GetPixels())sum+=c.r+c.g+c.b;
                return sum/(1200*800*3);
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
