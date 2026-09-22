using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class InventoryDesignChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static string Main()
    {
        Check(Application.isPlaying, "Run in Play Mode");
        var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        var inv = InventoryManager.Instance;
        var items = AssetDatabase.FindAssets("t:ItemSO").Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        Check(inv.GetSnapshot().Count == 0, "Use a fresh play session for fixture items");
        int[] amounts = {128,84,36,27,12,6,245,52,18,60,24,8,5,3};
        Item[] order = {Item.Coal,Item.Iron,Item.Copper,Item.Silver,Item.Gold,Item.Platinum,Item.Wood,Item.PlantFiber,Item.Rope,Item.Nails,Item.Torche,Item.Dynamite,Item.Ladder,Item.BridgePart};
        for(int i=0;i<order.Length;i++) inv.Add(items.First(x=>x.item==order[i]),amounts[i]);
        ui.ShowPanel();
        Check(ui.IsOpen && GameplayInputBlocker.IsBlocked,"Inventory must block gameplay");
        Check(ui.VisibleItemCount==14,"All items should display");
        var content = ui.transform.Find("Inventory Layout/Items Viewport/Items");
        Check(content.childCount==21,"Three rows of seven slots required");
        var gold=items.First(x=>x.item==Item.Gold);
        content.GetChild(4).GetComponent<Button>().onClick.Invoke();
        Check(ui.SelectedItem==gold,"Click must select Gold");
        ui.SetFilter(1); Check(ui.VisibleItemCount==6,"Ore filter");
        ui.SetFilter(2); Check(ui.VisibleItemCount==4,"Material filter");
        ui.SetFilter(3); Check(ui.VisibleItemCount==4,"Tool filter");
        ui.SetFilter(0); ui.SelectItem(gold);
        ui.SortItems(); Check(ui.SelectedItem==gold,"Sorting must preserve selection"); ui.SortItems();
        inv.Add(gold,99987);
        Canvas.ForceUpdateCanvases();
        var count=content.GetChild(4).Find("Count Badge/Text").GetComponent<TextMeshProUGUI>();
        Check(count.text=="99999", "Five digits must remain unabbreviated");
        Check(count.GetPreferredValues(count.text).x <= count.rectTransform.rect.width,"Five digits must fit badge");
        inv.Add(gold,1); Check(count.text=="100k","Large count abbreviation");
        inv.TryRemove(gold,99988);
        Check(count.text=="12","Inventory changes must refresh UI");
        // Drive the fade enumerator to completion without waiting between checks.
        ui.StopAllCoroutines(); var hide=ui.FadePanel(false); while(hide.MoveNext()) { }
        Check(!ui.IsOpen && !GameplayInputBlocker.IsBlocked,"Closing must release gameplay");
        var blocker=UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>();
        GameplayInputBlocker.SetBlocked(blocker,true); ui.ShowPanel();
        Check(!ui.IsOpen,"Inventory must not open over another modal");
        GameplayInputBlocker.SetBlocked(blocker,false);
        ui.ShowPanel(); ui.SelectItem(gold);
        return "Passed: 21 cells, all filters, click selection, sorting, live updates, five digits, compact counts, close release and modal guard. Fixture inventory remains for screenshot; exit Play Mode afterward.";
    }
}
