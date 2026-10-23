using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode checks for the iron pickaxe recipe: no recipe (asset file, loaded asset or shipped GPS profile)
// asks for a powerup, and the iron pickaxe can be crafted from wood, nails and iron.
// Only a temporary inventory is used; the run blueprint state is restored afterwards.
public static class BughuntRecipeChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
    static bool IsPowerup(ItemSO item) => item && item.category == ItemCategory.Powerup;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        int[] recipeState = RecipeUnlocks.CaptureRunState();
        GameObject host = null;
        try
        {
            // Asset files and loaded assets.
            var recipes = AssetDatabase.FindAssets("t:CraftingRecipe", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid))).Where(recipe => recipe).ToList();
            Check(recipes.Count >= 30, "Rezept-Assets gefunden (" + recipes.Count + ")");
            var itemReference = new Regex(@"^\s*- item: \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}", RegexOptions.Multiline);
            var badFiles = new List<string>(); var badLoaded = new List<string>();
            foreach (var recipe in recipes)
            {
                string path = AssetDatabase.GetAssetPath(recipe), text = File.ReadAllText(path);
                int start = text.IndexOf("\n  ingredients:", StringComparison.Ordinal);
                if (start >= 0)
                    foreach (Match match in itemReference.Matches(text.Substring(start)))
                        if (IsPowerup(AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)))) badFiles.Add(recipe.name);
                if (recipe.ingredients != null && recipe.ingredients.Any(ingredient => IsPowerup(ingredient.item))) badLoaded.Add(recipe.name);
            }
            Check(badFiles.Count == 0, "Keine Rezept-Datei verlangt ein Powerup" + (badFiles.Count > 0 ? ": " + string.Join(", ", badFiles) : ""));
            Check(badLoaded.Count == 0, "Kein geladenes Rezept verlangt ein Powerup" + (badLoaded.Count > 0 ? ": " + string.Join(", ", badLoaded) : ""));

            // Shipped GPS profile (applied over the assets at startup).
            var profile = AssetDatabase.LoadAssetAtPath<GpsProfile>("Assets/Resources/Gameplay/GpsProfile.asset");
            Check(profile && !string.IsNullOrWhiteSpace(profile.documentJson), "GPS-Profil geladen");
            var document = JsonUtility.FromJson<GpsDocument>(profile.documentJson);
            var gpsRecipes = document.records.Where(record => record.type == typeof(CraftingRecipe).AssemblyQualifiedName).ToList();
            Check(gpsRecipes.Count >= 30, "GPS-Rezepte gefunden (" + gpsRecipes.Count + ")");
            var badGps = new List<string>(); int resolved = 0;
            foreach (var record in gpsRecipes)
            {
                var ingredients = record.fields.Find(field => field.name == "ingredients");
                if (ingredients == null) continue;
                foreach (var ingredient in ingredients.children)
                {
                    var reference = ingredient.children.Find(child => child.name == "item");
                    var item = reference == null || string.IsNullOrEmpty(reference.text) ? null : profile.Resolve(reference.text) as ItemSO;
                    if (item) resolved++;
                    if (IsPowerup(item)) badGps.Add(record.name);
                }
            }
            Check(resolved > gpsRecipes.Count, "GPS-Zutaten aufgelöst (" + resolved + ")");
            Check(badGps.Count == 0, "Kein GPS-Rezept verlangt ein Powerup" + (badGps.Count > 0 ? ": " + string.Join(", ", badGps) : ""));

            // The iron pickaxe is crafted from wood, nails and iron.
            var pickaxe = ExoticCatalog.AllRecipes.First(recipe => recipe && recipe.output && recipe.output.item == Item.IronPickaxe);
            var wood = StartingResourcesSettings.Resolve((int)Item.Wood);
            var nails = StartingResourcesSettings.Resolve((int)Item.Nails);
            var iron = StartingResourcesSettings.Resolve((int)Item.Iron);
            Check(wood && nails && iron, "Holz, Nägel und Eisen aufgelöst");
            Check(pickaxe.TryGetCosts(out var costs) && costs.Count == 3 && costs.ContainsKey(wood) && costs.ContainsKey(nails) && costs.ContainsKey(iron),
                "Eisenspitzhacke kostet Holz, Nägel und Eisen (" + string.Join(", ", costs.Select(cost => cost.Key.name + " " + cost.Value)) + ")");
            var gpsPickaxe = gpsRecipes.First(record => profile.Resolve(record.assetKey) == pickaxe);
            var gpsItems = gpsPickaxe.fields.Find(field => field.name == "ingredients").children
                .Select(ingredient => profile.Resolve(ingredient.children.Find(child => child.name == "item").text)).ToList();
            Check(gpsItems.Count == 3 && gpsItems.Contains(wood) && gpsItems.Contains(nails) && gpsItems.Contains(iron), "GPS-Rezept der Eisenspitzhacke verlangt Holz, Nägel und Eisen");

            host = new GameObject("BughuntRecipeChecks") { hideFlags = HideFlags.HideAndDontSave };
            var inventory = host.AddComponent<InventoryManager>();
            RecipeUnlocks.Unlock(pickaxe);
            Check(RecipeUnlocks.IsUnlocked(pickaxe), "Bauplan der Eisenspitzhacke gekauft");
            foreach (var cost in costs) inventory.AddStartingItem(cost.Key, cost.Value);
            Check(CraftingService.GetMaxCraftable(pickaxe, inventory) >= 1, "Eisenspitzhacke mit Holz, Nägeln und Eisen herstellbar");
            Check(CraftingService.TryCraft(pickaxe, inventory, 1) && inventory.IsPickaxeUnlocked(pickaxe.output), "Herstellen schaltet die Eisenspitzhacke frei");
            Check(costs.All(cost => inventory.GetCount(cost.Key) == 0), "Zutaten wurden verbraucht");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks.Select(check => "PASS " + check));
            File.WriteAllText("Temp/BughuntRecipeChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/BughuntRecipeChecks.txt", string.Join("\n", checks.Select(check => "PASS " + check)) + "\nFAIL " + exception.Message);
            throw;
        }
        finally
        {
            if (host) Object.DestroyImmediate(host);
            RecipeUnlocks.RestoreRunState(recipeState);
        }
    }
}
