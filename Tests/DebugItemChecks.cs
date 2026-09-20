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
        window.SwitchTab(false);
        var content = panel.transform.Find("Card/WindowViewport/WindowContent");
        var dropdown = content.Find("GiftItem").GetComponent<TMP_Dropdown>();
        var amount = content.Find("GiftAmount").GetComponent<TMP_InputField>();
        var button = content.Find("GiftAdd").GetComponent<Button>();
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        var inventory = InventoryManager.Instance;
        Check(catalog && catalog.items.Length >= 6, "Catalog missing ores");
        Check(dropdown.options.Count == catalog.items.Length, "Dropdown/catalog mismatch");
        Check(catalog.items.Count(i => i.category == ItemCategory.Ore) >= 6, "Ore entries missing");
        var savedAmount = amount.text;
        int savedSelection = dropdown.value;
        int events = 0;
        Action listener = () => events++;
        inventory.OnInventoryChanged += listener;
        try
        {
            for (int i = 0; i < catalog.items.Length; i++)
            {
                dropdown.value = i;
                var item = catalog.items[i];
                int before = inventory.GetCount(item);
                amount.text = "10";
                button.onClick.Invoke();
                Check(inventory.GetCount(item) == before + 10, "Wrong item/amount: " + item.name);
                inventory.TryRemove(item, 10);
                foreach (var invalid in new[] { "0", "-1", "", "2147483648" })
                {
                    amount.SetTextWithoutNotify(invalid);
                    button.onClick.Invoke();
                    Check(inventory.GetCount(item) == before, "Invalid amount changed inventory");
                }
            }
            dropdown.value = 0;
            var first = catalog.items[0];
            int initial = inventory.GetCount(first);
            int fill = int.MaxValue - initial;
            inventory.Add(first, fill);
            try
            {
                amount.text = "1";
                button.onClick.Invoke();
                Check(inventory.GetCount(first) == int.MaxValue, "Inventory overflow");
            }
            finally { inventory.TryRemove(first, fill); }
            amount.text = "37";
            panel.Close(); panel.Toggle();
            Check(amount.text == "37" && dropdown.value == 0, "Reopen lost selection/amount");
            window.SwitchTab(true);
            Check(!dropdown.gameObject.activeInHierarchy, "Gift controls visible on tests tab");
            window.SwitchTab(false);
            dropdown.Show();
            Check(dropdown.IsExpanded, "Dropdown failed to open");
            dropdown.Hide();
            Check(events >= catalog.items.Length * 2, "Inventory events missing");
        }
        finally
        {
            inventory.OnInventoryChanged -= listener;
            amount.text = savedAmount;
            dropdown.value = savedSelection;
        }
        return new { passed = true, items = catalog.items.Length, events };
    }
}
