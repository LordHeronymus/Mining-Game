using System;
using System.Reflection;
using UnityEngine;

public static class BirdAppearanceChecks
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static int Constant(string name)=>(int)typeof(SurfaceBirds).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<SurfaceBirds>();
        var root=new GameObject("Bird appearance check");root.SetActive(false);
        var cameraObject=new GameObject("Bird appearance camera");
        var target=new RenderTexture(1280,640,24);
        var previous=RenderTexture.active;Texture2D image=null;
        try
        {
            var birds=root.AddComponent<SurfaceBirds>();birds.material=source.material;
            birds.birdsPerFlock=2;birds.altitude=new Vector2(5,5);
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.5f;
            camera.aspect=2;camera.transform.position=new Vector3(0,5,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.65f,.76f,.85f);
            root.layer=31;camera.cullingMask=1<<31;
            root.SetActive(true);
            var meshField=typeof(SurfaceBirds).GetField("mesh",Private);
            if(meshField.GetValue(birds)==null)typeof(SurfaceBirds).GetMethod("OnEnable",Private).Invoke(birds,null);
            var tick=typeof(SurfaceBirds).GetMethod("Tick",Private);tick.Invoke(birds,new object[]{camera,0f});
            var states=(Array)typeof(SurfaceBirds).GetField("birds",Private).GetValue(birds);
            for(int i=0;i<2;i++)
            {
                object state=states.GetValue(i);var type=state.GetType();
                type.GetField("x").SetValue(state,i==0?-.48f:.48f);
                type.GetField("y").SetValue(state,5f);
                type.GetField("direction").SetValue(state,i==0?1f:-1f);
                type.GetField("phase").SetValue(state,.6f);
                type.GetField("depth").SetValue(state,0f);
                type.GetField("scale").SetValue(state,1f);
                type.GetField("paletteIndex").SetValue(state,i);
                type.GetField("whiteWingtips").SetValue(state,i==0);
                states.SetValue(state,i);
            }
            tick.Invoke(birds,new object[]{camera,0f});
            var mesh=(Mesh)meshField.GetValue(birds);var vertices=mesh.vertices;var colors=mesh.colors;
            int stride=Constant("VerticesPerBird"),body=2*Constant("WingVertices"),beak=Constant("BeakOffset");
            for(int i=0;i<2;i++)
            {
                int start=i*stride;float direction=i==0?1:-1;
                if(colors[start+body].grayscale>=colors[start].grayscale)throw new Exception("Body is not darker than wings");
                if((vertices[start+beak+2].x-vertices[start+body].x)*direction<=.04f)throw new Exception("Beak does not project forward");
                if(vertices[start+beak].y<=vertices[start+beak+1].y)throw new Exception("Beak base is degenerate");
                if(colors[start+beak].r<.7f||colors[start+beak].g<.4f||colors[start+beak].b>.3f)throw new Exception("Beak is not yellow");
            }
            int rightWing=Constant("WingVertices");
            if(colors[20].grayscale<colors[0].grayscale+.2f)throw new Exception("White wingtip missing");
            if(colors[16]!=colors[0])throw new Exception("White extends into the inner 65%");
            if(colors[20]!=colors[rightWing+20])throw new Exception("Wingtip colors differ between wings");
            if(colors[stride+20]!=colors[stride])throw new Exception("Unmarked bird has white tips");
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image=new Texture2D(1280,640,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,640),0,0);image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"BirdBeaksPreview.png"),image.EncodeToPNG());
            return "PASS: darker bodies, yellow triangular beaks face both flight directions; rendered preview.";
        }
        finally
        {
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);
            if(image)UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
