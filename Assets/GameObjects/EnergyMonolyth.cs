using UnityEngine;
using System.Collections;

public class EnergyMonolyth : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);

    [SerializeField] CanvasGroup panel;
    [SerializeField] EnergyManager energyManager;
    [SerializeField] StatsManager stats;

    [SerializeField] float rechargeCost = 1.0f;
    [SerializeField] float fadeDuration = 0.1f;

    static bool IsPlayer(Collider2D other) => other && other.GetComponentInParent<PlayerMovement>();

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        StartCoroutine(FadePanel(true));
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        StartCoroutine(FadePanel(false));
    }

    IEnumerator FadePanel(bool enabled)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            panel.alpha = enabled
                ? Mathf.Clamp01(elapsed / fadeDuration)
                : 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        panel.alpha = enabled ? 1f : 0f;
        panel.blocksRaycasts = enabled;
        panel.interactable = enabled;
    }

    public void Recharge()
    {
        if (!stats.HasInfiniteMoney && stats.Money <= 0)
        {
            AudioManager.Instance.Play(SoundType.UI_Alert);
            return;
        }
        else AudioManager.Instance.Play(SoundType.Recharge);

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
    }
}
