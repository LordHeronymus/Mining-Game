using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public static class FrogAudioChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode required");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        SurfaceCritters frogs = null;
        foreach (var group in UnityEngine.Object.FindObjectsByType<SurfaceCritters>(FindObjectsSortMode.None))
            if (group.species == SurfaceCritters.Species.Frog) frogs = group;
        if (!frogs) throw new Exception("Frog population missing");
        var type = typeof(SurfaceCritters);
        var animals = (IList)type.GetField("animals", flags).GetValue(frogs);
        var animalType = type.GetNestedType("Critter", BindingFlags.NonPublic);
        var animal = Activator.CreateInstance(animalType, true);
        var camera = Camera.main;
        var center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, -camera.transform.position.z));
        animalType.GetField("x").SetValue(animal, center.x);
        animalType.GetField("scale").SetValue(animal, 1f);
        animalType.GetField("age").SetValue(animal, 1f);
        var update = type.GetMethod("UpdateCroaking", flags);
        var sourceField = type.GetField("croakSource", flags);
        var surface = type.GetField("surfaceY", flags);
        var oldY = surface.GetValue(frogs);
        float oldRate = frogs.croaksPerMinute, oldVolume = frogs.croakVolume;
        var oldSpecies = frogs.species;
        var oldAnimals = new object[animals.Count]; animals.CopyTo(oldAnimals, 0);
        try
        {
            type.GetMethod("StopCroaking", flags).Invoke(frogs, null);
            animals.Clear(); animals.Add(animal); surface.SetValue(frogs, center.y);
            frogs.croakVolume = .35f; frogs.croaksPerMinute = 12;
            update.Invoke(frogs, new object[] { 1f });
            var source = (AudioSource)sourceField.GetValue(frogs);
            if (!source || !source.isPlaying || !source.clip) throw new Exception("Visible frog did not croak: source=" + (source != null) + ", playing=" + (source && source.isPlaying) + ", clip=" + (source && source.clip ? source.clip.name + "/" + source.clip.loadState : "none") + ", manager=" + (AudioManager.Instance != null) + ", visible=" + type.GetMethod("IsFrogVisible", flags).Invoke(frogs, new object[] { animal, camera }));
            animalType.GetField("x").SetValue(animal, center.x + 10000f);
            update.Invoke(frogs, new object[] { .1f });
            if (source.isPlaying) throw new Exception("Offscreen frog kept croaking");
            animalType.GetField("x").SetValue(animal, center.x);
            frogs.croaksPerMinute = 0; update.Invoke(frogs, new object[] { 1f });
            if (source.isPlaying) throw new Exception("Zero rate did not silence frog");
            frogs.croaksPerMinute = 12; frogs.croakVolume = 0;
            update.Invoke(frogs, new object[] { 1f });
            if (source.isPlaying) throw new Exception("Zero volume did not silence frog");
            frogs.croakVolume = .35f; frogs.species = SurfaceCritters.Species.Snail;
            update.Invoke(frogs, new object[] { 1f });
            if (source.isPlaying) throw new Exception("Snail croaked");
            return "PASS: visible frog playback, offscreen stop, zero rate, zero volume, silent snails";
        }
        finally
        {
            type.GetMethod("StopCroaking", flags).Invoke(frogs, null);
            animals.Clear(); foreach (var original in oldAnimals) animals.Add(original);
            surface.SetValue(frogs, oldY); frogs.croaksPerMinute = oldRate;
            frogs.croakVolume = oldVolume; frogs.species = oldSpecies;
        }
    }
}
