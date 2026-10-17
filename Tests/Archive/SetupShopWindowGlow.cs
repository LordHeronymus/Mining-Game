using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupShopWindowGlow {
 public static object Main(){
 if(Application.isPlaying)return "PLAYING";
 var station=UnityEngine.Object.FindFirstObjectByType<ShopBuilding>();
 var sky=UnityEngine.Object.FindFirstObjectByType<SkyController>();
 var source=station.GetComponentsInChildren<SpriteRenderer>().First(x=>x.name!="Night Window Glow");
 var controller=station.GetComponent<NightGlow>();if(!controller)controller=Undo.AddComponent<NightGlow>(station.gameObject);
 var child=source.transform.Find("Night Window Glow");
 if(!child){var go=new GameObject("Night Window Glow");Undo.RegisterCreatedObjectUndo(go,"Add shop window glow");go.transform.SetParent(source.transform,false);child=go.transform;}
 var glow=child.GetComponent<SpriteRenderer>();if(!glow)glow=Undo.AddComponent<SpriteRenderer>(child.gameObject);
 const string path="Assets/GameObjects/Map/Shop/ShopWindowGlow.mat";
 var shader=Shader.Find("Mining/Shop Window Glow");if(!shader || ShaderUtil.ShaderHasError(shader))throw new Exception("Glow shader invalid");
 var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}glow.sharedMaterial=material;
 var lamp=station.transform.Find("Night Light");if(!lamp){var go=new GameObject("Night Light");Undo.RegisterCreatedObjectUndo(go,"Add shop window light");go.transform.SetParent(station.transform,false);lamp=go.transform;}
 var light=lamp.GetComponent<Light2D>();if(!light)light=Undo.AddComponent<Light2D>(lamp.gameObject);
 light.lightType=Light2D.LightType.Point;light.pointLightInnerAngle=360;light.pointLightOuterAngle=360;
 var global=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).First(x=>x.lightType==Light2D.LightType.Global);
 var from=new SerializedObject(global).FindProperty("m_ApplyToSortingLayers");var target=new SerializedObject(light);var layers=target.FindProperty("m_ApplyToSortingLayers");layers.arraySize=from.arraySize;for(int i=0;i<from.arraySize;i++)layers.GetArrayElementAtIndex(i).intValue=from.GetArrayElementAtIndex(i).intValue;target.ApplyModifiedProperties();
 controller.sky=sky;controller.source=source;controller.glow=glow;controller.localLight=light;controller.lightPosition=new Vector2(.638f,.46f);controller.lightColor=new Color(1f,.5f,.12f);controller.lightIntensity=.7f;controller.lightRadius=3f;controller.glowIntensity=1.5f;
 bool old=sky.IsNight;
 try {
 sky.SetNight(false);sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();if(light.intensity!=0||glow.enabled)throw new Exception("Day glow visible");
 sky.SetNight(true);sky.AdvanceFade(sky.SkyFadeDuration*.5f);controller.Refresh();if(Mathf.Abs(light.intensity-controller.lightIntensity*.5f)>.001f||Mathf.Abs(glow.color.a-source.color.a*.5f)>.001f)throw new Exception("Fade midpoint incorrect");
 sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();if(!glow.enabled||light.intensity!=controller.lightIntensity||glow.sprite!=source.sprite)throw new Exception("Night glow missing");
 if(Vector3.Distance(light.transform.position,source.transform.TransformPoint((source.sprite.rect.size*controller.lightPosition-source.sprite.pivot)/source.sprite.pixelsPerUnit))>.001f)throw new Exception("Light position incorrect");
 }finally{sky.SetNight(old);sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();}
 EditorUtility.SetDirty(controller);EditorUtility.SetDirty(glow);EditorUtility.SetDirty(light);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(station.gameObject.scene);if(!EditorSceneManager.SaveScene(station.gameObject.scene))throw new Exception("Save failed");
 return new {source=source.name,passed="day off, half fade, night glow, local light position, shader",saved=true};
 }
}


