using UnityEngine;
using System.Collections;

public class EnergyMonolyth : MonoBehaviour
{
    [SerializeField] CanvasGroup panel;
    [SerializeField] EnergyManager energyManager;
    [SerializeField] StatsManager stats;

    [SerializeField] float rechargeCost = 1.0f;
    [SerializeField] float fadeDuration = 0.1f;

    void OnTriggerEnter2D(Collider2D other)
    {
        StartCoroutine(FadePanel(true));
    }

    void OnTriggerExit2D(Collider2D other)
    {
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
        if (stats.Money <= 0)
        {
            AudioManager.Instance.Play(SoundType.UI_Alert);
            return;
        }
        else AudioManager.Instance.Play(SoundType.Recharge);

        float cost = energyManager.delta * rechargeCost;

        if (cost <= stats.Money)
        {
            stats.AddMoney(-Mathf.FloorToInt(cost));
            energyManager.energy = stats.MaxEnergy;
        }
        else
        {
            stats.AddMoney(-stats.Money);
            energyManager.energy += stats.Money / rechargeCost;
        }
    }
}
