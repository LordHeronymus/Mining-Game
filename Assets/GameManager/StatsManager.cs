using UnityEngine;

public class StatsManager : MonoBehaviour
{
    public static StatsManager Instance;

    public int Points { get; private set; } = 0;
    public int Money { get; private set; } = 0;


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
        HUDPoints.Instance?.UpdatePoints(Points);
    }

    public void AddMoney(int amount)
    {
        if (amount <= 0) return;
        Money += amount;
        // Optional: Event/Anzeige aktualisieren
        // z.B. HUDMoney.Instance?.UpdateMoney(Money);
    }
}
