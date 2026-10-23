using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Bughunt save/load (Edit Mode, isolated save directory): ladder towers above the surface keep a run loadable and
// are never written unloadable, a load from the recovery copy keeps that run in the main file, GPS rejects world
// sizes that cannot be saved, and the figure's facing is a saveable property instead of the transform scale.
public static class BughuntSaveLoadChecks
{
    const string Report = "Temp/BughuntSaveLoadChecks.txt";
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    [Serializable] sealed class Generation { public GpsValue[] generationSettings; }
    static int width, height;
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static string generationJson;

    // A run in the current file format: state JSON, then terrain, ore, artifact and ladder layers with empty cells.
    static byte[] Payload(int money, BoundsInt terrain, BoundsInt ladders)
    {
        string json = "{\"version\":1,\"seed\":7,\"width\":" + width + ",\"height\":" + height + "," + generationJson +
            ",\"savedUtc\":" + DateTime.UtcNow.Ticks + ",\"energy\":1,\"inventory\":{},\"stats\":{\"health\":1,\"money\":" + money + "}" +
            ",\"discovery\":{\"seed\":7,\"width\":" + width + ",\"height\":" + height + "}}";
        var empty = new BoundsInt(0, 0, 0, 0, 0, 1);
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(zip, Encoding.UTF8))
        {
            writer.Write(json);
            foreach (var bounds in new[] { terrain, empty, empty, ladders })
            {
                writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.size.x); writer.Write(bounds.size.y);
                writer.Write(0);
                for (int i = 0; i < bounds.size.x * bounds.size.y; i++) writer.Write(-1);
            }
        }
        return output.ToArray();
    }
    static void Write(int slot, string name, byte[] payload)
    {
        var summary = new GameSaveSystem.Summary { slot = slot, name = name, savedUtc = DateTime.UtcNow.Ticks };
        typeof(GameSaveSystem).GetMethod("WriteEnvelopeAtomic", Hidden).Invoke(null,
            new object[] { GameSaveSystem.SlotPath(slot), JsonUtility.ToJson(summary), payload, false });
    }
    static object Pending => typeof(GameSaveSystem).GetField("pending", Hidden).GetValue(null);
    static T Member<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    static bool Loads(int slot, BoundsInt terrain, BoundsInt ladders)
    {
        Write(slot, "Leitern", Payload(1, terrain, ladders));
        // Each case stands alone: without this, a rejected main file would fall back to the previous case's .bak.
        if (File.Exists(GameSaveSystem.SlotPath(slot) + ".bak")) File.Delete(GameSaveSystem.SlotPath(slot) + ".bak");
        bool ok = GameSaveSystem.PrepareLoad(slot, out _);
        GameSaveSystem.CancelPendingLoad();
        return ok;
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        if (GameSaveSystem.IsBusy || GameSaveSystem.HasPendingLoad || GameSaveSystem.IsAutoSaving) throw new InvalidOperationException("Spielstandsystem ist belegt.");
        checks.Clear();
        var save = typeof(GameSaveSystem);
        string previousDirectory = GameSaveSystem.TestDirectory, previousRunName = GameSaveSystem.ActiveRunName, previousError = GameSaveSystem.InitialSaveError;
        int previousSlot = GameSaveSystem.ActiveSlot; bool previousNewRun = GameSaveSystem.HasPendingNewRun;
        object previousActiveDirectory = save.GetField("activeDirectory", Hidden).GetValue(null);
        var slotsChanged = save.GetField("SlotsChanged", Hidden); object previousSlotsChanged = slotsChanged.GetValue(null);
        string directory = Path.GetFullPath("Temp/BughuntSaveLoad-" + DateTime.UtcNow.Ticks);
        GameObject root = null; Tile probeTile = null;
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        try
        {
            Directory.CreateDirectory(directory); GameSaveSystem.TestDirectory = directory; slotsChanged.SetValue(null, null);
            var map = Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
            Check(map, "MapGenerator in der Szene gefunden");
            var generation = GpsSettings.CaptureGeneration(map);
            width = (int)generation.First(field => field.name == "mapWidth").number; height = (int)generation.First(field => field.name == "mapHeight").number;
            string wrapped = JsonUtility.ToJson(new Generation { generationSettings = generation });
            generationJson = wrapped.Substring(1, wrapped.Length - 2);
            var skyField = typeof(LadderMap).GetField("SkyRows", BindingFlags.Public | BindingFlags.Static);
            Check(skyField != null, "LadderMap.SkyRows definiert die oberste Leiterreihe");
            int sky = (int)skyField.GetValue(null);
            Check(sky >= 200, "Leitern dürfen mindestens 200 Reihen über der Oberfläche stehen (" + sky + ")");
            var empty = new BoundsInt(0, 0, 0, 0, 0, 1);

            // C-placeables-2: ladder layers above the surface load again; the other layers stay strict.
            Check(Loads(1, empty, new BoundsInt(0, 1 - height, 0, 1, height + 20, 1)), "Leiterturm von der untersten Reihe bis 20 über der Oberfläche lädt");
            Check(Loads(1, empty, new BoundsInt(0, 6, 0, 1, 1, 1)), "Einzelne Leiter 6 Reihen über der Oberfläche lädt");
            Check(Loads(1, empty, new BoundsInt(-2, 4 + sky, 0, 3, 1, 1)), "Leiter an der obersten erlaubten Reihe lädt");
            Check(!Loads(1, empty, new BoundsInt(0, 5 + sky, 0, 1, 1, 1)), "Leiter über der Obergrenze wird weiterhin abgelehnt");
            Check(!Loads(1, empty, new BoundsInt(0, 1 - height, 0, 1, height + 9 + sky, 1)), "Zu hoher Leiter-Layer wird weiterhin abgelehnt");
            Check(!Loads(1, new BoundsInt(0, 6, 0, 1, 1, 1), empty), "Terrain über der Oberfläche wird weiterhin abgelehnt");

            // C-placeables-2: the save writer applies the same limits, so it never writes a file that loading rejects.
            root = new GameObject("Bughunt save/load grid", typeof(Grid)) { hideFlags = HideFlags.HideAndDontSave };
            var child = new GameObject("Ladders", typeof(Tilemap)) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(root.transform, false);
            var tilemap = child.GetComponent<Tilemap>();
            var catalog = Resources.Load<SaveAssetCatalog>("SaveAssetCatalog");
            Check(catalog, "Spielstand-Katalog gefunden");
            var layerType = save.GetNestedType("LayerSnapshot", BindingFlags.NonPublic);
            var finish = layerType.GetMethod("Finish", new[] { typeof(SaveAssetCatalog), typeof(int), typeof(int), typeof(bool) });
            Check(finish != null, "Erfassung prüft die Ebenengrenzen beim Abschluss");
            string Finish(bool ladders)
            {
                // As an array: Tilemap converts implicitly to bool and would pick CreateInstance(Type, bool nonPublic).
                try { finish.Invoke(Activator.CreateInstance(layerType, new object[] { tilemap }), new object[] { catalog, width, height, ladders }); return null; }
                catch (TargetInvocationException error) { return error.InnerException.Message; }
            }
            var ladderTile = Object.FindFirstObjectByType<LadderMap>(FindObjectsInactive.Include)?.segment;
            probeTile = ScriptableObject.CreateInstance<Tile>(); probeTile.hideFlags = HideFlags.HideAndDontSave;
            tilemap.SetTile(new Vector3Int(0, 1 - height, 0), ladderTile ? ladderTile : probeTile);
            tilemap.SetTile(new Vector3Int(0, 30, 0), ladderTile ? ladderTile : probeTile);
            Check(Finish(false) == "Ungültige Weltgrenzen.", "Speichern lehnt Terrain-Grenzen ab, die das Laden ablehnen würde");
            string ladderResult = Finish(true);
            Check(ladderResult == null || ladderResult.Contains("Katalog"), "Speichern nimmt einen Leiterturm über der Oberfläche an (" + ladderResult + ")");
            tilemap.SetTile(new Vector3Int(0, 9 + sky, 0), probeTile);
            Check(Finish(true) == "Ungültige Weltgrenzen.", "Speichern lehnt einen Leiter-Layer über der Obergrenze ab, statt eine unladbare Datei zu schreiben");

            // A-save-core-4: after a fallback to the recovery copy, the opening update keeps the loaded run.
            Write(2, "Sicherung", Payload(777, empty, empty));
            Write(2, "Beschädigt", Payload(5, new BoundsInt(0, 6, 0, 1, 1, 1), empty));
            string main = GameSaveSystem.SlotPath(2);
            Check(File.Exists(main + ".bak") && GameSaveSystem.GetSummary(2)?.name == "Beschädigt", "Hauptdatei mit gültiger Hülle, aber unladbarem Inhalt");
            Check(GameSaveSystem.PrepareLoad(2, out string error), "Laden fällt auf die Sicherung zurück: " + error);
            var run = Pending;
            Check(Member<RunSaveState>(run, "state").stats.money == 777, "Geladen wurde der Stand der Sicherung");
            var source = run.GetType().GetField("source");
            Check(source != null && ((string)source.GetValue(run)).EndsWith(".bak", StringComparison.Ordinal), "Der geladene Stand merkt sich seine Datei");
            GameSaveSystem.CancelPendingLoad();
            long before = DateTime.UtcNow.Ticks;
            var opened = save.GetMethod("MarkOpened", Hidden);
            Check(opened != null, "Abschluss des Ladens schreibt die geladene Datei");
            opened.Invoke(null, new[] { run });
            var summary = GameSaveSystem.GetSummary(2);
            Check(summary != null && !summary.recovered && summary.name == "Sicherung" && summary.lastOpenedUtc >= before, "Hauptdatei enthält danach den geladenen Stand mit neuer Öffnungszeit");
            File.Delete(main + ".bak");
            Check(GameSaveSystem.PrepareLoad(2, out error) && Member<RunSaveState>(Pending, "state").stats.money == 777, "Hauptdatei allein ist ladbar: " + error);
            GameSaveSystem.CancelPendingLoad();

            // A-save-core-7: GPS rejects a world larger than a save can hold, before it is generated.
            // A temporary profile takes any write-through, so the shipped profile and the scene stay untouched.
            GpsSettings.UseProfile(temporary, Copy(original));
            var record = GpsSettings.Document.records.Find(item => item.type == typeof(MapGenerator).AssemblyQualifiedName);
            Check(record != null, "GPS-Bereich der Karte gefunden");
            var widthNode = GpsSettings.GetValue(record.key, "mapWidth"); var heightNode = GpsSettings.GetValue(record.key, "mapHeight");
            double tooWide = Math.Floor(2000000 / heightNode.number) + 1, tooDeep = Math.Floor(2000000 / widthNode.number) + 1;
            var large = (tooWide <= 10000 ? widthNode : heightNode).Copy(); large.number = tooWide <= 10000 ? tooWide : tooDeep;
            Check(large.number <= 10000, "Testwert liegt im erlaubten Einzelbereich");
            Check(!GpsSettings.SetValue(record.key, large, out error) && error != null && error.Contains("Karte zu groß"), "Zu grosse Karte wird mit klarer Meldung abgelehnt: " + error);
            Check(GpsSettings.GetValue(record.key, large.name).number != large.number, "Abgelehnter Wert wird nicht übernommen");
            Check(GpsSettings.SetValue(record.key, widthNode.Copy(), out error), "Aktuelle Kartengrösse bleibt gültig: " + error);

            // A-save-integration-3: the visible facing is a property the save reads and restores, not the transform scale.
            var figure = new GameObject("Bughunt facing probe") { hideFlags = HideFlags.HideAndDontSave };
            figure.transform.SetParent(root.transform, false);
            var visual = figure.AddComponent<MinerPlayerVisual>();
            var facing = typeof(MinerPlayerVisual).GetProperty("FacingLeft");
            Check(facing != null && facing.CanRead && facing.CanWrite, "MinerPlayerVisual.FacingLeft ist les- und setzbar");
            Check(!(bool)facing.GetValue(visual), "Neue Figur schaut nach rechts");
            facing.SetValue(visual, true);
            Check((bool)facing.GetValue(visual) && figure.transform.localScale.x > 0, "Blick nach links wird gesetzt, ohne den Transform zu spiegeln");
            facing.SetValue(visual, false);
            Check(!(bool)facing.GetValue(visual), "Blick nach rechts wird gesetzt");

            File.WriteAllText(Report, string.Join("\n", checks) + "\nALL PASSED (" + checks.Count + ")\n");
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            File.WriteAllText(Report, string.Join("\n", checks) + "\nFAIL " + (ex is TargetInvocationException t ? t.InnerException : ex) + "\n");
            throw;
        }
        finally
        {
            GameSaveSystem.CancelPendingLoad();
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Object.DestroyImmediate(temporary);
            if (root) Object.DestroyImmediate(root);
            if (probeTile) Object.DestroyImmediate(probeTile);
            GameSaveSystem.TestDirectory = previousDirectory;
            save.GetProperty("ActiveSlot").GetSetMethod(true).Invoke(null, new object[] { previousSlot });
            save.GetProperty("ActiveRunName").GetSetMethod(true).Invoke(null, new object[] { previousRunName });
            save.GetProperty("HasPendingNewRun").GetSetMethod(true).Invoke(null, new object[] { previousNewRun });
            save.GetProperty("InitialSaveError").GetSetMethod(true).Invoke(null, new object[] { previousError });
            save.GetField("activeDirectory", Hidden).SetValue(null, previousActiveDirectory);
            slotsChanged.SetValue(null, previousSlotsChanged);
            try { Directory.Delete(directory, true); } catch (Exception) { }
        }
    }
}
