using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class AxePowerupChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    static Vector3[] Pose(MinerPlayerVisual visual, TileMiner miner, bool chopping)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TileMiner).GetField("<IsChoppingTree>k__BackingField", flags).SetValue(miner, chopping);
        typeof(MinerPlayerVisual).GetField("miningWeight", flags).SetValue(visual, 1f);
        typeof(MinerPlayerVisual).GetMethod("DrawPose", flags).Invoke(visual,
            new object[] { true, (Vector2)miner.transform.position + Vector2.right });
        return visual.GetComponentInChildren<MeshFilter>().sharedMesh.vertices.ToArray();
    }

    static bool SamePose(Vector3[] left, Vector3[] right) =>
        left.Length == right.Length && !left.Where((vertex, index) =>
            (vertex - right[index]).sqrMagnitude > .000001f).Any();

    public static string Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var trees = UnityEngine.Object.FindFirstObjectByType<SurfaceTrees>();
        var inventory = InventoryManager.Instance;
        var axe = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Tools/Axe.asset");
        var tree = ChoppableTree.ActiveTrees.FirstOrDefault(candidate => candidate && candidate.CanChop && candidate.Health >= 8);
        var miner = UnityEngine.Object.FindFirstObjectByType<TileMiner>();
        var visual = miner ? miner.GetComponent<MinerPlayerVisual>() : null;
        Check(trees && inventory && axe && tree && miner && visual, "Tree, miner, or axe fixture is missing.");
        var settings = new SerializedObject(trees);
        var multiplier = settings.FindProperty("axeHitMultiplier");
        float originalMultiplier = multiplier.floatValue;
        var originalInventory = inventory.GetSnapshot().ToArray();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var choppingField = typeof(TileMiner).GetField("<IsChoppingTree>k__BackingField", flags);
        var weightField = typeof(MinerPlayerVisual).GetField("miningWeight", flags);
        bool originalChopping = (bool)choppingField.GetValue(miner);
        float originalWeight = (float)weightField.GetValue(visual);
        try
        {
            inventory.ResetAll();
            multiplier.floatValue = 4f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(!trees.HasAxe && Mathf.Approximately(trees.HitDamage, 1f), "No-axe tree behavior.");
            var normalDig = Pose(visual, miner, false);
            var treeWithoutAxe = Pose(visual, miner, true);
            Check(SamePose(normalDig, treeWithoutAxe), "Without axe, tree chopping must draw the normal pickaxe dig pose.");
            int before = tree.Health;
            tree.Hit(Vector2.zero);
            Check(tree.Health == before - 1, "Tree must remain choppable without an axe.");

            inventory.Add(axe);
            Check(trees.HasAxe && Mathf.Approximately(trees.HitDamage, 4f), "Axe must activate without equipping.");
            var treeWithAxe = Pose(visual, miner, true);
            Check(!SamePose(treeWithoutAxe, treeWithAxe), "With axe, tree chopping must draw the axe pose.");
            before = tree.Health;
            tree.Hit(Vector2.zero);
            Check(tree.Health == before - 4, "Axe must quarter the required hits.");

            multiplier.floatValue = 2.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            before = tree.Health;
            tree.Hit(Vector2.zero);
            Check(tree.Health == Mathf.CeilToInt(before - 2.5f), "Configured multiplier must apply immediately.");

            var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
            ui.ShowPanel();
            int visibleItems = inventory.GetSnapshot().Count(pair => pair.Key &&
                pair.Key.category != ItemCategory.Powerup && pair.Value > 0);
            Check(ui.IsOpen && ui.VisibleItemCount == visibleItems,
                $"Axe powerup must stay out of the inventory view: open={ui.IsOpen}, visible={ui.VisibleItemCount}, expected={visibleItems}.");
            ui.HidePanel();
            return "PASS: axe ownership, default and configurable hit multiplier, chopping without axe, hidden inventory powerup.";
        }
        finally
        {
            multiplier.floatValue = originalMultiplier;
            settings.ApplyModifiedPropertiesWithoutUndo();
            choppingField.SetValue(miner, originalChopping);
            weightField.SetValue(visual, originalWeight);
            inventory.ResetAll();
            foreach (var entry in originalInventory) inventory.Add(entry.Key, entry.Value);
        }
    }
}
