using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static class HomeLayer2Checks {
 public static object Main() {
  if(!Application.isPlaying||!MainMenuController.IsVisible)throw new Exception("MainMenu Play required");
  new GameObject("L2 Checks").AddComponent<HomeLayer2Probe>();return "Started";
 }
}
public sealed class HomeLayer2Probe:MonoBehaviour {
 const string Report="Temp/HomeLayer2Checks.txt";
 const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 int count;
 HomeCaveVisual visual;
 int previous;
 void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(Report,"PASS "+(++count)+" "+message+"\n");}
 IEnumerator Start(){
  File.WriteAllText(Report,"");visual=Object.FindFirstObjectByType<HomeCaveVisual>();previous=visual.Layer;
  var run=Run();
  while(true){object next=null;bool more=false;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){File.AppendAllText(Report,"FAILED "+e+"\n");break;}if(!more)break;yield return next;}
  visual.SetLayer(previous);Destroy(gameObject);
 }
 IEnumerator Run(){
  var audio=Object.FindFirstObjectByType<LoadingAudio>();
  var music=(AudioSource[])typeof(LoadingAudio).GetField("homeSources",Hidden).GetValue(audio);
  visual.SetLayer(1);yield return new WaitForSecondsRealtime(1.5f);
  var meadow=(AudioSource[])typeof(LoadingAudio).GetField("meadowSources",Hidden).GetValue(audio);
  var underground=(AudioSource[])typeof(LoadingAudio).GetField("layer2Sources",Hidden).GetValue(audio);
  Check(meadow.Any(s=>s&&s.isPlaying&&s.volume>0),"L1 meadow retained");
  var source=music.First(s=>s&&s.isPlaying);int samples=source.timeSamples;
  visual.SetLayer(2);yield return new WaitForSecondsRealtime(.25f);
  Check(meadow.Any(s=>s&&s.volume>0)&&underground.Any(s=>s&&s.volume>0),"L1-L2 ambience overlaps during soft crossfade");
  yield return new WaitForSecondsRealtime(1.1f);
  Check(HomeLandscape.Layer2Clip&&HomeLandscape.Layer2Clip.name=="UndergroundClay","Original transition-rock ambience clip");
  Check(underground.Any(s=>s&&s.isPlaying&&s.clip==HomeLandscape.Layer2Clip&&s.volume>0),"L2 ambience actually plays");
  Check(meadow.All(s=>!s||!s.isPlaying&&s.volume==0),"L1 ambience stopped after fade");
  Check(visual.Layer==2&&!visual.WaterEffectsEnabled,"No L3 effects on L2");
  Check(visual.GetComponentsInChildren<RawImage>().Any(i=>i.texture==Resources.Load<Texture2D>("Homescreen/Layer2")&&i.color.a==1),"Approved L2 texture visible");
  Check(music.Contains(source)&&source.isPlaying&&source.timeSamples>samples,"Homescreen music continues without restart");
  LoadingAudio.PlayHomeWaterdrop();
  Check(!audio.GetComponents<AudioSource>().Any(s=>s&&s.isPlaying&&s.clip&&s.clip.name.StartsWith("HomeWaterdrop")),"L2 rejects drop sound");
  yield return new WaitForEndOfFrame();var screenshot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Design/Tiefenhall/Layer2Prototypes/L2-implemented.png",screenshot.EncodeToPNG());Destroy(screenshot);
  visual.SetLayer(3);yield return new WaitForSecondsRealtime(1);
  Check(underground.All(s=>!s||!s.isPlaying&&s.volume==0)&&visual.WaterEffectsEnabled,"L3 stops L2 ambience and restores cave effects");
  visual.SetLayer(2);yield return new WaitForSecondsRealtime(.15f);visual.SetLayer(1);yield return new WaitForSecondsRealtime(.15f);visual.SetLayer(2);yield return new WaitForSecondsRealtime(1);
  Check(underground.Any(s=>s&&s.isPlaying&&s.volume>0)&&meadow.All(s=>!s||!s.isPlaying),"Rapid switching settles on L2");
  visual.enabled=false;
  audio.enabled=false;yield return null;
  Check(underground.All(s=>!s||!s.isPlaying)&&meadow.All(s=>!s||!s.isPlaying),"Audio lifecycle stops both ambience banks");
  audio.enabled=true;visual.enabled=true;
  File.AppendAllText(Report,"COMPLETE "+count+" checks\n");
 }
}
