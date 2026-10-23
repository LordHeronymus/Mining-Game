using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Play Mode in a run (after loading). Result in Temp/InventoryDeleteChecks-result.txt, screenshot Temp/items-loeschen.png.
public static class InventoryDeleteChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Requires Play Mode in a run.");
        new GameObject("Inventory delete checks").AddComponent<InventoryDeleteProbe>();
        return "Checking right click, delete window, amount slider, red button, cancel, delete and close paths.";
    }
}
public sealed class InventoryDeleteProbe : MonoBehaviour
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Text(Component c) => c.GetComponentInChildren<TextMeshProUGUI>().text;
    IEnumerator Start()
    {
        while (LoadingProgress.Active) yield return null;
        var ui = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var inventory = InventoryManager.Instance; var tests = GpsSettings.Tests;
        var snapshot = inventory.GetSnapshot().ToDictionary(x => x.Key, x => x.Value);
        bool previousWeight = tests.noWeight;
        string result = "FAILED: incomplete";
        try
        {
            tests.noWeight = true; inventory.ResetAll();
            var catalog = Resources.Load<ItemCatalog>("ItemCatalog").items;
            var iron = catalog.First(x => x && x.item == Item.Iron);
            var wood = catalog.First(x => x && x.item == Item.Wood);
            inventory.AddStartingItem(iron, 15, false); inventory.AddStartingItem(wood, 4, false);
            ui.ShowPanel(); yield return new WaitForSecondsRealtime(.3f);
            Check(ui.IsOpen && !ui.DeleteDialogOpen, "Inventory opens without the delete window.");
            var content = ui.transform.Find("Inventory Layout/Items Viewport/Items");
            var cell = content.GetChild(0).GetComponent<InventoryCellDrag>();
            Check(cell.item() == iron, "Iron is in the first cell.");
            var left = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 1 };
            cell.OnPointerClick(left);
            Check(!ui.DeleteDialogOpen, "Left click does not open the delete window.");
            var right = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right, clickCount = 1 };
            cell.OnPointerClick(right); yield return null;
            Check(ui.DeleteDialogOpen && ui.DeleteItem == iron && ui.SelectedItem == iron, "Right click opens the window for the item.");
            var window = ui.transform.Find("Inventory Layout/Delete Dialog/Delete Window");
            var labels = window.GetComponentsInChildren<TextMeshProUGUI>().Select(x => x.text).ToArray();
            Check(labels.Contains(iron.displayName) && labels.Contains("Erz · Bestand 15") && labels.Contains("Wie viel löschen?"), "Name, stock and question shown.");
            var slider = ui.DeleteSlider; var confirm = ui.DeleteConfirmButton;
            Check(slider.wholeNumbers && slider.minValue == 1 && slider.maxValue == 15 && slider.value == 1, "Slider spans 1 to the whole stock.");
            Check(Text(confirm) == "1 löschen", "Button starts at one.");
            Check(slider.handleRect.parent.GetSiblingIndex() > slider.fillRect.parent.GetSiblingIndex(), "Handle number is drawn above the fill.");
            var frame = window.GetComponent<Image>();
            Check(frame.sprite && frame.sprite.texture.name == "BuyFrames-v2", "Window uses the opaque shop card frame.");
            var box = (RectTransform)window; var tabs = ui.transform.Find("Inventory Layout/Filter Alle") as RectTransform;
            var footer = ui.transform.Find("Inventory Layout/Selection Footer") as RectTransform;
            Check(-box.anchoredPosition.y >= -tabs.anchoredPosition.y + tabs.rect.height && -box.anchoredPosition.y + box.rect.height <= -footer.anchoredPosition.y,
                "Window fits between the filter tabs and the info bar.");
            var red = confirm.GetComponent<Image>().sprite;
            Check(red && red.name == "DeleteButton" && red.texture.name == "SelectionButtons-Red", "Delete button uses the red filled artwork.");
            slider.value = 6; yield return new WaitForSecondsRealtime(.4f);
            Check(ui.DeleteAmount == 6 && Text(slider.handleRect) == "6" && Text(confirm) == "6 löschen", "Handle and button follow the slider.");
            ScreenCapture.CaptureScreenshot("Temp/items-loeschen.png"); yield return null; yield return null;
            window.Find("Cancel").GetComponent<Button>().onClick.Invoke();
            Check(!ui.DeleteDialogOpen && inventory.GetCount(iron) == 15, "Abbrechen closes without deleting.");
            ui.OpenDeleteDialog(iron);
            Check(ui.DeleteAmount == 1, "Reopening starts at one.");
            slider.value = 6; confirm.onClick.Invoke();
            Check(!ui.DeleteDialogOpen && inventory.GetCount(iron) == 9, "Delete removes exactly the chosen amount.");
            ui.OpenDeleteDialog(iron); slider.value = 8;
            inventory.TryRemove(iron, 4);
            Check(slider.maxValue == 5 && ui.DeleteAmount == 5 && Text(confirm) == "5 löschen", "Stock change while open moves Max and clamps the amount.");
            ui.transform.Find("Inventory Layout/Delete Dialog/Delete Dim").GetComponent<Button>().onClick.Invoke();
            Check(!ui.DeleteDialogOpen && inventory.GetCount(iron) == 5, "Click beside the window closes it.");
            ui.OpenDeleteDialog(wood); slider.value = slider.maxValue;
            Check(Text(confirm) == "4 löschen", "Max selects the whole stock.");
            confirm.onClick.Invoke(); yield return null;
            Check(inventory.GetCount(wood) == 0 && ui.VisibleItemCount == 1, "Deleting everything removes the item from the grid.");
            inventory.AddStartingItem(wood, 1, false); ui.OpenDeleteDialog(wood);
            Check(!slider.interactable && Text(confirm) == "1 löschen", "A single item needs no slider.");
            ui.HidePanel(); yield return new WaitForSecondsRealtime(.3f);
            Check(!ui.IsOpen && !ui.DeleteDialogOpen, "Closing the inventory closes the window.");
            result = "PASS: right click opens, left click does not, name/stock, opaque frame, handle above fill, slider 1..Max with number in handle, red filled button, cancel, exact delete, live Max, click beside, delete all, single item, close with inventory.";
        }
        finally
        {
            if (ui.IsOpen) ui.HidePanel();
            inventory.ResetAll();
            foreach (var entry in snapshot) inventory.AddStartingItem(entry.Key, entry.Value, false);
            tests.noWeight = previousWeight;
            File.WriteAllText("Temp/InventoryDeleteChecks-result.txt", result); Destroy(gameObject);
        }
    }
}
