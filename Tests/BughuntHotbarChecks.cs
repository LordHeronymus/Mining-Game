using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode. Hotbar fixes: Q/E skip empty slots, an item dragged onto the hotbar moves instead of being copied,
// the HUD stays visible while the inventory fades out, and the inventory's tool tray never covers an
// end-of-run screen. Uses temporary objects only; PlayerPrefs hotbar layout and panel states are restored.
// Result in Temp/BughuntHotbarChecks.txt.
public static class BughuntHotbarChecks
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    const string SlotLayoutVersion = "CompactHud.SlotLayoutVersion", SlotLayoutPrefix = "CompactHud.Slot.";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    static MethodInfo Helper(string name)
    {
        var method = typeof(CompactHud).GetMethod(name, Hidden);
        if (method == null) throw new Exception("CompactHud." + name + " fehlt");
        return method;
    }

    static void SetStatic(Type type, string property, bool value) =>
        type.GetProperty(property, BindingFlags.Public | BindingFlags.Static).GetSetMethod(true).Invoke(null, new object[] { value });

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        bool gameOver = GameOverPanel.IsOpen, victory = GameVictoryPanel.IsOpen, result = ExpeditionResultPanel.IsOpen;
        bool hadVersion = PlayerPrefs.HasKey(SlotLayoutVersion);
        int oldVersion = PlayerPrefs.GetInt(SlotLayoutVersion, 0);
        var hadSlot = new bool[8]; var oldSlot = new int[8];
        for (int i = 0; i < 8; i++) { hadSlot[i] = PlayerPrefs.HasKey(SlotLayoutPrefix + i); oldSlot[i] = PlayerPrefs.GetInt(SlotLayoutPrefix + i, int.MinValue); }
        var go = new GameObject("Bughunt Hotbar Probe") { hideFlags = HideFlags.HideAndDontSave };
        var other = new GameObject("Bughunt Hotbar Other Panel") { hideFlags = HideFlags.HideAndDontSave };
        InventoryUI inventory = null; Behaviour otherPanel = null;
        try
        {
            // Q/E: step over empty slots in both directions, wrap around, end on the pickaxe.
            var adjacent = Helper("AdjacentSlot");
            int Step(int selected, int direction, params int[] usable) =>
                (int)adjacent.Invoke(null, new object[] { selected, direction, (Func<int, bool>)(i => Array.IndexOf(usable, i) >= 0) });
            Check(Step(0, -1, 1, 2) == 2, "Q von der Spitzhacke springt über leere Slots auf den letzten belegten Slot");
            Check(Step(0, 1, 2) == 2 && Step(2, 1, 2) == 0, "E überspringt einen leeren Slot 1 und kehrt danach zur Spitzhacke zurück");
            Check(Step(2, 1, 2, 5) == 5 && Step(5, -1, 2, 5) == 2, "Q/E kommen über Lücken in der Mitte hinweg");
            Check(Step(3, 1) == 0 && Step(3, -1) == 0 && Step(0, 1) == 0, "Leere Hotbar: Q/E landen auf der Spitzhacke");
            Check(Step(8, 1, 1) == 0 && Step(1, -1, 1, 8) == 0, "Spitzhacke bleibt beim Durchschalten erreichbar");

            // Inventory → hotbar: the item moves to the target slot, the selection follows, no duplicate remains.
            var torch = StartingResourcesSettings.Resolve((int)Item.Torche);
            var ladder = StartingResourcesSettings.Resolve((int)Item.Ladder);
            Check(CompactHud.IsHotbarItem(torch) && CompactHud.IsHotbarItem(ladder), "Fackel und Leiter gehören in die Hotbar");
            var hud = go.AddComponent<CompactHud>();
            hud.slots = new ItemSO[8];
            hud.AssignSlot(1, torch); hud.AssignSlot(2, ladder);
            hud.AssignSlot(4, torch);
            Check(!hud.slots[0] && hud.slots[3] == torch && hud.slots[1] == ladder, "Fackel aus dem Inventar auf Slot 4: Slot 1 wird frei, kein Duplikat");
            Check(Array.FindAll(hud.slots, slot => slot && slot.item == Item.Torche).Length == 1, "Fackel liegt genau einmal in der Hotbar");
            Check(PlayerPrefs.GetInt(SlotLayoutPrefix + 0) == int.MinValue && PlayerPrefs.GetInt(SlotLayoutPrefix + 3) == (int)Item.Torche,
                "Verschobene Belegung wird im bisherigen Format gespeichert");
            Check(hud.SelectSlot(4) && hud.SelectedSlot == 4, "Slot 4 auswählbar");
            hud.AssignSlot(7, torch);
            Check(!hud.slots[3] && hud.slots[6] == torch && hud.SelectedSlot == 7, "Auswahl folgt dem verschobenen Item");
            hud.AssignSlot(7, torch);
            Check(hud.slots[6] == torch && hud.SelectedSlot == 7, "Zuweisung auf denselben Slot bleibt unverändert");
            hud.AssignSlot(2, torch);
            Check(hud.slots[1] == torch && !hud.slots[6] && hud.SelectedSlot == 2 &&
                Array.FindAll(hud.slots, slot => slot && slot.item == Item.Ladder).Length == 0, "Überschriebener Slot ersetzt das alte Item wie bisher");

            // HUD visibility: hidden only by panels other than the inventory, also during the inventory's fade-out.
            var hides = Helper("HidesHud");
            bool HudHidden(Behaviour panel, bool open) => (bool)hides.Invoke(null, new object[] { panel, open });
            inventory = other.AddComponent<InventoryUI>();
            otherPanel = other.GetComponent<CanvasGroup>();
            Check(otherPanel && !GameplayInputBlocker.IsBlocked && !GameplayDebugPanel.IsOpen && !GameOverPanel.IsOpen, "Ausgangslage ohne offene Fenster");
            SetStatic(typeof(GameOverPanel), "IsOpen", false);
            Check(!HudHidden(inventory, false), "Ohne Sperre bleibt das HUD sichtbar");
            GameplayInputBlocker.SetBlocked(inventory, true);
            Check(!HudHidden(inventory, false), "Während das Inventar ausblendet (IsOpen schon false, Sperre noch aktiv), bleibt das HUD sichtbar");
            Check(!HudHidden(inventory, true), "Bei offenem Inventar bleibt das HUD sichtbar");
            GameplayInputBlocker.SetBlocked(otherPanel, true);
            Check(HudHidden(inventory, false), "Ein anderes Fenster blendet das HUD weiterhin aus");
            Check(!HudHidden(inventory, true), "Offenes Inventar über einem anderen Fenster zeigt das HUD wie bisher");
            GameplayInputBlocker.SetBlocked(inventory, false);
            Check(HudHidden(null, false) && HudHidden(inventory, false), "Ohne Inventar-Fenster gilt jede Sperre");
            SetStatic(typeof(GameOverPanel), "IsOpen", true);
            Check(!HudHidden(inventory, false), "Game over lässt das HUD sichtbar");
            SetStatic(typeof(GameOverPanel), "IsOpen", false);
            GameplayInputBlocker.SetBlocked(otherPanel, false);

            // Tool tray overlay: never on top of game over, victory or the expedition result.
            var shows = Helper("ShowsInventoryHotbar");
            bool Overlay() => (bool)shows.Invoke(null, new object[] { inventory });
            Check(!Overlay(), "Geschlossenes Inventar zeigt keine Schnellleiste im Overlay");
            typeof(InventoryUI).GetProperty("IsOpen").GetSetMethod(true).Invoke(inventory, new object[] { true });
            Check(Overlay(), "Offenes Inventar hebt die Schnellleiste ins Overlay");
            SetStatic(typeof(ExpeditionResultPanel), "IsOpen", true);
            Check(!Overlay(), "Expeditionsauswertung: Schnellleiste liegt nicht über den Knöpfen");
            SetStatic(typeof(ExpeditionResultPanel), "IsOpen", false);
            SetStatic(typeof(GameVictoryPanel), "IsOpen", true);
            Check(!Overlay(), "Siegbildschirm: Schnellleiste liegt nicht darüber");
            SetStatic(typeof(GameVictoryPanel), "IsOpen", false);
            SetStatic(typeof(GameOverPanel), "IsOpen", true);
            Check(!Overlay(), "Game over: Schnellleiste liegt nicht darüber (wie bisher)");
            SetStatic(typeof(GameOverPanel), "IsOpen", false);

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntHotbarChecks.txt", report + "\n");
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } wrapped) ex = wrapped.InnerException;
            File.WriteAllText("Temp/BughuntHotbarChecks.txt", string.Join("\n", checks) + "\nFAIL " + ex + "\n");
            throw;
        }
        finally
        {
            if (inventory) GameplayInputBlocker.SetBlocked(inventory, false);
            if (otherPanel) GameplayInputBlocker.SetBlocked(otherPanel, false);
            SetStatic(typeof(GameOverPanel), "IsOpen", gameOver);
            SetStatic(typeof(GameVictoryPanel), "IsOpen", victory);
            SetStatic(typeof(ExpeditionResultPanel), "IsOpen", result);
            Object.DestroyImmediate(go); Object.DestroyImmediate(other);
            if (hadVersion) PlayerPrefs.SetInt(SlotLayoutVersion, oldVersion); else PlayerPrefs.DeleteKey(SlotLayoutVersion);
            for (int i = 0; i < 8; i++)
                if (hadSlot[i]) PlayerPrefs.SetInt(SlotLayoutPrefix + i, oldSlot[i]); else PlayerPrefs.DeleteKey(SlotLayoutPrefix + i);
            PlayerPrefs.Save();
        }
    }
}
