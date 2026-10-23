using UnityEngine;
using System;
using System.Collections.Generic;

public class StatsManager : MonoBehaviour
{
    public const float LowHealthHeartbeatThresholdFraction = .25f;
    const float FastestHeartbeatHealthFraction = .01f;
    const float MedkitDurationSeconds = 30f;
    const float MedkitRegenMultiplier = 3f;
    [SerializeField] PlayerBaseStats baseStats;
    public static StatsManager Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Instance = null;

    public int Points { get; private set; } = 0;
    public int ArtifactPoints { get; private set; } = 0;
    [Min(1)] public int ultroniumRequiredToWin = 50;
    public bool HasWon { get; private set; }
    readonly HashSet<ArtifactTile> collectedArtifactTypes = new();
    AudioClip artifactCollectedSound;
    public int Money { get; private set; } = 0;
    public bool HasInfiniteMoney => GameplayTestSettings.InfiniteMoney;
    public bool CanAffordMoney(int amount) => HasInfiniteMoney || Money >= Mathf.Max(0, amount);

    public float MoveSpeed;
    public float MovementWeightFactor => InventoryManager.Instance ? InventoryManager.Instance.MovementWeightFactor : 1f;
    public float EffectiveMoveSpeed => MoveSpeed * MetaProgression.CurrentLoadout.movementMultiplier * GameplayTestSettings.MovementMultiplier * MovementWeightFactor;
    public float JumpHeightBlocks => baseStats.jumpHeightBlocks * MetaProgression.CurrentLoadout.jumpMultiplier;
    public float MiningSpeedMultiplier { get; set; } = 1f;
    public float MiningSpeed => GameplaySettings.BaseDiggingSpeed * MiningSpeedMultiplier;
    [Header("Health")]
    [Min(1f)] public float initialHealth = 100f;
    [Min(0f)] public float fallDamageHeightBlocks = 3f;
    [Min(0f)] public float fallDamageBase = 5f;
    [Min(1f)] public float fallDamageExponent = 1.5f;
    [Min(0f)] public float bigFallDamageThreshold = 30f;
    [Min(0f)] public float healthRegenPerSecond = 2f;
    [Min(0f)] public float healthRegenDelay = 3f;
    [Range(0f, 1f)] public float healthRegenLimitFraction = 0.5f;
    // Above the limit health keeps healing slowly: one HP every this many seconds (0 = stop at the limit).
    [Min(0f)] public float healthRegenAboveLimitInterval = 10f;
    [Range(0f, 1f)] public float bloodEdgeIntensity = 1f;
    [Min(.1f)] public float heartbeatFlashIntervalSeconds = 1.2f;
    [Range(-1f, 1f)] public float heartbeatFlashOffsetSeconds = .12f;
    public float MaxHealth => Mathf.Max(1f, initialHealth) * MetaProgression.CurrentLoadout.healthMultiplier;
    public float Health { get; private set; }
    public float HeartbeatIntervalSeconds => CalculateHeartbeatIntervalSeconds(Health / MaxHealth, heartbeatFlashIntervalSeconds);
    public event Action<float, float> OnHealthChanged;
    float lastHealthDamageTime;
    float medkitActiveUntil;
    public bool IsMedkitActive => Health > 0f && Time.time < medkitActiveUntil;
    // Future damage handlers must respect this gate before applying damage.
    public bool IsInvulnerable => GameplayTestSettings.GodMode;
    public bool CanTakeDamage => !IsInvulnerable && !UltroniumAltarChamber.VictorySequenceActive;
    public float Reach;
    public float MaxEnergy;

    public void RefreshEnergyCapacity()
    {
        if (!baseStats) return;
        float initial = baseStats.maxEnergy;
        if (float.IsNaN(initial) || float.IsInfinity(initial)) initial = 100f;
        MaxEnergy = Mathf.Max(1f, initial) * MetaProgression.CurrentLoadout.energyMultiplier * (InventoryManager.Instance
            ? InventoryManager.Instance.EnergyCapacityMultiplier : 1f);
    }

