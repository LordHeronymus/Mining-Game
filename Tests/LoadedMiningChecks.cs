using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LoadedMiningChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play required");
        var root = new GameObject("Loaded mining checks");
        Object.DontDestroyOnLoad(root);
        root.AddComponent<LoadedMiningProbe>();
        return "Started; report Temp/LoadedMiningChecks.txt";
    }
}

public sealed class LoadedMiningProbe : MonoBehaviour
{
    const string Report = "Temp/LoadedMiningChecks.txt";
    string oldDirectory;
    bool oldBackground;
    readonly List<string> passed = new();
    void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        passed.Add(label); File.WriteAllLines(Report, passed);
    }
    IEnumerator Start()
    {
        oldDirectory = GameSaveSystem.TestDirectory;
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/MiningReloadRepro");
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more = false; object current = null; Exception failure = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception e) { failure = e; }
            if (failure != null) { File.AppendAllText(Report, "\nFAIL: " + failure); Cleanup(); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "\nPASS"); Cleanup();
    }
    IEnumerator Run()
    {
        Check(RunNavigation.LoadGame(5, out string error), "Load existing save copy: " + error);
        float deadline = Time.realtimeSinceStartup + 120;
        while (RunNavigation.IsTransitioning || LoadingProgress.Active)
        {
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Load timeout");
            yield return null;
        }
        yield return null;
        var hud = Object.FindFirstObjectByType<CompactHud>();
        var miner = Object.FindFirstObjectByType<TileMiner>();
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        Check(!GameplayInputBlocker.IsBlocked && Time.timeScale == 1, "Gameplay released after restore");
        Check(hud.SelectedSlot == 0, "Saved empty selection falls back to pickaxe");
        var ids = hud.slots.Select(x => x ? (int)x.item : -1).ToArray();
        int empty = Array.FindIndex(hud.slots, x => !x);
        Check(empty >= 0, "Empty hotbar fixture exists");
        Check(hud.SelectSlot(empty + 1) && hud.SelectedSlot == 0, "Selecting empty slot retains usable pickaxe");
        hud.RestoreRunSlots(ids, empty + 1);
        Check(hud.SelectedSlot == 0, "Restoring empty selection uses pickaxe");
        int valid = Array.FindIndex(hud.slots, x => x && InventoryManager.Instance.WasOwnedThisRun(x));
        Check(valid >= 0, "Valid item fixture exists");
        hud.RestoreRunSlots(ids, valid + 1);
        Check(hud.SelectedSlot == valid + 1 && hud.SelectedItem, "Restoring valid item selection is preserved");
        hud.SelectSlot(0);
        var center = map.Terrain.WorldToCell(player.transform.position);
        Vector3Int? target = null;
        for (int y = -2; y <= 2 && target == null; y++)
            for (int x = -2; x <= 2 && target == null; x++)
            {
                var cell = center + new Vector3Int(x, y, 0);
                var block = map.GetBlockAt(cell);
                if (map.Terrain.HasTile(cell) && !map.IsCellProtected(cell) && block &&
                    block.hardnessIndex <= InventoryManager.Instance.EquippedPickaxeMaximumHardness &&
                    Vector2.Distance(map.Terrain.GetCellCenterWorld(cell), player.transform.position) <= StatsManager.Instance.Reach)
                    target = cell;
            }
        Check(target.HasValue, "Reachable mineable restored block exists");
        var hit = typeof(TileMiner).GetMethod("ApplyMiningHit", BindingFlags.Instance | BindingFlags.NonPublic);
        int hits = 0;
        while (map.Terrain.HasTile(target.Value) && hits++ < 100)
            hit.Invoke(miner, new object[] { target.Value, .5f });
        Check(!map.Terrain.HasTile(target.Value), "Actual mining hits remove a restored block");
        var camera = Camera.main;
        Check(camera && camera.GetComponent<CameraFollow>().enabled &&
            camera.GetComponent<CameraFollow>().target == player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,
            "Player camera follow and world clamp remain active");
        RunNavigation.MainMenu();
        while (RunNavigation.IsTransitioning) yield return null;
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == RunNavigation.MenuScene,
            "Returned to homescreen");
    }
    void Cleanup()
    {
        GameSaveSystem.TestDirectory = oldDirectory;
        Application.runInBackground = oldBackground;
        Object.Destroy(gameObject);
    }
}
