using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetWildlifePalette
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Apply the palette outside Play mode.");
        int rabbits = 0, critters = 0;
        foreach (var rabbit in UnityEngine.Object.FindObjectsByType<SurfaceRabbit>(FindObjectsSortMode.None))
        {
            var color = new Color(.94f, .955f, .98f);
            if (rabbit.furColor != color)
            { Undo.RecordObject(rabbit, "Whiter rabbit"); rabbit.furColor = color; EditorUtility.SetDirty(rabbit); }
            rabbits++;
        }
        foreach (var animal in UnityEngine.Object.FindObjectsByType<SurfaceCritters>(FindObjectsSortMode.None))
        {
            var body = animal.species == SurfaceCritters.Species.Frog ? new Color(.10f, .52f, .025f) : new Color(.62f, .40f, .16f);
            var shell = new Color(.58f, .20f, .045f);
            if (animal.bodyColor != body || animal.shellColor != shell)
            {
                Undo.RecordObject(animal, "Vivid wildlife colours");
                animal.bodyColor = body; animal.shellColor = shell; EditorUtility.SetDirty(animal);
            }
            critters++;
        }
        if (rabbits == 0 || critters != 2) throw new Exception("Expected rabbit template and both critter groups.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        return new { rabbits, critters, saved = EditorSceneManager.SaveScene(scene) };
    }
}
