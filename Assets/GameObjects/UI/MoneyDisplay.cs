using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Money labels (HUD, shop) count towards StatsManager.Money with an ease-out instead of jumping.
// All labels share one shown value, so HUD and shop always show the same number. Duration: F1 → UI → Geldanzeige.
[DisallowMultipleComponent]
public sealed class MoneyDisplay : MonoBehaviour
{
    public const float MaxCountSeconds = 5f;
    static readonly HashSet<MoneyDisplay> labels = new();
    static StatsManager source;
    static double from, to;
    static float startTime;
    TMP_Text text;
    long lastShown = long.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() { labels.Clear(); source = null; from = to = 0; }

    public static float CountSeconds
    {
        get
        {
            float seconds = GpsSettings.Preferences.moneyCountSeconds;
            return float.IsNaN(seconds) || float.IsInfinity(seconds) ? .6f : Mathf.Clamp(seconds, 0f, MaxCountSeconds);
        }
    }

    // The amount the labels show this frame; a new StatsManager.Money starts counting from the value shown now.
    public static long Shown
    {
        get
        {
            var stats = StatsManager.Instance;
            if (!stats) return 0;
            if (stats != source) Snap(stats);
            else if (stats.Money != to) { from = Evaluate(); to = stats.Money; startTime = Time.unscaledTime; }
            return (long)Math.Round(Evaluate());
        }
    }
    public static bool IsCounting => source && Evaluate() != to;

    // Run start and loaded saves show their amount at once instead of counting from the previous run.
    public static void Snap(StatsManager stats)
    {
        source = stats;
        from = to = stats ? stats.Money : 0;
    }
    // Ends a running count at once and refreshes every label (tests, screenshots).
    public static void Finish()
    {
        Snap(StatsManager.Instance);
        foreach (var label in labels) label.Refresh();
    }
    public static MoneyDisplay Bind(TMP_Text label)
    {
        if (!label) return null;
        var display = label.GetComponent<MoneyDisplay>() ?? label.gameObject.AddComponent<MoneyDisplay>();
        display.Refresh();
        return display;
    }

    static double Evaluate()
    {
        float duration = CountSeconds;
        float t = duration > 0f ? Mathf.Clamp01((Time.unscaledTime - startTime) / duration) : 1f;
        t = 1f - (1f - t) * (1f - t);
        return t >= 1f ? to : from + (to - from) * t;
    }

    void Awake() => text = GetComponent<TMP_Text>();
    void OnEnable() { labels.Add(this); Refresh(); }
    void OnDisable() => labels.Remove(this);
    void LateUpdate() => Refresh();
    public void Refresh()
    {
        if (!text) text = GetComponent<TMP_Text>();
        long shown = Shown;
        if (!text || shown == lastShown) return;
        lastShown = shown;
        text.text = ShopMoneyFormatter.Format(shown);
    }
}
