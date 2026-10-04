using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Run in an isolated active gameplay session after InstallExoticContent.Main.
public static class ExoticContentChecks
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++; File.AppendAllText("Temp/ExoticContentChecks.txt", "PASS " + message + "\n");
    }
    public static object Main()
    {
        if (!Application.isPlaying || (string.IsNullOrEmpty(GameSaveSystem.TestDirectory) && string.IsNullOrEmpty(MetaProgression.TestDirectory)))
            throw new InvalidOperationException("An isolated gameplay save/profile directory is required.");
        checks = 0; File.WriteAllText("Temp/ExoticContentChecks.txt", "");
        var map = Object.FindFirstObjectByType<MapGenerator>(); var inventory = InventoryManager.Instance;
        if (!map || !map.IsGenerated || !inventory || GameplayInputBlocker.IsBlocked) throw new InvalidOperationException("Ready, unpaused gameplay required.");
        var world = ExoticWorldContent.Ensure(map); world.Generate();
        var oldWorld = world.CaptureState(); var oldInventory = inventory.CaptureRunState();
        var energy = Object.FindFirstObjectByType<EnergyManager>(); float oldEnergy = energy ? energy.energy : 0;
        var oldRecipes = RecipeUnlocks.CaptureRunState();
        string previousMeta = MetaProgression.TestDirectory;
        var previousRun = MetaProgression.CaptureRunState();
        var recipes = ExoticCatalog.Recipes.ToArray();
        var cell = map.Terrain.WorldToCell(Object.FindFirstObjectByType<PlayerMovement>().transform.position) + new Vector3Int(3, -2, 0);
        var cells = new System.Collections.Generic.Dictionary<Vector3Int, TileBase>();
        for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++)
        { var c = cell + new Vector3Int(x, y, 0); cells[c] = map.Terrain.GetTile(c); map.Terrain.SetTile(c, null); }
        var ladders = map.GetComponent<LadderMap>();
        var oldLadder = ladders.Tiles.GetTile(cell);
        PlacedTorch testLamp = null, testTorch = null;
        try
        {
            MetaProgression.EndRun();
            MetaProgression.TestDirectory = Path.GetFullPath("Temp/ExoticContentProfile-" + DateTime.UtcNow.Ticks);
            MetaProgression.BeginRun("exotic-content-checks");
            inventory.RestoreRunState(new SavedInventory {
                items = Array.Empty<SavedItem>(), owned = Array.Empty<int>(), powerups = new[] { (int)Item.CopperPickaxe }, carryingLevel = 1, energyLevel = 1
            });
            Check(recipes.Length == 3, "All three exotic variants installed");
            Check(ExoticCatalog.AllRecipes.Count > recipes.Length, "Home catalog includes essential recipes");
            RecipeUnlocks.ResetRun();
            Check(ExoticCatalog.Offer("same-site").Length == 0, "Level one cannot find gated exotics");
            foreach (var recipe in ExoticCatalog.AllRecipes.Where(r => r && !r.exotic && !RecipeUnlocks.IsShopRecipe(r) && r.output.item != Item.Steel))
                Check(RecipeUnlocks.IsUnlocked(recipe), "Essential recipe available: " + recipe.output.displayName);
            MetaProgression.BeginRun("exotic-level-fixture", new MetaRunState { runId = "exotic-level-fixture", progressXp = MetaProgressionCatalog.XpForLevel(100) });
            MetaProgression.EndRun(); MetaProgression.BeginRun("exotic-content-checks");
            var offer = ExoticCatalog.Offer("same-site");
            Check(offer.Length == 3 && offer.Distinct().Count() == 3, "Find offers three unique available recipes");
            Check(offer.SequenceEqual(ExoticCatalog.Offer("same-site")), "Unclaimed offer is stable");
            Check(RecipeUnlocks.LearnExotic(offer[0]) && !RecipeUnlocks.LearnExotic(offer[0]), "Blueprint learns once immediately");
            Check(!ExoticCatalog.Offer("other-site").Contains(offer[0]), "Learned recipes never repeat in offers");
            var savedRecipes = RecipeUnlocks.CaptureRunState(); RecipeUnlocks.ResetRun();
            Check(!RecipeUnlocks.IsUnlocked(offer[0]) && ExoticCatalog.IsAvailable(offer[0]), "New run forgets recipe and preserves permanent availability");
            RecipeUnlocks.RestoreRunState(savedRecipes);
            Check(RecipeUnlocks.IsUnlocked(offer[0]), "Run recipe restoration preserves learned exotic");
            foreach (var recipe in recipes)
            {
                RecipeUnlocks.LearnExotic(recipe);
                foreach (var ingredient in recipe.ingredients) Check(inventory.Add(ingredient.item, ingredient.amount), "Fund ingredients: " + ingredient.item.displayName);
                Check(CraftingService.TryCraft(recipe, inventory, 1), "Craft " + recipe.output.displayName);
                if (recipe.output.category == ItemCategory.Powerup)
                    Check(inventory.IsPowerupUnlocked(recipe.output) && CraftingService.GetMaxCraftable(recipe, inventory) == 0, "Hammer owned once per run");
            }
            var iron = StartingResourcesSettings.Resolve((int)Item.IronLadder);
            Vector2 position = map.Terrain.GetCellCenterWorld(cell);
            ladders.Tiles.SetTile(cell, null);
            Check(ladders.TryPlace(cell, inventory, position, 10, iron), "Iron ladder consumes iron variant");
            Check(ladders.Tiles.GetTile(cell) == Resources.Load<Tile>("Exotics/IronLadderTile"), "Iron ladder has distinct saved tile asset");
            var catalog = Resources.Load<SaveAssetCatalog>("SaveAssetCatalog");
            Check(catalog.Resolve(catalog.Key(ladders.Tiles.GetTile(cell))) == ladders.Tiles.GetTile(cell), "Iron ladder GUID roundtrip");
            int ironBefore = inventory.GetCount(iron);
            Check(ladders.TryRemove(cell, inventory, position, 10) && inventory.GetCount(iron) == ironBefore + 1, "Removing iron ladder returns correct item");
            var lampItem = StartingResourcesSettings.Resolve((int)Item.LavaLamp);
            Check(PlacedTorch.TryPlace(map, lampItem, position, position, 10), "Lavalampe placed through normal inventory transaction");
            testLamp = PlacedTorch.Active.First(t => t.OwnerMap == map && t.Cell == cell);
            var torchCell = cell + new Vector3Int(2, 0, 0);
            PlacedTorch.CreateAt(map, StartingResourcesSettings.Resolve((int)Item.Torche), torchCell);
            testTorch = PlacedTorch.Active.First(t => t.OwnerMap == map && t.Cell == torchCell);
            Check(testLamp.Intensity > testTorch.Intensity * 2, "Lavalampe yields meaningfully stronger light");
            Check(PlacedTorch.Capture(map).Any(v => v.cell == cell && v.itemId == (int)Item.LavaLamp), "Light save records variant identity");
            var fixture = new ExoticWorldState {
                caches = new[] { new SavedBlueprintCache { cell = cell } },
                boulders = new[] { new SavedBoulder { cell = cell, health = MineBoulder.MaximumHealth } }
            };
            world.RestoreState(fixture);
            RecipeUnlocks.ResetRun();
            Check(world.TryDiscover(0), "Physical cache opens a blueprint offer");
            Check(world.PendingChoices.Length == 3 && world.CaptureState().pendingChoices.Length == 3, "Pending three-choice offer survives save capture");
            var chosen = world.PendingChoices[0];
            Check(world.LearnPending(chosen) && RecipeUnlocks.IsUnlocked(chosen), "Physical discovery learns chosen recipe immediately");
            Check(world.CaptureState().caches[0].claimed && !world.TryDiscover(0), "Physical cache cannot be claimed twice");
            var panel = Object.FindFirstObjectByType<ExoticBlueprintPanel>(); if (panel) Object.DestroyImmediate(panel.gameObject);
            var boulder = Object.FindObjectsByType<MineBoulder>(FindObjectsSortMode.None).Last(b => b.Map == map && b.Capture().cell == cell && b.Health > 0);
            Check(boulder.gameObject.layer == map.Terrain.gameObject.layer, "Boulder belongs to terrain collision layer");
            var craftedInventory = inventory.CaptureRunState();
            var noHammer = JsonUtility.FromJson<SavedInventory>(JsonUtility.ToJson(craftedInventory));
            noHammer.powerups = noHammer.powerups.Where(id => id != (int)Item.PercussionHammer).ToArray();
            inventory.RestoreRunState(noHammer);
            Check(!boulder.Hit() && boulder.Health == MineBoulder.MaximumHealth, "Ordinary equipment cannot mine boulders");
            inventory.RestoreRunState(craftedInventory);
            Check(boulder.Hit() && boulder.Health == MineBoulder.MaximumHealth - 1, "Crafted hammer chips boulder");
            var roundtrip = world.CaptureState();
            Check(roundtrip.boulders[0].health == MineBoulder.MaximumHealth - 1, "Boulder damage saved");
            Color damagedColor = boulder.GetComponent<SpriteRenderer>().color;
            world.RestoreState(roundtrip);
            boulder = Object.FindObjectsByType<MineBoulder>(FindObjectsSortMode.None).Last(b => b.Map == map && b.Capture().cell == cell && b.Health > 0);
            Check(boulder.GetComponent<SpriteRenderer>().color == damagedColor, "Saved boulder damage restores its visual tint");
            MetaProgression.EndRun();
            Check(MetaProgression.Spend("mining"), "Low-energy fixture buys one mining efficiency rank between runs");
            MetaProgression.BeginRun("exotic-efficient-hammer");
            float hitCost = 2f * MetaProgression.CurrentLoadout.miningEnergyMultiplier;
            Check(hitCost < 2f, "New fixture run captures reduced mining cost");
            energy.energy = hitCost + .005f;
            Check(boulder.Hit() && Mathf.Abs(energy.energy - .005f) < .0001f, "Hammer accepts low energy sufficient for its reduced cost and drains exactly that cost");
            float remainingHealth = boulder.Health;
            energy.energy = hitCost - .005f;
            Check(!boulder.Hit() && boulder.Health == remainingHealth && Mathf.Abs(energy.energy - (hitCost - .005f)) < .0001f,
                "Hammer rejects energy below actual reduced cost without damage or drain");
            BoulderField.Blast(map, boulder.HitPoint, .1f); Check(!boulder.GetComponent<Collider2D>().enabled && boulder.Health == 0, "Dynamite blast destroys boulder and removes collision");
            Check(world.CaptureState().boulders[0].health == 0, "Destroyed boulder stays destroyed in save");
            bool rejected = false;
            fixture.boulders[0].health = float.NaN;
            try { ExoticWorldContent.ValidateState(fixture, map.GeneratedWidth, map.GeneratedHeight); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Corrupt exotic state rejected before load");
            File.AppendAllText("Temp/ExoticContentChecks.txt", checks + " checks passed\n");
            return checks + " exotic content checks passed";
        }
        finally
        {
            var panel = Object.FindFirstObjectByType<ExoticBlueprintPanel>(); if (panel) Object.DestroyImmediate(panel.gameObject);
            if (testLamp) Object.DestroyImmediate(testLamp.gameObject);
            if (testTorch) Object.DestroyImmediate(testTorch.gameObject);
            MetaProgression.EndRun(); MetaProgression.TestDirectory = previousMeta;
            if (previousRun != null) MetaProgression.BeginRun(previousRun.runId, previousRun);
            inventory.RestoreRunState(oldInventory); RecipeUnlocks.RestoreRunState(oldRecipes);
            if (energy) energy.energy = oldEnergy;
            ladders.Tiles.SetTile(cell, oldLadder);
            foreach (var pair in cells) map.Terrain.SetTile(pair.Key, pair.Value);
            world.RestoreState(oldWorld);
        }
    }
}
