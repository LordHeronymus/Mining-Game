using System;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class InstallHomeL1Wind
{
    public static object Main()
    {
        const string path="Assets/Resources/Audio/HomeL1Wind.wav";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.Copy("Design/Tiefenhall/HomeAmbience/L1/Wind-L1-soft-loop-60s.wav",path,true);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(AudioImporter)AssetImporter.GetAtPath(path);
        importer.forceToMono=false;
        importer.loadInBackground=false;
        var settings=importer.defaultSampleSettings;
        settings.preloadAudioData=true;
        settings.loadType=AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat=AudioCompressionFormat.PCM;
        settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings=settings;
        importer.SaveAndReimport();
        var clip=Resources.Load<AudioClip>("Audio/HomeL1Wind");
        if(!clip || Math.Abs(clip.length-60)>.01 || clip.channels!=2 || clip.frequency!=44100)
            throw new Exception("Wind import mismatch");
        var samples=new float[clip.samples*clip.channels];
        if(!clip.GetData(samples,0))throw new Exception("PCM not readable");
        float seam=0,peak=0;
        for(int i=0;i<samples.Length;i++)peak=Math.Max(peak,Math.Abs(samples[i]));
        for(int c=0;c<clip.channels;c++) seam=Math.Max(seam,Math.Abs(samples[c]-samples[samples.Length-clip.channels+c]));
        if(seam>.00001 || peak>=1)throw new Exception("Seam or clipping");
        var result=new {clip.length,clip.channels,clip.frequency,seam,peak,compression=importer.defaultSampleSettings.compressionFormat.ToString()};
        File.WriteAllText("Temp/HomeL1WindImport.txt",Newtonsoft.Json.JsonConvert.SerializeObject(result));
        return result;
    }
}
