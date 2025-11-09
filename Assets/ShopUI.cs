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

    [Header("Details Panel")]
    [SerializeField] private Image selectedIcon;
    [SerializeField] private TextMeshProUGUI selectedNameText;
    [SerializeField] private TextMeshProUGUI selectedCountText;
    [SerializeField] private TextMeshProUGUI selectedWorthText;

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

        panel.alpha = 0f;
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

        // Nach Rebuild aktuelle Auswahl erneut hervorheben
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
        // Daten haben sich geändert (Verkauf etc.)
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
            if (selectedIcon) selectedIcon.enabled = false;
            if (selectedNameText) selectedNameText.text = "";
            if (selectedCountText) selectedCountText.text = "";
            if (selectedWorthText) selectedWorthText.text = "";
            return;
        }

        int count = InventoryManager.Instance?.GetCount(_selected) ?? 0;
        if (selectedIcon) { selectedIcon.enabled = true; selectedIcon.sprite = _selected.icon; }
        if (selectedNameText) selectedNameText.text = _selected.displayName;
        if (selectedCountText) selectedCountText.text = $"x{count}";
        if (selectedWorthText) selectedWorthText.text = $"{_selected.worth} each";
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
        if (!_selected || !_selected || InventoryManager.Instance == null) return;
        int have = InventoryManager.Instance.GetCount(_selected);
        if (have <= 0) return;

        int q = Mathf.Min(qty, have);
        ShopManager.Instance?.TrySell(_selected, q);
        Rebuild();
        UpdateDetails();
        UpdateButtons();
    }

    private void SellMax()
    {
        if (!_selected || InventoryManager.Instance == null) return;
        int have = InventoryManager.Instance.GetCount(_selected);
        if (have <= 0) return;

        ShopManager.Instance?.TrySell(_selected, have);
        Rebuild();
        UpdateDetails();
        UpdateButtons();
    }

    private void SellAll()
    {
        ShopManager.Instance?.SellAll();
        Rebuild();
        UpdateDetails();
        UpdateButtons();
    }
}
