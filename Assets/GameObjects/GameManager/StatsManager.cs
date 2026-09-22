using UnityEngine;
using System;

public class StatsManager : MonoBehaviour
{
    [SerializeField] PlayerBaseStats baseStats;
    public static StatsManager Instance;

    public int Points { get; private set; } = 0;
    public int Money { get; private set; } = 0;

    public float MoveSpeed;
    public float JumpForce;
    public float MiningSpeedMultiplier { get; set; } = 1f;
    public float MiningSpeed => GameplaySettings.BaseDiggingSpeed * MiningSpeedMultiplier * GameplayTestSettings.DiggingMultiplier;
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
        GameplaySettings.Initialize(baseStats.miningSpeed);
        HandlePoints(Vector2.zero, 0);
    }

    void Start()
    {
        Reset();
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

    void Reset()
    {
        Points = 0;
        Money = 100;
        HUDPoints.Instance?.UpdatePoints(Money, PointType.Money);
        OnMoneyChanged?.Invoke(Money);

        MoveSpeed = baseStats.moveSpeed;
        JumpForce = baseStats.jumpForce;
        MiningSpeedMultiplier = 1f;
        Reach = baseStats.reach;
        MaxEnergy = baseStats.maxEnergy;
    }
}
