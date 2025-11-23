using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;              // Kind "Icon"
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

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => shop.SelectItem(Item));
        }

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectionFrame) selectionFrame.enabled = selected;
    }
}
