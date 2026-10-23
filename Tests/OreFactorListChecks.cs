using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Edit-mode checks for the ore pickers in F1/GPS (GpsRuntimePanel.Ores, "Faktor je Erz"):
// only ores, gems and Ultronium are offered, no stone layers or dirt; both [OreOnly] fields use the picker;
// existing entries (also ones on Stone from before the picker) survive a GPS round trip unchanged.
// Changes nothing in the scene. Report: Temp/OreFactorListChecks.txt.
public static class OreFactorListChecks
{
    const string ReportPath = "Temp/OreFactorListChecks.txt";
    static readonly List<string> failures = new(), passed = new();

    static void Check(bool ok, string message) { if (ok) passed.Add(message); else failures.Add(message); }

    public static string Main()
    {
        if (Application.isPlaying) return "FAIL: Edit Mode erforderlich.";
        failures.Clear(); passed.Clear();
        Directory.CreateDirectory("Temp");
        try { Run(); }
        catch (Exception error) { failures.Add("Exception: " + error); }
        File.WriteAllLines(ReportPath, failures.Select(text => "FAIL " + text).Concat(passed.Select(text => "OK " + text)));
        return failures.Count == 0 ? "PASS (" + passed.Count + ")" : "FAIL " + failures.Count + " Fehler, erster: " + failures[0];
    }

    static void Run()
    {
        var ores = GpsRuntimePanel.OreChoices();
        var ids = ores.Select(block => block.id).ToArray();
        Check(ores.Length >= 14, "Mindestens 14 Erze wählbar (" + string.Join(", ", ores.Select(block => block.displayName)) + ")");
        Check(ores.All(block => block.HasOreOverlays), "Nur Blöcke mit Small/Medium/Rich-Overlays");
        Check(ores.All(block => !block.IsStone && block.id != BlockType.Dirt && block.id != BlockType.Empty), "Keine Gesteinsschichten, Erde oder Leer");
        foreach (var ore in new[] { BlockType.IronOre, BlockType.CopperOre, BlockType.SilverOre, BlockType.GoldOre, BlockType.PlatinumOre,
                     BlockType.Coal, BlockType.DiamondOre, BlockType.TitaniumOre, BlockType.TungstenOre, BlockType.UltroniumOre,
                     BlockType.OrangeGarnetOre, BlockType.MythrilOre, BlockType.EmeraldOre, BlockType.RubyOre })
            Check(ids.Contains(ore), ore + " wählbar");
        Check(ids.Distinct().Count() == ids.Length, "Jedes Erz nur einmal");
        Check(ores.All(block => !string.IsNullOrEmpty(block.displayName)), "Alle Erze mit Anzeigenamen");

        var oreSetting = GpsCodec.Read("0", typeof(VeinRichnessOre), new VeinRichnessOre(), GpsSettings.Profile.Key);
        var distribution = GpsCodec.Read("0", typeof(OreDistributionSetting), new OreDistributionSetting(), GpsSettings.Profile.Key);
        Check(GpsRuntimePanel.IsOreOnly(oreSetting, "ore"), "Faktor je Erz: Erz-Auswahl nur Erze");
        Check(GpsRuntimePanel.IsOreOnly(distribution, "ore"), "Erzverteilung: Erz-Auswahl nur Erze");
        Check(!GpsRuntimePanel.IsOreOnly(oreSetting, "factor") && !GpsRuntimePanel.IsOreOnly(null, "ore"), "Andere Felder unverändert");

        var settings = new VeinRichnessSettings
        {
            ores = new[]
            {
                new VeinRichnessOre { ore = BlockType.Stone, factor = 1f },
                new VeinRichnessOre { ore = BlockType.UltroniumOre, factor = .5f },
                new VeinRichnessOre { ore = BlockType.GoldOre, factor = 2.25f },
            }
        };
        var node = GpsCodec.Read("veinRichness", typeof(VeinRichnessSettings), settings, GpsSettings.Profile.Key);
        var list = node.children.Find(child => child.name == "ores");
        Check(list != null && GpsCodec.ResolveType(list.type) == typeof(VeinRichnessOre[]), "Liste wird als Faktor-je-Erz-Liste erkannt");
        var back = (VeinRichnessSettings)GpsCodec.Write(node.Copy(), GpsSettings.Profile.Resolve);
        Check(back.ores.Length == 3 && back.ores[0].ore == BlockType.Stone && back.ores[1].ore == BlockType.UltroniumOre &&
              Mathf.Approximately(back.ores[1].factor, .5f) && back.ores[2].ore == BlockType.GoldOre && Mathf.Approximately(back.ores[2].factor, 2.25f),
            "Bestehende Einträge bleiben beim Laden erhalten (auch alte auf Stein)");
        Check(Mathf.Approximately(back.Factor(BlockType.UltroniumOre), .5f) && Mathf.Approximately(back.Factor(BlockType.IronOre), 1f),
            "Faktoren wirken wie bisher");
    }
}
