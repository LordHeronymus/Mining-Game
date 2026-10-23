using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// F1 world values take effect as labelled: NewRun recipe fields at the next run start (with fresh exotic caches),
// grass distribution values in the running world, and changed song/whisper means for the pending time.
// Edit Mode; profile and save writes stay in Temp, recipe assets and GPS document are restored.
public static class BughuntWorldLiveChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string ReportPath = "Temp/BughuntWorldLiveChecks.txt";
    static readonly List<string> checks = new();
    static object Get(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static object Call(object o, string name, params object[] args) =>
        (o.GetType().GetMethod(name, Private) ?? throw new Exception("FAIL " + o.GetType().Name + "." + name + " fehlt")).Invoke(o, args);
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); checks.Add(label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp"); checks.Clear();
        var randomState = UnityEngine.Random.state;
        try
        {
            NewRunRecipes();
            GrassLayout();
            AmbienceSchedule();
            Check(UnityEngine.Random.state.Equals(randomState), "UnityEngine.Random state unchanged");
            File.WriteAllText(ReportPath, string.Join("\n", checks.ConvertAll(label => "PASS " + label)) + "\n");
            return "PASS " + checks.Count + " world live checks";
        }
        catch (Exception exception)
        {
            var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
            File.WriteAllText(ReportPath, string.Join("\n", checks.ConvertAll(label => "PASS " + label)) +
                "\n" + (inner.Message.StartsWith("FAIL ") ? inner.Message : "FAIL " + inner) + "\n");
            throw;
        }
        finally { UnityEngine.Random.state = randomState; }
    }

    // R3-gps-live-2: exotic/metaUnlockLevel (NewRun) reach the recipe assets and both exotic caches when a run begins.
    static void NewRunRecipes()
    {
        Check(GameSaveSystem.ActiveSlot == -1, "No active run in Edit Mode");
        var recipes = Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes");
        var originals = recipes.Select(item => (item, item.exotic, item.metaUnlockLevel, item.outputAmount)).ToArray();
        var recipe = ExoticCatalog.Recipes.FirstOrDefault(value => value.metaUnlockLevel < 900);
        Check(recipe, "Fixture: an exotic recipe exists");
        var record = GpsSettings.Document.records.Find(item => !string.IsNullOrEmpty(item.assetKey) && GpsSettings.Targets(item).Contains(recipe));
        Check(record != null, "Fixture: the recipe has a GPS record");
        var level = record.fields.Find(field => field.name == "metaUnlockLevel");
        var output = record.fields.Find(field => field.name == "outputAmount");
        Check(level != null && output != null, "Fixture: level and output fields exist");
        var levelBefore = level.Copy(); var outputBefore = output.Copy();
        int changedLevel = recipe.metaUnlockLevel + 7, changedOutput = recipe.outputAmount + 3, outputAmount = recipe.outputAmount;

        string previousSaves = GameSaveSystem.TestDirectory, previousMeta = MetaProgression.TestDirectory;
        var previousRun = MetaProgression.CaptureRunState();
        float previousPlayed = GameSaveSystem.PlayedSeconds;
        string root = Path.GetFullPath("Temp/BughuntWorldLive-" + DateTime.UtcNow.Ticks);
        try
        {
            // Prime both caches with the old level, as the main menu's blueprint collection does.
            ResetCaches();
            Check(ExoticBlueprintCatalog.Entries.Any(entry => entry.Recipe == recipe && entry.Level == recipe.metaUnlockLevel), "Blueprint cache holds the old level");
            // Edited like an F1 change in a run: stored in the document, not yet on the asset.
            level.number = changedLevel; output.number = changedOutput;
            GameSaveSystem.TestDirectory = root; MetaProgression.TestDirectory = Path.Combine(root, "Meta");
            Check(GameSaveSystem.BeginNewRun("BughuntWorldLive"), "A new test run begins");
            GameSaveSystem.LeaveRun();
            Check(recipe.metaUnlockLevel == changedLevel, "New run applies the NewRun level to the recipe asset (" + recipe.metaUnlockLevel + ")");
            Check(recipe.outputAmount == outputAmount, "New run leaves Immediately fields to their own path");
            Check(ExoticCatalog.Recipes.Contains(recipe) && ExoticCatalog.Recipes.Zip(ExoticCatalog.Recipes.Skip(1), (a, b) => a.metaUnlockLevel <= b.metaUnlockLevel).All(ok => ok),
                "Exotic catalog is rebuilt and sorted by the new level");
            Check(ExoticBlueprintCatalog.Entries.Any(entry => entry.Recipe == recipe && entry.Level == changedLevel), "Blueprint collection shows the new level");
        }
        finally
        {
            GameSaveSystem.LeaveRun();
            level.number = levelBefore.number; output.number = outputBefore.number;
            foreach (var (asset, exotic, unlock, amount) in originals) { asset.exotic = exotic; asset.metaUnlockLevel = unlock; asset.outputAmount = amount; }
            ResetCaches();
            GameSaveSystem.TestDirectory = previousSaves; MetaProgression.TestDirectory = previousMeta;
            GameSaveSystem.PlayedSeconds = previousPlayed;
            if (previousRun != null) MetaProgression.BeginRun(previousRun.runId, previousRun);
        }
    }

    static void ResetCaches()
    {
        ExoticCatalog.ResetCache();
        typeof(ExoticBlueprintCatalog).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    // R3-gps-live-3: distribution values lay the grassland out again (Rebuild clears the old sites); other values do not.
    static void GrassLayout()
    {
        string[] layout = { "maximumPatches", "minimumSpacing", "clusterPercent", "clusterSize", "randomness", "spawnQuietRadius", "nearSpawnDensity", "healingHerbRarityPercent" };
        string[] live = { "respawnSeconds", "fiberYield", "fiberSwayStrength", "healingHerbConversionFactor", "grassSizeMultiplier", "healingHerbSizeMultiplier" };
        var catalog = GpsSchema.Fields(typeof(SurfaceTallGrass)).Select(field => field.name).ToHashSet();
        Check(layout.Concat(live).All(catalog.Contains), "All probed grass fields are GPS fields");
        var probe = new GameObject("Grass layout probe") { hideFlags = HideFlags.HideAndDontSave };
        probe.SetActive(false);
        try
        {
            var grass = probe.AddComponent<SurfaceTallGrass>();
            probe.SetActive(true);
            var sites = (List<int>)Get(grass, "sites");
            foreach (string field in live)
            {
                sites.Clear(); sites.AddRange(new[] { 3, 4, 5 });
                Call(grass, "ApplySetting", field);
                Check(sites.Count == 3, field + " keeps the grassland layout");
            }
            foreach (string field in layout)
            {
                sites.Clear(); sites.AddRange(new[] { 3, 4, 5 });
                Call(grass, "ApplySetting", field);
                Check(sites.Count == 0, field + " lays the grassland out again");
            }
        }
        finally { Object.DestroyImmediate(probe); }
    }

    // R3-gps-live-4: the pending song/whisper time is drawn again from now when the mean changed, otherwise kept.
    static void AmbienceSchedule()
    {
        var schedule = typeof(SecondLayerAmbience).GetMethod("Schedule", BindingFlags.Static | BindingFlags.NonPublic);
        Check(schedule != null, "SecondLayerAmbience.Schedule exists");
        var random = new System.Random(42); var reference = new System.Random(42);
        object[] args = { 0d, float.NaN, 20f, 100d, random };
        Check((bool)schedule.Invoke(null, args), "Nothing scheduled draws a time");
        double expected = 100d - Math.Log(1d - reference.NextDouble()) * 20f * 60d;
        Check(Math.Abs((double)args[0] - expected) < 1e-6 && (float)args[1] == 20f, "Draw uses the current mean from now");
        double first = (double)args[0];
        args[3] = 160d;
        Check(!(bool)schedule.Invoke(null, args) && (double)args[0] == first, "Unchanged mean keeps the pending time");
        args[2] = .05f; args[3] = 200d;
        Check((bool)schedule.Invoke(null, args) && (float)args[1] == .05f, "Changed mean draws again");
        expected = 200d - Math.Log(1d - reference.NextDouble()) * .05f * 60d;
        Check(Math.Abs((double)args[0] - expected) < 1e-6 && (double)args[0] < first, "Redraw starts at now with the new mean");
    }
}
