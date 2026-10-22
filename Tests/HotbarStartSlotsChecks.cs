using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

// Requires a fresh Play start on the free MainMenu. Uses isolated save/profile directories.
// A new game fills hotbar slots 1, 2, 3 … with the starting items in their configured order, without gaps,
// even if an earlier run left another layout (torch, dynamite, ladder) in PlayerPrefs.
// Report: Temp/HotbarStartSlotsChecks.txt
public static class HotbarStartSlotsChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play start on the free MainMenu.";
        var go = new GameObject("Hotbar Start Slots Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<HotbarStartSlotsProbe>();
        return "Started, see Temp/HotbarStartSlotsChecks.txt";
    }
}

public sealed class HotbarStartSlotsProbe : MonoBehaviour
{
    const string Report = "Temp/HotbarStartSlotsChecks.txt";
    string save, meta;
    int oldVersion;
    readonly int[] oldLayout = new int[8];
    bool clean;
    int checks;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/HotbarStartSlots-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        oldVersion = PlayerPrefs.GetInt("CompactHud.SlotLayoutVersion", 0);
        for (int i = 0; i < oldLayout.Length; i++) oldLayout[i] = PlayerPrefs.GetInt("CompactHud.Slot." + i, int.MinValue);
        File.WriteAllText(Report, "");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { File.AppendAllText(Report, "FAILED " + error + "\n"); break; }
            if (!more) break;
            yield return next;
        }
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    IEnumerator Ready()
    {
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        yield return null; yield return null;
    }

    static string Layout(CompactHud hud) => string.Join(", ", hud.slots.Select(s => s ? s.item.ToString() : "-"));

    IEnumerator Run()
    {
        // Stale layout from an earlier run: torch, dynamite (not owned in the new run), ladder.
        PlayerPrefs.SetInt("CompactHud.SlotLayoutVersion", 1);
        int[] stale = { (int)Item.Torche, (int)Item.Dynamite, (int)Item.Ladder };
        for (int i = 0; i < 8; i++) PlayerPrefs.SetInt("CompactHud.Slot." + i, i < stale.Length ? stale[i] : int.MinValue);
        PlayerPrefs.Save();

        RunNavigation.NewGame("Hotbar-Test");
        yield return Ready();
        var hud = Object.FindFirstObjectByType<CompactHud>();
        var inventory = InventoryManager.Instance;
        Check(hud && inventory, "HUD and inventory present");
        string layout = Layout(hud);
        File.AppendAllText(Report, "Hotbar after new game: " + layout + "\n");

        var expected = StartingResourcesSettings.Load().items
            .Where(r => r.amount > 0).Select(r => StartingResourcesSettings.Resolve(r.itemId))
            .Where(CompactHud.IsHotbarItem).Select(item => item.item).Distinct().Take(8).ToList();
        var filled = hud.slots.TakeWhile(slot => slot).Select(slot => slot.item).ToList();
        Check(hud.slots.Skip(filled.Count).All(slot => !slot), "No gap in hotbar: " + layout);
        Check(filled.Take(expected.Count).SequenceEqual(expected),
            "Starting items in configured order (" + string.Join(", ", expected) + "): " + layout);
        Check(hud.slots.Where(slot => slot).All(slot => inventory.GetCount(slot) > 0), "Every hotbar item is owned: " + layout);
        if (expected.Count >= 2 && expected[0] == Item.Torche && expected[1] == Item.Ladder)
            Check(filled[0] == Item.Torche && filled[1] == Item.Ladder, "Default start: torch in 1, ladder in 2");
        for (int i = 0; i < 8; i++)
            Check(PlayerPrefs.GetInt("CompactHud.Slot." + i) == (hud.slots[i] ? (int)hud.slots[i].item : int.MinValue),
                "Compacted layout saved, slot " + (i + 1));

        // A saved run keeps its own hotbar on reload.
        bool saved = false;
        yield return GameSaveSystem.Save(GameSaveSystem.ActiveSlot, this, (ok, message) => saved = ok);
        Check(saved, "Run saved");
        Check(RunNavigation.LoadGame(GameSaveSystem.ActiveSlot, out _), "Saved run reload accepted");
        yield return Ready();
        hud = Object.FindFirstObjectByType<CompactHud>();
        Check(Layout(hud) == layout, "Reload keeps hotbar: " + Layout(hud));

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var cam = Camera.main;
        Check(player && cam.GetComponent<CameraFollow>().enabled && cam.GetComponent<CameraFollow>().target == player.transform &&
            cam.GetComponent<CameraWorldBorderClamp>().enabled, "Camera follows player");
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta;
        GameSaveSystem.TestDirectory = save;
        PlayerPrefs.SetInt("CompactHud.SlotLayoutVersion", oldVersion);
        for (int i = 0; i < oldLayout.Length; i++) PlayerPrefs.SetInt("CompactHud.Slot." + i, oldLayout[i]);
        PlayerPrefs.Save();
        Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
