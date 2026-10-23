using System;
using UnityEngine;

// One-time setup (Edit Mode, after the GPS profile picked up the new clip): default hover-click volume 0,2.
public static class UiHoverClickGpsSetup
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Requires Edit Mode");
        var clip = Resources.Load<AudioClip>("Audio/UiHoverClick");
        if (!clip) throw new Exception("Clip Resources/Audio/UiHoverClick missing");
        string key = GpsSettings.Profile.Key(clip);
        if (key == null) throw new Exception("Clip not registered in GPS profile (run Mining Game → GPS-Profil aktualisieren)");
        if (!GpsAudio.Change(key, "volume", .2, out string error, commit: true)) throw new Exception(error);
        GpsSettings.CommitPendingNow();
        var tune = GpsAudio.Tuning(clip);
        return "PASS: UiHoverClick GPS volume " + tune?.volume + ", category " + tune?.category + ", name " + tune?.displayName;
    }
}
