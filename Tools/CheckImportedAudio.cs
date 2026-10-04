var results = new System.Collections.Generic.List<object>();
System.IO.Directory.CreateDirectory("Temp/AudioMasteringDecoded");
foreach (var entry in GpsAudio.Record.fields.Find(f => f.name == "clips").children)
{
    var clip = GpsAudio.Clip(entry);
    string path = UnityEditor.AssetDatabase.GetAssetPath(clip);
    string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
    if (clip.loadType != UnityEngine.AudioClipLoadType.DecompressOnLoad)
    {
        results.Add(new { guid, path, streaming = true, rate = clip.frequency, channels = clip.channels, frames = clip.samples });
        continue;
    }
    bool wasLoaded = clip.loadState == UnityEngine.AudioDataLoadState.Loaded;
    clip.LoadAudioData();
    var samples = new float[clip.samples * clip.channels];
    if (!clip.GetData(samples, 0)) throw new System.Exception("Cannot read decoded clip: " + path);
    var bytes = new byte[samples.Length * 4];
    System.Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
    System.IO.File.WriteAllBytes("Temp/AudioMasteringDecoded/" + guid + ".f32", bytes);
    results.Add(new { guid, path, streaming = false, rate = clip.frequency, channels = clip.channels, frames = clip.samples });
    if (!wasLoaded) clip.UnloadAudioData();
}
System.IO.File.WriteAllText("AudioMastering/unity-decoded.json", Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
return new { clips = results.Count };
