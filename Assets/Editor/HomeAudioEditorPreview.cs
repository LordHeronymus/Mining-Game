using UnityEditor;
using UnityEngine;
using System.Reflection;

public static class HomeAudioEditorPreview
{
    static AudioClip rendered;
    static System.Type AudioUtil => typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    public static void Play(HomeAudioClipTuning tuning)
    {
        if (tuning == null || !tuning.clip) return;
        if (Application.isPlaying) { LoadingAudio.PreviewHomeClip(tuning); return; }
        Stop();
        var clip = tuning.clip;
        clip.LoadAudioData();
        var input = new float[clip.samples * clip.channels];
        if (!clip.GetData(input, 0)) return;
        float pitch = AudioManager.TunedPitch(clip, tuning.SamplePitch());
        float volume = AudioManager.TunedAmbienceVolume(clip,
            AudioManager.AmbienceVolume * tuning.SampleVolume()) * PlayerSettings.Master;
        int frames = Mathf.Max(1, Mathf.FloorToInt(clip.samples / pitch));
        var output = new float[frames * clip.channels];
        for (int frame = 0; frame < frames; frame++)
        {
            float position = frame * pitch;
            int first = Mathf.Min((int)position, clip.samples - 1);
            int second = Mathf.Min(first + 1, clip.samples - 1);
            for (int channel = 0; channel < clip.channels; channel++)
                output[frame * clip.channels + channel] = Mathf.Lerp(input[first * clip.channels + channel],
                    input[second * clip.channels + channel], position - first) * volume;
        }
        rendered = AudioClip.Create("Homescreen Preview", frames, clip.channels, clip.frequency, false);
        rendered.hideFlags = HideFlags.HideAndDontSave;
        rendered.SetData(output, 0);
        AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null)?.Invoke(null, new object[] { rendered, 0, false });
        EditorApplication.update -= Cleanup;
        EditorApplication.update += Cleanup;
    }

    static void Cleanup()
    {
        var playing = AudioUtil?.GetMethod("IsPreviewClipPlaying", BindingFlags.Public | BindingFlags.Static);
        if (playing != null && !(bool)playing.Invoke(null, null)) Stop();
    }

    public static void Stop()
    {
        EditorApplication.update -= Cleanup;
        if (!rendered) return;
        AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        Object.DestroyImmediate(rendered);
        rendered = null;
    }
}
