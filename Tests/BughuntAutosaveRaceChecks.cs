using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode checks: the autosave captures after Update, while Destroy takes effect only at frame end. A torch taken
// back into the inventory and a dynamite charge that exploded in that frame must not reach the save state.
// Uses temporary hidden objects and puts them into the static active sets itself (OnEnable does not run in Edit Mode).
public static class BughuntAutosaveRaceChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const string Report = "Temp/BughuntAutosaveRaceChecks.txt";
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) { File.AppendAllText(Report, "FAIL " + message + "\n"); throw new Exception(message); }
        checks++; File.AppendAllText(Report, "PASS " + message + "\n");
    }
    static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }
    static HashSet<T> ActiveSet<T>() =>
        (HashSet<T>)typeof(T).GetField("active", PrivateStatic)?.GetValue(null) ?? throw new MissingFieldException(typeof(T).Name, "active");

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks = 0; Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");
        var torches = ActiveSet<PlacedTorch>();
        var charges = ActiveSet<PlacedDynamite>();
        var root = new GameObject("Bughunt autosave race") { hideFlags = HideFlags.HideAndDontSave };
        var item = ScriptableObject.CreateInstance<ItemSO>();
        item.hideFlags = HideFlags.HideAndDontSave;
        item.item = Item.Torche;
        PlacedTorch torch = null;
        PlacedDynamite charge = null;
        try
        {
            var map = root.AddComponent<MapGenerator>();

            var torchObject = new GameObject("Bughunt torch") { hideFlags = HideFlags.HideAndDontSave };
            torchObject.transform.SetParent(root.transform, false);
            torch = torchObject.AddComponent<PlacedTorch>();
            var cell = new Vector3Int(4, -6, 0);
            SetField(torch, "map", map);
            SetField(torch, "cell", cell);
            SetField(torch, "item", item);
            torches.Add(torch);
            Check(PlacedTorch.Capture(map).Any(saved => saved.cell == cell && saved.itemId == (int)Item.Torche) &&
                PlacedTorch.CountTorches(map) == 1, "Platzierte Fackel steht im Spielstand");

            // TryRemoveAt refunds the item and calls Destroy; the GameObject still exists until frame end.
            var unregister = typeof(PlacedTorch).GetMethod("Unregister", PrivateStatic);
            Check(unregister != null, "Fackel-Abbau nimmt die Fackel sofort aus der Laufzeitliste (Unregister vorhanden)");
            unregister.Invoke(null, new object[] { torch });
            Check(torch && PlacedTorch.Capture(map).Length == 0, "Im selben Bild abgebaute Fackel fehlt in placedLights");
            Check(!PlacedTorch.Active.Any(x => x && x.OwnerMap == map && !x.IsLavaLamp),
                "Im selben Bild abgebaute Fackel fehlt in der alten Fackelliste (torches)");
            Check(PlacedTorch.CountTorches(map) == 0, "Abgebaute Fackel zählt nicht mehr als stehende Fackel");

            var chargeObject = new GameObject("Bughunt dynamite") { hideFlags = HideFlags.HideAndDontSave };
            chargeObject.transform.SetParent(root.transform, false);
            chargeObject.transform.position = new Vector3(1.25f, -3.75f, 0f);
            charge = chargeObject.AddComponent<PlacedDynamite>();
            SetField(charge, "map", map);
            SetField(charge, "fuse", 1.2f);
            charges.Add(charge);
            var burning = PlacedDynamite.Capture(map);
            Check(burning.Length == 1 && Mathf.Approximately(burning[0].fuse, 1.2f) &&
                (Vector2)burning[0].position == new Vector2(1.25f, -3.75f), "Brennende Dynamitladung steht mit Restzeit im Spielstand");
            foreach (float spent in new[] { 0f, -.016f })
            {
                // Update: fuse -= deltaTime; fuse <= 0 -> Explode -> Destroy at frame end.
                SetField(charge, "fuse", spent);
                Check(PlacedDynamite.Capture(map).Length == 0, $"Im selben Bild explodierte Ladung (Zündschnur {spent:0.###}) fehlt im Spielstand");
            }
            // With the spent charge the whole capture was rejected ("Ungültige Dynamitladung.") and the autosave failed.
            SetField(charge, "fuse", -.016f);
            string rejected = null;
            try { ExoticWorldContent.ValidateState(new ExoticWorldState { dynamite = PlacedDynamite.Capture(map) }, 60, 120); }
            catch (InvalidDataException error) { rejected = error.Message; }
            Check(rejected == null, "Erfassung nach Explosion besteht die Dynamit-Prüfung des Spielstands" + (rejected == null ? "" : ": " + rejected));
        }
        finally
        {
            if (torch) torches.Remove(torch);
            if (charge) charges.Remove(charge);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(item);
        }
        string result = $"PASS {checks} Autosave-Endbild-Prüfungen";
        File.AppendAllText(Report, result + "\n");
        return result;
    }
}
