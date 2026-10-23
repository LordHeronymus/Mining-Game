using System;
using UnityEngine;

// Requires Play Mode. Checks that the artifact find sound has its own F1 volume, independent of Ding Light.
public static class ArtifactFoundVolumeChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Requires Play Mode");
        var audio = AudioManager.Instance;
        if (!audio) throw new Exception("AudioManager missing");
        var clip = Resources.Load<AudioClip>("Audio/ArtifactCollected");
        if (!clip) throw new Exception("Clip Resources/Audio/ArtifactCollected missing");
        var tune = GpsAudio.Tuning(clip);
        if (tune == null) throw new Exception("GPS audio entry for ArtifactCollected missing");
        if (Mathf.Abs(tune.volume - 1f) > .001f) throw new Exception("GPS clip volume " + tune.volume + ", expected 1");

        float found = audio.GetVolume(AudioVolumeSetting.ArtifactFound);
        float ding = audio.GetVolume(AudioVolumeSetting.DingLight);
        if (Mathf.Abs(found - .6f) > .001f) throw new Exception("Artefakt-Fund volume " + found + ", expected 0.6");
        if (Mathf.Abs(ding - .35f) > .001f) throw new Exception("Ding Light volume changed: " + ding);
        var section = GpsSchema.Sections.Find(s => s.tab == "Audio" && s.title == "Artefakt-Fund" && s.source == typeof(AudioManager));
        if (section == null || Array.Find(section.fields, f => f.name == "artifactFoundVolume")?.label != "Lautstärke (%)")
            throw new Exception("F1 field Audio → Artefakt-Fund → Lautstärke (%) missing");

        try
        {
            audio.SetVolume(AudioVolumeSetting.ArtifactFound, .9f);
            if (Mathf.Abs(audio.GetVolume(AudioVolumeSetting.ArtifactFound) - .9f) > .001f) throw new Exception("Artefakt-Fund volume not settable");
            if (Mathf.Abs(audio.GetVolume(AudioVolumeSetting.DingLight) - ding) > .001f) throw new Exception("Changing Artefakt-Fund changed Ding Light");
        }
        finally { audio.SetVolume(AudioVolumeSetting.ArtifactFound, found); }

        float before = ding * .1699f, after = GpsAudio.Volume(clip, found);
        return "PASS: Artefakt-Fund " + (found * 100f).ToString("0") + " %, clip 100 %, effective " + after.ToString("0.00")
            + " (before " + before.ToString("0.00") + ", x" + (after / before).ToString("0.0") + "); Ding Light unchanged " + (ding * 100f).ToString("0") + " %";
    }
}
