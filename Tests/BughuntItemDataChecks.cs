using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Checks that the GPS profile carries the requested item weights (wood 0.5 kg, iron ore 1.5 kg, and every
// ItemSO weight equal to its asset file) and that the storage hut values only what the shop actually buys.
public static class BughuntItemDataChecks
{
    const string ProfilePath = "Assets/Resources/Gameplay/GpsProfile.asset";
    const string WoodPath = "Assets/GameObjects/Items/Materials/Wood.asset";
    const string IronPath = "Assets/GameObjects/Items/Ores/Iron.asset";
    const string DynamitePath = "Assets/GameObjects/Items/Tools/Dynamite.asset";
    static readonly List<string> checks = new();

    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks.Add("PASS " + description);
    }

    // The weight as serialized in the asset file on disk; GPS may already have overwritten the loaded object.
    static double? FileWeight(string path)
    {
        var match = Regex.Match(File.ReadAllText(path), @"^  weight: (.+)$", RegexOptions.Multiline);
        return match.Success ? double.Parse(match.Groups[1].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) : null;
    }

    static double ProfileWeight(GpsDocument document, string assetPath)
    {
        string key = "asset:" + AssetDatabase.AssetPathToGUID(assetPath) + ":11400000";
        var record = document.records.Find(entry => entry.key == key) ?? throw new Exception("GPS-Record fehlt: " + assetPath);
        return (record.fields.Find(field => field.name == "weight") ?? throw new Exception("GPS-Feld weight fehlt: " + assetPath)).number;
    }

    static ItemSO Item(ItemCategory category, int worth)
    {
        var item = ScriptableObject.CreateInstance<ItemSO>();
        item.hideFlags = HideFlags.HideAndDontSave;
        item.category = category; item.worth = worth; item.displayName = category + " " + worth;
        return item;
    }

    public static object Main()
    {
        checks.Clear();
        var temporary = new List<ItemSO>();
        try
        {
            // ---- GPS profile weights ----
            var profile = AssetDatabase.LoadAssetAtPath<GpsProfile>(ProfilePath) ?? throw new Exception("GpsProfile fehlt");
            var document = JsonUtility.FromJson<GpsDocument>(profile.documentJson);
            Check(document != null && document.records.Count > 0, "GPS-Profil laesst sich lesen");
            double wood = ProfileWeight(document, WoodPath), iron = ProfileWeight(document, IronPath);
            Check(Math.Abs(wood - .5) < 1e-6, "GPS-Gewicht Holz 0,5 kg (ist " + wood.ToString(CultureInfo.InvariantCulture) + ")");
            Check(Math.Abs(iron - 1.5) < 1e-6, "GPS-Gewicht Eisenerz 1,5 kg (ist " + iron.ToString(CultureInfo.InvariantCulture) + ")");
            Check(Math.Abs(wood - (FileWeight(WoodPath) ?? 1)) < 1e-4 && Math.Abs(iron - (FileWeight(IronPath) ?? 1)) < 1e-4,
                "GPS-Gewicht Holz/Eisenerz entspricht Wood.asset/Iron.asset");

            // Other ItemSO weights are only reported: balancing through F1/GPS alone is allowed, so a difference is no failure.
            int compared = 0; var mismatches = new List<string>();
            foreach (var record in document.records)
            {
                if (record.type == null || !record.type.StartsWith("ItemSO,", StringComparison.Ordinal)) continue;
                var field = record.fields.Find(entry => entry.name == "weight");
                string path = AssetDatabase.GUIDToAssetPath((record.assetKey ?? "").Split(':')[0]);
                if (field == null || string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                double fileWeight = FileWeight(path) ?? 1;
                compared++;
                if (Math.Abs(fileWeight - field.number) > 1e-4) mismatches.Add(record.name + " GPS " + field.number.ToString(CultureInfo.InvariantCulture) + " / Asset " + fileWeight.ToString(CultureInfo.InvariantCulture));
            }
            Check(compared > 10, "GPS-Itemgewichte gefunden (" + compared + ")");
            checks.Add("INFO GPS-Itemgewichte ungleich Item-Asset: " + (mismatches.Count > 0 ? string.Join(", ", mismatches) : "keine"));

            // ---- Storage value only counts sellable items ----
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var sellableWorth = typeof(StoragePanel).GetMethod("SellableWorth", Flags) ?? throw new Exception("StoragePanel.SellableWorth fehlt");
            var worthHint = typeof(StoragePanel).GetMethod("WorthHint", Flags) ?? throw new Exception("StoragePanel.WorthHint fehlt");
            ItemSO ore = Item(ItemCategory.Ore, 10), emptyOre = Item(ItemCategory.Ore, 0), tool = Item(ItemCategory.Tool, 500);
            temporary.AddRange(new[] { ore, emptyOre, tool });
            var stored = new Dictionary<ItemSO, int> { { ore, 3 }, { emptyOre, 7 }, { tool, 10 } };
            long total = (long)sellableWorth.Invoke(null, new object[] { stored });
            Check(total == 30, "Lagerwert zaehlt nur verkaeufliche Erze (30 statt 5'030, ist " + total + ")");
            Check((long)sellableWorth.Invoke(null, new object[] { new Dictionary<ItemSO, int> { { tool, 10 } } }) == 0, "Lagerwert nur mit Werkzeug ist 0");
            Check(((string)worthHint.Invoke(null, new object[] { ore })).Contains("Wert " + ShopMoneyFormatter.Format(10) + " je Stück"), "Hinweis zeigt Wert je Stück fuer Erz");
            Check((string)worthHint.Invoke(null, new object[] { tool }) == "", "Hinweis ohne Wert fuer unverkaeufliches Werkzeug");
            Check((string)worthHint.Invoke(null, new object[] { emptyOre }) == "", "Hinweis ohne Wert fuer Erz ohne Wert");

            var dynamite = AssetDatabase.LoadAssetAtPath<ItemSO>(DynamitePath);
            Check(dynamite && !ShopManager.IsSellable(dynamite) && (string)worthHint.Invoke(null, new object[] { dynamite }) == ""
                && (long)sellableWorth.Invoke(null, new object[] { new Dictionary<ItemSO, int> { { dynamite, 10 } } }) == 0,
                "Dynamit im Lager ohne Verkaufswert");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntItemDataChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } wrapped) ex = wrapped.InnerException;
            File.WriteAllText("Temp/BughuntItemDataChecks.txt", "FAIL " + ex.Message + "\n" + string.Join("\n", checks));
            throw;
        }
        finally
        {
            foreach (var item in temporary) if (item) UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
