using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class LoadingPickaxeFadeChecks
{
    public static Task<string> Main()
    {
        if (!EditorApplication.isPlaying || LoadingProgress.Active)
            throw new InvalidOperationException("Run after loading has finished.");
        var result = new TaskCompletionSource<string>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        bool home = MainMenuController.IsVisible;
        var originalAudio = UnityEngine.Object.FindFirstObjectByType<LoadingAudio>();
        object originalCompleted = originalAudio ? typeof(LoadingAudio).GetField("completedAt", flags).GetValue(originalAudio) : -1f;
        object originalFadeIn = originalAudio ? typeof(LoadingAudio).GetField("yogaFadeInAt", flags).GetValue(originalAudio) : -1f;
        var clip = AudioClip.Create("Isolated loading audio tail", 44100 * 10, 1, 44100, false);
        LoadingAudio.Begin();
        LoadingAudio.PlayLoadingPickaxe(clip);
        var audio = UnityEngine.Object.FindFirstObjectByType<LoadingAudio>();
        var source = (AudioSource)typeof(LoadingAudio).GetField("loadingPickaxe", flags).GetValue(audio);
        var visuals = UnityEngine.Object.FindObjectsByType<LoadingCrystalVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (visuals.Length == 0) throw new InvalidOperationException("No prepared loading visual.");
        LoadingAudio.Complete();
        typeof(LoadingCrystalVisual).GetMethod("OnDisable", flags).Invoke(visuals[0], null);
        double began = EditorApplication.timeSinceStartup;
        float prior = 1f;
        bool checkedTail = false;
        EditorApplication.CallbackFunction poll = null;
        poll = () =>
        {
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - began;
                if (elapsed < .08) return;
                if (source.gameObject != audio.gameObject || !source.gameObject.activeInHierarchy)
                    throw new Exception("Impact source did not survive visual disable.");
                float expected = (float)typeof(LoadingAudio).GetMethod("LoadingFadeOutGain", flags).Invoke(audio, null);
                if (Mathf.Abs(source.volume - expected) > .02f || source.volume > prior + .001f)
                    throw new Exception($"Impact and ambience fade envelopes differ: source={source.volume}, expected={expected}, prior={prior}, elapsed={elapsed}, stopping={GameAudioLifecycle.IsStopping}.");
                prior = source.volume;
                if (elapsed > .9 && expected > .05f)
                {
                    if (!source.isPlaying || source.volume >= .999f)
                        throw new Exception("Impact tail stopped at visual completion or did not fade.");
                    checkedTail = true;
                }
                if (elapsed < LoadingAudio.Settings.yogaFadeOutSeconds + .15f) return;
                if (!checkedTail || source.isPlaying || source.volume > .001f)
                    throw new Exception("Fade did not finish cleanly.");
                Finish("PASS: Impact survives visual disable, shares the ambience fade envelope, decreases smoothly and stops only at silence.");
            }
            catch (Exception e) { Finish("FAIL: " + e.Message); }
        };
        void Finish(string message)
        {
            EditorApplication.update -= poll;
            source.Stop();
            if (home) LoadingAudio.StartHome();
            else
            {
                typeof(LoadingAudio).GetField("completedAt", flags).SetValue(audio, originalCompleted);
                typeof(LoadingAudio).GetField("yogaFadeInAt", flags).SetValue(audio, originalFadeIn);
                typeof(LoadingAudio).GetMethod("ApplyVolume", flags).Invoke(audio, null);
            }
            UnityEngine.Object.Destroy(clip);
            System.IO.File.WriteAllText("Temp/LoadingPickaxeFadeChecks.txt", message);
            result.SetResult(message);
        }
        EditorApplication.update += poll;
        return result.Task;
    }
}