    // F1/GPS changes the base stats during a run; the copies follow without resetting the run.
    public void ApplyBaseStats(PlayerBaseStats changed)
    {
        if (!baseStats || changed != baseStats) return;
        MoveSpeed = baseStats.moveSpeed;
        Reach = baseStats.reach;
    }

    public Action<int> OnMoneyChanged;

    void OnEnable()
    {
        TileMiner.OnBlockMined += HandlePoints;
    }

    void OnDisable()
    {
        TileMiner.OnBlockMined -= HandlePoints;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Awake()
    {
        GpsSettings.ApplyComponent(this);
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        if (!baseStats)
        {
            enabled = false;
            return;
        }
        GameplaySettings.Initialize(baseStats.miningSpeed);
        HandlePoints(Vector2.zero, 0);
    }

    void Start()
    {
        ResetRun();
    }

    public void ResetRun()
    {
        foreach (var altar in FindObjectsByType<UltroniumAltarChamber>(FindObjectsSortMode.None)) altar.ResetChargeForNewRun();
        foreach (var storage in FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None)) storage.ResetForNewRun();
        RecipeUnlocks.ResetRun();
        ResetRunValues();
        var inventory = InventoryManager.Instance;
        if (inventory)
        {
            inventory.ResetAll();
            // A saved run replaces these items a few frames later; announcing them played the pickup bling on load.
            bool announce = !GameSaveSystem.HasPendingLoad;
            var resources = StartingResourcesSettings.Load();
            foreach (var resource in resources.items)
            {
                var item = StartingResourcesSettings.Resolve(resource.itemId);
                if (item && resource.amount > 0) inventory.AddStartingItem(item, resource.amount, announce);
            }
            var loadout = MetaProgression.CurrentLoadout;
            if (loadout.startingTorches > 0) inventory.AddStartingItem(StartingResourcesSettings.Resolve((int)Item.Torche), loadout.startingTorches, announce);
            if (loadout.startingLadders > 0) inventory.AddStartingItem(StartingResourcesSettings.Resolve((int)Item.Ladder), loadout.startingLadders, announce);
        }
        var hud = FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include);
        if (hud)
        {
            hud.AddStartingResourcesToSlots();
            hud.RefreshHotbarIconLayouts();
        }
        FindFirstObjectByType<EnergyManager>()?.ResetForNewRun();
    }

    void Update()
    {
        RefreshEnergyCapacity();
        // A lowered maximum (F1/GPS) caps the current health, like the energy.
        if (Health > MaxHealth) { Health = MaxHealth; OnHealthChanged?.Invoke(Health, MaxHealth); }
        float healthFraction = Health / MaxHealth;
        AudioManager.Instance?.SetLowHealthHeartbeat(LowHealthHeartbeatActive(Health, healthFraction));


        bool medkitActive = IsMedkitActive;
        float speed = HealthRegenSpeed(Health, medkitActive);
        if (speed <= 0f || (!medkitActive && Time.time - lastHealthDamageTime < Mathf.Max(0f, healthRegenDelay))) return;

        float limit = Health < HealthRegenLimit && !medkitActive ? HealthRegenLimit : MaxHealth;
        float nextHealth = Mathf.Min(limit, Health + speed * Time.deltaTime);
        if (Mathf.Approximately(nextHealth, Health)) return;
        Health = nextHealth;
        OnHealthChanged?.Invoke(Health, MaxHealth);
    }

    float HealthRegenLimit => MaxHealth * Mathf.Clamp01(healthRegenLimitFraction);

    // HP per second: normal speed up to the limit, then one HP per healthRegenAboveLimitInterval; the medkit triples the normal speed up to full.
    internal float HealthRegenSpeed(float health, bool medkitActive)
    {
        if (healthRegenPerSecond <= 0f || health >= MaxHealth) return 0f;
        if (medkitActive) return healthRegenPerSecond * MedkitRegenMultiplier;
        if (health < HealthRegenLimit) return healthRegenPerSecond;
        return healthRegenAboveLimitInterval > 0f ? 1f / healthRegenAboveLimitInterval : 0f;
    }

    // No warning heartbeat once the run has ended: climax, end screens and the expedition result.
    internal static bool LowHealthHeartbeatActive(float health, float healthFraction) => health > 0f &&
        !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen && !ExpeditionResultPanel.IsOpen && !UltroniumAltarChamber.VictorySequenceActive &&
        healthFraction <= LowHealthHeartbeatThresholdFraction;

    public bool CompleteAltarVictory(UltroniumAltarChamber altar)
    {
        if (HasWon || GameOverPanel.IsOpen || Health <= 0f || !altar || !altar.IsCompleting ||
            altar.DepositedUltronium < Mathf.Max(1, ultroniumRequiredToWin)) return false;
        HasWon = true;
        MetaProgressionRuntime.RecordVictory();
        GameVictoryPanel.Show();
        return true;
    }

    public SavedStats CaptureRunState()
    {
        var artifacts = new List<string>(); foreach (var tile in collectedArtifactTypes) if (tile) artifacts.Add(tile.name);
        return new SavedStats { money = Money, points = Points, artifactPoints = ArtifactPoints, won = HasWon,
            health = Health, medkitRemaining = Mathf.Max(0, medkitActiveUntil - Time.time),
            damageCooldown = Mathf.Max(0, healthRegenDelay - (Time.time - lastHealthDamageTime)),
            miningMultiplier = MiningSpeedMultiplier, artifacts = artifacts.ToArray() };
    }

    public void RestoreRunState(SavedStats state, MapGenerator map)
    {
        Money = state.money; Points = state.points; ArtifactPoints = state.artifactPoints; HasWon = state.won;
        Health = Mathf.Clamp(state.health, 0, MaxHealth); MiningSpeedMultiplier = state.miningMultiplier;
        medkitActiveUntil = Time.time + state.medkitRemaining;
        lastHealthDamageTime = Time.time - healthRegenDelay + state.damageCooldown;
        collectedArtifactTypes.Clear();
        foreach (string name in state.artifacts ?? Array.Empty<string>())
            foreach (var entry in map.artifactSettings)
                if (entry != null && entry.tile && entry.tile.name == name) collectedArtifactTypes.Add(entry.tile);
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        HUDPoints.Instance?.UpdatePoints(Points, PointType.Points);
        MoneyDisplay.Snap(this);
        OnMoneyChanged?.Invoke(Money); OnHealthChanged?.Invoke(Health, MaxHealth);
    }

    public bool ApplyDamage(float amount)
    {
        if (!CanTakeDamage || Health <= 0f || !(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount))
            return false;

        float nextHealth = Mathf.Max(0f, Health - amount);
        if (Mathf.Approximately(nextHealth, Health)) return false;
        Health = nextHealth;
        lastHealthDamageTime = Time.time;
        AudioManager.Instance?.Play(Health <= 0f ? SoundType.Death : SoundType.Hurt);
        OnHealthChanged?.Invoke(Health, MaxHealth);
        return true;
    }

    public bool TryUseMedkit(ItemSO medkit = null)
    {
        // The medkit only multiplies the regeneration; with regeneration 0 it could not heal and is kept.
        if (Health <= 0f || Health >= MaxHealth - 0.001f || IsMedkitActive || GameOverPanel.IsOpen ||
            healthRegenPerSecond * MedkitRegenMultiplier <= 0f) return false;
        var inventory = InventoryManager.Instance;
        if (!inventory || (medkit && medkit.item != Item.Medkit)) return false;
        if (!medkit)
        {
            foreach (var entry in inventory.GetSnapshot())
                if (entry.Key && entry.Key.item == Item.Medkit && entry.Value > 0)
                {
                    medkit = entry.Key;
                    break;
                }
        }
        if (!medkit || !inventory.TryRemove(medkit)) return false;
        medkitActiveUntil = Time.time + MedkitDurationSeconds;
        AudioManager.Instance?.PlayMedkitUseSound();
        return true;
    }

    public void SetHealthForDebug(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        float nextHealth = Mathf.Clamp(value, 0f, MaxHealth);
        if (Mathf.Approximately(nextHealth, Health)) return;
        if (nextHealth < Health) lastHealthDamageTime = Time.time;
        Health = nextHealth;
        OnHealthChanged?.Invoke(Health, MaxHealth);
    }

    public static float CalculateFallDamage(float fallHeightBlocks, float damageHeightBlocks,
        float baseDamage, float exponent)
    {
        float excessHeight = Mathf.Max(0f, fallHeightBlocks - Mathf.Max(0f, damageHeightBlocks));
        if (excessHeight <= 0f || baseDamage <= 0f) return 0f;
        return baseDamage * Mathf.Pow(excessHeight, Mathf.Max(1f, exponent));
    }

    public static float CalculateHeartbeatIntervalSeconds(float healthFraction, float baseIntervalSeconds)
    {
        float progress = Mathf.Clamp01((LowHealthHeartbeatThresholdFraction - healthFraction) /
            (LowHealthHeartbeatThresholdFraction - FastestHeartbeatHealthFraction));
        return Mathf.Clamp(baseIntervalSeconds, .1f, 5f) / Mathf.Lerp(1f, 2f, progress);
    }
    void HandlePoints(Vector2 pos, int points)
    {
        Points += points;
        HUDPoints.Instance?.UpdatePoints(Points, PointType.Points);
    }

    public void AddMoney(int amount)
    {
        if (amount < 0 && HasInfiniteMoney) return;
        Money += amount;
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        OnMoneyChanged?.Invoke(Money); // für ShopUI
    }

    public void CollectArtifact(ArtifactTile artifact, int cash, int artifactPoints)
    {
        if (!artifact) return;
        int pointReward = Mathf.Max(0, artifactPoints);
        ArtifactPoints += pointReward;
        ItemFeed.Instance?.ShowArtifactPoints(pointReward);
        HandlePoints(Vector2.zero, 1);
        int reward = Mathf.Max(0, cash);
        AddMoney(reward);
        ItemFeed.Instance?.ShowMoney(reward);
        if (!collectedArtifactTypes.Add(artifact)) return;
        ArtifactDiscoveryView.ShowArtifact(artifact);
        if (!AudioManager.Instance) return;
        if (!artifactCollectedSound)
            artifactCollectedSound = Resources.Load<AudioClip>("Audio/ArtifactCollected");
        AudioManager.Instance.PlayClip(artifactCollectedSound,
            AudioManager.Instance.GetVolume(AudioVolumeSetting.ArtifactFound), 0f);
    }

    // Not named Reset: Unity calls Reset() on components in the editor, before baseStats exists.
    void ResetRunValues()
    {
        Points = 0;
        ArtifactPoints = 0;
        HasWon = false;
        collectedArtifactTypes.Clear();
        Money = (int)Math.Min(int.MaxValue, (long)StartingResourcesSettings.Load().money + MetaProgression.CurrentLoadout.startMoney);
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        MoneyDisplay.Snap(this);
        OnMoneyChanged?.Invoke(Money);

        MoveSpeed = baseStats.moveSpeed;

        MiningSpeedMultiplier = 1f;
        Reach = baseStats.reach;
        RefreshEnergyCapacity();
        Health = MaxHealth;
        medkitActiveUntil = 0f;
        lastHealthDamageTime = Time.time;
        OnHealthChanged?.Invoke(Health, MaxHealth);
    }
}
