using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Edit Mode checks: blueprints bought in the shop live only in the run state, not in the PlayerPrefs that every
// editor of the project shares. A parallel editor that deletes or sets the old keys must not change this run.
// The run blueprint state and the old PlayerPrefs keys are restored afterwards.
public static class BughuntBlueprintPrefsChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
    static readonly Item[] EnergyItems = { Item.CrystalPendant, Item.CopperEnergyBracelet, Item.EnergyStorageVial, Item.RuneBelt, Item.CrystalHeart, Item.CrystalHarness, Item.TravelMonolith };

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var shopRecipes = ExoticCatalog.AllRecipes.Concat(Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes")).Where(RecipeUnlocks.IsShopRecipe).Distinct().ToList();
        var pickaxe = shopRecipes.First(recipe => recipe.output.item == Item.IronPickaxe);
        var medkit = shopRecipes.First(recipe => recipe.output.item == Item.Medkit);
        var energy = shopRecipes.FirstOrDefault(recipe => Array.IndexOf(EnergyItems, recipe.output.item) >= 0);
        const string pickaxeKey = "workbench.recipe.iron-pickaxe.unlocked", medkitKey = "workbench.recipe.medkit.unlocked";
        string energyKey = energy ? "workbench.recipe.energy." + (int)energy.output.item : null;
        var keys = new[] { pickaxeKey, medkitKey, energyKey }.Where(key => key != null).ToArray();
        var previousPrefs = keys.ToDictionary(key => key, key => PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null);
        int[] recipeState = RecipeUnlocks.CaptureRunState();
        try
        {
            RecipeUnlocks.ResetRun();
            foreach (var key in keys) PlayerPrefs.DeleteKey(key);
            Check(RecipeUnlocks.Unlock(pickaxe) && RecipeUnlocks.IsUnlocked(pickaxe) && RecipeUnlocks.IsIronPickaxeUnlocked, "Eisenspitzhacken-Bauplan im Run gekauft");
            Check(!PlayerPrefs.HasKey(pickaxeKey), "Kauf schreibt nichts in die geteilten PlayerPrefs");

            // Another editor starts a new run (old code deletes the keys) or buys other blueprints (old code sets them).
            PlayerPrefs.DeleteKey(pickaxeKey);
            PlayerPrefs.SetInt(medkitKey, 1);
            if (energyKey != null) PlayerPrefs.SetInt(energyKey, 1);
            Check(RecipeUnlocks.IsUnlocked(pickaxe) && RecipeUnlocks.CaptureRunState().Contains((int)Item.IronPickaxe), "Gekaufter Bauplan bleibt trotz gelöschtem PlayerPrefs-Schlüssel erhalten und wird gespeichert");
            Check(!RecipeUnlocks.IsUnlocked(medkit) && !RecipeUnlocks.IsMedkitUnlocked && !RecipeUnlocks.CaptureRunState().Contains((int)Item.Medkit), "Fremder Medkit-Kauf erscheint nicht im eigenen Run");
            if (energy) Check(!RecipeUnlocks.IsUnlocked(energy), "Fremder Energie-Bauplan erscheint nicht im eigenen Run (" + energy.output.item + ")");
            Check(RecipeUnlocks.Unlock(medkit) && RecipeUnlocks.IsUnlocked(medkit), "Medkit-Bauplan trotz fremdem Schlüssel kaufbar");
            if (energy) Check(RecipeUnlocks.Unlock(energy) && RecipeUnlocks.IsUnlocked(energy), "Energie-Bauplan im Run gekauft");

            // A new run here must not touch the other editor's keys.
            var saved = RecipeUnlocks.CaptureRunState();
            RecipeUnlocks.ResetRun();
            Check(!RecipeUnlocks.IsUnlocked(pickaxe) && !RecipeUnlocks.IsUnlocked(medkit) && (!energy || !RecipeUnlocks.IsUnlocked(energy)), "Neuer Run setzt gekaufte Baupläne zurück");
            Check(PlayerPrefs.GetInt(medkitKey, 0) == 1 && (energyKey == null || PlayerPrefs.GetInt(energyKey, 0) == 1), "Neuer Run löscht keine PlayerPrefs eines anderen Editors");
            RecipeUnlocks.RestoreRunState(saved);
            Check(RecipeUnlocks.IsUnlocked(pickaxe) && RecipeUnlocks.IsUnlocked(medkit) && (!energy || RecipeUnlocks.IsUnlocked(energy)), "Laden stellt die gekauften Baupläne wieder her");

            // Play start without domain reload clears the in-memory state.
            var reset = typeof(RecipeUnlocks).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType == RuntimeInitializeLoadType.SubsystemRegistration).ToList();
            Check(reset.Count > 0, "SubsystemRegistration-Reset vorhanden");
            foreach (var method in reset) method.Invoke(null, null);
            Check(!RecipeUnlocks.IsUnlocked(pickaxe) && !RecipeUnlocks.IsUnlocked(medkit), "Play-Start ohne Domain-Reload vergisst Baupläne des letzten Runs");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks.Select(check => "PASS " + check));
            File.WriteAllText("Temp/BughuntBlueprintPrefsChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/BughuntBlueprintPrefsChecks.txt", string.Join("\n", checks.Select(check => "PASS " + check)) + "\nFAIL " + exception.Message);
            throw;
        }
        finally
        {
            RecipeUnlocks.RestoreRunState(recipeState);
            foreach (var pair in previousPrefs)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value); else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save();
        }
    }
}
