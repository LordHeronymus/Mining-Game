using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class HotbarSlotDragChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var original = (ItemSO[])hud.slots.Clone();
        var originalSelection = hud.SelectedSlot;
        int oldVersion = PlayerPrefs.GetInt("CompactHud.SlotLayoutVersion", 0);
        int[] oldLayout = new int[8];
        for (int i = 0; i < oldLayout.Length; i++) oldLayout[i] = PlayerPrefs.GetInt("CompactHud.Slot." + i, int.MinValue);
        try
        {
            hud.CancelHotbarDrag();
            var bar = hud.transform.Find("Hotbar");
            var source = bar.GetChild(1).GetComponent<HotbarSlotDrag>();
            var target = bar.GetChild(2).GetComponent<HotbarSlotDrag>();
            var sourceItem = hud.slots[0];
            var targetItem = hud.slots[1];
            Check(source && target && source.slotIndex == 1 && target.slotIndex == 2, "Every item slot needs a drag handler.");
            Vector2 sourcePoint = RectTransformUtility.WorldToScreenPoint(null, source.transform.TransformPoint(((RectTransform)source.transform).rect.center));
            Vector2 targetPoint = RectTransformUtility.WorldToScreenPoint(null, target.transform.TransformPoint(((RectTransform)target.transform).rect.center));
            var down = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = sourcePoint };
            source.OnPointerDown(down);
            source.OnPointerUp(down);
            bar.GetChild(1).GetComponent<Button>().onClick.Invoke();
            Check(hud.SelectedSlot == 1 && hud.slots[0] == sourceItem, "Short click selects without moving.");
            source.OnPointerDown(down);
            await Task.Delay(560);
            source.OnDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = targetPoint });
            source.OnPointerUp(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = targetPoint });
            Check(hud.slots[0] == targetItem && hud.slots[1] == sourceItem, "Holding 0.5 seconds then dragging swaps both slots.");
            Check(hud.SelectedSlot == 2, "Selected item follows its new slot.");
            Check(PlayerPrefs.GetInt("CompactHud.SlotLayoutVersion") == 1 &&
                PlayerPrefs.GetInt("CompactHud.Slot.0") == (int)targetItem.item &&
                PlayerPrefs.GetInt("CompactHud.Slot.1") == (int)sourceItem.item, "Swap persists hotbar layout.");
            Check(!hud.transform.Find("Dragged Item").gameObject.activeSelf, "Drag icon is removed after drop.");
            return "PASS: short click selects; 0.5-second hold swaps slots, carries selection and saves the new layout.";
        }
        finally
        {
            for (int i = 0; i < original.Length; i++) hud.AssignSlot(i + 1,
                CompactHud.IsHotbarItem(original[i]) ? original[i] : null);
            hud.SelectSlot(originalSelection);
            PlayerPrefs.SetInt("CompactHud.SlotLayoutVersion", oldVersion);
            for (int i = 0; i < oldLayout.Length; i++) PlayerPrefs.SetInt("CompactHud.Slot." + i, oldLayout[i]);
            PlayerPrefs.Save();
        }
    }
}
