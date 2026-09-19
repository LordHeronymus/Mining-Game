using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SetupNightStars {
 public static object Main() {
 if (Application.isPlaying) throw new Exception("Setup requires Edit mode.");
 var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
 var stars = sky.GetComponent<NightSkyStars>();
 if (!stars) stars = Undo.AddComponent<NightSkyStars>(sky.gameObject);
 const string path = "Assets/GameObjects/Background/NightStar.mat";
 var material = AssetDatabase.LoadAssetAtPath<Material>(path);
 var shader = Shader.Find("Mining/Night Star");
 if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Star shader invalid.");
 if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material,path); }
 Undo.RecordObject(stars,"Configure night stars"); stars.material = material;
 EditorUtility.SetDirty(stars); AssetDatabase.SaveAssets();
 EditorSceneManager.MarkSceneDirty(sky.gameObject.scene); EditorSceneManager.SaveScene(sky.gameObject.scene);
 return Verify();
 }
 public static object Verify() {
 var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
 var stars = sky.GetComponent<NightSkyStars>();
 bool wasNight = sky.IsNight;
 var camera = sky.GetComponentInParent<SurfaceBackgroundController>().RenderCamera;
 var oldPosition = camera.transform.position;
 try {
 sky.SetNight(true); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh(); stars.Refresh(0f);
 var system = stars.GetComponentsInChildren<ParticleSystem>(true).Single();
 var before = new ParticleSystem.Particle[1000]; int count = system.GetParticles(before);
 if(count < 100) throw new Exception("Missing stars");
 int id = system.GetInstanceID(); stars.Refresh(2f);
 var after = new ParticleSystem.Particle[1000]; system.GetParticles(after);
 bool pulsed = false;
 for(int i=0;i<count;i++) {
 if(before[i].position != after[i].position) throw new Exception("Stars drifted");
 if(Mathf.Abs(before[i].startSize-after[i].startSize)>.00001f) pulsed = true;
 }
 if(!pulsed) throw new Exception("No size animation");
 float smallest=float.PositiveInfinity,largest=0f;byte dimmest=255,brightest=0;
 for(int sample=0;sample<60;sample++) {
 stars.Refresh(sample*stars.pulsePeriod/20f);system.GetParticles(after);
 smallest=Mathf.Min(smallest,after[0].startSize);largest=Mathf.Max(largest,after[0].startSize);
 dimmest=Math.Min(dimmest,after[0].startColor.a);brightest=Math.Max(brightest,after[0].startColor.a);
 if(after[0].position!=before[0].position)throw new Exception("Pulse moved star");
 }
 if(largest/smallest<1.8f || brightest-dimmest<15)throw new Exception("Pulse not pronounced enough");
 var properties=new MaterialPropertyBlock();system.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(properties);
 if(properties.GetFloat("_Brightness")<3f)throw new Exception("Brightness multiplier missing");
 if(ShaderUtil.ShaderHasError(stars.material.shader))throw new Exception("Star shader compilation failed");
 if(before.Take(count).Max(p=>p.startSize)<before.Take(count).Min(p=>p.startSize)*1.5f) throw new Exception("No size diversity");
 camera.transform.position += new Vector3(3f,2f,0f); sky.Refresh(); stars.Refresh(2f); system.GetParticles(after);
 if(before[0].position == after[0].position) throw new Exception("No parallax");
 camera.transform.position = oldPosition; sky.Refresh(); stars.Refresh(2f);
 var previousTarget=camera.targetTexture; var previousActive=RenderTexture.active;
 var rt=new RenderTexture(1280,720,24); var capture=new Texture2D(1280,720,TextureFormat.RGB24,false);
 try {camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();File.WriteAllBytes("Temp/NightStarsPreview.png",capture.EncodeToPNG());}
 finally {camera.targetTexture=previousTarget;RenderTexture.active=previousActive;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(capture);}
 sky.SetNight(false); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh();stars.Refresh(2f);system.GetParticles(after);
 if(after.Take(count).Any(p=>p.startColor.a!=0)) throw new Exception("Stars visible by day");
 if(system.GetInstanceID()!=id) throw new Exception("Particles recreated");
 return new {count,passed="fixed positions, gentle pulse, size diversity, parallax, day fade, persistent pool, shader"};
 } finally {camera.transform.position=oldPosition;sky.SetNight(wasNight);sky.AdvanceFade(sky.SkyFadeDuration);sky.Refresh();stars.Refresh(0f);}
 }
}
