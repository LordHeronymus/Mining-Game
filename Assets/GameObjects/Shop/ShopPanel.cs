using UnityEngine;
using System.Collections;
using TMPro;

public class ShopPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private SellPage sellPage;
    [SerializeField] private BuyPage buyPage;

    [SerializeField] TextMeshProUGUI sellText;
    [SerializeField] TextMeshProUGUI buyText;

    [SerializeField] float fadeDuration = 0.1f;
    [SerializeField] Color tabHighlight = Color.yellow;

    private bool panelVisible = false;

    void Awake()
    {
        if (panel) panel.alpha = 0f;
        sellText.color = tabHighlight;
        sellPage.gameObject.SetActive(true);
        buyPage.gameObject.SetActive(false);
    }

    void Update()
    {
        if (panelVisible && Input.GetKeyDown(KeyCode.Escape)) ShowPanel(false);
    }

    public void ShowPanel(bool show)
    {
        sellPage.Rebuild();
        StopAllCoroutines();
        StartCoroutine(FadePanel(show));
        if (show)
        {
            AudioManager.Instance.Play(SoundType.UI_Click);
            AudioManager.Instance?.Play(SoundType.DoorOpen);
        }
    }

    public IEnumerator FadePanel(bool enabled)
    {
        yield return new WaitForSeconds(0.2f);
        InfoPanel.Instance?.ShowPanel(!enabled);
        panelVisible = enabled;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            panel.alpha = enabled ? a : 1f - a;
            yield return null;
        }
        panel.alpha = enabled ? 1f : 0f;
        panel.blocksRaycasts = enabled;
        panel.interactable = enabled;
    }

    public void ShowSellPage()
    {
        AudioManager.Instance.Play(SoundType.UI_Click);
        buyPage.gameObject.SetActive(false);
        sellPage.gameObject.SetActive(true);
        sellText.color = tabHighlight;
        buyText.color = Color.white;
    }

    public void ShowBuyPage()
    {
        AudioManager.Instance.Play(SoundType.UI_Click);
        sellPage.gameObject.SetActive(false);
        buyPage.gameObject.SetActive(true);
        buyText.color = tabHighlight;
        sellText.color = Color.white;
    }

}
