using System.Diagnostics;

// A loading slice leaves time for the UI, rendering and engine-side Tilemap work.
public sealed class LoadingWorkBudget
{
    const double Milliseconds = 4;
    long began = Stopwatch.GetTimestamp();
    public bool Expired => (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency >= Milliseconds;
    public void Restart() => began = Stopwatch.GetTimestamp();
}
