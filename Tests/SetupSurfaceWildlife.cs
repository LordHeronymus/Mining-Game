using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupSurfaceWildlife
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Install outside Play mode.");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Background/BirdSilhouette.mat");
        if (!map || !sky || !player || !material) throw new Exception("Map, sky, player or material missing.");
        var root = GameObject.Find("Surface Wildlife");
        if (!root)
        {
            root = new GameObject("Surface Wildlife"); Undo.RegisterCreatedObjectUndo(root, "Add surface wildlife");
            var parent = GameObject.Find("Map Objects"); if (parent) root.transform.SetParent(parent.transform, false);
        }
        foreach (SurfaceCritters.Species species in Enum.GetValues(typeof(SurfaceCritters.Species)))
        {
            string name = species == SurfaceCritters.Species.Frog ? "Frogs" : "Snails";
            var child = root.transform.Find(name);
            if (child) continue;
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Add " + name);
            var animals = Undo.AddComponent<SurfaceCritters>(go);
            animals.species = species; animals.map = map; animals.sky = sky; animals.player = player.transform; animals.material = material;
            if (species == SurfaceCritters.Species.Snail)
            { animals.size = .7f; animals.bodyColor = new Color(.62f, .40f, .16f); animals.restDuration = new Vector2(3, 6); }
            EditorUtility.SetDirty(animals);
        }
        if (!root.transform.Find("Fireflies"))
        {
            string path = "Assets/GameObjects/Background/FireflyGlow.mat";
            var glow = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!glow)
            {
                var shader = Shader.Find("Mining/Firefly Glow"); if (!shader) throw new Exception("Glow shader missing.");
                glow = new Material(shader); AssetDatabase.CreateAsset(glow, path);
            }
            var go = new GameObject("Fireflies"); go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Add fireflies");
            var flies = Undo.AddComponent<SurfaceFireflies>(go);
            flies.map = map; flies.sky = sky; flies.material = glow;
            EditorUtility.SetDirty(flies);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.scene);
        return "Installed Map Objects / Surface Wildlife: Frogs, Snails, Fireflies.";
    }
}
