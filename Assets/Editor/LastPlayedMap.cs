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
            }
            if (state == PlayModeStateChange.ExitingPlayMode) Capture();
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
    }

    static void StoreInitialSnapshot(MapGenerator map, MapGenerator.MapGenerationSnapshot snapshot)
    {
        if (!Application.isPlaying || !map || snapshot == null || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        initialSnapshots[map] = snapshot;
    }

    static void CaptureInitial(MapGenerator map)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        if (!scheduledInitialCaptures.Add(map)) return;
        EditorApplication.delayCall += () => CaptureInitialNow(map);
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

    public static void Capture()
    {
        var maps = UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None);
        foreach (var map in maps)
            if (map && map.IsGenerationStreaming) map.CompleteStreamingGeneration();
        foreach (var map in new List<MapGenerator>(scheduledInitialCaptures))
            CaptureInitialNow(map);
        SessionState.EraseString(Pending);
        if (!GameplayTestSettings.KeepMapInEditor)
        {
            SessionState.SetString(Pending, SessionState.GetString(Initial, ""));
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
            writer.Write(2);
            writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(map).ToString());
            writer.Write(map.ActiveSeed); writer.Write(map.GeneratedWidth); writer.Write(map.GeneratedHeight);
            WriteLayer(writer, map.GetComponent<Tilemap>());
            WriteLayer(writer, map.EnsureOreOverlay());
        }
    }

    static void Write(MapGenerator map, MapGenerator.MapGenerationSnapshot snapshot, string path)
    {
        using (var stream = new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(map).ToString());
            writer.Write(snapshot.seed); writer.Write(snapshot.width); writer.Write(snapshot.height);
            var bounds = new BoundsInt(snapshot.offsetX, 1 - snapshot.height, 0, snapshot.width, snapshot.height, 1);
            WriteGeneratedLayer(writer, bounds, snapshot.terrainTiles, snapshot.seed, false);
            WriteGeneratedLayer(writer, bounds, snapshot.oreTiles, snapshot.seed, true);
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
                int x = i % bounds.size.x;
                int depth = bounds.size.y - 1 - i / bounds.size.x;
                int turns = (int)(OreVeins.Hash(seed, x, depth, 0x9abcu) % 4);
                flags = TileFlags.None;
                color = Color.white;
                matrix = Matrix4x4.Rotate(Quaternion.Euler(0, 0, turns * 90)) * ore.transform;
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
            if(version != 1 && version != 2) throw new InvalidDataException("Unbekanntes Mapformat.");
            GlobalObjectId.TryParse(reader.ReadString(), out var mapId);
            var map = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(mapId) as MapGenerator;
            if (!map) throw new InvalidOperationException("Map-Szene ist nicht geladen.");
            int seed=reader.ReadInt32(), width=reader.ReadInt32(), height=reader.ReadInt32();
            ReadLayer(reader, map.GetComponent<Tilemap>());
            var overlay = map.EnsureOreOverlay();
            if (version >= 2) ReadLayer(reader, overlay);
            else overlay.ClearAllTiles();
            map.RestorePreviewMetadata(seed,width,height);
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
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
                tilemap.SetTileFlags(cell,flags[i]);
            }
            tilemap.CompressBounds();
            EditorUtility.SetDirty(tilemap);
    }
}
