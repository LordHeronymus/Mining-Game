using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class InstallLevelUpAssets
{
 public static string Main(){
 const string art="Assets/Resources/Progression/LevelUpMedallion.png";
 AssetDatabase.ImportAsset(art);var importer=(TextureImporter)AssetImporter.GetAtPath(art);
 importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
 const string path="Assets/Resources/Progression/LevelUpPresentation.asset";
 var settings=AssetDatabase.LoadAssetAtPath<LevelUpPresentationAssets>(path);
 if(!settings){settings=ScriptableObject.CreateInstance<LevelUpPresentationAssets>();AssetDatabase.CreateAsset(settings,path);}
 settings.medallion=AssetDatabase.LoadAssetAtPath<Sprite>(art);
 const string audio="Assets/AC Audio/UI/LevelUp.wav";
 if(!AssetDatabase.LoadAssetAtPath<AudioClip>(audio)){
  var error=AssetDatabase.MoveAsset("Assets/ZZZ New Assets/epic upgrade boom.wav",audio);
  if(!string.IsNullOrEmpty(error))throw new Exception(error);
 }
 settings.sound=AssetDatabase.LoadAssetAtPath<AudioClip>(audio);
 settings.impactSeconds=.16f;
 if(settings.sound){settings.sound.LoadAudioData();var data=new float[Mathf.Min(settings.sound.samples,(int)(settings.sound.frequency*.45f))*settings.sound.channels];if(settings.sound.GetData(data,0)){double largest=0;int block=settings.sound.frequency*settings.sound.channels/50;for(int i=0;i+block<data.Length;i+=block){double sum=0;for(int j=i;j<i+block;j++)sum+=data[j]*data[j];if(sum>largest){largest=sum;settings.impactSeconds=Mathf.Clamp((float)i/settings.sound.channels/settings.sound.frequency,.04f,.4f);}}}}
 EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);
 return "Medallion="+(bool)settings.medallion+"; original clip="+settings.sound?.name+"; impact="+settings.impactSeconds;
 }
}
