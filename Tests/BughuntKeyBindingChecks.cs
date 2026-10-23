using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

// Edit Mode. Key binding fixes: Esc can be captured again for "Einstellungen", the settings key does not close
// workbench/storage/shop while a search field is focused (Esc still does), closing the shop releases a selected
// search field, and the hotbar key labels follow the current bindings in a short form (at most three characters). Uses a temporary EventSystem that is never
// enabled (EventSystem.current stays untouched); hotbar binding PlayerPrefs are restored. Result in
// Temp/BughuntKeyBindingChecks.txt.
public static class BughuntKeyBindingChecks
{
    const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static string Key(GameAction action, int slot) => "settings.binding." + action + "." + slot;

    static MethodInfo Method(Type type, string name, BindingFlags flags)
    {
        var method = type.GetMethod(name, flags);
        if (method == null) throw new MissingMethodException(type.Name + "." + name);
        return method;
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var loaded = typeof(GameBindings).GetField("loaded", StaticMembers);
        if (loaded == null) throw new MissingFieldException("GameBindings.loaded");
        var slots = Enumerable.Range(0, 8).Select(i => (GameAction)((int)GameAction.Slot1 + i)).ToArray();
        var keys = slots.SelectMany(action => new[] { Key(action, 0), Key(action, 1) }).ToArray();
        var backup = keys.ToDictionary(key => key, key => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null);
        void Reload() => loaded.SetValue(null, false);
        var probes = new List<GameObject>();
        GameObject Probe(string name, Transform parent = null, bool active = true)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(active);
            if (parent) go.transform.SetParent(parent, false);
            probes.Add(go);
            return go;
        }
        FieldInfo allowInput = null; TMP_InputField field = null;
        try
        {
            // R2-input-1: Esc cancels a capture, except for "Einstellungen", where it is bound instead.
            var escapeCancels = Method(typeof(SettingsPanel), "EscapeCancelsCapture", StaticMembers);
            bool EscapeCancels(GameAction action) => (bool)escapeCancels.Invoke(null, new object[] { action });
            Check(!EscapeCancels(GameAction.Settings), "Einstellungen: Esc wird belegt statt abgebrochen");
            Check(EscapeCancels(GameAction.Jump) && EscapeCancels(GameAction.Slot1) && EscapeCancels(GameAction.Inventory),
                "Andere Aktionen: Esc bricht die Aufnahme weiterhin ab");
            Check(Calls(Method(typeof(SettingsPanel), "CaptureKey", InstanceMembers), escapeCancels),
                "SettingsPanel.CaptureKey nutzt die Esc-Regel");

            // R2-input-3: the settings key closes a window only while no text field is focused; Esc always closes.
            var focused = Method(typeof(GameBindings), "TextFieldFocused", StaticMembers);
            var allowed = Method(typeof(GameBindings), "CloseAllowed", StaticMembers);
            bool Focused(EventSystem system) => (bool)focused.Invoke(null, new object[] { system });
            bool Allowed(bool escape, EventSystem system) => (bool)allowed.Invoke(null, new object[] { escape, system });
            // Never enabled, so it does not register as EventSystem.current; the probes are inactive, so no select handlers run.
            var events = Probe("Bughunt KeyBinding EventSystem", null, false).AddComponent<EventSystem>();
            Check(!Focused(null) && !Focused(events) && Allowed(false, null), "Ohne Auswahl schliesst die Einstellungen-Taste");
            var plain = Probe("Bughunt KeyBinding Button", null, false);
            events.SetSelectedGameObject(plain);
            Check(events.currentSelectedGameObject == plain && !Focused(events) && Allowed(false, events),
                "Ausgewählter Knopf ohne Textfeld blockiert das Schliessen nicht");
            field = Probe("Bughunt KeyBinding Search", null, false).AddComponent<TMP_InputField>();
            allowInput = typeof(TMP_InputField).GetField("m_AllowInput", InstanceMembers);
            Check(allowInput != null, "TMP_InputField.m_AllowInput vorhanden");
            events.SetSelectedGameObject(field.gameObject);
            Check(!field.isFocused && !Focused(events) && Allowed(false, events), "Ausgewähltes, nicht fokussiertes Suchfeld blockiert nicht");
            allowInput.SetValue(field, true);
            Check(field.isFocused && Focused(events), "Fokussiertes Suchfeld wird als Texteingabe erkannt");
            Check(!Allowed(false, events), "Einstellungen-Taste schliesst bei fokussiertem Suchfeld nicht");
            Check(Allowed(true, events), "Esc schliesst auch bei fokussiertem Suchfeld");
            allowInput.SetValue(field, false);
            var closeDown = Method(typeof(GameBindings), "CloseDown", StaticMembers);
            Check(Calls(closeDown, allowed), "GameBindings.CloseDown nutzt die Fokusregel");
            Check(Calls(Method(typeof(WorkbenchPanel), "Update", InstanceMembers), closeDown), "Werkbank schliesst über CloseDown");
            Check(Calls(Method(typeof(StoragePanel), "Update", InstanceMembers), closeDown), "Lager schliesst über CloseDown");
            Check(Calls(Method(typeof(ShopPanel), "Update", InstanceMembers), closeDown), "Shop schliesst über CloseDown");

            // R2-input-2: closing the shop releases a selection below it and keeps any other selection.
            var release = Method(typeof(ShopPanel), "ReleaseSelection", StaticMembers);
            var shopRoot = Probe("Bughunt KeyBinding Shop", null, false).transform;
            var search = Probe("Search", Probe("BuyPage", shopRoot, false).transform, false);
            events.SetSelectedGameObject(search);
            release.Invoke(null, new object[] { events, shopRoot });
            Check(events.currentSelectedGameObject == null, "Shop schliessen hebt die Auswahl des Suchfelds auf");
            events.SetSelectedGameObject(plain);
            release.Invoke(null, new object[] { events, shopRoot });
            Check(events.currentSelectedGameObject == plain, "Auswahl ausserhalb des Shops bleibt erhalten");
            release.Invoke(null, new object[] { null, shopRoot });
            Check(Calls(Method(typeof(ShopPanel), "ShowPanel", InstanceMembers), release), "ShopPanel.ShowPanel gibt die Auswahl frei");
            events.SetSelectedGameObject(null);

            // R2-input-4: hotbar key labels come from the current bindings and refresh after a change.
            var label = Method(typeof(CompactHud), "HotbarKeyLabel", StaticMembers);
            string Label(GameAction action) => (string)label.Invoke(null, new object[] { action });
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            Reload();
            Check(Enumerable.Range(0, 8).All(i => Label(slots[i]) == (i + 1).ToString()), "Standardbelegung zeigt 1 bis 8");
            int version = GameBindings.Version;
            GameBindings.Set(GameAction.Slot1, 0, KeyCode.Z);
            Check(GameBindings.Version != version && Label(GameAction.Slot1) == "Z", "Hotbar 1 auf Z zeigt Z und meldet die Änderung");
            GameBindings.Set(GameAction.Slot2, 0, KeyCode.None);
            Check(Label(GameAction.Slot2) == "N2", "Leere erste Taste zeigt die zweite Taste (kurz)");
            GameBindings.Set(GameAction.Slot2, 1, KeyCode.None);
            Check(Label(GameAction.Slot2) == "", "Ohne Taste bleibt die Beschriftung leer");
            GameBindings.Set(GameAction.Slot3, 0, KeyCode.Alpha4);
            Check(Label(GameAction.Slot3) == "4" && Label(GameAction.Slot4) == "4", "Getauschte Ziffern werden angezeigt");
            // R4-input-0: long key names get a short form that fits the socket label (about three characters).
            var shortKey = Method(typeof(CompactHud), "ShortKey", StaticMembers);
            string Short(KeyCode key) => (string)shortKey.Invoke(null, new object[] { key });
            var tooLong = ((KeyCode[])Enum.GetValues(typeof(KeyCode))).Where(key => key != KeyCode.None && Short(key).Length > 3).ToArray();
            Check(tooLong.Length == 0, "Alle Tastennamen der Hotbar haben höchstens 3 Zeichen" + (tooLong.Length > 0 ? " (" + tooLong[0] + ")" : ""));
            Check(Short(KeyCode.Alpha1) == "1" && Short(KeyCode.Keypad3) == "N3" && Short(KeyCode.LeftShift) == "Sh" &&
                  Short(KeyCode.LeftControl) == "Str" && Short(KeyCode.Mouse3) == "M4" && Short(KeyCode.F12) == "F12" && Short(KeyCode.Z) == "Z",
                "Kurzformen für Ziffern, Num, Shift, Strg, Maus und F-Tasten");
            var distinct = new[] { KeyCode.KeypadPlus, KeyCode.KeypadMinus, KeyCode.KeypadMultiply, KeyCode.KeypadDivide, KeyCode.KeypadPeriod,
                KeyCode.KeypadEnter, KeyCode.KeypadEquals, KeyCode.Numlock, KeyCode.LeftWindows, KeyCode.LeftCommand, KeyCode.LeftBracket,
                KeyCode.RightBracket, KeyCode.BackQuote, KeyCode.Backslash, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Keypad1 };
            Check(distinct.Select(Short).Distinct().Count() == distinct.Length, "Num-Block, Klammern, Bild-Tasten und Systemtasten bleiben unterscheidbar");
            GameBindings.Set(GameAction.Slot5, 0, KeyCode.LeftShift);
            Check(Label(GameAction.Slot5) == "Sh", "Hotbar-Beschriftung nutzt die Kurzform");
            var minSize = typeof(CompactHud).GetField("KeyLabelMinSize", StaticMembers);
            Check(minSize != null && (float)minSize.GetValue(null) <= 12f, "Tastenfeld darf die Schrift für drei Zeichen verkleinern");
            var refresh = Method(typeof(CompactHud), "RefreshKeyLabels", InstanceMembers);
            var versionGetter = typeof(GameBindings).GetProperty("Version", StaticMembers)?.GetGetMethod(true);
            Check(versionGetter != null && Calls(Method(typeof(CompactHud), "Update", InstanceMembers), versionGetter) &&
                  Calls(Method(typeof(CompactHud), "Update", InstanceMembers), refresh), "HUD frischt die Beschriftung nach Änderungen auf");
            Check(Calls(Method(typeof(CompactHud), "Build", InstanceMembers), refresh) && Calls(refresh, label),
                "HUD setzt die Beschriftung beim Aufbau aus der Belegung");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntKeyBindingChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } inner) ex = inner.InnerException;
            File.WriteAllText("Temp/BughuntKeyBindingChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks)); throw ex;
        }
        finally
        {
            if (allowInput != null && field) allowInput.SetValue(field, false);
            foreach (var entry in backup)
                if (entry.Value == null) PlayerPrefs.DeleteKey(entry.Key); else PlayerPrefs.SetString(entry.Key, entry.Value);
            PlayerPrefs.Save();
            Reload();
            for (int i = probes.Count - 1; i >= 0; i--) if (probes[i]) Object.DestroyImmediate(probes[i]);
        }
    }

    // True when the IL of caller calls callee (call/callvirt followed by a method token).
    static bool Calls(MethodInfo caller, MethodBase callee)
    {
        var il = caller.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return false;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F) continue;
            try
            {
                var target = caller.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                if (target != null && target.Module == callee.Module && target.MetadataToken == callee.MetadataToken) return true;
            }
            catch (Exception) { } // operand bytes that are no method token
        }
        return false;
    }
}
