using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupSurfaceRabbit
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Install outside Play mode.");
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if(!map)throw new Exception("Map missing.");
        var rabbit=UnityEngine.Object.FindFirstObjectByType<SurfaceRabbit>();
        if(!rabbit)
        {
            var go=new GameObject("Surface Rabbit");Undo.RegisterCreatedObjectUndo(go,"Add surface rabbit");
            var parent=GameObject.Find("Map Objects");if(parent)go.transform.SetParent(parent.transform,false);
            go.transform.position=new Vector3(-2,map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y,0);
            rabbit=Undo.AddComponent<SurfaceRabbit>(go);
        }
        Undo.RecordObject(rabbit,"Configure rabbit");rabbit.map=map;
        rabbit.material=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Background/BirdSilhouette.mat");
        if(!rabbit.material)throw new Exception("Unlit material missing.");
        EditorUtility.SetDirty(rabbit);EditorSceneManager.MarkSceneDirty(rabbit.gameObject.scene);
        Vector3 original=rabbit.transform.position;
        var stop=typeof(SurfaceRabbit).GetMethod("OnDisable",Private);
        var start=typeof(SurfaceRabbit).GetMethod("OnEnable",Private);
        var tick=typeof(SurfaceRabbit).GetMethod("Tick",Private);
        var cameraObject=new GameObject("Rabbit preview camera");
        var target=new RenderTexture(800,600,24);
        var previous=RenderTexture.active;Texture2D capture=null;
        float minX=float.MaxValue,maxX=float.MinValue,maxLift=0;
        try
        {
            stop.Invoke(rabbit,null);start.Invoke(rabbit,null);
            float ground=map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y;
            for(int i=0;i<600;i++)
            {
                tick.Invoke(rabbit,new object[]{.05f});
                var position=rabbit.transform.position;
                minX=Mathf.Min(minX,position.x);maxX=Mathf.Max(maxX,position.x);maxLift=Mathf.Max(maxLift,position.y-ground);
                if(position.y<ground-.001f)throw new Exception("Rabbit fell below surface");
            }
            if(maxX-minX<.5f||maxLift<.1f)throw new Exception("Rabbit did not hop");
            // Render a seated pose near its authored starting position.
            stop.Invoke(rabbit,null);rabbit.transform.position=original;start.Invoke(rabbit,null);
            tick.Invoke(rabbit,new object[]{0f});
            var camera=cameraObject.AddComponent<Camera>();
            camera.transform.position=rabbit.transform.position+new Vector3(0,.35f,-10);
            camera.orthographic=true;camera.orthographicSize=.72f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.35f,.45f);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            capture=new Texture2D(800,600,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,800,600),0,0);capture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"SurfaceRabbitPreview.png"),capture.EncodeToPNG());
            return "PASS: 30s surface movement; travelled range="+(maxX-minX)+", hop height="+maxLift+"; preview rendered.";
        }
        finally
        {
            RenderTexture.active=previous;stop.Invoke(rabbit,null);rabbit.transform.position=original;
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);
            if(capture)UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
