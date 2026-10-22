using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

// Edit Mode. After a drag released over another object (no click reaches the source slot), the next press on the
// source slot must not swallow its click: right click in storage (move half), left click on an emptied hotbar slot.
// The click that ends a drag on its own slot stays swallowed. Uses temporary objects only; result in
// Temp/BughuntDragClickChecks.txt.
public static class BughuntDragClickChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    static ItemDragHold Hold(Component input) =>
        (ItemDragHold)input.GetType().GetField("hold", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);

    // A drag that ran and was released: the hold now waits to swallow the click that would follow on its own slot.
    static void Dropped(ItemDragHold hold)
    {
        hold.Down(new Vector2(10, 10));
        hold.Dragging = true;
        if (!hold.Up(new Vector2(400, 10))) throw new Exception("Drag wurde nicht als beendet gemeldet");
    }

    static PointerEventData Press(PointerEventData.InputButton button) =>
        new(EventSystem.current) { button = button, position = new Vector2(10, 10) };

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var go = new GameObject("Bughunt Drag Click Probe") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var storage = go.AddComponent<StorageSlotInput>();
            var hotbar = go.AddComponent<HotbarSlotDrag>();
            var storageHold = Hold(storage);

            Dropped(storageHold);
            Check(storageHold.ConsumeClick(), "Der Klick, der ein Ziehen auf dem eigenen Lager-Slot beendet, wird verschluckt");
            Check(!storageHold.ConsumeClick(), "Danach wird kein weiterer Klick verschluckt");

            Dropped(storageHold);
            storage.OnPointerDown(Press(PointerEventData.InputButton.Right));
            Check(!storageHold.ConsumeClick(), "Lager: Rechtsklick nach Ablegen auf der Gegenseite wirkt (Hälfte verschieben)");

            Dropped(storageHold);
            storage.OnPointerDown(Press(PointerEventData.InputButton.Left));
            Check(!storageHold.ConsumeClick(), "Lager: Linksklick auf einen leer gewordenen Slot wird nicht verschluckt");

            Dropped(Hold(hotbar));
            hotbar.OnPointerDown(Press(PointerEventData.InputButton.Left));
            Check(!hotbar.ConsumeClick(), "Hotbar: Linksklick auf den leer gewordenen Quell-Slot wählt ihn wieder aus");

            Dropped(Hold(hotbar));
            Check(hotbar.ConsumeClick(), "Hotbar: Klick, der ein Ziehen auf dem eigenen Slot beendet, bleibt verschluckt");

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntDragClickChecks.txt", report + "\n");
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntDragClickChecks.txt", string.Join("\n", checks) + "\nFAIL " + ex + "\n"); throw; }
        finally { Object.DestroyImmediate(go); }
    }
}
