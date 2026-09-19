using System;
using System.Reflection;
using UnityEngine;
public static class BirdNightChecks {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static FieldInfo Pool=typeof(SurfaceBirds).GetField("birds",Flags);
 static MethodInfo Tick=typeof(SurfaceBirds).GetMethod("Tick",Flags);
 static int Count(SurfaceBirds birds){int count=0;foreach(var bird in (Array)Pool.GetValue(birds))if((bool)bird.GetType().GetField("active").GetValue(bird))count++;return count;}
 static void Place(SurfaceBirds birds,float x) {var pool=(Array)Pool.GetValue(birds);for(int i=0;i<pool.Length;i++){var b=pool.GetValue(i);b.GetType().GetField("x").SetValue(b,x);b.GetType().GetField("y").SetValue(b,0f);pool.SetValue(b,i);}}
 static void Step(SurfaceBirds birds,Camera camera,float dt)=>Tick.Invoke(birds,new object[]{camera,dt});
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 public static object Main(){
 var source=UnityEngine.Object.FindFirstObjectByType<SurfaceBirds>();
 var root=new GameObject("Bird night verification");root.SetActive(false);
 try{
 var cameraObject=new GameObject("Test camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=10;camera.aspect=1.6f;camera.transform.position=new Vector3(0,0,-10);
 var background=root.AddComponent<SurfaceBackgroundController>();background.targetCamera=camera;
 var skyObject=new GameObject("Sky");skyObject.transform.SetParent(root.transform);var sky=skyObject.AddComponent<SkyController>();
 var birdObject=new GameObject("Birds");birdObject.transform.SetParent(root.transform);var birds=birdObject.AddComponent<SurfaceBirds>();birds.material=source.material;birds.altitude=Vector2.zero;birds.flightSpeed=.1f;birds.nightOffscreenDespawnDelay=3f;
 root.SetActive(true);typeof(SurfaceBirds).GetMethod("OnEnable",Flags).Invoke(birds,null);Step(birds,camera,0f);int initial=Count(birds);Check(initial>0,"Day spawn");
 sky.SetNight(true);Place(birds,0f);Step(birds,camera,100f);Check(Count(birds)==initial,"Visible birds must survive and no night spawns");
 Place(birds,40f);Step(birds,camera,2f);Check(Count(birds)==initial,"Offscreen delay");
 Place(birds,0f);Step(birds,camera,.1f);Place(birds,40f);Step(birds,camera,2f);Check(Count(birds)==initial,"Reentry resets timer");
 Step(birds,camera,1.1f);Check(Count(birds)==0,"Despawn after continuous offscreen delay");Step(birds,camera,100f);Check(Count(birds)==0,"No night respawn");
 sky.SetNight(false);Step(birds,camera,20f);Check(Count(birds)>0,"Day spawning resumes");
 Place(birds,0f);sky.SetNight(true);camera.transform.position=new Vector3(0,-100,-10);Step(birds,camera,2f);Check(Count(birds)>0,"Underground must respect delay");Step(birds,camera,1.1f);Check(Count(birds)==0,"Vertical offscreen despawn");
 return "Passed: daytime spawn, night spawn stop, visible survival, offscreen delay, timer reset on reentry, no night respawn, dawn restart, vertical offscreen delay";
 }finally{root.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}
 }
}

