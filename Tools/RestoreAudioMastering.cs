// Pipeline eval_file. Explicitly invoked rollback of this mastering batch only.
if (UnityEditor.EditorApplication.isPlaying || GpsSettings.HasUnsavedChanges)
    throw new System.InvalidOperationException("Stop Play Mode and preserve pending GPS edits first.");
var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("AudioMastering/applied.json"));
var plans = new System.Collections.Generic.List<(Newtonsoft.Json.Linq.JToken row, string current, string restored)>();
System.Func<string,string> hash = path => {
    using var input = System.IO.File.OpenRead(path);
    using var sha = System.Security.Cryptography.SHA256.Create();
    return System.BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
};
var catalog = GpsSettings.GetValue(GpsAudio.Record.key, "clips");
foreach (var row in manifest["clips"])
{
    string path = UnityEditor.AssetDatabase.GUIDToAssetPath((string)row["guid"]);
    if (!System.IO.File.Exists(path) || hash(path) != (string)row["output_sha256"])
        throw new System.InvalidOperationException("Audio changed after mastering: " + path);
    string original = "AudioMastering/Originals/" + (string)row["path"];
    if (hash(original) != (string)row["sha256"] || hash(original + ".meta") != (string)row["meta_sha256"])
        throw new System.InvalidOperationException("Original backup mismatch: " + original);
    string restored = System.IO.Path.ChangeExtension(path, System.IO.Path.GetExtension((string)row["path"]));
    if (restored != path && System.IO.File.Exists(restored))
        throw new System.InvalidOperationException("Restore destination exists: " + restored);
    var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(path);
    string key = GpsSettings.Profile.Key(clip);
    var entry = catalog.children.Find(e => e.children.Find(f => f.name == "clip")?.text == key);
    if (entry == null || System.Math.Abs(entry.children.Find(f => f.name == "volume").number - (double)row["new_volume"]) > .00001)
        throw new System.InvalidOperationException("Clip volume edited after mastering: " + path);
    entry.children.Find(f => f.name == "volume").number = (double)row["volume"];
    plans.Add((row, path, restored));
}
UnityEditor.AssetDatabase.StartAssetEditing();
try
{
    foreach (var plan in plans)
    {
        if (plan.current != plan.restored)
        {
            string error = UnityEditor.AssetDatabase.MoveAsset(plan.current, plan.restored);
            if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException(error);
        }
        string original = "AudioMastering/Originals/" + (string)plan.row["path"];
        System.IO.File.Copy(original, plan.restored, true);
        System.IO.File.Copy(original + ".meta", plan.restored + ".meta", true);
        UnityEditor.AssetDatabase.ImportAsset(plan.restored, UnityEditor.ImportAssetOptions.ForceUpdate);
    }
}
finally { UnityEditor.AssetDatabase.StopAssetEditing(); }
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
if (!GpsSettings.SetValue(GpsAudio.Record.key, catalog, out string saveError) || !GpsSettings.Save(out saveError))
    throw new System.InvalidOperationException(saveError);
return new { restored = plans.Count };
