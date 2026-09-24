using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public static class WorkbenchSearchInputChecks
{
    public static async Task<object> Main()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>();
        panel.ShowPanel(true);
        var go = new GameObject("Workbench Input Probe");
        var probe = go.AddComponent<Probe>(); probe.panel = panel;
        try { return await probe.Run(); }
        finally { panel.SetSearch(""); UnityEngine.Object.Destroy(go); }
    }

    sealed class Probe : MonoBehaviour
    {
        public WorkbenchPanel panel;
        readonly TaskCompletionSource<object> done = new();
        public Task<object> Run() { StartCoroutine(Test()); return done.Task; }
        IEnumerator Test()
        {
            var input = panel.transform.Find("Layout/Search").GetComponent<TMP_InputField>();
            var pointer = new PointerEventData(EventSystem.current) {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null,
                    input.transform.TransformPoint(((RectTransform)input.transform).rect.center))
            };
            ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return null; yield return null;
            if (!input.isFocused) { done.TrySetException(new Exception("Search did not acquire keyboard focus.")); yield break; }
            input.ProcessEvent(Event.KeyboardEvent("b"));
            input.ProcessEvent(Event.KeyboardEvent("r"));
            yield return null;
            if (!panel.IsOpen || input.text != "br" || panel.VisibleRecipeCount != 1)
            { done.TrySetException(new Exception("Typed search did not filter to bridge.")); yield break; }
            input.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null);
            done.TrySetResult("PASS: pointer focus, keyboard text input and live search across running frames.");
        }
    }
}
