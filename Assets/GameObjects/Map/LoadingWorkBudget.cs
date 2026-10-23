using System.Diagnostics;
using UnityEngine;

// A loading slice leaves time for the UI, rendering and engine-side Tilemap work.
// The slice adapts so that whole loading frames stay within a 120 Hz frame (8.3 ms), or within the display
// interval when V-Sync is on and the display is faster (144 Hz: 6.9 ms), so no refresh is skipped.
public sealed class LoadingWorkBudget
{
    public static double TargetFrameMilliseconds
    {
        get
        {
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            if (QualitySettings.vSyncCount <= 0 || refresh <= 0) return 1000.0 / 120;
            return System.Math.Min(1000.0 / 120, 1000.0 * QualitySettings.vSyncCount / refresh);
        }
    }
    // At most half of a frame: the rest is left for rendering and the engine-side Tilemap work.
    static double MaxMilliseconds => System.Math.Min(4, TargetFrameMilliseconds * .5);
    static double milliseconds = 4;
    static int frame = -1, queuedCells;
    static long began;

    public LoadingWorkBudget() => EnsureFrame();
    public bool Expired
    {
        get
        {
            EnsureFrame();
            return (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency >= milliseconds ||
                queuedCells >= Mathf.Max(1024, (int)(8192 * milliseconds / 4));
        }
    }
    // All preparation stages share the same allowance within a rendered frame.
    public void Restart() => EnsureFrame();
    public void ChargeTiles(int cells) { EnsureFrame(); queuedCells += cells; }
    static void EnsureFrame()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount; began = Stopwatch.GetTimestamp(); queuedCells = 0;
    }
    public static void Begin()
    {
        milliseconds = MaxMilliseconds; frame = -1; EnsureFrame();
    }
    public static void ObserveFrame(float seconds)
    {
        // Allow a small timing tolerance around the target frame interval.
        double elapsed = seconds * 1000;
        if (elapsed > TargetFrameMilliseconds + .5) milliseconds = System.Math.Max(.75, milliseconds * .8);
        else if (elapsed <= TargetFrameMilliseconds + .25) milliseconds = System.Math.Min(MaxMilliseconds, milliseconds + .1);
    }
}
