using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MusicRoutingChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play required");
        int checks = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks++; }
        float music = PlayerSettings.Music, master = AudioListener.volume;
        var clip = AudioClip.Create("Music routing probe", 44100, 1, 44100, false);
        var loading = Object.FindFirstObjectByType<LoadingAudio>();
        var update = typeof(LoadingAudio).GetMethod("UpdateHome", BindingFlags.Instance | BindingFlags.NonPublic);
        var sources = (AudioSource[])typeof(LoadingAudio).GetField("homeSources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading);
        try
        {
            Check(JsonUtility.FromJson<GpsDocument>("{\"preferences\":{\"masterVolume\":0.7}}").preferences.musicVolume == 1f,
                "Old profiles retain audible default music");
            GpsSettings.Preferences.musicVolume = 1f;
            float ambience = AudioManager.TunedAmbienceVolume(clip, .6f);
            float effects = AudioManager.TunedVolume(clip, .6f);
            update.Invoke(loading, null);
            float fullHome = sources.Sum(s => s.volume);
            Check(fullHome > 0, "Real homescreen music is audible at full music gain");
            GpsSettings.Preferences.musicVolume = .25f;
            update.Invoke(loading, null);
            Check(Mathf.Abs(sources.Sum(s => s.volume) / fullHome - .25f) < .01f, "Live homescreen sources follow music gain");
            Check(Mathf.Approximately(AudioManager.TunedMusicVolume(clip, .6f), .15f), "Music category scales independently");
            GpsSettings.Preferences.musicVolume = 0f;
            update.Invoke(loading, null);
            Check(sources.All(s => s.volume == 0), "Music zero mutes actual soundtrack sources");
            Check(AudioManager.TunedAmbienceVolume(clip, .6f) == ambience && AudioManager.TunedVolume(clip, .6f) == effects,
                "Muting music preserves ambience and effects");
            Check(AudioListener.volume == master, "Music does not alter master volume");
            File.WriteAllText("Temp/MusicRoutingChecks.txt", checks + " checks PASS");
            return new { checks, result = "PASS" };
        }
        finally
        {
            GpsSettings.Preferences.musicVolume = music;
            update.Invoke(loading, null);
            Object.Destroy(clip);
        }
    }
}
