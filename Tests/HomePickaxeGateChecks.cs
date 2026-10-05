using System;
using System.Reflection;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
public static class HomePickaxeGateChecks {
 public static object Main() {
  if(!Application.isPlaying||!MainMenuController.IsVisible)throw new Exception("MainMenu Play required");
  const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance;
  var audio=UnityEngine.Object.FindFirstObjectByType<LoadingAudio>();int previous=LoadingAudio.HomeLayer;
  var timer=typeof(LoadingAudio).GetField("nextDistantAt",f);
  var update=typeof(LoadingAudio).GetMethod("UpdateDistantDetails",f);
  var sources=(List<AudioSource>)typeof(LoadingAudio).GetField("distantSources",f).GetValue(audio);
  int passed=0;
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception(message);passed++;};
  try {
   LoadingAudio.SetHomeLayer(2);timer.SetValue(audio,Time.unscaledTime-1);update.Invoke(audio,null);
   check(sources.Any(s=>s&&s.isPlaying&&s.clip.name.StartsWith("HomeDistantPickaxe")),"L2 should play distant pickaxes");
   LoadingAudio.SetHomeLayer(1);
   check(sources.All(s=>!s||!s.isPlaying),"L1 should stop an existing pickaxe tail immediately");
   timer.SetValue(audio,Time.unscaledTime-1);update.Invoke(audio,null);
   check(sources.All(s=>!s||!s.isPlaying),"L1 should not start another pickaxe");
   check(float.IsPositiveInfinity((float)timer.GetValue(audio)),"L1 should reset scheduling");
   LoadingAudio.SetHomeLayer(4);timer.SetValue(audio,Time.unscaledTime-1);update.Invoke(audio,null);
   check(sources.Any(s=>s&&s.isPlaying&&s.clip.name.StartsWith("HomeDistantPickaxe")),"L4 should retain distant pickaxes");
   return "PASS "+passed+" pickaxe layer checks";
  } finally {LoadingAudio.SetHomeLayer(1);LoadingAudio.SetHomeLayer(previous);}
 }
}
