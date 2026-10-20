using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a fresh Play start from MainMenu (run after the game finished loading).
public static class StorageUiChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || LoadingProgress.Active) throw new Exception("Requires Play Mode after the game finishes loading.");
        new GameObject("Storage UI checks").AddComponent<StorageUiProbe>();
        return "Running storage entry button, chest layout, click/drag moves and triple-capacity withdraw checks; result in Temp/StorageUiChecks-result.txt.";
    }
}

public sealed class StorageUiProbe : MonoBehaviour
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    IEnumerator Start()
    {
        var storage = Object.FindFirstObjectByType<SurfaceStorageBuilding>();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var inventory = InventoryManager.Instance;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        string result = "FAILED: incomplete";
        if (!storage || !player || !inventory || !catalog)
        {
            File.WriteAllText("Temp/StorageUiChecks-result.txt", "FAILED: storage, player, inventory or catalog missing");
            Destroy(gameObject); yield break;
        }
        var snapshot = inventory.GetSnapshot().ToDictionary(x => x.Key, x => x.Value);
        var storedBefore = storage.CaptureRunState();
        var playerPosition = player.transform.position;
        var iron = catalog.items.First(x => x && x.item == Item.Iron);
        var coal = catalog.items.First(x => x && x.item == Item.Coal);
        var gold = catalog.items.First(x => x && x.item == Item.Gold);
        StoragePanel panel = null;
        try
        {
            inventory.ResetAll();
            storage.RestoreRunState(Array.Empty<SavedItem>());
            Teleport(player, storage.transform.position + Vector3.up * 1.2f);
            float started = Time.realtimeSinceStartup;
            while (!storage.HasVisitor && Time.realtimeSinceStartup - started < 2) yield return new WaitForFixedUpdate();
            Check(storage.HasVisitor, "Player inside the storage trigger.");
            yield return new WaitForSecondsRealtime(.35f);
            var entry = storage.EntryButton;
            Check(entry && entry.GetComponent<CanvasGroup>().alpha > .99f && entry.GetComponent<CanvasGroup>().interactable, "Entry button fades in near the hut.");
            Check(entry.GetComponentInParent<Canvas>().renderMode == RenderMode.WorldSpace, "Entry button is a world-space sign.");
            Check(entry.GetComponentInChildren<TextMeshProUGUI>().text == "Lager öffnen", "Entry label.");
            Check(entry.GetComponent<GoldButtonFeedback>(), "Entry button uses the gold wood button style.");

            entry.onClick.Invoke();
            panel = StoragePanel.Instance;
            Check(panel && panel.IsOpen && storage.IsOpen && GameplayInputBlocker.IsBlocked, "Entry opens the storage panel and blocks gameplay.");
            yield return null;
            Check(entry.GetComponent<CanvasGroup>().alpha < 1 || !entry.GetComponent<CanvasGroup>().interactable, "Entry button hides while the panel is open.");
            var layout = panel.transform.Find("Layout");
            var backpackFrame = (RectTransform)layout.Find("Backpack Frame");
            var storageFrame = (RectTransform)layout.Find("Storage Frame");
            Check(backpackFrame.anchoredPosition.x < storageFrame.anchoredPosition.x, "Backpack is on the left, storage on the right.");
            Check(layout.Find("Background").GetComponent<Image>().sprite.texture.name == "BuyBackground-v2", "Shop background artwork.");

            inventory.Add(iron, 10); inventory.Add(coal, 7); inventory.Add(gold, 3);
            yield return null;
            var ironSlot = panel.BackpackSlots.First(x => x.item == iron);
            panel.Click(ironSlot, PointerEventData.InputButton.Left, false);
            Check(storage.GetStoredCount(iron) == 1 && inventory.GetCount(iron) == 9, "Click deposits one.");
            panel.Click(panel.BackpackSlots.First(x => x.item == iron), PointerEventData.InputButton.Right, false);
            Check(storage.GetStoredCount(iron) == 6 && inventory.GetCount(iron) == 4, "Right click deposits half (rounded up).");
            panel.Click(panel.BackpackSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, true);
            Check(storage.GetStoredCount(iron) == 10 && inventory.GetCount(iron) == 0, "Shift click deposits the stack.");
            Check(panel.StorageSlots.Any(x => x.item == iron && x.amount == 10) && panel.BackpackSlots.All(x => x.item != iron), "Grids refresh after moves.");
            panel.Click(panel.StorageSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, false);
            Check(storage.GetStoredCount(iron) == 9 && inventory.GetCount(iron) == 1, "Click on the storage side withdraws one.");

            panel.SetCategory(1);
            Check(panel.BackpackSlots.Where(x => x.item).All(x => x.item.category == ItemCategory.Ore), "Ore tab filters both sides.");
            panel.SetCategory(0); panel.SetSearch("kohle");
            Check(panel.BackpackSlots.Count(x => x.item) == 1 && panel.BackpackSlots[0].item == coal, "Search filters items.");
            panel.SetSearch("");

            Canvas.ForceUpdateCanvases();
            var coalSlot = panel.BackpackSlots.First(x => x.item == coal);
            var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var drop = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(camera, storageFrame.TransformPoint(storageFrame.rect.center)),
                pressEventCamera = camera
            };
            panel.BeginDrag(coalSlot, drop); panel.Drop(drop);
            Check(storage.GetStoredCount(coal) == 7 && inventory.GetCount(coal) == 0, "Dragging onto the storage side moves the whole stack.");

            panel.DepositOres();
            Check(inventory.GetCount(iron) == 0 && inventory.GetCount(gold) == 0, "Ore deposit button stores all ores.");
            panel.WithdrawAll();
            Check(storage.StoredItems.Count == 0 && inventory.GetCount(coal) == 7, "Withdraw all empties the storage when the weight fits.");
            panel.DepositAll();
            Check(inventory.GetCount(coal) == 0 && inventory.GetCount(iron) == 0, "Deposit all stores non-hotbar items.");

            if (!GameplayTestSettings.NoWeight && iron.EffectiveWeight > 0)
            {
                inventory.ResetAll();
                double weight = iron.EffectiveWeight;
                int fit = (int)Math.Floor(inventory.MaximumWeight / weight + 1e-6);
                storage.RestoreRunState(new[] { new SavedItem { id = (int)Item.Iron, count = fit + 25 } });
                panel.Click(panel.StorageSlots.First(x => x.item == iron), PointerEventData.InputButton.Left, true);
                Check(inventory.GetCount(iron) == fit && storage.GetStoredCount(iron) == 25, $"Withdraw stops exactly at three times the carrying capacity ({fit} iron).");
                Check(inventory.TotalWeight > inventory.CarryingCapacity && inventory.TotalWeight <= inventory.MaximumWeight + 1e-4,
                    "Player may carry above the capacity (slower) but not above the maximum.");
                Check(Mathf.Abs(inventory.MaximumWeight - inventory.CarryingCapacity * 3) < .001f, "Maximum is three times the capacity.");
                Check(storage.WithdrawUpTo(iron, 1) == 0, "No further withdraw at the maximum.");
                yield return null;
                var slower = (RectTransform)layout.Find("Weight Bar/Slower");
                var normal = (RectTransform)layout.Find("Weight Bar/Normal");
                Check(slower.sizeDelta.x > 1 && normal.sizeDelta.x > 1, "Weight bar shows the slower zone above the capacity.");
            }

            storage.Close();
            Check(!panel.IsOpen && !GameplayInputBlocker.IsBlocked, "Close unblocks gameplay.");
            entry.onClick.Invoke();
            Check(panel.IsOpen, "Panel reopens.");
            Teleport(player, playerPosition);
            started = Time.realtimeSinceStartup;
            while (panel.IsOpen && Time.realtimeSinceStartup - started < 2) yield return new WaitForFixedUpdate();
            Check(!panel.IsOpen && !GameplayInputBlocker.IsBlocked, "Walking away closes the storage.");
            result = "PASS: entry sign, chest layout (backpack left, storage right), click/shift/right-click/drag moves, filters, quick buttons, withdraw up to 3x carrying capacity, close rules.";
        }
        finally
        {
            if (panel && panel.IsOpen) panel.Hide();
            Teleport(player, playerPosition);
            inventory.ResetAll(); foreach (var item in snapshot) inventory.Add(item.Key, item.Value);
            storage.RestoreRunState(storedBefore);
            File.WriteAllText("Temp/StorageUiChecks-result.txt", result);
            Destroy(gameObject);
        }
    }

    static void Teleport(PlayerMovement player, Vector3 position)
    {
        var body = player.GetComponent<Rigidbody2D>();
        player.transform.position = position;
        if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }
}
