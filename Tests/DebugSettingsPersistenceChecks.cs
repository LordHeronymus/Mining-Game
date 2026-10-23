using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Debug-menu edits must be stored at once and survive play-mode restarts, domain reloads and editor restarts.
// Run three times, each from a fresh Play start (CI: local-only, needs several play sessions):
//   1. "changed"  – flips Testmodus and God Mode with the F1 panel buttons, edits the panel alpha in its input field and
//                   a component value, closes the panel and checks that everything is committed and written to the profile.
//   2. "verified" – after Play stop/start (optionally a domain reload or editor restart): values are still the changed ones.
//   3. "restored" – after another restart: values still changed, then the original values are restored and stored again.
// State lives in Temp/DebugSettingsPersistence.json; it is deleted after the restore.
public static class DebugSettingsPersistenceChecks
{
    const string ProfilePath = "Assets/Resources/Gameplay/GpsProfile.asset";
    static string StatePath => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "DebugSettingsPersistence.json");

    [Serializable]
    sealed class State
    {
        public string phase;
        public bool testModeDisabled, godMode;
        public float backdropAlpha;
        public double hitOffset;
    }

    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static async Task<object> Main()
    {
        if (!Application.isPlaying) return "Requires Play mode (fresh Play start).";
        Check(string.IsNullOrEmpty(GameSaveSystem.TestDirectory) && GpsSettings.AutoCommit, "Auto commit is switched off.");
        var state = File.Exists(StatePath) ? JsonUtility.FromJson<State>(File.ReadAllText(StatePath)) : null;
        if (state == null)
        {
            state = new State { phase = "changed", testModeDisabled = Tests.testModeDisabled, godMode = Tests.godMode,
                backdropAlpha = GpsSettings.Preferences.panelBackdropAlpha, hitOffset = HitOffset() };
            File.WriteAllText(StatePath, JsonUtility.ToJson(state));
            await ChangeThroughPanel(state);
            ExpectChanged(state, "right after the edit");
            return new { phase = state.phase, testModeDisabled = Tests.testModeDisabled, godMode = Tests.godMode,
                backdropAlpha = GpsSettings.Preferences.panelBackdropAlpha, hitOffset = HitOffset(), next = "Stop Play, start Play, run again." };
        }
        ExpectChanged(state, "after the restart (" + state.phase + ")");
        if (state.phase == "changed")
        {
            state.phase = "verified"; File.WriteAllText(StatePath, JsonUtility.ToJson(state));
            return new { phase = state.phase, kept = true, next = "Restart again (domain reload or editor), start Play, run again to restore." };
        }
        Restore(state);
        File.Delete(StatePath);
        return new { phase = "restored", kept = true, testModeDisabled = Tests.testModeDisabled, godMode = Tests.godMode,
            backdropAlpha = GpsSettings.Preferences.panelBackdropAlpha, hitOffset = HitOffset() };
    }

    static GameplayTestSettingsData Tests => GpsSettings.Tests;
    static GpsRecord VisualRecord => GpsSettings.Document.records.Find(record => record.type == typeof(MinerPlayerVisual).AssemblyQualifiedName);
    static double HitOffset() => VisualRecord.fields.Find(field => field.name == "miningHitOffsetMs").number;
    static float ChangedAlpha(State state) => state.backdropAlpha > .5f ? .25f : .75f;
    static double ChangedOffset(State state) => state.hitOffset + (state.hitOffset > 400 ? -7 : 7);

    static async Task ChangeThroughPanel(State state)
    {
        var before = File.GetLastWriteTimeUtc(ProfilePath);
        GpsRuntimePanel.Open();
        try
        {
            var panel = UnityEngine.Object.FindFirstObjectByType<GpsRuntimePanel>();
            panel.SelectTab(GpsSchema.TestTab); await WaitFrames(2);
            Click(panel, "+ Testmodus"); await WaitFrames(2);
            ToggleNextTo(panel, "Testmodus aktiviert"); await WaitFrames(2);
            ToggleNextTo(panel, "God Mode"); await WaitFrames(2);
            panel.SelectTab("UI"); await WaitFrames(2);
            Click(panel, "+ GPS-Panel"); await WaitFrames(2);
            var input = Next(panel, "Hintergrund-Alpha").GetComponent<TMP_InputField>();
            string alpha = ChangedAlpha(state).ToString(System.Globalization.CultureInfo.InvariantCulture);
            input.SetTextWithoutNotify(alpha); input.onEndEdit.Invoke(alpha); await WaitFrames(2);
            var offset = GpsSettings.GetValue(VisualRecord.key, "miningHitOffsetMs"); offset.number = ChangedOffset(state);
            Check(GpsSettings.SetValueAndCommit(VisualRecord.key, offset, out string error), error);
            Check(GpsSettings.HasPendingCommits, "Panel edits were not queued for saving.");
        }
        finally { GpsRuntimePanel.Close(); }
        Check(!GpsSettings.HasPendingCommits, "Closing the panel did not store the queued edits.");
        var committed = JsonUtility.FromJson<GpsDocument>(GpsSettings.CommittedJson);
        Check(committed.tests.testModeDisabled == !state.testModeDisabled && committed.tests.godMode == !state.godMode, "Test switches are not committed.");
        Check(Mathf.Approximately(committed.preferences.panelBackdropAlpha, ChangedAlpha(state)), "Panel alpha is not committed.");
        Check(GpsSettings.Profile.documentJson.Contains("\"testModeDisabled\": " + (!state.testModeDisabled ? "true" : "false")),
            "Profile asset does not hold the new Testmodus value.");
        Check(File.GetLastWriteTimeUtc(ProfilePath) > before, "Profile asset was not written to disk.");
    }

    static void ExpectChanged(State state, string when)
    {
        Check(Tests.testModeDisabled == !state.testModeDisabled, "Testmodus reverted " + when + ".");
        Check(Tests.godMode == !state.godMode, "God Mode reverted " + when + ".");
        Check(Mathf.Approximately(GpsSettings.Preferences.panelBackdropAlpha, ChangedAlpha(state)), "Panel alpha reverted " + when + ".");
        Check(Math.Abs(HitOffset() - ChangedOffset(state)) < .001, "Component value reverted " + when + ".");
    }

    static void Restore(State state)
    {
        void Set(string key, string field, Action<GpsValue> change)
        { var node = GpsSettings.GetValue(key, field); change(node); Check(GpsSettings.SetValueAndCommit(key, node, out string error), error); }
        Set("tests", "testModeDisabled", node => node.flag = state.testModeDisabled);
        Set("tests", "godMode", node => node.flag = state.godMode);
        Set("preferences", "panelBackdropAlpha", node => node.number = state.backdropAlpha);
        Set(VisualRecord.key, "miningHitOffsetMs", node => node.number = state.hitOffset);
        GpsSettings.CommitPendingNow();
        var committed = JsonUtility.FromJson<GpsDocument>(GpsSettings.CommittedJson);
        Check(committed.tests.testModeDisabled == state.testModeDisabled && committed.tests.godMode == state.godMode &&
            Mathf.Approximately(committed.preferences.panelBackdropAlpha, state.backdropAlpha), "Restoring the original values failed.");
    }

    static Transform Content(GpsRuntimePanel panel) => panel.transform.Find("Card/Viewport/Content");
    static void Click(GpsRuntimePanel panel, string name)
    {
        var button = Content(panel).Cast<Transform>().FirstOrDefault(child => child.name == name)?.GetComponent<Button>();
        Check(button, "Panel button missing: " + name); button.onClick.Invoke();
    }
    static Transform Next(GpsRuntimePanel panel, string label)
    {
        var content = Content(panel);
        for (int i = 0; i + 1 < content.childCount; i++)
            if (content.GetChild(i).GetComponent<TextMeshProUGUI>()?.text == label) return content.GetChild(i + 1);
        throw new Exception("Panel row missing: " + label);
    }
    static void ToggleNextTo(GpsRuntimePanel panel, string label)
    {
        var button = Next(panel, label).GetComponent<Button>();
        Check(button, "Toggle missing: " + label); button.onClick.Invoke();
    }

    static Task WaitFrames(int count)
    {
        var source = new TaskCompletionSource<bool>();
        var waiter = new GameObject("Persistence check waiter").AddComponent<FrameWaiter>();
        waiter.frames = count; waiter.source = source;
        return source.Task;
    }
    sealed class FrameWaiter : MonoBehaviour
    {
        public int frames;
        public TaskCompletionSource<bool> source;
        void Update() { if (--frames > 0) return; source.TrySetResult(true); Destroy(gameObject); }
    }
}
