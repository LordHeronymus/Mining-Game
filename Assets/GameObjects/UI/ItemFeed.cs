using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ItemFeed : MonoBehaviour
{
    sealed class Entry
    {
        public ItemSO item;
        public int amount;
        public RectTransform rect;
        public CanvasGroup group;
        public TextMeshProUGUI label;
        public float born;
    }

    const int MaximumEntries = 4;
    const float Lifetime = 3.5f;
    readonly List<Entry> entries = new();
    InventoryManager inventory;
    Coroutine binding;

    void OnEnable() => binding = StartCoroutine(BindInventory());

    void OnDisable()
    {
        if (binding != null) StopCoroutine(binding);
        binding = null;
        if (inventory) inventory.OnItemGained -= Show;
        inventory = null;
    }

    IEnumerator BindInventory()
    {
        while (!InventoryManager.Instance) yield return null;
        inventory = InventoryManager.Instance;
        inventory.OnItemGained += Show;
        binding = null;
    }

    void Show(ItemSO item, int amount)
    {
        if (!item || amount <= 0) return;
        float now = Time.unscaledTime;
        var entry = entries.Find(row => row.item == item);
        if (entry != null)
        {
            entry.amount += amount;
            entry.born = now;
            entry.label.text = $"+{entry.amount}  {Name(item)}";
            entries.Remove(entry);
            entries.Insert(0, entry);
            return;
        }

        entry = CreateEntry(item, amount);
        entry.born = now;
        entries.Insert(0, entry);
        if (entries.Count > MaximumEntries)
        {
            var oldest = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            Destroy(oldest.rect.gameObject);
        }
    }

    Entry CreateEntry(ItemSO item, int amount)
    {
        var rect = MakeRect("Pickup " + item.name, transform, 0f, 0f, 360f, 64f);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(-24f, 110f);

        var background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(.16f, .095f, .065f, .88f);
        background.raycastTarget = false;
        var border = rect.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(.66f, .43f, .22f, .8f);
        border.effectDistance = new Vector2(2f, 2f);

        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var iconRect = MakeRect("Icon", rect, 10f, 8f, 48f, 48f);
        var icon = iconRect.gameObject.AddComponent<Image>();
        icon.sprite = item.icon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var textRect = MakeRect("Amount and Item", rect, 70f, 5f, 280f, 54f);
        var label = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = $"+{amount}  {Name(item)}";
        label.fontSize = 25f;
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, .91f, .73f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return new Entry { item = item, amount = amount, rect = rect, group = group, label = label };
    }

    void Update()
    {
        float now = Time.unscaledTime;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            float age = now - entry.born;
            if (age >= Lifetime)
            {
                entries.RemoveAt(i);
                Destroy(entry.rect.gameObject);
                continue;
            }
            float fadeIn = Mathf.Clamp01(age / .24f);
            float fadeOut = Mathf.Clamp01((Lifetime - age) / .65f);
            entry.group.alpha = Mathf.Min(fadeIn, fadeOut);
            Vector2 target = new Vector2(0f, 110f - i * 72f);
            entry.rect.anchoredPosition = Vector2.Lerp(entry.rect.anchoredPosition,
                target, 1f - Mathf.Exp(-13f * Time.unscaledDeltaTime));
        }
    }

    static string Name(ItemSO item) => string.IsNullOrWhiteSpace(item.displayName)
        ? item.name : item.displayName;

    static RectTransform MakeRect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}
