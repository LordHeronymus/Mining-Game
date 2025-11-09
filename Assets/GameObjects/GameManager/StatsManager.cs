using UnityEngine;
using System;

public class StatsManager : MonoBehaviour
{
    public static StatsManager Instance;

    public int Points { get; private set; } = 0;
    public int Money { get; private set; } = 0;

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
        HandlePoints(Vector2.zero, 0);
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
        OnMoneyChanged?.Invoke(Money);
    }
}
