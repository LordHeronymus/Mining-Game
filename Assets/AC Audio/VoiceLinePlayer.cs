using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runtime host for the character voice lines. VoiceLineDirector decides what/when; this component plays the clips,
// watches energy/carry thresholds, listens to gameplay hooks and ducks the effects (AudioManager.TunedVolume).
public sealed class VoiceLinePlayer : MonoBehaviour
{
    const double LoadTimeoutSeconds = 3d;
    public static VoiceLinePlayer Instance { get; private set; }
    public static float DuckFactor { get; private set; } = 1f;
    public bool Armed { get; private set; }

    VoiceLineSettingsAsset settings;
    VoiceLineLibraryAsset library;
    VoiceLineDirector director;
    AudioSource source;
    readonly VoiceEnergyWatcher energyWatcher = new();
    readonly VoiceCarryWatcher carryWatcher = new();
    EnergyManager energyManager;
    AudioClip activeClip;
    VoiceLineTake activeTake;
    VoiceLinePlay nextPlay;
    bool revealPending, revealRestored, endScreen, paused, loading, fading, testLine, preloadStarted, lastNoWeight, resumeAfterBlock, sawLoading;
    double pausedSince, loadingStart, fadeStart, nextPoll, nextEnergySearch, lastMaxEnergy;
    float fade = 1f, fadeFrom = 1f, lastCapacity;
    int blockedScene;

