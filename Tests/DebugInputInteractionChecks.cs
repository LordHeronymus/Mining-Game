using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public static class DebugInputInteractionChecks
{
    public static async Task<object> Main()
    {
        float original = GameplayTestSettings.ConfiguredDiggingMultiplier;
        var panel = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if (!GameplayDebugPanel.IsOpen) panel.Toggle();
        panel.GetComponent<GameplayDebugWindow>().SwitchTab(true);
        var input = panel.transform.Find("Card/WindowViewport/WindowContent/TestMultiplier").GetComponent<TMP_InputField>();
        var probe = new GameObject("Debug Input Probe").AddComponent<Probe>();
        probe.input = input;
        probe.screenPosition = RectTransformUtility.WorldToScreenPoint(null,
            input.targetGraphic.rectTransform.TransformPoint(input.targetGraphic.rectTransform.rect.center));
        await probe.completion.Task;
        var result = new { probe.frames, probe.screenPosition, probe.before, probe.afterClick, probe.afterType,
            saved = GameplayTestSettings.ConfiguredDiggingMultiplier, unsaved = GameplayTestSettings.HasUnsavedChanges,
            selectionLog = probe.selectionLog.ToArray() };
        UnityEngine.Object.Destroy(probe.gameObject);
        bool passed = probe.afterClick && probe.afterType == "2" &&
            GameplayTestSettings.ConfiguredDiggingMultiplier == 2f && !GameplayTestSettings.HasUnsavedChanges;
        GameplayTestSettings.SetDiggingMultiplier(original);
        GameplayTestSettings.Save(out _);
        if (!passed)
            throw new Exception("Input interaction failed: " + string.Join(" | ", probe.selectionLog));
        return result;
    }

    public static object Persistence()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        panel.Close();
        panel.Toggle();
        panel.GetComponent<GameplayDebugWindow>().SwitchTab(true);
        string reopened = panel.transform.Find("Card/WindowViewport/WindowContent/TestMultiplier").GetComponent<TMP_InputField>().text;
        string expected = GameplayTestSettings.ConfiguredDiggingMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (reopened != expected || GameplayTestSettings.HasUnsavedChanges)
            throw new Exception("Saved factor was not restored.");
        return new { reopened, saved = GameplayTestSettings.ConfiguredDiggingMultiplier };
    }

    sealed class Probe : MonoBehaviour
    {
        public TMP_InputField input;
        public Vector2 screenPosition;
        public readonly TaskCompletionSource<bool> completion = new();
        public readonly List<string> selectionLog = new();
        public int frames;
        public string before, afterType;
        public bool afterClick;
        int stage;
        string previousSelection;

        void Update()
        {
            frames++;
            string selected = EventSystem.current.currentSelectedGameObject
                ? EventSystem.current.currentSelectedGameObject.name : "none";
            if (selected != previousSelection)
            {
                selectionLog.Add($"f{frames}:{selected}, focused={input.isFocused}");
                previousSelection = selected;
            }
            if (stage == 0 && frames > 3)
            {
                before = input.text;
                var pointer = new PointerEventData(EventSystem.current) {
                    button = PointerEventData.InputButton.Left,
                    position = screenPosition
                };
                ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                stage++;
            }
            else if (stage == 1 && frames > 8)
            {
                afterClick = input.isFocused && EventSystem.current.currentSelectedGameObject == input.gameObject;
                input.text = "2";
                input.onEndEdit.Invoke(input.text);
                stage++;
            }
            else if (stage == 2 && frames > 12)
            {
                afterType = input.text;
                completion.TrySetResult(true);
                stage++;
            }
        }
    }
}
