using System;
using UnityEngine;

public static class SurfaceHarvestIdentityChecks
{
    public static object Main()
    {
        var root = new GameObject("Harvest identity probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        int checks = 0;
        try {
            var tree = root.AddComponent<ChoppableTree>();
            var savedTree = new SavedTree { cell = 4, health = 1, sproutScale = 1, harvestId = "tree-growth-A" };
            tree.RestoreRunState(savedTree);
            if (tree.CaptureRunState(0).harvestId != savedTree.harvestId) throw new Exception("Tree restore/capture identity"); checks++;
            var forest = JsonUtility.FromJson<SavedForest>(JsonUtility.ToJson(new SavedForest { trees = new[] { tree.CaptureRunState(0) } }));
            if (forest.trees[0].harvestId != savedTree.harvestId) throw new Exception("Tree world serialization"); checks++;
            savedTree.harvestId = null;
            tree.RestoreRunState(savedTree);
            string legacy = tree.CaptureRunState(0).harvestId;
            tree.RestoreRunState(savedTree);
            if (legacy != "legacy-tree:4" || tree.CaptureRunState(0).harvestId != legacy) throw new Exception("Stable legacy tree identity"); checks++;
            var patch = root.AddComponent<TallGrassPatch>();
            patch.RestoreHarvestId("grass-growth-A");
            if (patch.HarvestId != "grass-growth-A") throw new Exception("Grass identity restore"); checks++;
            var grassland = JsonUtility.FromJson<SavedGrassland>(JsonUtility.ToJson(new SavedGrassland {
                patches = new[] { new SavedGrass { cell = 7, harvestId = patch.HarvestId } }
            }));
            if (grassland.patches[0].harvestId != "grass-growth-A") throw new Exception("Grass world serialization"); checks++;
            patch.RestoreHarvestId(null); string oldGrass = patch.HarvestId;
            patch.RestoreHarvestId(null);
            if (patch.HarvestId != oldGrass || string.IsNullOrEmpty(oldGrass)) throw new Exception("Stable legacy grass identity"); checks++;
            System.IO.File.WriteAllText("Temp/SurfaceHarvestIdentityChecks.txt", "PASS " + checks + " identity/serialization checks\n");
            return "PASS " + checks;
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
