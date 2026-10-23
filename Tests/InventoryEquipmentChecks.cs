using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Inventory tab "Ausrüstung": fifth tab, six shop-style cards, detail with blueprint and stat boxes.
// Requires a fresh Play start (MainMenu -> run); restores the inventory afterwards.
public static class InventoryEquipmentChecks
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }

    public static string Main()
    {
        passed = 0;
        if (!Application.isPlaying) return "Requires Play Mode (fresh run).";
        var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        var inv = InventoryManager.Instance;
        Check(ui && inv, $"Fixture: ui={!!ui}, inventory={!!inv}");
        var saved = inv.CaptureRunState();
        try
        {
            inv.ResetAll();
            ui.ShowPanel();
            var layout = ui.transform.Find("Inventory Layout");
            var tabs = Enumerable.Range(0, 5).Select(i => layout.Find("Filter " + new[] { "Alle", "Erze", "Materialien", "Werkzeuge", "Ausrüstung" }[i])).ToArray();
            Check(tabs.All(t => t), "Five tabs incl. Ausrüstung");
            var first = (RectTransform)tabs[0]; var last = (RectTransform)tabs[4];
            Check(Mathf.Approximately(first.anchoredPosition.x, 222) && Mathf.Approximately(last.anchoredPosition.x + last.sizeDelta.x, 1452),
                "Tabs span the item area (222..1452)");

            tabs[4].GetComponent<Button>().onClick.Invoke();
            Check(ui.Filter == InventoryUI.EquipmentFilter, "Tab click selects equipment filter");
            var root = layout.Find("Equipment");
            Check(root && root.gameObject.activeSelf, "Equipment view visible");
            Check(!layout.Find("Items Viewport").gameObject.activeSelf && !layout.Find("Selection Footer").gameObject.activeSelf,
                "Item grid and footer hidden on equipment tab");
            Check(ui.EquipmentEntries.Count == 6, "Six entries");
            Check(Enumerable.Range(0, 6).All(i => root.Find("Equipment " + i).gameObject.activeSelf), "Six cards active");

            // Fresh run: copper pickaxe, no tools, no upgrades.
            var e = ui.EquipmentEntries;
            Check(e[0].item && e[0].item.item == Item.CopperPickaxe && e[0].badge == "Stufe 1", "Start pickaxe: Kupfer, Stufe 1");
            Check(!e[1].owned && e[1].badge == "–" && e[1].note == "Noch nicht gefunden", "Scythe missing");
            Check(!e[4].owned && e[4].name == "Kein Traglast-Teil" && e[4].badge == "Stufe 1", "No carrying upgrade");
            Check(!e[5].owned && e[5].badge == "Stufe 1", "No energy upgrade");
            var card1 = root.Find("Equipment 1");
            Check(card1.Find("Icon").GetComponent<Image>().color.a < .9f, "Missing tool icon dimmed");

            // Unlock items: the tab follows live.
            inv.Add(StartingResourcesSettings.Resolve((int)Item.IronPickaxe));
            inv.Add(StartingResourcesSettings.Resolve((int)Item.Scythe));
            inv.Add(StartingResourcesSettings.Resolve((int)Item.Backpack));
            inv.Add(StartingResourcesSettings.Resolve((int)Item.CrystalPendant));
            e = ui.EquipmentEntries;
            Check(e[0].name == "Eisenspitzhacke" && e[0].badge == "Stufe 2" && e[0].note == "Nächste: Stahlspitzhacke", "Iron pickaxe level 2");
            Check(e[0].stats[1].value == "bis 2" && e[0].stats[2].value == "×1,3", $"Pickaxe stats: {e[0].stats[1].value} {e[0].stats[2].value}");
            Check(e[1].owned && e[1].badge == EquipmentOverview.Check, "Scythe owned");
            Check(card1.Find("Badge/Check").gameObject.activeSelf, "Check mark on owned tool");
            Check(e[4].owned && e[4].name == "Rucksack" && e[4].badge == "Stufe 2", "Backpack is carrying level 2");
            Check(e[4].stats[1].value == inv.CarryingCapacity.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " kg",
                "Carrying value matches capacity");
            Check(e[4].note.StartsWith("Nächste: Lastengürtel"), "Next carrying item");
            Check(e[5].owned && e[5].name == "Kristallanhänger" && e[5].stats[1].value == "×1,5", "Pendant energy ×1,5");

            // Detail: blueprint for the pickaxe, plain icon for the scythe.
            ui.SelectEquipment(0);
            var detail = root.Find("Equipment Detail");
            var blueprint = detail.Find("Blueprint").GetComponent<Image>();
            Check(blueprint.enabled && blueprint.sprite && blueprint.sprite.name.Contains("IronPickaxe"), "Pickaxe detail shows blueprint");
            Check(detail.Find("Text").GetComponent<TextMeshProUGUI>().text == "Eisenspitzhacke", "Detail name");
            root.Find("Equipment 1").GetComponent<Button>().onClick.Invoke();
            Check(ui.SelectedEquipment == 1, "Card click selects");
            Check(!blueprint.enabled && detail.Find("Item Icon").GetComponent<Image>().enabled, "Scythe detail shows icon");
            Check(detail.Find("Next/Text").GetComponent<TextMeshProUGUI>().text == "Mäht hohes Gras", "Scythe note");
            Canvas.ForceUpdateCanvases();
            foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>())
                Check(label.font && label.font.name.Contains("Title"), "Equipment text uses the shop font: " + label.name);

            ui.SetFilter(0);
            Check(!root.gameObject.activeSelf && layout.Find("Items Viewport").gameObject.activeSelf, "Back to item grid");
            ui.StopAllCoroutines(); var hide = ui.FadePanel(false); while (hide.MoveNext()) { }
            return $"Passed {passed} checks: five tabs, equipment view, live unlocks, stats, blueprint detail, selection, shop font.";
        }
        finally
        {
            inv.RestoreRunState(saved);
            ui.SetFilter(0);
        }
    }
}
