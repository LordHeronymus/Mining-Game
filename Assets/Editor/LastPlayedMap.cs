using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Only the terrain is carried back into Edit mode. Generation settings stay intact.
[InitializeOnLoad]
public static class LastPlayedMap
{
    const string Pending = "MiningGame.LastPlayedMaps";
    const string Initial = "MiningGame.InitialPlayedMaps";
    const string Preview = "MiningGame.PreviewBeforePlay";
    static readonly HashSet<MapGenerator> scheduledInitialCaptures = new();
    static readonly Dictionary<MapGenerator, MapGenerator.MapGenerationSnapshot> initialSnapshots = new();
    static LastPlayedMap()
    {
        MapGenerator.InitialMapPrepared += StoreInitialSnapshot;
        MapGenerator.InitialMapGenerated += CaptureInitial;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                SessionState.EraseString(Initial);
                SessionState.EraseString(Pending);
                scheduledInitialCaptures.Clear();
                initialSnapshots.Clear();
                StripPreviewForPlay();
            }
            if (state == PlayModeStateChange.ExitingPlayMode) Capture();
            if (state == PlayModeStateChange.EnteredEditMode) { ForgetDestroyedMaps(); Restore(); }
        };
    }

    static void StoreInitialSnapshot(MapGenerator map, MapGenerator.MapGenerationSnapshot snapshot)
    {
        if (!Application.isPlaying || !map || snapshot == null || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        ForgetDestroyedMaps();
        initialSnapshots[map] = snapshot;
    }

    // Every new world reloads the scene, so earlier maps are destroyed. CaptureInitialNow never writes their
    // snapshot; drop it instead of holding the complete tile arrays until the next Play start.
    static void ForgetDestroyedMaps()
    {
        scheduledInitialCaptures.RemoveWhere(map => !map);
        List<MapGenerator> destroyed = null;
        foreach (var map in initialSnapshots.Keys)
            if (!map) (destroyed ??= new List<MapGenerator>()).Add(map);
        if (destroyed != null) foreach (var map in destroyed) initialSnapshots.Remove(map);
    }

    static void CaptureInitial(MapGenerator map)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        if (!scheduledInitialCaptures.Add(map)) return;
        // Keep the immutable initial snapshot until Play stops. Compressing a
        // complete editor preview during loading blocks the animated screen.
        // Capture() flushes this queue before the runtime scene is removed.
    }

    static void CaptureInitialNow(MapGenerator map)
    {
        scheduledInitialCaptures.Remove(map);
        if (!map || !Application.isPlaying || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        try
        {
            Directory.CreateDirectory("Library/LastPlayedMaps");
            string path = "Library/LastPlayedMaps/" + GlobalObjectId.GetGlobalObjectIdSlow(map) + ".initial.bin";
            var files = new List<string>(SessionState.GetString(Initial, "").Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries));
            if (files.Contains(path)) return;
            if (initialSnapshots.TryGetValue(map, out var snapshot)) Write(map, snapshot, path);
            else Write(map, path);
            initialSnapshots.Remove(map);
            files.Add(path);
            SessionState.SetString(Initial, string.Join("\n", files));
        }
        catch (Exception ex) { Debug.LogError("Ursprüngliche Map konnte nicht gesichert werden: " + ex.Message); }
    }

    // Play Mode loads the gameplay scene as it is open in the editor. A restored preview of the last world would
    // otherwise be integrated by the scene load and then cleared tile by tile behind the loading screen (hundreds of
    // milliseconds and ~90 MB of tile-change data). Keep a copy and strip it; it returns after Play Mode.
    static void StripPreviewForPlay()
    {
        SessionState.EraseString(Preview);
        try
        {
            var files = new List<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var map in root.GetComponentsInChildren<MapGenerator>(true))
                {
                    if (!map.Terrain || map.Terrain.GetUsedTilesCount() == 0) continue;
                    Directory.CreateDirectory("Library/LastPlayedMaps");
                    string path = "Library/LastPlayedMaps/" + GlobalObjectId.GetGlobalObjectIdSlow(map) + ".preview.bin";
                    Write(map, path);
                    files.Add(path);
                }
                if (files.Count > 0) LoadingSceneData.Strip(scene);
            }
            if (files.Count > 0) SessionState.SetString(Preview, string.Join("\n", files));
        }
        catch (Exception ex) { Debug.LogError("Map-Vorschau konnte vor dem Play-Start nicht ausgelagert werden: " + ex.Message); }
    }

    public static void Capture()
    {
        var maps = UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None);
        foreach (var map in maps)
            if (map && map.IsGenerationStreaming) map.CompleteStreamingGeneration();
        foreach (var map in new List<MapGenerator>(scheduledInitialCaptures))
            CaptureInitialNow(map);
        SessionState.EraseString(Pending);
        // A Play session without a generated world (e.g. only the main menu) keeps the previous preview.
        string fallback = SessionState.GetString(Preview, "");
        SessionState.EraseString(Preview);
        if (!GameplayTestSettings.KeepMapInEditor)
        {
            string initial = SessionState.GetString(Initial, "");
            SessionState.SetString(Pending, string.IsNullOrEmpty(initial) ? fallback : initial);
            return;
        }
        if (!Array.Exists(maps, map => map && map.IsGenerated && !string.IsNullOrEmpty(map.gameObject.scene.path)))
        {
            SessionState.SetString(Pending, fallback);
            return;
        }
        var files = new List<string>();
        Directory.CreateDirectory("Library/LastPlayedMaps");
        foreach (var map in maps)
        {
            if (!map.IsGenerated || string.IsNullOrEmpty(map.gameObject.scene.path)) continue;
            try
            {
                string id = GlobalObjectId.GetGlobalObjectIdSlow(map).ToString();
                string path = "Library/LastPlayedMaps/" + id + ".bin";
                Write(map, path);
                files.Add(path);
            }
            catch (Exception ex) { Debug.LogError("Letzte Map konnte nicht gesichert werden: " + ex.Message); }
        }
        SessionState.SetString(Pending, string.Join("\n", files));
    }

    static void Write(MapGenerator map, string path)
    {
        using (var stream = new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(5);
            writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(map).ToString());
            writer.Write(map.ActiveSeed); writer.Write(map.GeneratedWidth); writer.Write(map.GeneratedHeight);
            WriteLayer(writer, map.GetComponent<Tilemap>());
            WriteLayer(writer, map.EnsureOreOverlay());
            WriteLayer(writer, map.EnsureArtifactOverlay());
            var ladders = map.GetComponent<LadderMap>();
            writer.Write(ladders != null);
            if (ladders) WriteLayer(writer, ladders.EnsureTiles());
            writer.Write(JsonUtility.ToJson(map.AltarChamber ? map.AltarChamber.CaptureState() : default(UltroniumAltarChamber.SaveState)));
        }
    }

    static void Write(MapGenerator map, MapGenerator.MapGenerationSnapshot snapshot, string path)
    {
        using (var stream = new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(5);
            writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(map).ToString());
            writer.Write(snapshot.seed); writer.Write(snapshot.width); writer.Write(snapshot.height);
            var bounds = new BoundsInt(snapshot.offsetX, 1 - snapshot.height, 0, snapshot.width, snapshot.height, 1);
            WriteGeneratedLayer(writer, bounds, snapshot.terrainTiles, snapshot.seed, false);
            WriteGeneratedLayer(writer, bounds, snapshot.oreTiles, snapshot.seed, true);
            WriteGeneratedLayer(writer, bounds, snapshot.artifactTiles, snapshot.seed, false);
            writer.Write(false); // A freshly generated map has no player-built ladders.
            var altarState = map.AltarChamber ? map.AltarChamber.CaptureState() : default;
            altarState.deposited = 0; altarState.victoryShown = false;
            writer.Write(JsonUtility.ToJson(altarState));
        }
    }

    static void WriteLayer(BinaryWriter writer, Tilemap tilemap)
    {
        var bounds = tilemap.cellBounds;
        var tiles = tilemap.GetTilesBlock(bounds);
        var palette = new List<TileBase>();
        var indices = new Dictionary<TileBase,int>();
        foreach (var tile in tiles)
            if (tile && !indices.ContainsKey(tile)) { indices[tile] = palette.Count; palette.Add(tile); }
        {
            writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.zMin);
            writer.Write(bounds.size.x); writer.Write(bounds.size.y); writer.Write(bounds.size.z);
            writer.Write(palette.Count);
            foreach (var tile in palette)
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(tile);
                if (!EditorUtility.IsPersistent(tile)) throw new InvalidOperationException("Ein Tile besitzt kein gespeichertes Asset: " + tile.name);
                writer.Write(id.ToString());
            }
            int i=0;
            foreach (var cell in bounds.allPositionsWithin)
            {
                var tile = tiles[i++];
                writer.Write(tile ? indices[tile] : -1);
                if (!tile) continue;
                var color = tilemap.GetColor(cell);
                var matrix = tilemap.GetTransformMatrix(cell);
                writer.Write((int)tilemap.GetTileFlags(cell));
                bool custom = color != Color.white || matrix != Matrix4x4.identity;
                writer.Write(custom);
                if (!custom) continue;
                writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a);
                for(int n=0;n<16;n++) writer.Write(matrix[n]);
            }
        }
    }

    static void WriteGeneratedLayer(BinaryWriter writer, BoundsInt bounds, TileBase[] tiles, int seed, bool oreOverlay)
    {
        var palette = new List<TileBase>();
        var indices = new Dictionary<TileBase, int>();
        foreach (var tile in tiles)
            if (tile && !indices.ContainsKey(tile)) { indices[tile] = palette.Count; palette.Add(tile); }

        writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.zMin);
        writer.Write(bounds.size.x); writer.Write(bounds.size.y); writer.Write(bounds.size.z);
        writer.Write(palette.Count);
        foreach (var tile in palette)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(tile);
            if (!EditorUtility.IsPersistent(tile)) throw new InvalidOperationException("Ein Tile besitzt kein gespeichertes Asset: " + tile.name);
            writer.Write(id.ToString());
        }

        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            writer.Write(tile ? indices[tile] : -1);
            if (!tile) continue;

            var data = new TileData();
            tile.GetTileData(Vector3Int.zero, null, ref data);
            Color color = data.color;
            Matrix4x4 matrix = data.transform;
            TileFlags flags = data.flags;
            if (oreOverlay && tile is OreTile ore)
            {
                flags = TileFlags.None;
                color = Color.white;
                matrix = ore.transform;
            }
            writer.Write((int)flags);
            bool custom = color != Color.white || matrix != Matrix4x4.identity;
            writer.Write(custom);
            if (!custom) continue;
            writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a);
            for (int n = 0; n < 16; n++) writer.Write(matrix[n]);
        }
    }

    public static void Restore()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string pending = SessionState.GetString(Pending, "");
        if (string.IsNullOrEmpty(pending)) return;
        var failed = new List<string>();
        foreach (string path in pending.Split('\n'))
        {
            try { Read(path); }
            catch (Exception ex) { failed.Add(path); Debug.LogError("Letzte Map konnte nicht wiederhergestellt werden: " + ex.Message); }
        }
        SessionState.SetString(Pending, string.Join("\n", failed));
        SceneView.RepaintAll();
    }

    static void Read(string path)
    {
        using (var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        using (var reader = new BinaryReader(stream))
        {
            int version = reader.ReadInt32();
            if(version < 1 || version > 5) throw new InvalidDataException("Unbekanntes Mapformat.");
            GlobalObjectId.TryParse(reader.ReadString(), out var mapId);
            var map = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(mapId) as MapGenerator;
            if (!map) throw new InvalidOperationException("Map-Szene ist nicht geladen.");
            int seed=reader.ReadInt32(), width=reader.ReadInt32(), height=reader.ReadInt32();
            ReadLayer(reader, map.GetComponent<Tilemap>());
            var overlay = map.EnsureOreOverlay();
            if (version >= 2) ReadLayer(reader, overlay);
            else overlay.ClearAllTiles();
            var artifacts = map.EnsureArtifactOverlay();
            if (version >= 4) ReadLayer(reader, artifacts);
            else artifacts.ClearAllTiles();
            var ladders = map.GetComponent<LadderMap>();
            if (version >= 3 && reader.ReadBoolean())
            {
                if (!ladders) ladders = map.gameObject.AddComponent<LadderMap>();
                ReadLayer(reader, ladders.EnsureTiles());
            }
            else if (ladders) ladders.Clear();
            var altar = map.AltarChamber;
            if (!altar) altar = map.gameObject.AddComponent<UltroniumAltarChamber>();
            if (version >= 5) altar.RestoreState(JsonUtility.FromJson<UltroniumAltarChamber.SaveState>(reader.ReadString()));
            else altar.RestoreState(default);
            map.RestorePreviewMetadata(seed,width,height);
            EditorUtility.SetDirty(map);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
    }

    static void ReadLayer(BinaryReader reader, Tilemap tilemap)
    {
            var bounds = new BoundsInt(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
            var palette=new TileBase[reader.ReadInt32()];
            for(int i=0;i<palette.Length;i++)
            {
                GlobalObjectId.TryParse(reader.ReadString(),out var id);
                palette[i]=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as TileBase;
                if(!palette[i]) throw new InvalidDataException("Tile-Asset fehlt.");
            }
            int count=checked(bounds.size.x*bounds.size.y*bounds.size.z);
            var tiles=new TileBase[count];var flags=new TileFlags[count];
            var custom=new Dictionary<int,(Color color,Matrix4x4 matrix)>();
            for(int i=0;i<count;i++)
            {
                int index=reader.ReadInt32(); if(index<0)continue;
                tiles[i]=palette[index];flags[i]=(TileFlags)reader.ReadInt32();
                if(!reader.ReadBoolean())continue;
                var color=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var matrix=new Matrix4x4();for(int n=0;n<16;n++)matrix[n]=reader.ReadSingle();
                custom[i]=(color,matrix);
            }
            tilemap.ClearAllTiles();tilemap.SetTilesBlock(bounds,tiles);
            int cellIndex=0;
            foreach(var cell in bounds.allPositionsWithin)
            {
                int i=cellIndex++;if(!tiles[i])continue;
                tilemap.SetTileFlags(cell,TileFlags.None);
                if(custom.TryGetValue(i,out var value)) {tilemap.SetColor(cell,value.color);tilemap.SetTransformMatrix(cell,value.matrix);}
                // Keep ore overlays at their authored scale and orientation after loading.
                if(tiles[i] is OreTile ore)tilemap.SetTransformMatrix(cell,ore.transform);
                tilemap.SetTileFlags(cell,flags[i]);
            }
            tilemap.CompressBounds();
            EditorUtility.SetDirty(tilemap);
    }
}
