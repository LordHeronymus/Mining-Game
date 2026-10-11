using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class InstallLevelUpSound {
 public static object Main(){
  if(EditorApplication.isPlaying)throw new Exception("Install in Edit Mode");
  const string path="Assets/AC Audio/UI/LevelUp-ElevenLabs.wav";
  File.Copy("Assets/Design/Tiefenhall/LevelUpV2/Audio/LevelUp-ElevenLabs-V2-cut.wav",path,true);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
  var importer=(AudioImporter)AssetImporter.GetAtPath(path);
  var sample=importer.defaultSampleSettings;
  sample.loadType=AudioClipLoadType.DecompressOnLoad;sample.compressionFormat=AudioCompressionFormat.PCM;
  sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;sample.preloadAudioData=true;
  importer.defaultSampleSettings=sample;importer.forceToMono=false;importer.loadInBackground=false;
  importer.SaveAndReimport();
  var assets=AssetDatabase.LoadAssetAtPath<LevelUpPresentationAssets>("Assets/Resources/Progression/LevelUpPresentation.asset");
  var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
  if(!assets||!clip||Mathf.Abs(clip.length-2.205f)>.01f)throw new Exception("Missing or invalid selected clip");
  assets.sound=clip;
  clip.LoadAudioData();var data=new float[Mathf.RoundToInt(clip.frequency*.25f)*clip.channels];
  if(!clip.GetData(data,0))throw new Exception("Cannot read impact timing");
  double largest=0;int block=clip.frequency*clip.channels/100;
  for(int i=0;i+block<=data.Length;i+=block){double sum=0;for(int j=i;j<i+block;j++)sum+=data[j]*data[j];if(sum>largest){largest=sum;assets.impactSeconds=Mathf.Clamp((float)i/clip.channels/clip.frequency,.04f,.4f);}}
  EditorUtility.SetDirty(assets);AssetDatabase.SaveAssetIfDirty(assets);
  return new {path,clip.length,clip.channels,assets.impactSeconds};
 }
}
