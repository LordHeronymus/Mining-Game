using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuyPage : MonoBehaviour
{
    RectTransform content;
    Button purchaseButton;
    TextMeshProUGUI priceText;
    TMP_FontAsset font;
    Material fontMaterial;

    void Awake()
    {
        var scroll = GetComponentInChildren<ScrollRect>(true);
        if (!scroll || !scroll.content) return;
        var sourceText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (sourceText) { font = sourceText.font; fontMaterial = sourceText.fontSharedMaterial; }
        content = scroll.content;
        var grid = content.GetComponent<GridLayoutGroup>();
        if (grid) grid.enabled = false;
        foreach (Transform child in content) child.gameObject.SetActive(false);
        BuildMedkitCard();
    }

    void OnEnable()
    {
        if (StatsManager.Instance) StatsManager.Instance.OnMoneyChanged += OnMoneyChanged;
        Refresh();
    }

    void OnDisable()
    {
        if (StatsManager.Instance) StatsManager.Instance.OnMoneyChanged -= OnMoneyChanged;
    }

    void OnMoneyChanged(int _) => Refresh();

    void BuildMedkitCard()
    {
        var card = new GameObject("Medkit-Rezept", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(content, false);
        var rect = (RectTransform)card.transform;
        Place(rect, 12, 12, 620, 184);
        var background = card.GetComponent<Image>();
        background.color = new Color32(54, 34, 23, 240);

        var icon = new GameObject("Rezept-Sprite", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(card.transform, false);
        Place((RectTransform)icon.transform, 10, 10, 164, 164);
        var iconImage = icon.GetComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>("Shop/MedkitRecipe");
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        Label(card.transform, "Medkit-Rezept", 192, 20, 404, 46, 31);
        priceText = Label(card.transform, "150 $", 192, 75, 190, 40, 28);

        var buttonObject = new GameObject("Kaufen", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(card.transform, false);
        Place((RectTransform)buttonObject.transform, 388, 106, 204, 58);
        buttonObject.GetComponent<Image>().color = new Color32(138, 75, 33, 255);
        purchaseButton = buttonObject.GetComponent<Button>();
        purchaseButton.onClick.AddListener(() =>
        {
            if (ShopManager.Instance && ShopManager.Instance.TryBuyMedkitRecipe()) Refresh();
        });
        Label(buttonObject.transform, "Kaufen", 0, 0, 204, 58, 27).alignment = TextAlignmentOptions.Center;
        Refresh();
    }

    void Refresh()
    {
        if (!purchaseButton || !priceText) return;
        bool unlocked = RecipeUnlocks.IsMedkitUnlocked;
        priceText.text = unlocked ? "Gekauft" : "150 $";
        purchaseButton.gameObject.SetActive(!unlocked);
        purchaseButton.interactable = !unlocked && StatsManager.Instance &&
            StatsManager.Instance.Money >= RecipeUnlocks.MedkitPrice;
    }

    TextMeshProUGUI Label(Transform parent, string value, float x, float y, float width, float height, float size)
    {
        var obj = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        Place((RectTransform)obj.transform, x, y, width, height);
        var label = obj.GetComponent<TextMeshProUGUI>();
        if (font) label.font = font;
        if (fontMaterial) label.fontSharedMaterial = fontMaterial;
        label.text = value;
        label.fontSize = size;
        label.color = new Color32(255, 235, 200, 255);
        label.raycastTarget = false;
        return label;
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
