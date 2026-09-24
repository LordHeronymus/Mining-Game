using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;

public static class MovementTestMultiplierChecks
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static object Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        string path = GameplayTestSettings.FilePath;
        string original = File.Exists(path) ? File.ReadAllText(path) : null;
        var debug = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var stats = UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        Check(debug && player && stats, "Debug panel or player is missing.");
        bool debugWasOpen = GameplayDebugPanel.IsOpen;
        var body = player.GetComponent<Rigidbody2D>();
        Vector2 oldVelocity = body.linearVelocity;
        float oldAccelerationTime = player.accelTime;
        float oldGroundedCoeff = player.groundedCoeff;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var inputX = typeof(PlayerMovement).GetField("inputX", flags);
        var inputY = typeof(PlayerMovement).GetField("inputY", flags);
        var previousRun = typeof(PlayerMovement).GetField("previousRunVelocity", flags);
        var fixedUpdate = typeof(PlayerMovement).GetMethod("FixedUpdate", flags);
        var syncFlyMode = typeof(PlayerMovement).GetMethod("SyncFlyMode", flags);
        var reset = typeof(GameplayTestSettings).GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic);
        bool oldFly = GameplayTestSettings.GetConfiguredMode(GameplayTestMode.Fly);
        try
        {
            if (!GameplayDebugPanel.IsOpen) debug.Toggle();
            var window = debug.GetComponent<GameplayDebugWindow>();
            window.SwitchTab(true);
            var movementInput = debug.transform.Find("Card/WindowViewport/WindowContent/MovementMultiplier")
                .GetComponent<TMP_InputField>();
            movementInput.text = "2.5";
            Check(window.ApplyTestInput(), "Movement multiplier could not be applied.");
            Check(GameplayTestSettings.ConfiguredMovementMultiplier == 2.5f && !GameplayTestSettings.HasUnsavedChanges,
                "Movement multiplier was not saved.");
            var saved = JsonUtility.FromJson<GameplayTestSettingsData>(File.ReadAllText(path));
            Check(saved.version == 3 && saved.movementMultiplier == 2.5f, "Saved test settings omit movement multiplier.");

            movementInput.text = "21";
            Check(!window.ApplyTestInput() && GameplayTestSettings.ConfiguredMovementMultiplier == 2.5f,
                "Out-of-range multiplier was accepted.");
            movementInput.text = "2.5";
            Check(GameplayTestSettings.SetMode(GameplayTestMode.Active, false, out string error), error);
            Check(GameplayTestSettings.MovementMultiplier == 1f && stats.EffectiveMoveSpeed == stats.MoveSpeed,
                "Inactive test mode changed movement speed.");
            Check(GameplayTestSettings.SetMode(GameplayTestMode.Active, true, out error), error);
            Check(Mathf.Approximately(stats.EffectiveMoveSpeed, stats.MoveSpeed * 2.5f),
                "Active movement speed does not use the multiplier.");
            debug.Close();
            Check(!GameplayInputBlocker.IsBlocked, "Gameplay is still blocked after closing debug panel.");

            Check(GameplayTestSettings.SetMode(GameplayTestMode.Fly, true, out error), error);
            syncFlyMode.Invoke(player, null);
            player.accelTime = .001f;
            player.groundedCoeff = 1f;
            body.linearVelocity = Vector2.zero;
            inputX.SetValue(player, 1f);
            inputY.SetValue(player, 1f);
            previousRun.SetValue(player, 0f);
            fixedUpdate.Invoke(player, null);
            Check(Mathf.Approximately(body.linearVelocity.x, stats.MoveSpeed * 2.5f) &&
                Mathf.Approximately(body.linearVelocity.y, stats.MoveSpeed * 2.5f),
                $"Horizontal or flying movement did not use the multiplier: {body.linearVelocity}, expected {stats.MoveSpeed * 2.5f}.");

            File.WriteAllText(path, "{\"version\":2,\"diggingMultiplier\":7,\"testModeDisabled\":false}");
            reset.Invoke(null, null);
            Check(GameplayTestSettings.ConfiguredMovementMultiplier == 1f &&
                GameplayTestSettings.ConfiguredDiggingMultiplier == 7f,
                "Existing version-2 test settings did not migrate to a 1× movement factor.");
            return "PASS: debug input, persistence, test-mode gate, real horizontal/flying velocity and version-2 migration.";
        }
        finally
        {
            player.accelTime = oldAccelerationTime;
            player.groundedCoeff = oldGroundedCoeff;
            inputX.SetValue(player, 0f);
            inputY.SetValue(player, 0f);
            previousRun.SetValue(player, 0f);
            GameplayTestSettings.SetMode(GameplayTestMode.Fly, oldFly, out _);
            fixedUpdate.Invoke(player, null);
            body.linearVelocity = oldVelocity;
            if (original == null) File.Delete(path);
            else File.WriteAllText(path, original);
            reset.Invoke(null, null);
            _ = GameplayTestSettings.ConfiguredMovementMultiplier;
            if (debugWasOpen && !GameplayDebugPanel.IsOpen) debug.Toggle();
        }
    }
}
