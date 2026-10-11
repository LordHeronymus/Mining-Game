using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class LevelUpGlowContactChecks {
 public static object Main(){if(!Application.isPlaying||!MainMenuController.IsVisible||GameplayInputBlocker.IsBlocked)throw new Exception("Start in free MainMenu");var go=new GameObject("Level Up Contact Probe");Object.DontDestroyOnLoad(go);go.AddComponent<LevelUpContactProbe>();return "Started";}
}
public sealed class LevelUpContactProbe:MonoBehaviour {
 const string Report="Temp/LevelUpGlowContactChecks.txt",Folder="Assets/Design/Tiefenhall/LevelUpV2/Contact";
 string save,meta;bool clean;int checks;GpsDocument original;GpsProfile profile;string committed;bool dirty;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");}
 IEnumerator Start(){save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;original=GpsSettings.Document;profile=GpsSettings.Profile;committed=GpsSettings.CommittedJson;dirty=GpsSettings.HasUnsavedChanges;
  GpsSettings.UseProfile(profile,JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(original)));GpsSettings.Document.tests.testModeDisabled=true;GpsSettings.Preferences.levelUpAnimationOffsetSeconds=0;
  GameSaveSystem.TestDirectory=Path.GetFullPath("Temp/GlowContact-"+DateTime.UtcNow.Ticks);MetaProgression.TestDirectory=Path.Combine(GameSaveSystem.TestDirectory,"Profile");Directory.CreateDirectory(Folder);File.WriteAllText(Report,"");
  var run=Run();while(true){bool more=false;object next=null;Exception error=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){error=e;}if(error!=null){File.AppendAllText(Report,"FAILED "+error+"\n");break;}if(!more)break;yield return next;}
  if(!MainMenuController.IsVisible&&!RunNavigation.IsTransitioning)RunNavigation.MainMenu();yield return Ready();Cleanup();
 }
 IEnumerator Ready(){float end=Time.realtimeSinceStartup+120;while(LoadingProgress.Active||RunNavigation.IsTransitioning){if(Time.realtimeSinceStartup>end)throw new Exception("Loading timeout");yield return null;}yield return null;yield return null;}
 IEnumerator Run(){
  MetaProgression.BeginRun("contact-seed",new MetaRunState{runId="contact-seed",progressXp=MetaProgressionCatalog.XpForLevel(24),ended=true});MetaProgression.EndRun();RunNavigation.NewGame("Glow-Kontakt-Test");yield return Ready();
  var player=Object.FindFirstObjectByType<PlayerMovement>();Check(player&&Camera.main.GetComponent<CameraFollow>().target==player.transform&&Camera.main.GetComponent<CameraFollow>().enabled&&Camera.main.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh gameplay camera follow and clamp");
  var view=GameplayLevelUpPresentation.Instance;long xp=MetaProgression.TotalXp;
  Check(GameplayLevelUpPresentation.Preview(),"Actual level-up preview starts");yield return new WaitForSecondsRealtime(.4f);
  var lights=view.GetComponentsInChildren<LevelUpGlowGraphic>();var back=lights.Single(g=>!g.front);var sprite=Resources.Load<LevelUpPresentationAssets>("Progression/LevelUpPresentation").medallion;
  Check(back.material.GetTexture("_SealTex")==sprite.texture,"Halo uses original seal alpha texture");
  Check((view.GetComponent<Canvas>().additionalShaderChannels&AdditionalCanvasShaderChannels.TexCoord1)!=0,"Canvas carries silhouette mode to shader");
  Check(back.material.shader.isSupported&&UnityEditor.ShaderUtil.GetShaderMessages(back.material.shader).All(m=>m.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Halo shader compiles and is supported");
  Check(Mathf.Abs(back.material.GetFloat("_SealSize")-360)<.001f,"Halo and medallion share identical scale");
  var mesh=back.canvasRenderer.GetMesh();Check(mesh&&mesh.uv2.Take(4).All(uv=>uv.x==1),"Silhouette quad is separate from orbit and rays");
  foreach(float phase in new[]{.4f,1.2f,2.2f}){
   typeof(GameplayLevelUpPresentation).GetField("elapsed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,phase);yield return null;yield return new WaitForEndOfFrame();
   string name=phase==.4f?"Peak":phase==1.2f?"Hold":"End";
   Capture(view,Folder+"/"+name);Check(view.GetComponent<CanvasGroup>().alpha>(name=="End"?.1f:.99f),"Visible glow during "+name);
  }
  Check(MetaProgression.TotalXp==xp,"Optical preview never awards XP");
  RunNavigation.MainMenu();yield return Ready();Check(!view.IsShowing&&view.GetComponent<CanvasGroup>().alpha==0,"Scene exit clears glow");File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
 }
 static void Capture(GameplayLevelUpPresentation view,string path){
  var full=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(path+"-gameplay.png",full.EncodeToPNG());
  var rect=view.transform.Find("Level Up Medallion").GetComponent<RectTransform>();var corners=new Vector3[4];rect.GetWorldCorners(corners);
  var low=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var high=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
  int x=Mathf.Clamp(Mathf.FloorToInt(low.x)-60,0,full.width-1),y=Mathf.Clamp(Mathf.FloorToInt(low.y)-60,0,full.height-1);
  int width=Mathf.Min(full.width-x,Mathf.CeilToInt(high.x-low.x)+120),height=Mathf.Min(full.height-y,Mathf.CeilToInt(high.y-low.y)+120);
  var close=new Texture2D(width,height,TextureFormat.RGB24,false);close.SetPixels(full.GetPixels(x,y,width,height));close.Apply();File.WriteAllBytes(path+"-close.png",close.EncodeToPNG());Object.Destroy(close);Object.Destroy(full);
 }
 void Cleanup(){if(clean)return;clean=true;MetaProgression.EndRun();MetaProgression.TestDirectory=meta;GameSaveSystem.TestDirectory=save;GpsSettings.UseProfile(profile,original);typeof(GpsSettings).GetField("dirty",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,dirty);typeof(GpsSettings).GetField("savedJson",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,committed);Object.Destroy(gameObject);}
 void OnDestroy(){if(!clean)Cleanup();}
}
