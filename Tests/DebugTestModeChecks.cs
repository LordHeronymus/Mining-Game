using System;
using System.Threading.Tasks;
using UnityEngine;

public static class DebugTestModeChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static async Task<object> Main()
    {
        bool active = GameplayTestSettings.GetConfiguredMode(GameplayTestMode.Active);
        bool noClip = GameplayTestSettings.GetConfiguredMode(GameplayTestMode.NoClip);
        bool globalLighting = GameplayTestSettings.GetConfiguredMode(GameplayTestMode.GlobalLighting);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var collider = player.GetComponent<Collider2D>();
        var lighting = UnityEngine.Object.FindFirstObjectByType<MoonlightController>();
        Check(player && collider && lighting && lighting.daylight, "Player or global light is missing.");

        try
        {
            Check(GameplayTestSettings.SetMode(GameplayTestMode.Active, true, out var error), error);
            Check(GameplayTestSettings.SetMode(GameplayTestMode.NoClip, true, out error), error);
            Check(GameplayTestSettings.SetMode(GameplayTestMode.GlobalLighting, false, out error), error);
            await WaitFrames(3);
            lighting.Refresh();
            Check(!collider.enabled, "No Clip did not disable the player collider.");
            Check(player.IsFlying, "No Clip did not enable free movement.");
            Check(!lighting.daylight.enabled, "Global Lighting did not disable the global light.");

            Check(GameplayTestSettings.SetMode(GameplayTestMode.NoClip, false, out error), error);
            Check(GameplayTestSettings.SetMode(GameplayTestMode.GlobalLighting, true, out error), error);
            await WaitFrames(3);
            lighting.Refresh();
            Check(collider.enabled, "Player collider was not restored.");
            Check(lighting.daylight.enabled, "Global light was not restored.");
            return new { passed = true, noClip = true, globalLighting = true, persisted = !GameplayTestSettings.HasUnsavedChanges };
        }
        finally
        {
            GameplayTestSettings.SetMode(GameplayTestMode.NoClip, noClip, out _);
            GameplayTestSettings.SetMode(GameplayTestMode.GlobalLighting, globalLighting, out _);
            GameplayTestSettings.SetMode(GameplayTestMode.Active, active, out _);
        }
    }

    static Task WaitFrames(int count)
    {
        var source = new TaskCompletionSource<bool>();
        var waiter = new GameObject("Debug test waiter").AddComponent<FrameWaiter>();
        waiter.frames = count;
        waiter.source = source;
        return source.Task;
    }

    sealed class FrameWaiter : MonoBehaviour
    {
        public int frames;
        public TaskCompletionSource<bool> source;
        void Update()
        {
            if (--frames > 0) return;
            source.TrySetResult(true);
            Destroy(gameObject);
        }
    }
}
