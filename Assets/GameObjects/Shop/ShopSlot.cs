using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopSlot : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;              // Kind "Icon"
    [SerializeField] private TextMeshProUGUI countText;    // Kind "CountText"
    [SerializeField] private Image countBadge;
    [SerializeField] private Image selectionFrame;         // optionaler Rahmen fürs Highlight
    [SerializeField] private Button button;                // Button auf demselben GO

    // Exponieren, damit ShopUI vergleichen kann
    public ItemSO Item { get; private set; }
    public int Count { get; private set; }

    private SellPage shop;                                   // Referenz auf ShopUI für Callbacks

    /// <summary> Slot befüllen und Klick-Callback setzen. </summary>
    public void Bind(ItemSO item, int count, SellPage shopUI)
    {
        Item = item;
        Count = count;
        shop = shopUI;

        if (iconImage) { iconImage.sprite = item ? item.icon : null; iconImage.enabled = item && item.icon; }
        UpdateCountDisplay(count);

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => shop.HandleItemClick(Item));
        }

        SetSelected(false);
    }

    /// <summary> Visuelles Highlight umschalten. </summary>
    public void SetSelected(bool selected)
    {
        if (selectionFrame) selectionFrame.enabled = selected;
    }

    /// <summary> Falls nur Anzeige aktualisiert werden soll (z. B. nach Verkauf). </summary>
    public void SetCount(int newCount)
    {
        Count = newCount;
        UpdateCountDisplay(newCount);
    }

    private void UpdateCountDisplay(int count)
    {
        if (!countText) return;
        countText.text = count.ToString();
        float width = Mathf.Max(52f, Mathf.Ceil(countText.GetPreferredValues(countText.text).x) + 18f);
        if (countBadge) countBadge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        countText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }
}
