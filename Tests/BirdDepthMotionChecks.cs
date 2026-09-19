using System;
using System.Reflection;
using UnityEngine;

public static class BirdDepthMotionChecks
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<SurfaceBirds>();
        var root=new GameObject("Bird depth check");root.SetActive(false);
        var cameraObject=new GameObject("Bird depth check camera");
        try
        {
            root.transform.SetParent(source.transform.parent,false);
            var birds=root.AddComponent<SurfaceBirds>();birds.material=source.material;
            birds.flockInterval=new Vector2(60,60);
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=30;
            camera.aspect=2;camera.transform.position=new Vector3(0,0,-10);
            root.SetActive(true);
            var meshField=typeof(SurfaceBirds).GetField("mesh",Private);
            if(meshField.GetValue(birds)==null)typeof(SurfaceBirds).GetMethod("OnEnable",Private).Invoke(birds,null);
            var tick=typeof(SurfaceBirds).GetMethod("Tick",Private);
            tick.Invoke(birds,new object[]{camera,0f});
            var mesh=(Mesh)meshField.GetValue(birds);
            int stride=(int)typeof(SurfaceBirds).GetField("VerticesPerBird",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
            int body=2*(int)typeof(SurfaceBirds).GetField("WingVertices",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
            var initial=new float[birds.birdsPerFlock];
            var vertices=mesh.vertices;
            for(int i=0;i<initial.Length;i++)initial[i]=vertices[i*stride+body+1].x-vertices[i*stride+body].x;
            float min=1,max=1,last=1;
            for(int frame=0;frame<120;frame++)
            {
                tick.Invoke(birds,new object[]{camera,.1f});vertices=mesh.vertices;
                float factor=(vertices[body+1].x-vertices[body].x)/initial[0];
                for(int i=1;i<initial.Length;i++)
                {
                    float other=(vertices[i*stride+body+1].x-vertices[i*stride+body].x)/initial[i];
                    if(Mathf.Abs(other-factor)>.002f)throw new Exception("Flock size changes are not synchronized");
                }
                if(Mathf.Abs(factor-last)>.03f)throw new Exception("Depth movement jumped");
                min=Mathf.Min(min,factor);max=Mathf.Max(max,factor);last=factor;
            }
            if(max-min<.05f)throw new Exception("Insufficient depth movement");
            return "PASS: five birds scale together, individual proportions remain constant, smooth motion; scale range "+min+" to "+max;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
