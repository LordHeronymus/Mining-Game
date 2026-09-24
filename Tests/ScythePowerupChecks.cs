using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ScythePowerupChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var grass = UnityEngine.Object.FindFirstObjectByType<SurfaceTallGrass>();
        var miner = UnityEngine.Object.FindFirstObjectByType<TileMiner>();
        var inventory = InventoryManager.Instance;
        var scythe = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Tools/Scythe.asset");
        var fiber = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Materials/PlantFiber.asset");
        var patch = grass ? grass.ActivePatches.FirstOrDefault() : null;
        Check(grass && miner && inventory && scythe && fiber && patch,
            $"Fixture: grass={!!grass}, miner={!!miner}, inventory={!!inventory}, scythe={!!scythe}, fiber={!!fiber}, patch={!!patch}");
        Check(scythe.category == ItemCategory.Powerup, "Scythe must be a powerup.");
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        Check(hud && !hud.slots.Contains(scythe), "Scythe must not occupy a hotbar slot.");

        var originalInventory = inventory.GetSnapshot().ToArray();
        Vector3 originalPosition = miner.transform.position;
        try
        {
            inventory.ResetAll();
            int originalFiber = inventory.GetCount(fiber);
            Check(!grass.HasScythe, "Scythe possession state without powerup.");
            int before = grass.PatchCount;
            Check(!patch.Cut() && grass.PatchCount == before, "Grass cannot be cut without scythe.");
            Check(!miner.TryCutGrassAt(patch.transform.position), "Miner must reject cutting without scythe.");
            Check(!grass.GetComponent<TallGrassCutParticles>(), "Rejected cut must not emit grass blades.");

            inventory.Add(scythe);
            Check(grass.HasScythe && grass.ScytheIcon, "Scythe activates without equipping.");
            patch.SetReachGlow(2);
            Check(patch.ReachGlowLevel == 2, "Grass hover glow.");
            patch.SetReachGlow(0);
            Check(patch.ReachGlowLevel == 0, "Grass hover glow reset.");
            miner.transform.position = patch.transform.position;
            Check(miner.TryCutGrassAt(patch.transform.position), "Owned scythe cuts reachable grass.");
            Check(grass.PatchCount == before - 1, "Cut grass patch must disappear.");
            Check(inventory.GetCount(fiber) > originalFiber, "Cutting grass must yield fibers.");
            var grassParticles = grass.GetComponent<TallGrassCutParticles>();
            Check(grassParticles && grassParticles.ActiveCount == 20,
                "Scythe cut must emit grass blades after the patch disappears.");
            var bladeSystem = grassParticles.transform.Find("Flying Grass Blades")
                .GetComponent<ParticleSystem>();
            bladeSystem.Simulate(2f, true, false);
            Check(grassParticles.ActiveCount == 0, "Grass blades must fade and expire.");

            var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
            ui.ShowPanel();
            int visibleItems = inventory.GetSnapshot().Count(pair => pair.Key &&
                pair.Key.category != ItemCategory.Powerup && pair.Value > 0);
            Check(ui.IsOpen && ui.VisibleItemCount == visibleItems, "Scythe must stay out of inventory view.");
            ui.HidePanel();
            return "PASS: scythe ownership, guarded harvesting, fibers, glow, hotbar, inventory.";
        }
        finally
        {
            miner.transform.position = originalPosition;
            inventory.ResetAll();
            foreach (var entry in originalInventory) inventory.Add(entry.Key, entry.Value);
        }
    }
}
