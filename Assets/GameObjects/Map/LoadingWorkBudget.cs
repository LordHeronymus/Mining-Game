using System.Diagnostics;
using UnityEngine;

// A loading slice leaves time for the UI, rendering and engine-side Tilemap work.
public sealed class LoadingWorkBudget
{
    public const double TargetFrameMilliseconds = 1000.0 / 60;
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
        milliseconds = 4; frame = -1; EnsureFrame();
    }
    public static void ObserveFrame(float seconds)
    {
        // Allow a small timing tolerance around the 60 Hz refresh interval.
        double elapsed = seconds * 1000;
        if (elapsed > TargetFrameMilliseconds + .5) milliseconds = System.Math.Max(.75, milliseconds * .8);
        else if (elapsed <= TargetFrameMilliseconds + .25) milliseconds = System.Math.Min(4, milliseconds + .1);
    }
}