    static double Now => Time.realtimeSinceStartupAsDouble;
#if UNITY_EDITOR
    static bool IsolatedTest => !string.IsNullOrEmpty(GameSaveSystem.TestDirectory);
#else
    static bool IsolatedTest => false; // GameSaveSystem.TestDirectory exists in the editor only.
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; DuckFactor = 1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (Instance) return;
        var root = new GameObject("Voice Lines");
        DontDestroyOnLoad(root);
        root.AddComponent<VoiceLinePlayer>();
    }

    public static void Raise(VoiceLineEvent e) { if (Instance) Instance.Submit(e, false); }
    public static void PlayTest(VoiceLineEvent e) { if (Instance) Instance.Submit(e, true); }
    public static void StopSpeaking() { if (Instance) Instance.Stop(Now); }

    public static VoiceLineEvent? CraftEvent(Item output, bool energyUpgraded, bool carryUpgraded, bool pickaxeUpgraded)
    {
        if (energyUpgraded) return VoiceLineEvent.UpgradeEnergy;
        if (carryUpgraded) return VoiceLineEvent.UpgradeCarry;
        if (pickaxeUpgraded) return VoiceLineEvent.CraftedPickaxe;
        return output switch
        {
            Item.CopperPickaxe or Item.IronPickaxe or Item.SteelPickaxe or Item.TitaniumPickaxe or Item.TungstenPickaxe or
                Item.ObsidianPickaxe or Item.MythrilPickaxe or Item.DiamondPickaxe => VoiceLineEvent.CraftedPickaxe,
            Item.Axe => VoiceLineEvent.CraftedAxe,
            Item.Scythe => VoiceLineEvent.CraftedScythe,
            Item.Medkit => VoiceLineEvent.CraftedMedkit,
            Item.Torche => VoiceLineEvent.CraftedTorches,
            _ => null
        };
    }

    void Awake()
    {
        if (Instance && Instance != this) { enabled = false; Destroy(gameObject); return; }
        Instance = this;
        settings = Resources.Load<VoiceLineSettingsAsset>("Audio/VoiceLineSettings");
        if (!settings) { settings = ScriptableObject.CreateInstance<VoiceLineSettingsAsset>(); settings.hideFlags = HideFlags.HideAndDontSave; }
        library = Resources.Load<VoiceLineLibraryAsset>("Audio/VoiceLineLibrary");
        director = new VoiceLineDirector(Environment.TickCount ^ GetInstanceID()) { lines = LineCount, takes = TakeCount };
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f; source.priority = 0;
        source.ignoreListenerPause = false;
    }

    void OnEnable()
    {
        PlayerMovement.FallDamaged += FallDamaged;
        ShopManager.Sold += Sold;
        ShopManager.RecipeBought += RecipeBought;
        CraftingService.Crafted += Crafted;
        EnergyMonolyth.Recharged += Recharged;
        LoadingScreen.GameplayRevealed += GameplayRevealed;
    }

    void OnDisable()
    {
        PlayerMovement.FallDamaged -= FallDamaged;
        ShopManager.Sold -= Sold;
        ShopManager.RecipeBought -= RecipeBought;
        CraftingService.Crafted -= Crafted;
        EnergyMonolyth.Recharged -= Recharged;
        LoadingScreen.GameplayRevealed -= GameplayRevealed;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null; DuckFactor = 1f;
    }

    int LineCount(VoiceLineEvent e) => library ? library.LineCount(e) : 0;
    int TakeCount(VoiceLineEvent e, int line) => library ? library.TakeCount(e, line) : 0;

    string Submit(VoiceLineEvent e, bool force)
    {
        if (!force && !Armed) return null; // non-forced events outside a revealed run are ignored
        string reason = director.Submit(e, Now, settings.Config(), force);
        if (settings.debugLog) Debug.Log($"[Voice] {e}: {reason}");
        return reason;
    }

    // Hooks (static events in gameplay code).
    void FallDamaged(float applied, bool big)
    {
        if (big) Raise(VoiceLineEvent.FallBig);
        else if (applied >= settings.smallFallMinimumDamage) Raise(VoiceLineEvent.FallSmall);
    }

    void Sold(int money, double soldWeight, bool all)
    {
        var inventory = InventoryManager.Instance;
        float capacity = inventory ? inventory.CarryingCapacity : 0f;
        if (all || capacity > 0f && soldWeight / capacity >= settings.saleMinimumCarryShare) Raise(VoiceLineEvent.OreSold);
    }

    void RecipeBought(CraftingRecipe recipe) => Raise(VoiceLineEvent.BlueprintBought);

    void Crafted(CraftingRecipe recipe, CraftOutcome outcome)
    {
        // Coal stands in for a missing output: it maps to no line, so only the upgrade flags can still speak.
        var e = CraftEvent(recipe && recipe.output ? recipe.output.item : Item.Coal,
            outcome.energyUpgraded, outcome.carryUpgraded, outcome.pickaxeUpgraded);
        if (e.HasValue) Raise(e.Value);
    }

    void Recharged(float before, float after, float max)
    {
        if (max > 0f && (after - before) / max >= settings.refillMinimumFraction && after / max >= settings.refillTargetFraction)
            Raise(VoiceLineEvent.EnergyRefilled);
    }

    void GameplayRevealed(bool restored) { revealPending = true; revealRestored = restored; }

    void Update()
    {
        double now = Now;
        var config = settings.Config();
        bool blocked = RunNavigation.IsTransitioning || LoadingProgress.Active || MainMenuController.IsVisible;
        if (Armed && blocked) Disarm(now);
        if (blocked)
        {
            if (LoadingProgress.Active || SceneManager.GetActiveScene().handle != blockedScene) sawLoading = true;
        }
        else if (revealPending) Arm();
        else if (resumeAfterBlock)
        {
            // A transition that was cancelled without loading (e.g. a failed save load) returns to the same run.
            resumeAfterBlock = false;
            if (!sawLoading && SceneManager.GetActiveScene().handle == blockedScene) { Armed = true; InvalidateWatchers(); }
        }

        bool end = GameOverPanel.IsOpen || GameVictoryPanel.IsOpen || ExpeditionResultPanel.IsOpen ||
            UltroniumAltarChamber.VictorySequenceActive;
        if (end && !endScreen) Stop(now);
        endScreen = end;

        StepPlayback(now);
        PollWatchers(now);

        bool stopping = GameAudioLifecycle.IsStopping;
        bool canStart = director.PendingForced ? !stopping
            : Armed && !stopping && Time.timeScale > 0f && !endScreen &&
              !IsolatedTest && PlayerSettings.Voice > 0f;
        if (director.Update(now, config, canStart, out var play))
        {
            energyWatcher.Acknowledge(play.trigger); carryWatcher.Acknowledge(play.trigger);
            if (settings.debugLog) Debug.Log($"[Voice] {play.trigger}: start line {play.line} take {play.take}{(play.interrupt ? " (interrupt)" : "")}");
            if (play.interrupt && activeTake != null && !loading)
            {
                // Fade out the current line; Finished is not called for it (the director already tracks the new one).
                if (!fading) { fading = true; fadeStart = now; fadeFrom = fade; }
                nextPlay = play;
            }
            else Begin(play, now);
        }

        if (activeTake != null && !loading)
            source.volume = stopping ? 0f : GpsAudio.Volume(activeClip,
                settings.volume * activeTake.gain * PlayerSettings.Voice * fade, source);
        UpdateDuck();
    }

    void Arm()
    {
        revealPending = false; resumeAfterBlock = false; Armed = true;
        InvalidateWatchers();
        if (!preloadStarted && library) { preloadStarted = true; StartCoroutine(Preload()); }
        if (GameSaveSystem.InitialSaveError == null) Submit(revealRestored ? VoiceLineEvent.SaveLoaded : VoiceLineEvent.NewWorld, false);
    }

    void Disarm(double now)
    {
        Stop(now);
        director.Reset();
        InvalidateWatchers();
        revealPending = false; Armed = false;
        resumeAfterBlock = true; sawLoading = false; blockedScene = SceneManager.GetActiveScene().handle;
    }

    void InvalidateWatchers() { energyWatcher.Invalidate(); carryWatcher.Invalidate(); }

    // Stops the current line (if any) and forgets the pending request.
    void Stop(double now) { Drop(now); director.ClearPending(); }

    // Ends the current line (if any) without touching the pending request.
    void Drop(double now)
    {
        if (source) source.Stop();
        if (director.IsSpeaking) director.Finished(now);
        Clear();
    }

    void Clear()
    {
        activeClip = null; activeTake = null;
        paused = loading = fading = testLine = false; fade = 1f;
        if (source) source.ignoreListenerPause = false;
    }

    void Begin(VoiceLinePlay play, double now)
    {
        source.Stop(); Clear();
        var take = library ? library.Take(play.trigger, play.line, play.take) : null;
        if (take == null || !take.clip) { director.Finished(now); return; }
        activeTake = take; activeClip = take.clip; source.clip = activeClip;
        // Only forced test lines (F1) play through the full pause; a regular line (e.g. after an interrupt fade) is held.
        testLine = play.forced && GamePause.IsPaused; source.ignoreListenerPause = testLine;
        if (activeClip.loadState == AudioDataLoadState.Loaded) { StartClip(); return; }
        if (activeClip.loadState != AudioDataLoadState.Loading) activeClip.LoadAudioData();
        loading = true; loadingStart = now;
    }

    void StartClip()
    {
        loading = false; fade = 1f;
        source.pitch = GpsAudio.Pitch(activeClip, 1f, source);
        source.volume = GameAudioLifecycle.IsStopping ? 0f : GpsAudio.Volume(activeClip,
            settings.volume * activeTake.gain * PlayerSettings.Voice, source);
        source.Play();
    }

    void StepPlayback(double now)
    {
        if (activeTake == null) return;
        if (!activeClip) { Drop(now); return; }
        if (fading)
        {
            float seconds = settings.interruptFadeSeconds;
            fade = seconds <= 0f ? 0f : fadeFrom * Mathf.Clamp01(1f - (float)((now - fadeStart) / seconds));
            if (fade <= 0f) Begin(nextPlay, now);
            return;
        }
        bool hold = Time.timeScale <= 0f && !endScreen && !testLine;
        if (loading)
        {
            var state = activeClip.loadState;
            if (state == AudioDataLoadState.Failed ||
                state != AudioDataLoadState.Loaded && now - loadingStart > LoadTimeoutSeconds) { Drop(now); return; }
            if (state != AudioDataLoadState.Loaded) return;
            if (!hold) { paused = false; StartClip(); return; }
        }
        if (hold)
        {
            // The full pause (pause menu, settings) holds the line until it continues; other stops drop it.
            if (!paused) { if (!loading) source.Pause(); paused = true; pausedSince = now; }
            else if (GamePause.IsPaused) pausedSince = now;
            else if (now - pausedSince > settings.pauseDropSeconds) Drop(now);
            return;
        }
        if (paused) { paused = false; source.UnPause(); return; }
        if (!source.isPlaying) { director.Finished(now); Clear(); }
    }

    void PollWatchers(double now)
    {
        if (!Armed || endScreen || paused || Time.timeScale <= 0f || now < nextPoll) return;
        nextPoll = now + Mathf.Max(.02f, settings.watcherPollSeconds);
        PollEnergy(now);
        PollCarry();
    }

    void PollEnergy(double now)
    {
        var stats = StatsManager.Instance;
        if (!energyManager && now >= nextEnergySearch) { nextEnergySearch = now + 1d; energyManager = FindFirstObjectByType<EnergyManager>(); }
        if (!stats || !energyManager) { energyWatcher.Invalidate(); return; }
        float max = stats.MaxEnergy, value = energyManager.energy;
        if (max <= 0f || value <= 0f) return;
        double fraction = value / (double)max, low = settings.energyLowFraction, critical = settings.energyCriticalFraction;
        if (energyWatcher.HasBaseline && Math.Abs(max - lastMaxEnergy) > lastMaxEnergy * .005d) energyWatcher.Rebaseline(fraction, low, critical);
        lastMaxEnergy = max;
        var open = energyWatcher.Step(fraction, low, critical, settings.energyRearmMargin);
        if (open.HasValue && VoiceLineDirector.IsTerminal(Submit(open.Value, false))) energyWatcher.Acknowledge(open.Value);
    }

    void PollCarry()
    {
        var inventory = InventoryManager.Instance;
        if (!inventory) { carryWatcher.Invalidate(); return; }
        bool noWeight = GameplayTestSettings.NoWeight;
        float capacity = inventory.CarryingCapacity;
        double ratio = noWeight || capacity <= 0f ? 0d : inventory.TotalWeight / capacity;
        if (carryWatcher.HasBaseline && (noWeight != lastNoWeight || !Mathf.Approximately(capacity, lastCapacity))) carryWatcher.Rebaseline(ratio);
        lastNoWeight = noWeight; lastCapacity = capacity;
        var open = carryWatcher.Step(ratio, settings.carryRearmMargin);
        if (open.HasValue && VoiceLineDirector.IsTerminal(Submit(open.Value, false))) carryWatcher.Acknowledge(open.Value);
    }

    void UpdateDuck()
    {
        bool speaking = activeTake != null && !loading && !paused && !GameAudioLifecycle.IsStopping;
        float target = speaking && settings.duckOtherAudio ? Mathf.Clamp01(settings.duckVolume) : 1f;
        float seconds = settings.duckFadeSeconds;
        DuckFactor = seconds <= 0f ? target : Mathf.MoveTowards(DuckFactor, target,
            Time.unscaledDeltaTime * Mathf.Max(.05f, 1f - Mathf.Clamp01(settings.duckVolume)) / seconds);
    }

    // One-time per session: decode the voice clips in the background so the first lines start without delay.
    IEnumerator Preload()
    {
        int budget = 0;
        foreach (var clip in library.AllClips())
        {
            if (!clip || clip.loadState != AudioDataLoadState.Unloaded) continue;
            clip.LoadAudioData();
            if (++budget < Mathf.Max(1, settings.preloadClipsPerFrame)) continue;
            budget = 0;
            yield return null;
        }
    }
}
