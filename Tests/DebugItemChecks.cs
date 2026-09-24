using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class DebugItemChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object Main()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if (!GameplayDebugPanel.IsOpen) panel.Toggle();
        var window = panel.GetComponent<GameplayDebugWindow>();
        window.SwitchTab("Items");
        var content = panel.transform.Find("Card/WindowViewport/WindowContent");
        var amount = content.Find("GiftAmount").GetComponent<TMP_InputField>();
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        var inventory = InventoryManager.Instance;
        Check(catalog && catalog.items.Length >= 6, "Catalog missing ores");
        Check(catalog.items.Count(i => i.category == ItemCategory.Ore) >= 6, "Ore entries missing");
        Check(content.Find("GiftSectionOre") && content.Find("GiftSectionMisc") && content.Find("GiftSectionTool"),
            "Creative item sections missing");

        string savedAmount = amount.text;
        int events = 0;
        Action listener = () => events++;
        inventory.OnInventoryChanged += listener;
        try
        {
            content.Find("GiftPreset2").GetComponent<Button>().onClick.Invoke();
            Check(amount.text == "64", "Stack preset did not set the amount");
            foreach (var item in catalog.items)
            {
                var card = content.Find("GiftCard" + (int)item.item).GetComponent<Button>();
                Check(card && card.gameObject.activeInHierarchy, "Item card missing: " + item.name);
                int before = inventory.GetCount(item);
                amount.text = "10";
                card.onClick.Invoke();
                Check(inventory.GetCount(item) == before + 10, "Wrong item/amount: " + item.name);
                inventory.TryRemove(item, 10);
                foreach (var invalid in new[] { "0", "-1", "", "2147483648" })
                {
                    amount.text = invalid;
                    card.onClick.Invoke();
                    Check(inventory.GetCount(item) == before, "Invalid amount changed inventory");
                }
            }

            var first = catalog.items[0];
            var firstCard = content.Find("GiftCard" + (int)first.item).GetComponent<Button>();
            int initial = inventory.GetCount(first);
            int fill = int.MaxValue - initial;
            inventory.Add(first, fill);
            try
            {
                amount.text = "1";
                firstCard.onClick.Invoke();
                Check(inventory.GetCount(first) == int.MaxValue, "Inventory overflow");
            }
            finally { inventory.TryRemove(first, fill); }

            amount.text = "37";
            panel.Close(); panel.Toggle();
            Check(amount.text == "37", "Reopen lost the chosen amount");
            window.SwitchTab(true);
            Check(!firstCard.gameObject.activeInHierarchy, "Gift controls visible on tests tab");
            window.SwitchTab("Items");
            Check(firstCard.gameObject.activeInHierarchy, "Items tab did not restore the cards");
            Check(events >= catalog.items.Length * 2, "Inventory events missing");
        }
        finally
        {
            inventory.OnInventoryChanged -= listener;
            amount.text = savedAmount;
        }
        return new { passed = true, items = catalog.items.Length, events };
    }
}
