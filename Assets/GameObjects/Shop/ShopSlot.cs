using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopSlot : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;              // Kind "Icon"
    [SerializeField] private TextMeshProUGUI countText;    // Kind "CountText"
    [SerializeField] private Image selectionFrame;         // optionaler Rahmen fürs Highlight
    [SerializeField] private Button button;                // Button auf demselben GO

    // Exponieren, damit ShopUI vergleichen kann
    public ItemSO Item { get; private set; }
    public int Count { get; private set; }

    private ShopUI shop;                                   // Referenz auf ShopUI für Callbacks

    /// <summary> Slot befüllen und Klick-Callback setzen. </summary>
    public void Bind(ItemSO item, int count, ShopUI shopUI)
    {
        Item = item;
        Count = count;
        shop = shopUI;

        if (iconImage) { iconImage.sprite = item ? item.icon : null; iconImage.enabled = item && item.icon; }
        if (countText) countText.text = count.ToString();

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => shop.SelectItem(Item));
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
        if (countText) countText.text = newCount.ToString();
    }
}
