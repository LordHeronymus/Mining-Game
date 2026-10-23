using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Esc with a selected ladder only leaves build mode (no pause menu, no settings), and Esc that closes
// the pause menu or a panel no longer also deselects the ladder.
public static class BughuntLadderEscapeChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static int Order(Type type) => type.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var items = new List<ItemSO>();
        ItemSO Make(Item item) { var so = ScriptableObject.CreateInstance<ItemSO>(); so.hideFlags = HideFlags.HideAndDontSave; so.item = item; items.Add(so); return so; }
        try
        {
            var decide = typeof(PlayerLadder).GetMethod("LeavesBuildModeOnEscape", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Check(decide != null, "PlayerLadder.LeavesBuildModeOnEscape vorhanden");
            bool Leaves(bool menuOpen, bool blocked, ItemSO selected) => (bool)decide.Invoke(null, new object[] { menuOpen, blocked, selected });
            ItemSO ladder = Make(Item.Ladder), ironLadder = Make(Item.IronLadder), torch = Make(Item.Torche), dynamite = Make(Item.Dynamite);

            Check(Leaves(false, false, ladder) && Leaves(false, false, ironLadder), "Esc mit gewählter Holz-/Eisenleiter beendet nur den Baumodus");
            Check(!Leaves(false, false, null) && !Leaves(false, false, torch) && !Leaves(false, false, dynamite), "Ohne Leiter öffnet Esc weiterhin das Pausemenü");
            Check(!Leaves(true, false, ladder), "Esc bei offenem Pausemenü schliesst es, ohne die Leiter abzuwählen");
            Check(!Leaves(false, true, ladder), "Esc bei offenem Panel (Werkbank, Inventar, Einstellungen) wählt die Leiter nicht ab");

            // The decision must run before every other Esc reader, so no panel has closed (and unblocked) yet.
            int menu = Order(typeof(RunPauseMenu));
            foreach (var type in new[] { typeof(SettingsPanel), typeof(WorkbenchPanel), typeof(InventoryUI), typeof(ShopPanel),
                         typeof(StoragePanel), typeof(PlayerMapPanel), typeof(PlayerLadder), typeof(CompactHud) })
                Check(menu < Order(type), "RunPauseMenu (" + menu + ") läuft vor " + type.Name + " (" + Order(type) + ")");

            string ladderSource = File.ReadAllText("Assets/GameObjects/Player/PlayerLadder.cs");
            string menuSource = File.ReadAllText("Assets/GameObjects/UI/RunPauseMenu.cs");
            Check(!ladderSource.Contains("GameAction.Settings"), "PlayerLadder wertet Esc nicht mehr selbst aus (kein Abwählen beim Schliessen eines Panels)");
            Check(menuSource.Contains("PlayerLadder.LeavesBuildModeOnEscape(") && menuSource.Contains("GameBindings.Down(GameAction.Settings)"),
                "RunPauseMenu fragt die Leiter für Escape und die Einstellungstaste ab");

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntLadderEscapeChecks.txt", report); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntLadderEscapeChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally { foreach (var item in items) if (item) Object.DestroyImmediate(item); }
    }
}
