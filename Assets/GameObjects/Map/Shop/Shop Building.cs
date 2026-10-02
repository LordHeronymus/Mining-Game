using UnityEngine;
using System.Collections;

public class ShopBuilding : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;

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
}
