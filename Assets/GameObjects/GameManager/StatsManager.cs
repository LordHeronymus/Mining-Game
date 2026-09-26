using UnityEngine;
using System;
using System.Collections.Generic;

public class StatsManager : MonoBehaviour
{
    public const float LowHealthHeartbeatThresholdFraction = .25f;
    const float FastestHeartbeatHealthFraction = .01f;
    [SerializeField] PlayerBaseStats baseStats;
    public static StatsManager Instance;

    public int Points { get; private set; } = 0;
    public int ArtifactPoints { get; private set; } = 0;
    readonly HashSet<ArtifactTile> collectedArtifactTypes = new();
    AudioClip artifactCollectedSound;
    public int Money { get; private set; } = 0;

    public float MoveSpeed;
    public float EffectiveMoveSpeed => MoveSpeed * GameplayTestSettings.MovementMultiplier;
    public float JumpHeightBlocks => baseStats.jumpHeightBlocks;
    public float MiningSpeedMultiplier { get; set; } = 1f;
    public float MiningSpeed => GameplaySettings.BaseDiggingSpeed * MiningSpeedMultiplier;
    [Header("Health")]
    [Min(1f)] public float initialHealth = 100f;
    [Min(0f)] public float fallDamageHeightBlocks = 3f;
    [Min(0f)] public float fallDamageBase = 5f;
    [Min(1f)] public float fallDamageExponent = 1.5f;
    [Min(0f)] public float healthRegenPerSecond = 2f;
    [Min(0f)] public float healthRegenDelay = 3f;
    [Range(0f, 1f)] public float healthRegenLimitFraction = 0.5f;
    [Range(0f, 1f)] public float bloodEdgeIntensity = 1f;
    [Min(.1f)] public float heartbeatFlashIntervalSeconds = 1.2f;
    [Range(-1f, 1f)] public float heartbeatFlashOffsetSeconds = .12f;
    public float MaxHealth => Mathf.Max(1f, initialHealth);
    public float Health { get; private set; }
    public float HeartbeatIntervalSeconds => CalculateHeartbeatIntervalSeconds(Health / MaxHealth, heartbeatFlashIntervalSeconds);
    public event Action<float, float> OnHealthChanged;
    float lastHealthDamageTime;
    // Future damage handlers must respect this gate before applying damage.
    public bool IsInvulnerable => GameplayTestSettings.GodMode;
    public bool CanTakeDamage => !IsInvulnerable;
    public float Reach;
    public float MaxEnergy;

    public Action<int> OnMoneyChanged;

    void OnEnable()
    {
        TileMiner.OnBlockMined += HandlePoints;
    }

    void OnDisable()
    {
        TileMiner.OnBlockMined -= HandlePoints;
    }

    void Awake()
    {
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
        Reset();
        var inventory = InventoryManager.Instance;
        if (inventory)
        {
            inventory.ResetAll();
            var resources = StartingResourcesSettings.Load();
            foreach (var resource in resources.items)
            {
                var item = StartingResourcesSettings.Resolve(resource.itemId);
                if (item && resource.amount > 0) inventory.Add(item, resource.amount);
            }
        }
    }

    void Update()
    {
        float healthFraction = Health / MaxHealth;
        bool heartbeatActive = Health > 0f && !GameOverPanel.IsOpen &&
            healthFraction <= LowHealthHeartbeatThresholdFraction;
        AudioManager.Instance?.SetLowHealthHeartbeat(heartbeatActive);


        float regenerationLimit = MaxHealth * Mathf.Clamp01(healthRegenLimitFraction);
        if (healthRegenPerSecond <= 0f || Health >= regenerationLimit ||
            Time.time - lastHealthDamageTime < Mathf.Max(0f, healthRegenDelay)) return;

        float nextHealth = Mathf.Min(regenerationLimit, Health + healthRegenPerSecond * Time.deltaTime);
        if (Mathf.Approximately(nextHealth, Health)) return;
        Health = nextHealth;
        OnHealthChanged?.Invoke(Health, MaxHealth);
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
        if (Health <= 0f || Health >= MaxHealth - 0.001f || GameOverPanel.IsOpen) return false;
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
        Health = MaxHealth;
        OnHealthChanged?.Invoke(Health, MaxHealth);
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
        Money += amount;
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        OnMoneyChanged?.Invoke(Money); // für ShopUI
    }

    public void CollectArtifact(ArtifactTile artifact, int cash)
    {
        if (!artifact) return;
        ArtifactPoints++;
        HandlePoints(Vector2.zero, 1);
        int reward = Mathf.Max(0, cash);
        AddMoney(reward);
        ItemFeed.Instance?.ShowMoney(reward);
        if (!collectedArtifactTypes.Add(artifact) || !AudioManager.Instance) return;
        if (!artifactCollectedSound)
            artifactCollectedSound = Resources.Load<AudioClip>("Audio/ArtifactCollected");
        AudioManager.Instance.PlayClip(artifactCollectedSound,
            AudioManager.Instance.GetVolume(AudioVolumeSetting.DingLight), 0f);
    }

    void Reset()
    {
        Points = 0;
        ArtifactPoints = 0;
        collectedArtifactTypes.Clear();
        Money = StartingResourcesSettings.Load().money;
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        OnMoneyChanged?.Invoke(Money);

        MoveSpeed = baseStats.moveSpeed;

        MiningSpeedMultiplier = 1f;
        Reach = baseStats.reach;
        MaxEnergy = baseStats.maxEnergy;
        Health = MaxHealth;
        lastHealthDamageTime = Time.time;
        OnHealthChanged?.Invoke(Health, MaxHealth);
    }
}
