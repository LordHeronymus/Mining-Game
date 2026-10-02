using System;

public static class PerformanceMonitorControl
{
    static Func<bool> readEnabled;
    static Action<bool> changeEnabled;

    public static bool IsAvailable => readEnabled != null && changeEnabled != null;
    public static bool IsEnabled => readEnabled != null && readEnabled();

    // The Editor backend survives Play transitions without holding scene objects.
    public static void Configure(Func<bool> read, Action<bool> change)
    {
        readEnabled = read;
        changeEnabled = change;
    }

    public static void SetEnabled(bool enabled) => changeEnabled?.Invoke(enabled);
}
