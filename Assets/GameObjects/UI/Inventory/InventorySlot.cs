using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventorySlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;           // -> Kind "Icon"
    [SerializeField] private TextMeshProUGUI countText; // -> Kind "CountText"

    public void Set(Sprite icon, int count)
    {
        if (iconImage) { iconImage.sprite = icon; iconImage.enabled = icon != null; }
        if (countText) countText.text = count.ToString();
    }
}