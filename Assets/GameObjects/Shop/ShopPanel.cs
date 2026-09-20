using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;

public class ShopPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private SellPage sellPage;
    [SerializeField] private BuyPage buyPage;

    [SerializeField] TextMeshProUGUI sellText;
    [SerializeField] TextMeshProUGUI buyText;
    [SerializeField] Image sellTabBackground;
    [SerializeField] Image buyTabBackground;

    [SerializeField] float fadeDuration = 0.1f;
    [SerializeField] Color tabHighlight = new Color(1f, 0.55f, 0.16f);
    [SerializeField] Color activeTabColor = new Color(0.63f, 0.25f, 0.06f);
    [SerializeField] Color inactiveTabColor = new Color(0.22f, 0.13f, 0.10f);

    private bool panelVisible = false;

    void Awake()
    {
        if (panel) panel.alpha = 0f;
        sellText.color = tabHighlight;
        sellPage.gameObject.SetActive(true);
        buyPage.gameObject.SetActive(false);
        UpdateTabAppearance(true);
    }

    void Update()
    {
        if (panelVisible && Input.GetKeyDown(KeyCode.Escape)) ShowPanel(false);
    }

    void OnEnable()
    {
        if (panelVisible) GameplayInputBlocker.SetBlocked(this, true);
    }

    void OnDisable() => GameplayInputBlocker.SetBlocked(this, false);

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
        if (enabled) GameplayInputBlocker.SetBlocked(this, true);
        panelVisible = enabled;
        yield return new WaitForSeconds(0.2f);
        InfoPanel.Instance?.ShowPanel(!enabled);

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
        if (!enabled) GameplayInputBlocker.SetBlocked(this, false);
    }

    public void ShowSellPage()
    {
        AudioManager.Instance.Play(SoundType.UI_Click);
        buyPage.gameObject.SetActive(false);
        sellPage.gameObject.SetActive(true);
        sellText.color = tabHighlight;
        buyText.color = Color.white;
        UpdateTabAppearance(true);
    }

    public void ShowBuyPage()
    {
        AudioManager.Instance.Play(SoundType.UI_Click);
        sellPage.gameObject.SetActive(false);
        buyPage.gameObject.SetActive(true);
        buyText.color = tabHighlight;
        sellText.color = Color.white;
        UpdateTabAppearance(false);
    }

    private void UpdateTabAppearance(bool selling)
    {
        if (sellTabBackground) sellTabBackground.color = selling ? activeTabColor : inactiveTabColor;
        if (buyTabBackground) buyTabBackground.color = selling ? inactiveTabColor : activeTabColor;
    }

}
