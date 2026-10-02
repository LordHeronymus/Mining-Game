using System;
using UnityEngine;

public enum GameAction
{
    MoveLeft, MoveRight, MoveUp, MoveDown, Jump, Inventory, Workbench, Settings,
    Mine, Place, Remove, SmartCursor, QuickLadder, UseMedkit, PreviousSlot, NextSlot,
    Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8,
    SellTen, SellAll, DebugPanel, Map
}

public static class GameBindings
{
    public readonly struct Entry
    {
        public readonly GameAction action;
        public readonly string label;
        public readonly KeyCode first, second;
        public Entry(GameAction action, string label, KeyCode first, KeyCode second = KeyCode.None)
        { this.action = action; this.label = label; this.first = first; this.second = second; }
    }

    public static readonly Entry[] Entries =
    {
        new(GameAction.MoveLeft, "Links bewegen", KeyCode.A, KeyCode.LeftArrow),
        new(GameAction.MoveRight, "Rechts bewegen", KeyCode.D, KeyCode.RightArrow),
        new(GameAction.MoveUp, "Nach oben / Klettern", KeyCode.W, KeyCode.UpArrow),
        new(GameAction.MoveDown, "Nach unten / Klettern", KeyCode.S, KeyCode.DownArrow),
        new(GameAction.Jump, "Springen", KeyCode.Space),
        new(GameAction.Inventory, "Inventar", KeyCode.Tab, KeyCode.I),
        new(GameAction.Workbench, "Werkbank", KeyCode.B),
        new(GameAction.Settings, "Einstellungen", KeyCode.Escape),
        new(GameAction.Mine, "Abbauen", KeyCode.Mouse0),
        new(GameAction.Place, "Platzieren", KeyCode.Mouse0),
        new(GameAction.Remove, "Entfernen", KeyCode.Mouse1),
        new(GameAction.SmartCursor, "Smart-Cursor", KeyCode.LeftControl, KeyCode.Mouse2),
        new(GameAction.QuickLadder, "Leiter wählen", KeyCode.L),
        new(GameAction.UseMedkit, "Medkit verwenden", KeyCode.H),
        new(GameAction.PreviousSlot, "Hotbar zurück", KeyCode.Q),
        new(GameAction.NextSlot, "Hotbar vor", KeyCode.E),
        new(GameAction.Slot1, "Hotbar 1", KeyCode.Alpha1, KeyCode.Keypad1),
        new(GameAction.Slot2, "Hotbar 2", KeyCode.Alpha2, KeyCode.Keypad2),
        new(GameAction.Slot3, "Hotbar 3", KeyCode.Alpha3, KeyCode.Keypad3),
        new(GameAction.Slot4, "Hotbar 4", KeyCode.Alpha4, KeyCode.Keypad4),
        new(GameAction.Slot5, "Hotbar 5", KeyCode.Alpha5, KeyCode.Keypad5),
        new(GameAction.Slot6, "Hotbar 6", KeyCode.Alpha6, KeyCode.Keypad6),
        new(GameAction.Slot7, "Hotbar 7", KeyCode.Alpha7, KeyCode.Keypad7),
        new(GameAction.Slot8, "Hotbar 8", KeyCode.Alpha8, KeyCode.Keypad8),
        new(GameAction.SellTen, "Zehn verkaufen", KeyCode.LeftShift, KeyCode.RightShift),
        new(GameAction.SellAll, "Alles verkaufen", KeyCode.LeftControl, KeyCode.RightControl),
        new(GameAction.DebugPanel, "Debug-Einstellungen", KeyCode.F1),
        new(GameAction.Map, "Karte", KeyCode.M)
    };

    static readonly KeyCode[,] keys = new KeyCode[Entries.Length, 2];
    static bool loaded;
    static string Preference(GameAction action, int slot) => "settings.binding." + action + "." + slot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => loaded = false;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        foreach (var entry in Entries)
        {
            int i = (int)entry.action;
            keys[i, 0] = Read(entry.action, 0, entry.first);
            keys[i, 1] = Read(entry.action, 1, entry.second);
        }
    }

    static KeyCode Read(GameAction action, int slot, KeyCode fallback)
    {
        string value = PlayerPrefs.GetString(Preference(action, slot), fallback.ToString());
        return Enum.TryParse(value, out KeyCode key) && Enum.IsDefined(typeof(KeyCode), key) ? key : fallback;
    }

    public static KeyCode Get(GameAction action, int slot) { Load(); return keys[(int)action, slot]; }

    public static void Set(GameAction action, int slot, KeyCode key)
    {
        Load();
        slot = Mathf.Clamp(slot, 0, 1);
        if (action == GameAction.Settings && key == KeyCode.None && keys[(int)action, 1 - slot] == KeyCode.None)
            return;
        keys[(int)action, slot] = key;
        PlayerPrefs.SetString(Preference(action, slot), key.ToString());
        PlayerPrefs.Save();
    }

    public static void RestoreDefaults()
    {
        foreach (var entry in Entries)
        {
            PlayerPrefs.DeleteKey(Preference(entry.action, 0));
            PlayerPrefs.DeleteKey(Preference(entry.action, 1));
        }
        PlayerPrefs.Save();
        loaded = false;
        Load();
    }

    public static bool Held(GameAction action)
    {
        Load();
        int i = (int)action;
        return (keys[i, 0] != KeyCode.None && Input.GetKey(keys[i, 0])) ||
               (keys[i, 1] != KeyCode.None && Input.GetKey(keys[i, 1]));
    }

    public static bool Down(GameAction action)
    {
        Load();
        int i = (int)action;
        return (keys[i, 0] != KeyCode.None && Input.GetKeyDown(keys[i, 0])) ||
               (keys[i, 1] != KeyCode.None && Input.GetKeyDown(keys[i, 1]));
    }

    public static string Display(KeyCode key) => key switch
    {
        KeyCode.None => "+", KeyCode.Space => "Leertaste", KeyCode.Mouse0 => "Linksklick",
        KeyCode.Mouse1 => "Rechtsklick", KeyCode.Mouse2 => "Mittelklick",
        KeyCode.LeftArrow => "←", KeyCode.RightArrow => "→", KeyCode.UpArrow => "↑", KeyCode.DownArrow => "↓",
        KeyCode.LeftControl => "Strg links", KeyCode.RightControl => "Strg rechts",
        KeyCode.LeftShift => "Shift links", KeyCode.RightShift => "Shift rechts",
        KeyCode.LeftAlt => "Alt links", KeyCode.RightAlt => "Alt rechts",
        KeyCode.Tab => "Tab", KeyCode.Escape => "Esc",
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)key - (int)KeyCode.Alpha0).ToString(),
        >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => "Num " + ((int)key - (int)KeyCode.Keypad0),
        _ => key.ToString()
    };
}
