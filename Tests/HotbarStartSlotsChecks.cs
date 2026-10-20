using System;
using System.Linq;
using UnityEngine;

// Requires a fresh Play start from MainMenu into a new game.
// A new run fills hotbar slots 1, 2, 3 … with the starting items in their configured order, without gaps,
// even if an earlier run left another layout (e.g. dynamite in slot 2) in PlayerPrefs.
public static class HotbarStartSlotsChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object Main()
    {
        if (!Application.isPlaying) return "Requires Play Mode (fresh Play start, new game).";
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var stats = StatsManager.Instance;
        var inventory = InventoryManager.Instance;
        Check(hud && stats && inventory, "HUD, StatsManager and InventoryManager are required.");
        int oldVersion = PlayerPrefs.GetInt("CompactHud.SlotLayoutVersion", 0);
        int[] oldLayout = new int[8];
        for (int i = 0; i < oldLayout.Length; i++) oldLayout[i] = PlayerPrefs.GetInt("CompactHud.Slot." + i, int.MinValue);
        try
        {
            var dynamite = StartingResourcesSettings.Resolve((int)Item.Dynamite);
            var torch = StartingResourcesSettings.Resolve((int)Item.Torche);
            var ladder = StartingResourcesSettings.Resolve((int)Item.Ladder);
            // Stale layout from an earlier run: torch, dynamite (not owned now), ladder.
            hud.AssignSlot(1, torch); hud.AssignSlot(2, dynamite); hud.AssignSlot(3, ladder);
            stats.ResetRun();

            var expected = StartingResourcesSettings.Load().items
                .Where(r => r.amount > 0).Select(r => StartingResourcesSettings.Resolve(r.itemId))
                .Where(CompactHud.IsHotbarItem).Select(item => item.item).Distinct().Take(8).ToList();
            var actual = hud.slots.TakeWhile(slot => slot).Select(slot => slot.item).ToList();
            string layout = string.Join(", ", hud.slots.Select(s => s ? s.item.ToString() : "-"));
            Check(hud.slots.Skip(actual.Count).All(slot => !slot), "Gap in hotbar: " + layout);
            Check(actual.Take(expected.Count).SequenceEqual(expected),
                "Starting items not in order. Expected " + string.Join(", ", expected) + ", got " + layout);
            Check(hud.slots.Where(slot => slot).All(slot => inventory.GetCount(slot) > 0), "Hotbar holds an item not owned: " + layout);
            for (int i = 0; i < 8; i++)
                Check(PlayerPrefs.GetInt("CompactHud.Slot." + i) == (hud.slots[i] ? (int)hud.slots[i].item : int.MinValue),
                    "Compacted layout is saved.");
            return "PASS: new run hotbar = " + layout;
        }
        finally
        {
            PlayerPrefs.SetInt("CompactHud.SlotLayoutVersion", oldVersion);
            for (int i = 0; i < oldLayout.Length; i++) PlayerPrefs.SetInt("CompactHud.Slot." + i, oldLayout[i]);
            PlayerPrefs.Save();
        }
    }
}
