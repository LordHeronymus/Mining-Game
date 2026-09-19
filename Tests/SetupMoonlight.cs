using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupMoonlight {
 public static object Main() {
 if(Application.isPlaying) throw new Exception("Use Edit mode.");
 var sky=UnityEngine.Object.FindFirstObjectByType<SkyController>();
 var global=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Single(x=>x.lightType==Light2D.LightType.Global);
 var controller=sky.GetComponent<MoonlightController>();
 if(!controller) controller=Undo.AddComponent<MoonlightController>(sky.gameObject);
 controller.enabled=false;
 var child=sky.transform.Find("Moonlight");
 if(!child) {var go=new GameObject("Moonlight");Undo.RegisterCreatedObjectUndo(go,"Add moonlight");go.transform.SetParent(sky.transform,false);child=go.transform;}
 var light=child.GetComponent<Light2D>();if(!light)light=Undo.AddComponent<Light2D>(child.gameObject);
 light.lightType=Light2D.LightType.Point;light.pointLightInnerAngle=360f;light.pointLightOuterAngle=360f;light.intensity=0f;
 var from=new SerializedObject(global);var to=new SerializedObject(light);
 to.FindProperty("m_ApplyToSortingLayers").arraySize=from.FindProperty("m_ApplyToSortingLayers").arraySize;
 for(int i=0;i<to.FindProperty("m_ApplyToSortingLayers").arraySize;i++)to.FindProperty("m_ApplyToSortingLayers").GetArrayElementAtIndex(i).intValue=from.FindProperty("m_ApplyToSortingLayers").GetArrayElementAtIndex(i).intValue;
 to.ApplyModifiedProperties();
 controller.daylight=global;controller.moonlight=light;controller.dayIntensity=global.intensity;controller.enabled=true;
 EditorUtility.SetDirty(controller);EditorUtility.SetDirty(light);
 EditorSceneManager.MarkSceneDirty(sky.gameObject.scene);EditorSceneManager.SaveScene(sky.gameObject.scene);
 bool original=sky.IsNight;var camera=sky.GetComponentInParent<SurfaceBackgroundController>().RenderCamera;var old=camera.transform.position;
 try {
 sky.SetNight(true);sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();
 if(light.intensity<=0f || global.intensity>=controller.dayIntensity)throw new Exception("Night lighting missing");
 Vector3 first=light.transform.position;
 var layer=sky.GetComponent<ParallaxLayer>();var day=layer.Renderers.First(x=>x.enabled && !layer.IsBottomEdge(x.sprite));sky.TryGetNightRenderer(day,out var night);
 Vector3 expected=new Vector3(night.bounds.min.x+night.bounds.size.x*controller.moonPosition.x,night.bounds.min.y+night.bounds.size.y*controller.moonPosition.y,night.bounds.center.z);
 if(Vector3.Distance(first,expected)>.001f)throw new Exception("Moon position mismatch");
 camera.transform.position+=new Vector3(3,2,0);sky.Refresh();controller.Refresh();if(light.transform.position==first)throw new Exception("Moon light failed to follow sky");camera.transform.position=old;
 sky.Refresh();controller.Refresh();
 foreach(var entry in sky.GetComponentInParent<SurfaceBackgroundController>().layers) if(entry.layer!=layer)entry.layer.Refresh();
 sky.GetComponent<NightSkyStars>().Refresh(2f);
 var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;var rt=new RenderTexture(1280,720,24);var capture=new Texture2D(1280,720,TextureFormat.RGB24,false);
 try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();File.WriteAllBytes("Temp/MoonlightPreview.png",capture.EncodeToPNG());}finally{camera.targetTexture=previousTarget;RenderTexture.active=previousActive;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(capture);}
 sky.SetNight(false);sky.AdvanceFade(sky.SkyFadeDuration*.5f);sky.Refresh();controller.Refresh();if(Mathf.Abs(global.intensity-Mathf.Lerp(controller.dayIntensity,controller.nightAmbient,.5f))>.001f)throw new Exception("Midpoint failed");
 sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();if(light.intensity!=0f || global.intensity!=controller.dayIntensity)throw new Exception("Day restore failed");
 return "Passed: moon position, parallax, night intensity, fade midpoint, day restoration";
 }finally{camera.transform.position=old;sky.SetNight(original);sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();foreach(var entry in sky.GetComponentInParent<SurfaceBackgroundController>().layers)if(entry.layer!=sky.GetComponent<ParallaxLayer>())entry.layer.Refresh();sky.GetComponent<NightSkyStars>().Refresh(0f);}
 }
}
