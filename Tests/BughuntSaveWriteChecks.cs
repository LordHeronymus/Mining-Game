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

// Bughunt save writing (Edit Mode, isolated save directory): removed ladders no longer enlarge the saved ladder layer and
// ladders that would stretch it beyond a save are refused (R3-save-write-0), a failed autosave shows a longer notice while
// one that gave up stays quiet (R3-save-write-1), and a load from the recovery copy keeps the name and creation date of
// the renamed main file (R3-save-write-2).
public static class BughuntSaveWriteChecks
{
    const string Report = "Temp/BughuntSaveWriteChecks.txt";
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    [Serializable] sealed class Generation { public GpsValue[] generationSettings; }
    static int width, height;
    static string generationJson;

    // A run in the current file format: state JSON, then terrain, ore, artifact and ladder layers with empty cells.
    static byte[] Payload(int money, BoundsInt terrain)
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
            foreach (var bounds in new[] { terrain, empty, empty, empty })
            {
                writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.size.x); writer.Write(bounds.size.y);
                writer.Write(0);
                for (int i = 0; i < bounds.size.x * bounds.size.y; i++) writer.Write(-1);
            }
        }
        return output.ToArray();
    }
    static void Write(int slot, string name, long created, byte[] payload)
    {
        var summary = new GameSaveSystem.Summary { slot = slot, name = name, savedUtc = DateTime.UtcNow.Ticks, createdUtc = created };
        typeof(GameSaveSystem).GetMethod("WriteEnvelopeAtomic", Hidden).Invoke(null,
            new object[] { GameSaveSystem.SlotPath(slot), JsonUtility.ToJson(summary), payload, false });
    }
    static object Pending => typeof(GameSaveSystem).GetField("pending", Hidden).GetValue(null);
    static T Member<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    static long Area(BoundsInt bounds) => (long)bounds.size.x * bounds.size.y;

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
        string directory = Path.GetFullPath("Temp/BughuntSaveWrite-" + DateTime.UtcNow.Ticks);
        GameObject root = null; Tile probeTile = null;
        try
        {
            Directory.CreateDirectory(directory); GameSaveSystem.TestDirectory = directory; slotsChanged.SetValue(null, null);
            var map = Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
            Check(map, "MapGenerator in der Szene gefunden");
            var generation = GpsSettings.CaptureGeneration(map);
            width = (int)generation.First(field => field.name == "mapWidth").number; height = (int)generation.First(field => field.name == "mapHeight").number;
            string wrapped = JsonUtility.ToJson(new Generation { generationSettings = generation });
            generationJson = wrapped.Substring(1, wrapped.Length - 2);
            long maxCells = Convert.ToInt64(save.GetField("MaxCells", Hidden).GetValue(null));

            // R3-save-write-0: ladders placed far apart and removed again leave Unity's bounds grown.
            root = new GameObject("Bughunt save write grid", typeof(Grid)) { hideFlags = HideFlags.HideAndDontSave };
            var child = new GameObject("Ladders", typeof(Tilemap)) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(root.transform, false);
            var tilemap = child.GetComponent<Tilemap>();
            var ladderTile = Object.FindFirstObjectByType<LadderMap>(FindObjectsInactive.Include)?.segment;
            probeTile = ScriptableObject.CreateInstance<Tile>(); probeTile.hideFlags = HideFlags.HideAndDontSave;
            TileBase tile = ladderTile ? ladderTile : probeTile;
            var left = new Vector3Int(-6000, 1 - height, 0); var right = new Vector3Int(6000, 1000, 0);
            tilemap.SetTile(left, tile); tilemap.SetTile(right, tile);
            tilemap.SetTile(new Vector3Int(0, 1 - height, 0), tile); tilemap.SetTile(new Vector3Int(0, 30, 0), tile);
            tilemap.SetTile(left, null); tilemap.SetTile(right, null);
            Check(Area(tilemap.cellBounds) > maxCells, "Ausgangslage: entfernte Leitern lassen die Tilemap-Grenzen gross (" + tilemap.cellBounds.size + ")");
            var layerType = save.GetNestedType("LayerSnapshot", BindingFlags.NonPublic);
            var compacted = layerType.GetMethod("Compacted", BindingFlags.Public | BindingFlags.Static);
            Check(compacted != null, "Leiterebene wird vor dem Erfassen verdichtet (LayerSnapshot.Compacted)");
            object layer;
            try { layer = compacted.Invoke(null, new object[] { tilemap }); }
            catch (TargetInvocationException failure) { throw new Exception("Verdichtete Leiterebene scheitert: " + failure.InnerException.Message); }
            var bounds = (BoundsInt)layerType.GetField("bounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(layer);
            Check(bounds.xMin == 0 && bounds.size.x == 1 && bounds.yMin == 1 - height && bounds.yMax == 31,
                "Erfasste Leiterebene umfasst nur die stehenden Leitern (" + bounds + ")");
            var finish = layerType.GetMethod("Finish", new[] { typeof(SaveAssetCatalog), typeof(int), typeof(int), typeof(bool) });
            var catalog = Resources.Load<SaveAssetCatalog>("SaveAssetCatalog");
            Check(catalog && finish != null, "Spielstand-Katalog und Abschluss der Erfassung gefunden");
            string result = null;
            try { finish.Invoke(layer, new object[] { catalog, width, height, true }); }
            catch (TargetInvocationException failure) { result = failure.InnerException.Message; }
            Check(result == null || result.Contains("Katalog"), "Verdichtete Leiterebene besteht die Speichergrenzen (" + result + ")");

            // R3-save-write-0: a ladder that would stretch the saved block beyond MaxCells is refused.
            var fits = typeof(LadderMap).GetMethod("FitsSave", Hidden);
            Check(fits != null, "LadderMap prüft die Hüllfläche beim Setzen (FitsSave)");
            bool Fits(BoundsInt b, Vector3Int cell) => (bool)fits.Invoke(null, new object[] { b, cell });
            var one = new BoundsInt(0, 0, 0, 1, 1, 1);
            Check(Fits(new BoundsInt(0, 0, 0, 0, 0, 1), new Vector3Int(5000, -9000, 0)), "Erste Leiter passt immer");
            Check(Fits(one, new Vector3Int(1999, 999, 0)) && 2000L * 1000 <= maxCells, "Hülle genau an der Grenze ist erlaubt");
            Check(!Fits(one, new Vector3Int(1999, 1000, 0)) && 2000L * 1001 > maxCells, "Hülle über der Grenze wird abgelehnt");
            Check(Fits(new BoundsInt(-300, -1199, 0, 600, 1231, 1), new Vector3Int(299, 40, 0)), "Standardkarte mit Himmelsleitern bleibt frei");

            // R3-save-write-1: a real failure shows a longer notice, a skipped autosave stays quiet.
            var failedField = save.GetField("SaveFailedMessage", BindingFlags.Public | BindingFlags.Static);
            Check(failedField != null, "Fehlschlag des Speicherns hat eine eigene Meldung");
            string failed = (string)failedField.GetValue(null);
            var autosaveObject = new GameObject("Bughunt autosave probe") { hideFlags = HideFlags.HideAndDontSave };
            autosaveObject.transform.SetParent(root.transform, false);
            var autosave = autosaveObject.AddComponent<RunAutosave>();
            var report = typeof(RunAutosave).GetMethod("Report", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(report != null, "Autosave wertet Erfolg und Fehlschlag aus");
            var noticeField = typeof(RunAutosave).GetField("notice", BindingFlags.NonPublic | BindingFlags.Instance);
            var untilField = typeof(RunAutosave).GetField("noticeUntil", BindingFlags.NonPublic | BindingFlags.Instance);
            var label = new GameObject("Bughunt autosave notice") { hideFlags = HideFlags.HideAndDontSave };
            label.transform.SetParent(root.transform, false);
            var notice = label.AddComponent(noticeField.FieldType);
            noticeField.SetValue(autosave, notice);
            var text = noticeField.FieldType.GetProperty("text");
            text.SetValue(notice, "Automatisch gespeichert");
            report.Invoke(autosave, new object[] { false, "Speichern ist gerade nicht möglich." });
            Check((float)untilField.GetValue(autosave) <= 0 && (string)text.GetValue(notice) == "Automatisch gespeichert", "Übersprungener Autosave bleibt still");
            report.Invoke(autosave, new object[] { false, failed });
            float failedFor = (float)untilField.GetValue(autosave) - Time.unscaledTime;
            Check(failedFor > 4 && (string)text.GetValue(notice) == "Automatisches Speichern fehlgeschlagen",
                "Fehlgeschlagener Autosave zeigt einen länger sichtbaren Hinweis (" + failedFor + " s)");
            report.Invoke(autosave, new object[] { true, "Gespeichert" });
            float savedFor = (float)untilField.GetValue(autosave) - Time.unscaledTime;
            Check(savedFor > 0 && savedFor < failedFor && (string)text.GetValue(notice) == "Automatisch gespeichert",
                "Nächster Erfolg zeigt wieder den kurzen Hinweis");

            // R3-save-write-2: renamed main file with an unloadable payload, loadable recovery copy.
            long createdBackup = new DateTime(2021, 1, 1).Ticks, createdMain = new DateTime(2020, 1, 1).Ticks;
            Write(4, "Sicherung", createdBackup, Payload(777, new BoundsInt(0, 0, 0, 0, 0, 1)));
            Write(4, "Alt", createdMain, Payload(5, new BoundsInt(0, 6, 0, 1, 1, 1)));
            string main = GameSaveSystem.SlotPath(4);
            Check(File.Exists(main + ".bak"), "Hauptdatei mit unladbarem Inhalt und ladbare Sicherung");
            Check(GameSaveSystem.RenameSlot(4, "Neu", out string error) && GameSaveSystem.GetSummary(4)?.name == "Neu", "Umbenennen gelingt: " + error);
            Check(GameSaveSystem.PrepareLoad(4, out error) && Member<RunSaveState>(Pending, "state").stats.money == 777, "Laden fällt auf die Sicherung zurück: " + error);
            Check(GameSaveSystem.ActiveRunName == "Neu", "Geladener Lauf trägt den neuen Namen (" + GameSaveSystem.ActiveRunName + ")");
            var run = Pending; GameSaveSystem.CancelPendingLoad();
            save.GetMethod("MarkOpened", Hidden).Invoke(null, new[] { run });
            var summary = GameSaveSystem.GetSummary(4);
            Check(summary != null && !summary.recovered && summary.name == "Neu" && summary.createdUtc == createdMain,
                "Nach dem Öffnen behält die Hauptdatei Name und Erstellungsdatum (" + summary?.name + ")");
            Check(GameSaveSystem.PrepareLoad(4, out error) && Member<RunSaveState>(Pending, "state").stats.money == 777 && GameSaveSystem.ActiveRunName == "Neu",
                "Hauptdatei enthält danach den geladenen Stand unter dem neuen Namen: " + error);
            GameSaveSystem.CancelPendingLoad();

            // Without a valid main header, the recovery copy's own name stays.
            Write(5, "Sicherung", createdBackup, Payload(777, new BoundsInt(0, 0, 0, 0, 0, 1)));
            Write(5, "Alt", createdMain, Payload(5, new BoundsInt(0, 6, 0, 1, 1, 1)));
            File.WriteAllBytes(GameSaveSystem.SlotPath(5), new byte[64]);
            Check(GameSaveSystem.PrepareLoad(5, out error) && GameSaveSystem.ActiveRunName == "Sicherung", "Beschädigte Hauptdatei: Name der Sicherung bleibt: " + error);
            run = Pending; GameSaveSystem.CancelPendingLoad();
            save.GetMethod("MarkOpened", Hidden).Invoke(null, new[] { run });
            summary = GameSaveSystem.GetSummary(5);
            Check(summary != null && !summary.recovered && summary.name == "Sicherung" && summary.createdUtc == createdBackup,
                "Beschädigte Hauptdatei: Öffnen schreibt die Kopfdaten der Sicherung");

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
