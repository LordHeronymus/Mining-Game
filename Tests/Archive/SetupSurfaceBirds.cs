using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupSurfaceBirds
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Run setup outside Play mode.");
        var background=UnityEngine.Object.FindFirstObjectByType<SurfaceBackgroundController>();
        if(!background)throw new Exception("Surface background missing.");
        var transform=background.transform.Find("Birds");
        if(!transform)
        {
            var child=new GameObject("Birds");
            Undo.RegisterCreatedObjectUndo(child,"Add surface birds");
            transform=child.transform;transform.SetParent(background.transform,false);
        }
        var birds=transform.GetComponent<SurfaceBirds>();
        if(!birds)birds=Undo.AddComponent<SurfaceBirds>(transform.gameObject);
        const string path="Assets/GameObjects/Background/BirdSilhouette.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            material.SetColor("_Color",Color.white);
            AssetDatabase.CreateAsset(material,path);
        }
        Undo.RecordObject(birds,"Configure surface birds");birds.material=material;
        EditorUtility.SetDirty(birds);EditorSceneManager.MarkSceneDirty(background.gameObject.scene);
        AssetDatabase.SaveAssets();
        var camera=background.RenderCamera;
        var originalPosition=camera.transform.position;
        var previousTarget=camera.targetTexture;
        var previousActive=RenderTexture.active;
        var target=new RenderTexture(1280,720,24);
        Texture2D capture=null;
        var tick=typeof(SurfaceBirds).GetMethod("Tick",Private);
        var stop=typeof(SurfaceBirds).GetMethod("OnDisable",Private);
        try
        {
            stop.Invoke(birds,null);
            typeof(SurfaceBirds).GetMethod("OnEnable",Private).Invoke(birds,null);
            tick.Invoke(birds,new object[]{camera,0f});
            var mesh=(Mesh)typeof(SurfaceBirds).GetField("mesh",Private).GetValue(birds);
            int vertexCount=mesh.vertexCount;
            var first=mesh.vertices;
            tick.Invoke(birds,new object[]{camera,.2f});
            var second=mesh.vertices;
            if((first[0]-second[0]).sqrMagnitude<.001f)throw new Exception("Birds did not move");
            bool flapped=false;
            for(int frame=0;frame<15;frame++)
            {
                tick.Invoke(birds,new object[]{camera,.15f});
                var pose=mesh.vertices;
                if(Mathf.Abs((first[24]-first[0]).y-(pose[24]-pose[0]).y)>.005f)flapped=true;
            }
            if(!flapped)throw new Exception("Wings did not animate");
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            capture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"SurfaceBirdsPreview.png"),capture.EncodeToPNG());
            for(int i=0;i<300;i++)tick.Invoke(birds,new object[]{camera,.2f});
            if(mesh.vertexCount!=vertexCount)throw new Exception("Bird capacity changed");
            camera.transform.position=new Vector3(originalPosition.x,-30,originalPosition.z);
            tick.Invoke(birds,new object[]{camera,.1f});
            if(transform.GetComponentInChildren<MeshRenderer>().enabled)throw new Exception("Birds visible underground");
            return "PASS: bird flight, animated wings, fixed capacity, underground hiding; preview rendered.";
        }
        finally
        {
            stop.Invoke(birds,null);
            camera.transform.position=originalPosition;camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
            UnityEngine.Object.DestroyImmediate(target);if(capture)UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
