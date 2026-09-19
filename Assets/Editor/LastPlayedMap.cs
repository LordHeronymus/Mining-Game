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
    static LastPlayedMap()
    {
        MapGenerator.InitialMapGenerated += CaptureInitial;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                SessionState.EraseString(Initial);
                SessionState.EraseString(Pending);
            }
            if (state == PlayModeStateChange.ExitingPlayMode) Capture();
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
    }

    static void CaptureInitial(MapGenerator map)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(map.gameObject.scene.path)) return;
        try
        {
            Directory.CreateDirectory("Library/LastPlayedMaps");
            string path = "Library/LastPlayedMaps/" + GlobalObjectId.GetGlobalObjectIdSlow(map) + ".initial.bin";
            var files = new List<string>(SessionState.GetString(Initial, "").Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries));
            if (files.Contains(path)) return;
            Write(map, path);
            files.Add(path);
            SessionState.SetString(Initial, string.Join("\n", files));
        }
        catch (Exception ex) { Debug.LogError("Ursprüngliche Map konnte nicht gesichert werden: " + ex.Message); }
    }

    public static void Capture()
    {
        SessionState.EraseString(Pending);
        if (!GameplayTestSettings.KeepMapInEditor)
        {
            SessionState.SetString(Pending, SessionState.GetString(Initial, ""));
            return;
        }
        var files = new List<string>();
        Directory.CreateDirectory("Library/LastPlayedMaps");
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
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
        var tilemap = map.GetComponent<Tilemap>();
        var bounds = tilemap.cellBounds;
        var tiles = tilemap.GetTilesBlock(bounds);
        var palette = new List<TileBase>();
        var indices = new Dictionary<TileBase,int>();
        foreach (var tile in tiles)
            if (tile && !indices.ContainsKey(tile)) { indices[tile] = palette.Count; palette.Add(tile); }
        using (var stream = new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(1);
            writer.Write(GlobalObjectId.GetGlobalObjectIdSlow(map).ToString());
            writer.Write(map.ActiveSeed); writer.Write(map.GeneratedWidth); writer.Write(map.GeneratedHeight);
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
            if(reader.ReadInt32()!=1) throw new InvalidDataException("Unbekanntes Mapformat.");
            GlobalObjectId.TryParse(reader.ReadString(), out var mapId);
            var map = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(mapId) as MapGenerator;
            if (!map) throw new InvalidOperationException("Map-Szene ist nicht geladen.");
            int seed=reader.ReadInt32(), width=reader.ReadInt32(), height=reader.ReadInt32();
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
            var tilemap=map.GetComponent<Tilemap>();
            tilemap.ClearAllTiles();tilemap.SetTilesBlock(bounds,tiles);
            int cellIndex=0;
            foreach(var cell in bounds.allPositionsWithin)
            {
                int i=cellIndex++;if(!tiles[i])continue;
                tilemap.SetTileFlags(cell,TileFlags.None);
                if(custom.TryGetValue(i,out var value)) {tilemap.SetColor(cell,value.color);tilemap.SetTransformMatrix(cell,value.matrix);}
                tilemap.SetTileFlags(cell,flags[i]);
            }
            tilemap.CompressBounds();map.RestorePreviewMetadata(seed,width,height);
            EditorUtility.SetDirty(tilemap);EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
    }
}
