// Inspect the same codec output as streamed playback, restoring load settings.
var rows = Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText("AudioMastering/unity-decoded.json"));
var checkedPaths = new System.Collections.Generic.List<string>();
foreach (var row in rows)
{
    if (!(bool)row["streaming"]) continue;
    string path = (string)row["path"];
    var importer = (UnityEditor.AudioImporter)UnityEditor.AssetImporter.GetAtPath(path);
    var original = importer.defaultSampleSettings;
    bool background = importer.loadInBackground;
    try
    {
        var inspection = original;
        inspection.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad;
        importer.defaultSampleSettings = inspection;
        importer.loadInBackground = false;
        importer.SaveAndReimport();
        var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(path);
        clip.LoadAudioData();
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0)) throw new System.Exception("Cannot inspect " + path);
        var bytes = new byte[samples.Length * 4];
        System.Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        System.IO.File.WriteAllBytes("Temp/AudioMasteringDecoded/" + (string)row["guid"] + ".f32", bytes);
        clip.UnloadAudioData();
        checkedPaths.Add(path);
    }
    finally
    {
        importer.defaultSampleSettings = original;
        importer.loadInBackground = background;
        importer.SaveAndReimport();
    }
}
return checkedPaths;
