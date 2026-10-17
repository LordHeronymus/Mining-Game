using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupPlayerGlow {
 public static object Main(){
 if(Application.isPlaying)return "PLAYING";
 var station=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
 var sky=UnityEngine.Object.FindFirstObjectByType<SkyController>();
 var source=station.GetComponentsInChildren<SpriteRenderer>().First(x=>x.name!="Night Energy Glow");
 var controller=station.GetComponent<NightGlow>();if(!controller)controller=Undo.AddComponent<NightGlow>(station.gameObject);
 var child=source.transform.Find("Night Energy Glow");
 if(!child){var go=new GameObject("Night Energy Glow");Undo.RegisterCreatedObjectUndo(go,"Add player night glow");go.transform.SetParent(source.transform,false);child=go.transform;}
 var glow=child.GetComponent<SpriteRenderer>();if(!glow)glow=Undo.AddComponent<SpriteRenderer>(child.gameObject);
 const string path="Assets/GameObjects/MonolithEnergyGlow.mat";
 var shader=Shader.Find("Mining/Monolith Energy Glow");if(!shader || ShaderUtil.ShaderHasError(shader))throw new Exception("Glow shader invalid");
 var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}glow.sharedMaterial=material;
 var lamp=station.transform.Find("Night Light");if(!lamp){var go=new GameObject("Night Light");Undo.RegisterCreatedObjectUndo(go,"Add player night light");go.transform.SetParent(station.transform,false);lamp=go.transform;}
 var light=lamp.GetComponent<Light2D>();if(!light)light=Undo.AddComponent<Light2D>(lamp.gameObject);
 light.lightType=Light2D.LightType.Point;light.pointLightInnerAngle=360;light.pointLightOuterAngle=360;
 var global=UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).First(x=>x.lightType==Light2D.LightType.Global);
 var from=new SerializedObject(global).FindProperty("m_ApplyToSortingLayers");var target=new SerializedObject(light);var layers=target.FindProperty("m_ApplyToSortingLayers");layers.arraySize=from.arraySize;for(int i=0;i<from.arraySize;i++)layers.GetArrayElementAtIndex(i).intValue=from.GetArrayElementAtIndex(i).intValue;target.ApplyModifiedProperties();
 controller.sky=sky;controller.source=source;controller.glow=glow;controller.localLight=light;controller.lightIntensity=.35f;controller.lightRadius=1.8f;controller.glowIntensity=.35f;controller.lightColor=new Color(.25f,.8f,1f);
 bool old=sky.IsNight;Vector3 originalPosition=station.transform.position;
 try {
 sky.SetNight(false);sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();if(light.intensity!=0||glow.enabled)throw new Exception("Day glow visible");
 sky.SetNight(true);sky.AdvanceFade(sky.SkyFadeDuration*.5f);controller.Refresh();if(Mathf.Abs(light.intensity-controller.lightIntensity*.5f)>.001f||Mathf.Abs(glow.color.a-source.color.a*.5f)>.001f)throw new Exception("Fade midpoint incorrect");
 sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();if(!glow.enabled||light.intensity!=controller.lightIntensity||glow.sprite!=source.sprite)throw new Exception("Night glow missing");
 station.transform.position+=new Vector3(1f,.5f,0f);controller.Refresh();if(Vector3.Distance(light.transform.position,source.bounds.center)>.001f)throw new Exception("Light position incorrect");
 }finally{station.transform.position=originalPosition;sky.SetNight(old);sky.AdvanceFade(sky.SkyFadeDuration);controller.Refresh();}
 EditorUtility.SetDirty(controller);EditorUtility.SetDirty(glow);EditorUtility.SetDirty(light);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(station.gameObject.scene);if(!EditorSceneManager.SaveScene(station.gameObject.scene))throw new Exception("Save failed");
 return new {source=source.name,passed="day off, half fade, night glow, local light position, shader",saved=true};
 }
}

