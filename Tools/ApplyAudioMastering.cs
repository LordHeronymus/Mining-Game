// Pipeline eval_file. Run only after Tools/audio_mastering.py render succeeds.
if (UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Stop Play Mode before replacing audio assets.");
if (GpsSettings.HasUnsavedChanges)
    throw new System.InvalidOperationException("GPS has unsaved changes; preserve them before mastering.");
var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("AudioMastering/manifest.json"));
var rows = (Newtonsoft.Json.Linq.JArray)manifest["clips"];
var catalog = GpsSettings.GetValue(GpsAudio.Record.key, "clips");
var plans = new System.Collections.Generic.List<(Newtonsoft.Json.Linq.JToken row, string source, string target)>();
System.Func<string, string> hash = path => {
    using var stream = System.IO.File.OpenRead(path);
    using var sha = System.Security.Cryptography.SHA256.Create();
    return System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
};
// Validate the entire batch before modifying a single asset.
foreach (var row in rows)
{
    string source = UnityEditor.AssetDatabase.GUIDToAssetPath((string)row["guid"]);
    string target = System.IO.Path.ChangeExtension(source, ".wav").Replace('\\', '/');
    if (string.IsNullOrEmpty(source) || hash(source) != (string)row["sha256"])
        throw new System.InvalidOperationException("Source changed: " + source);
    if (hash((string)row["candidate"]) != (string)row["output_sha256"])
        throw new System.InvalidOperationException("Candidate changed: " + source);
    if (source != target && System.IO.File.Exists(target))
        throw new System.InvalidOperationException("Destination already exists: " + target);
    var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(source);
    string key = GpsSettings.Profile.Key(clip);
    var entry = catalog.children.Find(e => e.children.Find(f => f.name == "clip")?.text == key);
    if (entry == null || System.Math.Abs(entry.children.Find(f => f.name == "volume").number - (double)row["volume"]) > .00001)
        throw new System.InvalidOperationException("GPS volume changed since analysis: " + source);
    entry.children.Find(f => f.name == "volume").number = (double)row["new_volume"];
    plans.Add((row, source, target));
}
HomeAudioEditorPreview.Stop();
UnityEditor.AssetDatabase.StartAssetEditing();
try
{
    foreach (var plan in plans)
    {
        if (plan.source != plan.target)
        {
            string moveError = UnityEditor.AssetDatabase.MoveAsset(plan.source, plan.target);
            if (!string.IsNullOrEmpty(moveError)) throw new System.InvalidOperationException(moveError);
        }
        System.IO.File.Copy((string)plan.row["candidate"], plan.target, true);
        plan.row["installed_path"] = plan.target;
        UnityEditor.AssetDatabase.ImportAsset(plan.target, UnityEditor.ImportAssetOptions.ForceUpdate);
    }
}
finally { UnityEditor.AssetDatabase.StopAssetEditing(); }
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
foreach (var plan in plans)
{
    if (UnityEditor.AssetDatabase.AssetPathToGUID(plan.target) != (string)plan.row["guid"])
        throw new System.InvalidOperationException("GUID changed: " + plan.target);
    var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(plan.target);
    var importer = (UnityEditor.AudioImporter)UnityEditor.AssetImporter.GetAtPath(plan.target);
    int channels = importer.forceToMono ? 1 : (int)plan.row["after"]["channels"];
    if (!clip || System.Math.Abs(clip.length - (double)plan.row["after"]["duration"]) > .001 || clip.channels != channels)
        throw new System.InvalidOperationException("Imported duration/channels differ: " + plan.target);
}
if (!GpsSettings.SetValue(GpsAudio.Record.key, catalog, out string error) || !GpsSettings.Save(out error))
    throw new System.InvalidOperationException(error);
System.IO.File.WriteAllText("AudioMastering/applied.json", manifest.ToString(Newtonsoft.Json.Formatting.Indented));
return new { clips = plans.Count, valid = GpsSettings.ValidateDocument(GpsSettings.Document, out error), error };
