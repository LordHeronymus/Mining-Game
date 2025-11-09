// ShopUI.cs
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform content;
    [SerializeField] private ShopSlot slotPrefab;
    [SerializeField] private CanvasGroup panel;

    // --- Details Panel (nur Texte) ---
    [Header("Details Panel (Texts)")]
    [SerializeField] private GameObject detailsPanel;
    [SerializeField] private TextMeshProUGUI oreNameText;    // OreName
    [SerializeField] private TextMeshProUGUI oreWorthText;   // OreWorth (Stückpreis)
    [SerializeField] private TextMeshProUGUI countText;      // Count (Anzahl im Inventar)
    [SerializeField] private TextMeshProUGUI totalWorthText; // TotalWorth = worth * count

    [Header("Buttons")]
    [SerializeField] private Button sell1Button;
    [SerializeField] private Button sell10Button;
    [SerializeField] private Button sellMaxButton;
    [SerializeField] private Button sellAllButton;

    [SerializeField] float fadeDuration = 0.1f;

    private bool panelVisible = false;
    private ItemSO _selected;

    void Awake()
    {
        if (sell1Button) sell1Button.onClick.AddListener(() => Sell(1));
        if (sell10Button) sell10Button.onClick.AddListener(() => Sell(10));
        if (sellMaxButton) sellMaxButton.onClick.AddListener(SellMax);
        if (sellAllButton) sellAllButton.onClick.AddListener(SellAll);

        if (panel) panel.alpha = 0f;
    }

    void OnEnable()
    {
        if (InventoryManager.Instance) InventoryManager.Instance.OnInventoryChanged += RefreshSelectionAndList;
    }

    void OnDisable()
    {
        if (InventoryManager.Instance) InventoryManager.Instance.OnInventoryChanged -= RefreshSelectionAndList;
    }

    void Update()
    {
        if (panelVisible && Input.GetKeyDown(KeyCode.Escape)) HidePanel();
    }

    // ---- Öffnen/Schließen ----
    public void ShowPanel()
    {
        Rebuild();
        StopAllCoroutines();
        StartCoroutine(FadePanel(true));
    }

    public void HidePanel()
    {
        StopAllCoroutines();
        StartCoroutine(FadePanel(false));
    }

    public IEnumerator FadePanel(bool enabled)
    {
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

    // ---- Liste/Slots ----
    private void Rebuild()
    {
        if (!content || InventoryManager.Instance == null) return;
        ClearChildren();

        var snap = InventoryManager.Instance.GetSnapshot();

        foreach (var kv in snap
            .Where(k => k.Key != null && k.Key.category == ItemCategory.Ore)
            .OrderBy(k => k.Key.displayName))
        {
            var slot = Instantiate(slotPrefab, content);
            slot.Bind(kv.Key, kv.Value, this);
            slot.name = $"Slot_{kv.Key.displayName}";
        }

        ApplySelectionHighlight();
        UpdateDetails();
        UpdateButtons();
    }

    private void ClearChildren()
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    private void RefreshSelectionAndList()
    {
        Rebuild();
    }

    // ---- Auswahl ----
    public void SelectItem(ItemSO item)
    {
        _selected = item;
        ApplySelectionHighlight();
        UpdateDetails();
        UpdateButtons();
    }

    private void ApplySelectionHighlight()
    {
        for (int i = 0; i < content.childCount; i++)
        {
            var slot = content.GetChild(i).GetComponent<ShopSlot>();
            if (!slot) continue;
            slot.SetSelected(_selected != null && slot.Item == _selected);
        }
    }

    // ---- Details & Buttons ----
    private void UpdateDetails()
    {
        if (_selected == null)
        {
            detailsPanel.SetActive(false);
            if (oreNameText) oreNameText.text = "";
            if (oreWorthText) oreWorthText.text = "";
            if (countText) countText.text = "";
            if (totalWorthText) totalWorthText.text = "";
            return;
        }
        else
        {
            detailsPanel.SetActive(true);
        }

        int count = InventoryManager.Instance?.GetCount(_selected) ?? 0;
        int worth = _selected.worth;
        int total = worth * count;

        if (oreNameText) oreNameText.text = _selected.displayName;
        if (oreWorthText) oreWorthText.text = $"Worth: {worth}";
        if (countText) countText.text = $"Count: {count}";
        if (totalWorthText) totalWorthText.text = $"Total: {total}";
    }

    private void UpdateButtons()
    {
        int count = (_selected && InventoryManager.Instance) ? InventoryManager.Instance.GetCount(_selected) : 0;
        bool canSell = _selected && _selected.worth > 0 && count > 0;

        if (sell1Button) sell1Button.interactable = canSell && count >= 1;
        if (sell10Button) sell10Button.interactable = canSell && count >= 10;
        if (sellMaxButton) sellMaxButton.interactable = canSell && count >= 1;
    }

    private void Sell(int qty)
    {
        if (!_selected || InventoryManager.Instance == null) return;
        int have = InventoryManager.Instance.GetCount(_selected);
        if (have <= 0) return;

        int q = Mathf.Min(qty, have);
        ShopManager.Instance?.TrySell(_selected, q);
        Rebuild();           // Liste & Counts sofort aktualisieren
    }

    private void SellMax()
    {
        if (!_selected || InventoryManager.Instance == null) return;
        int have = InventoryManager.Instance.GetCount(_selected);
        if (have <= 0) return;

        ShopManager.Instance?.TrySell(_selected, have);
        Rebuild();
    }

    private void SellAll()
    {
        ShopManager.Instance?.SellAll();
        Rebuild();
    }
}
