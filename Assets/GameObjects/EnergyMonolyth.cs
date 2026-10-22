using UnityEngine;
using System.Collections.Generic;

public class EnergyMonolyth : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);

    public static event System.Action<float, float, float> Recharged; // (before, after, max)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Recharged = null;

    // Legacy "Energy" sign from the scene; replaced at runtime by the shared gold EntrySign.
    [SerializeField] CanvasGroup panel;
    [SerializeField] EnergyManager energyManager;
    [SerializeField] StatsManager stats;

    [SerializeField] float rechargeCost = 1.0f;

    readonly HashSet<Collider2D> visitors = new();
    EntrySign entrySign;

    public EntrySign EntrySign => entrySign;

    static bool IsPlayer(Collider2D other) => other && other.GetComponentInParent<PlayerMovement>();

    void Start()
    {
        var legacyCanvas = panel ? panel.GetComponentInParent<Canvas>(true) : null;
        int layer = legacyCanvas ? legacyCanvas.gameObject.layer : gameObject.layer;
        if (legacyCanvas) legacyCanvas.gameObject.SetActive(false);
        var sprite = EntrySign.MainSprite(transform);
        if (sprite) entrySign = EntrySign.Create(transform, sprite, "Energie aufladen",
            () => { if (visitors.Count > 0 && !GameplayInputBlocker.IsBlocked) Recharge(); }, layer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPlayer(other)) visitors.Add(other);
    }

    void OnTriggerExit2D(Collider2D other) => visitors.Remove(other);

    void LateUpdate()
    {
        visitors.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
        if (entrySign) entrySign.Visible = visitors.Count > 0 && !GameplayInputBlocker.IsBlocked;
    }

    void OnDisable() => visitors.Clear();

    public void Recharge()
    {
        if (!stats.HasInfiniteMoney && stats.Money <= 0)
        {
            AudioManager.Instance.Play(SoundType.UI_Alert);
            return;
        }
        else AudioManager.Instance.Play(SoundType.Recharge);

        float before = energyManager.energy;
        float cost = energyManager.delta * rechargeCost;

        if (stats.HasInfiniteMoney || cost <= stats.Money)
        {
            if (!stats.HasInfiniteMoney) stats.AddMoney(-Mathf.FloorToInt(cost));
            energyManager.energy = stats.MaxEnergy;
        }
        else
        {
            int availableMoney = stats.Money;
            float affordableEnergy = availableMoney / rechargeCost;
            stats.AddMoney(-availableMoney);
            energyManager.energy = Mathf.Min(stats.MaxEnergy, energyManager.energy + affordableEnergy);
        }
        if (energyManager.energy > before + 0.001f) Recharged?.Invoke(before, energyManager.energy, stats.MaxEnergy);
    }
}
