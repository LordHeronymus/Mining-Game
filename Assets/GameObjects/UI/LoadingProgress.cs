using System;
using System.Collections.Generic;
using UnityEngine;

public static class LoadingProgress
{
    public static readonly string[] Labels = { "Spielszene laden", "Terrain aufbauen", "Höhlen formen",
        "Erzadern verteilen", "Artefakte platzieren", "Altarkammer aufbauen", "Beleuchtung vorbereiten", "Spieler und UI bereitmachen" };
    static readonly float[] Defaults = { .15f, 1f, .5f, 2f, 1f, .3f, .7f, .2f };
    static readonly string[] RestoreLabels = { "Spielszene laden", "Terrain wiederherstellen", "Erze wiederherstellen",
        "Artefakte wiederherstellen", "Leitern wiederherstellen", "Spielwelt wiederherstellen", "Beleuchtung vorbereiten", "Spiel fortsetzen" };
    static readonly float[] RestoreDefaults = { .15f, 2f, 1f, .5f, .1f, .7f, .7f, .2f };
    public static bool Restoring { get; private set; }
    public static string CurrentLabel => (Restoring ? RestoreLabels : Labels)[Stage];
    const string Registry = "LoadingCalibration.Keys.v1";
    static float[] weights = new float[8], durations = new float[8];
    static string profile;
    static double started;
    static float total;
    public static int Stage { get; private set; }
    public static float Target { get; private set; }
    public static bool Ready { get; private set; }
    public static bool Active { get; private set; }
    // The batchmode editor (cloud checks, Docs/Cloud-Tests.md) never renders a Game view, so WaitForEndOfFrame
    // would never resume there; the next frame is close enough for loading and fades.
    static readonly WaitForEndOfFrame endOfFrame = new();
    public static object EndOfFrame => Application.isBatchMode ? null : endOfFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        Stage = 0; Target = 0; Ready = false; Active = false; profile = null;
    }

    public static void Begin(bool restoring = false)
    {
        Restoring = restoring;
        Active = true; Ready = false; Stage = 0; Target = 0f; profile = null;
        Array.Clear(durations, 0, durations.Length);
        started = Time.realtimeSinceStartupAsDouble;
        Configure(0, 0);
    }

    public static void Configure(int width, int height)
    {
        profile = $"LoadingCalibration.v1.{(Restoring ? "Restore." : "")}{width}x{height}";
        var defaults = Restoring ? RestoreDefaults : Defaults;
        total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            float value = PlayerPrefs.GetFloat(profile + "." + i, defaults[i]);
            weights[i] = float.IsNaN(value) || float.IsInfinity(value) ? defaults[i] : Mathf.Clamp(value, .005f, 600f);
            total += weights[i];
        }
        // Old scene-load timings must not dominate progress after the preview
        // terrain was removed from the scene. Reserve at most 3% for this phase.
        float sceneWeight = Mathf.Min(weights[0], (total - weights[0]) * .03f / .97f);
        total += sceneWeight - weights[0]; weights[0] = sceneWeight;
    }

    public static void SetStage(int stage)
    {
        if (!Active || stage == Stage) return;
        durations[Stage] += (float)(Time.realtimeSinceStartupAsDouble - started);
        Report(1f);
        Stage = stage; started = Time.realtimeSinceStartupAsDouble;
    }

    public static void Report(float fraction)
    {
        if (!Active) return;
        float complete = 0f;
        for (int i = 0; i < Stage; i++) complete += weights[i];
        Target = Mathf.Max(Target, Mathf.Min(.995f, (complete + weights[Stage] * Mathf.Clamp01(fraction)) / total));
    }

    // Called once per frame by the loading screen. Stages report progress in bursts (or only at their end);
    // estimating from the calibrated stage duration keeps the bar moving between reports. The estimate
    // approaches the end of the stage asymptotically, so a slower-than-calibrated stage never overshoots.
    public static void Advance()
    {
        if (!Active || Ready || total <= 0f) return;
        float expected = Mathf.Max(.05f, weights[Stage]);
        float elapsed = (float)(Time.realtimeSinceStartupAsDouble - started);
        Report(.95f * (1f - Mathf.Exp(-1.8f * elapsed / expected)));
    }

    public static void Complete()
    {
        if (!Active || Ready) return;
        durations[Stage] += (float)(Time.realtimeSinceStartupAsDouble - started);
        var keys = new HashSet<string>(PlayerPrefs.GetString(Registry, "").Split('|'));
        for (int i = 0; i < durations.Length; i++)
        {
            string key = profile + "." + i;
            float sample = Mathf.Max(.005f, durations[i]);
            float previous = PlayerPrefs.GetFloat(key, sample);
            PlayerPrefs.SetFloat(key, Mathf.Lerp(previous, Mathf.Clamp(sample, previous * .25f, previous * 4f), .25f));
            keys.Add(key);
        }
        keys.Remove(""); PlayerPrefs.SetString(Registry, string.Join("|", keys)); PlayerPrefs.Save();
        Target = 1f; Ready = true;
    }

    public static void Dismiss() => Active = false;
    public static void ResetCalibration()
    {
        foreach (string key in PlayerPrefs.GetString(Registry, "").Split('|'))
            if (key.StartsWith("LoadingCalibration.v1.", StringComparison.Ordinal)) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.DeleteKey(Registry); PlayerPrefs.Save();
    }
}
