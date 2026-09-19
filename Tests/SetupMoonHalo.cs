using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupMoonHalo {
 public static object Main(){
 if(Application.isPlaying)return "PLAYING";
 var controller=UnityEngine.Object.FindFirstObjectByType<MoonlightController>();
 const string path="Assets/GameObjects/Background/MoonHalo.mat";
 var shader=Shader.Find("Mining/Moon Halo");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Halo shader invalid");
 var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
 Undo.RecordObject(controller,"Add moon halo");controller.haloMaterial=material;
 var sky=controller.GetComponent<SkyController>();bool old=sky.IsNight;
 try{
 sky.SetNight(true);sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();
 var halo=controller.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name=="Moon halo (generated)");
 if(!halo.enabled||Vector3.Distance(halo.transform.position,controller.moonlight.transform.position)>.001f)throw new Exception("Halo not aligned with moon");
 int id=halo.GetInstanceID();var props=new MaterialPropertyBlock();halo.GetPropertyBlock(props);
 if(props.GetColor("_Tint").a<=0||props.GetFloat("_Intensity")<=0)throw new Exception("Halo invisible");
 var camera=controller.GetComponentInParent<SurfaceBackgroundController>().RenderCamera;
 var pos=camera.transform.position;float zoom=camera.orthographicSize;
 var rt=new RenderTexture(640,640,24);var capture=new Texture2D(640,640,TextureFormat.RGB24,false);var target=camera.targetTexture;var active=RenderTexture.active;
 try{camera.transform.position=new Vector3(halo.transform.position.x,halo.transform.position.y,pos.z);camera.orthographicSize=halo.bounds.extents.y*1.4f;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;capture.ReadPixels(new Rect(0,0,640,640),0,0);capture.Apply();System.IO.File.WriteAllBytes("Temp/MoonHaloPreview.png",capture.EncodeToPNG());}
 finally{camera.transform.position=pos;camera.orthographicSize=zoom;camera.targetTexture=target;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(capture);}
 sky.SetNight(false);sky.AdvanceFade(sky.SkyFadeDuration*.5f);sky.Refresh();controller.Refresh();halo.GetPropertyBlock(props);
 if(Mathf.Abs(props.GetColor("_Tint").a-controller.haloColor.a*.5f)>.001f)throw new Exception("Halo fade mismatch");
 sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();if(halo.enabled||halo.GetInstanceID()!=id)throw new Exception("Halo day state or pooling failed");
 }finally{sky.SetNight(old);sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();controller.Refresh();}
 EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);if(!EditorSceneManager.SaveScene(controller.gameObject.scene))throw new Exception("Scene save failed");
 return "Passed: alignment, visibility, midpoint fade, daylight off, persistent renderer, shader. Saved.";
 }
}
