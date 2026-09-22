using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class LadderPersistenceChecks
{
    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var ladders = UnityEngine.Object.FindFirstObjectByType<LadderMap>();
        var map = ladders.Map;
        var cell = new Vector3Int(0, 10, 0);
        var old = ladders.Tiles.GetTile(cell);
        string path = "Library/LastPlayedMaps/LadderPersistenceCheck.bin";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            ladders.Tiles.SetTile(cell, ladders.segment);
            typeof(LastPlayedMap).GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(MapGenerator), typeof(string) }, null).Invoke(null, new object[] { map, path });
            ladders.Tiles.SetTile(cell, null);
            typeof(LastPlayedMap).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { path });
            if (ladders.Tiles.GetTile(cell) != ladders.segment) throw new Exception("Ladder layer lost during map restore");
            if (ladders.Tiles.GetTransformMatrix(cell) != Matrix4x4.identity) throw new Exception("Ladder transform changed");
            return "PASS: version 3 map snapshot restores ladder asset and one-cell transform.";
        }
        finally
        {
            ladders.Tiles.SetTile(cell, old);
            ladders.Tiles.CompressBounds();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
