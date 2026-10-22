using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Requires a fresh Play start on the free MainMenu. Uses isolated save/profile directories.
// Settings, pause menu and F1 pause the whole game: time, energy, play time, autosave and challenge timers stand still,
// world audio (ambience loop, voice line) holds its position and continues on close; UI/preview sources keep playing.
// Report: Temp/GamePauseChecks.txt
public static class GamePauseChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked)
            return "Requires fresh Play start on the free MainMenu.";
        var go = new GameObject("Game Pause Probe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GamePauseProbe>();
        return "Started, see Temp/GamePauseChecks.txt";
    }
}

public sealed class GamePauseProbe : MonoBehaviour
{
    const string Report = "Temp/GamePauseChecks.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    string save, meta;
    bool clean, noEnergy;
    int checks;

    void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }
    void Note(string text) => File.AppendAllText(Report, text + "\n");

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/GamePause-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        noEnergy = GameplayTestSettings.NoEnergyConsume;
        File.WriteAllText(Report, "");
        var routine = Run();
        while (true)
        {
            bool more = false; object next = null; Exception error = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; } catch (Exception e) { error = e; }
            if (error != null) { File.AppendAllText(Report, "FAILED " + error + "\n"); break; }
            if (!more) break;
            yield return next;
        }
        if (GpsRuntimePanel.IsOpen) GpsRuntimePanel.Close();
        if (!MainMenuController.IsVisible && !RunNavigation.IsTransitioning) RunNavigation.MainMenu();
        float end = Time.realtimeSinceStartup + 120;
        while ((LoadingProgress.Active || RunNavigation.IsTransitioning) && Time.realtimeSinceStartup < end) yield return null;
        Cleanup();
    }

    IEnumerator Ready()
    {
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Loading timeout");
            yield return null;
        }
        yield return null; yield return null;
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);

    sealed class Snapshot
    {
        public float energy, played, sinceSave; public double active; public int ambience, voice;
    }

    Snapshot Take(EnergyManager energy, AudioSource ambience, AudioSource voice) => new Snapshot
    {
        energy = energy.energy, played = GameSaveSystem.PlayedSeconds,
        sinceSave = RunAutosave.Instance ? RunAutosave.Instance.SecondsSinceSave : 0,
        active = MetaProgression.CurrentRun?.activeSeconds ?? 0,
        ambience = ambience ? ambience.timeSamples : 0, voice = voice && voice.clip ? voice.timeSamples : 0
    };

    IEnumerator Run()
    {
        RunNavigation.NewGame("Pause-Test");
        yield return Ready();
        if (noEnergy) Note("NOTE NoEnergyConsume is on: energy drain after resume not checked");
        var energy = Object.FindFirstObjectByType<EnergyManager>();
        var settings = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        var pause = Object.FindFirstObjectByType<RunPauseMenu>();
        var surface = Object.FindFirstObjectByType<SurfaceAmbience>();
        var voice = VoiceLinePlayer.Instance;
        Check(energy && settings && pause && voice, "Energy, settings, pause menu and voice player present");
        Check(Time.timeScale == 1f && !GamePause.IsPaused && !AudioListener.pause, "Run starts unpaused");
        yield return Wait(1.5f);
        AudioSource ambience = null;
        if (surface)
        {
            var sources = Field<AudioSource[]>(surface, "sources");
            ambience = sources[Field<int>(surface, "current")];
        }
        Note(ambience && ambience.isPlaying ? "Surface ambience playing" : "NOTE surface ambience not playing, ambience checks skipped");
        if (ambience && !ambience.isPlaying) ambience = null;

        foreach (var (name, open, close) in new (string, Action, Action)[]
        {
            ("Settings", () => settings.Open(), () => settings.GetType().GetMethod("Close", Private).Invoke(settings, null)),
            ("Pause menu", () => pause.Open(), () => pause.Close()),
            ("F1", GpsRuntimePanel.Open, GpsRuntimePanel.Close),
        })
        {
            // A voice line that is running when the pause starts must hold and continue, not restart or drop.
            VoiceLinePlayer.PlayTest(VoiceLineEvent.NewWorld);
            var voiceSource = Field<AudioSource>(voice, "source");
            float voiceWait = Time.realtimeSinceStartup + 5;
            while (!(voiceSource.isPlaying && voiceSource.timeSamples > 0) && Time.realtimeSinceStartup < voiceWait) yield return null;
            bool voiceRunning = voiceSource.isPlaying && voiceSource.clip && voiceSource.clip.length > 3f;
            if (!voiceRunning) Note("NOTE " + name + ": no voice line longer than 3 s running, voice checks skipped");
            yield return Wait(.3f);

            open(); yield return null; yield return null;
            Check(Time.timeScale == 0f && GamePause.IsPaused && AudioListener.pause && GameplayInputBlocker.IsBlocked,
                name + ": time stopped, game paused, listener paused");
            var before = Take(energy, ambience, voiceSource);
            yield return Wait(2.5f); // longer than the voice drop delay (1.5 s)
            var during = Take(energy, ambience, voiceSource);
            Check(during.energy == before.energy, name + ": no energy lost (" + before.energy + ")");
            Check(during.played == before.played && during.sinceSave == before.sinceSave && during.active == before.active,
                name + ": play time, autosave and challenge timers stand still");
            if (ambience) Check(during.ambience == before.ambience, name + ": ambience holds its position");
            if (voiceRunning)
                Check(Field<VoiceLineTake>(voice, "activeTake") != null && during.voice == before.voice,
                    name + ": voice line holds (not dropped) at sample " + during.voice);

            HomeClickAudio.PlayUi(); yield return null;
            var click = GameObject.Find("HomeClickAudio")?.GetComponent<AudioSource>();
            Check(click && click.ignoreListenerPause && click.isPlaying, name + ": UI click sound plays during pause");
            var preview = Resources.Load<AudioClip>("Audio/HomeClick");
            GpsAudioPreview.Play(preview); yield return null;
            var previewSource = GameObject.Find("GPS Audio Preview")?.GetComponent<AudioSource>();
            Check(previewSource && previewSource.ignoreListenerPause && previewSource.isPlaying, name + ": preview sound plays during pause");
            GpsAudioPreview.Stop();

            close(); yield return null;
            Check(Time.timeScale == 1f && !GamePause.IsPaused && !AudioListener.pause && !GameplayInputBlocker.IsBlocked,
                name + ": close resumes time and audio");
            yield return Wait(1f);
            var after = Take(energy, ambience, voiceSource);
            Check(after.played > during.played && (noEnergy || after.energy < during.energy), name + ": play time and energy run again");
            if (ambience) Check(ambience.isPlaying && after.ambience != during.ambience, name + ": ambience continues");
            if (voiceRunning && voiceSource.isPlaying)
                Check(after.voice > during.voice, name + ": voice line continues from where it stopped (" + during.voice + " → " + after.voice + ")");
            VoiceLinePlayer.StopSpeaking();
            yield return Wait(.5f);
        }

        // Settings opened from the pause menu: closing the settings keeps the pause menu paused.
        pause.Open(); yield return null;
        settings.Open(); yield return null;
        settings.GetType().GetMethod("Close", Private).Invoke(settings, null); yield return null;
        Check(RunPauseMenu.IsOpen && Time.timeScale == 0f && GamePause.IsPaused && AudioListener.pause,
            "Settings from the pause menu: closing the settings keeps the pause");
        pause.Close(); yield return null;
        Check(Time.timeScale == 1f && !GamePause.IsPaused && !AudioListener.pause, "Closing the pause menu then resumes");

        // F1 voice test while paused plays through the pause.
        GpsRuntimePanel.Open(); yield return null;
        VoiceLinePlayer.PlayTest(VoiceLineEvent.NewWorld);
        var testSource = Field<AudioSource>(voice, "source");
        float testWait = Time.realtimeSinceStartup + 5;
        while (!testSource.isPlaying && Time.realtimeSinceStartup < testWait) yield return null;
        int firstSample = testSource.timeSamples;
        yield return Wait(.5f);
        Check(testSource.isPlaying && testSource.ignoreListenerPause && testSource.timeSamples > firstSample, "F1 voice test plays during the pause");
        VoiceLinePlayer.StopSpeaking();
        GpsRuntimePanel.Close(); yield return null;
        Check(!testSource.ignoreListenerPause && !GamePause.IsPaused && Time.timeScale == 1f, "After F1 the voice source pauses with the game again");

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var cam = Camera.main;
        Check(player && cam.GetComponent<CameraFollow>().enabled && cam.GetComponent<CameraFollow>().target == player.transform &&
            cam.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled, "Camera follows player");
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
    }

    void Cleanup()
    {
        if (clean) return;
        clean = true;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta;
        GameSaveSystem.TestDirectory = save;
        Destroy(gameObject);
    }

    void OnDestroy() { if (!clean) Cleanup(); }
}
