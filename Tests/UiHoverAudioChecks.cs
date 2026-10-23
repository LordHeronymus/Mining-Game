using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Requires Play Mode. Builds a temporary overlay canvas and removes it again.
public static class UiHoverAudioChecks
{
    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Requires Play Mode");
        var clip = Resources.Load<AudioClip>("Audio/UiHoverClick");
        if (!clip) throw new Exception("Clip Resources/Audio/UiHoverClick missing");
        if (clip.length > .2f) throw new Exception("Hover click too long: " + clip.length);
        var tune = GpsAudio.Tuning(clip);
        if (tune == null) throw new Exception("GPS audio entry for UiHoverClick missing");
        if (Mathf.Abs(tune.volume - .2f) > .001f) throw new Exception("GPS volume " + tune.volume);
        if (tune.category != "Shop und UI" || tune.displayName != "Knopf-Hover-Klick") throw new Exception("GPS entry metadata: " + tune.category + " / " + tune.displayName);
        if (!GameObject.Find("UiHoverAudio")) throw new Exception("UiHoverAudio watcher missing");
        HomeUi.EnsureEventSystem();

        var root = new GameObject("UiHoverAudioChecks", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            var first = MakeButton(root.transform, "First", new Vector2(-150, 0));
            var second = MakeButton(root.transform, "Second", new Vector2(150, 0));
            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderObject.transform.SetParent(root.transform, false);
            var slider = sliderObject.GetComponent<Slider>();
            Canvas.ForceUpdateCanvases();

            // Pointer raycasts only hit the temporary canvas after it has been drawn once, so this script checks the
            // hover rule directly; the raycast path was verified manually (label hit resolves to its button).
            UiHoverAudio.Hover(null);
            int start = UiHoverAudio.PlayCount;
            UiHoverAudio.Hover(first);
            if (UiHoverAudio.PlayCount != start + 1) throw new Exception("Entering a button did not click");
            UiHoverAudio.Hover(first); UiHoverAudio.Hover(first);
            if (UiHoverAudio.PlayCount != start + 1) throw new Exception("Staying on a button clicked again");
            UiHoverAudio.Hover(second);
            if (UiHoverAudio.PlayCount != start + 2) throw new Exception("Moving to the next button did not click");
            UiHoverAudio.Hover(null); UiHoverAudio.Hover(second);
            if (UiHoverAudio.PlayCount != start + 3) throw new Exception("Re-entering a button did not click");
            UiHoverAudio.Hover(null);
            first.interactable = false; UiHoverAudio.Hover(first);
            if (UiHoverAudio.PlayCount != start + 3) throw new Exception("Disabled button clicked");
            UiHoverAudio.Hover(slider);
            if (UiHoverAudio.PlayCount != start + 3) throw new Exception("Slider clicked");
            return "PASS: clip " + clip.length.ToString("0.000") + " s, GPS volume " + tune.volume.ToString("0.###") + "; enter clicks once, staying silent, next/re-enter click, disabled and slider silent";
        }
        finally
        {
            UiHoverAudio.Hover(null);
            UnityEngine.Object.Destroy(root);
        }
    }

    static Button MakeButton(Transform parent, string name, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(200, 80); rect.anchoredPosition = position;
        var label = new GameObject("Label", typeof(RectTransform), typeof(Image));
        label.transform.SetParent(go.transform, false);
        ((RectTransform)label.transform).sizeDelta = new Vector2(120, 40);
        return go.GetComponent<Button>();
    }
}
