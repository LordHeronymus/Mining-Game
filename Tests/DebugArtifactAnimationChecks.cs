using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class DebugArtifactAnimationChecks
{
    public static object Main()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if (!GameplayDebugPanel.IsOpen) panel.Toggle();
        var window = panel.GetComponent<GameplayDebugWindow>();
        window.SwitchTab(true);
        var content = panel.transform.Find("Card/WindowViewport/WindowContent");
        var scroll = panel.transform.Find("Card/WindowViewport").GetComponent<ScrollRect>();
        var settings = UnityEngine.Object.FindFirstObjectByType<MapGenerator>().artifactSettings;
        var artifacts = settings.Select(s => s.tile).Where(t => t && t.sprite).Distinct().ToArray();
        if (artifacts.Length != 10) throw new Exception("Expected 10 configured artifacts.");
        if (!content.Find("ArtifactAnimationSection").gameObject.activeInHierarchy)
            throw new Exception("Artifact animation section missing in test settings.");
        if (!content.Find("ArtifactAnimationYOffsetHeader").gameObject.activeInHierarchy)
            throw new Exception("Y offset header missing in test settings.");
        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < artifacts.Length; i++)
        {
            var row = content.Find("ArtifactAnimation_" + i);
            if (!row || !row.gameObject.activeInHierarchy) throw new Exception("Button missing: " + artifacts[i].displayName);
            var button = row.GetComponent<Button>();
            if (!button || !button.interactable) throw new Exception("Button disabled: " + artifacts[i].displayName);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label.text != artifacts[i].displayName || label.isTextOverflowing)
                throw new Exception("Button label clipped: " + artifacts[i].displayName);
            var offset = content.Find("ArtifactAnimationYOffsetInput_" + i)?.GetComponent<TMP_InputField>();
            if (!offset || !offset.gameObject.activeInHierarchy)
                throw new Exception("Y offset input missing: " + artifacts[i].displayName);
        }
        var bottomRow = (RectTransform)content.Find("ArtifactAnimation_9");
        if (scroll.content.rect.height < -bottomRow.anchoredPosition.y + bottomRow.rect.height)
            throw new Exception("Last button is outside scroll content.");

        var stats = StatsManager.Instance;
        int points = stats.Points, artifactPoints = stats.ArtifactPoints, money = stats.Money;
        int collected = ((System.Collections.Generic.HashSet<ArtifactTile>)typeof(StatsManager)
            .GetField("collectedArtifactTypes", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(stats)).Count;
        content.Find("ArtifactAnimation_2").GetComponent<Button>().onClick.Invoke();
        var preview = ArtifactDiscoveryView.Instance;
        if (!preview || !preview.IsShowing || preview.CurrentArtifact != artifacts[2])
            throw new Exception("Jademaske button did not start the correct preview.");
        if (!GameplayDebugPanel.IsOpen || Time.timeScale != 0f)
            throw new Exception("Preview did not overlay the open debug panel.");
        int collectedAfter = ((System.Collections.Generic.HashSet<ArtifactTile>)typeof(StatsManager)
            .GetField("collectedArtifactTypes", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(stats)).Count;
        if (stats.Points != points || stats.ArtifactPoints != artifactPoints ||
            stats.Money != money || collectedAfter != collected)
            throw new Exception("Preview changed game progress.");
        UnityEngine.Object.DestroyImmediate(preview.gameObject);
        if (Time.timeScale != 1f || !GameplayDebugPanel.IsOpen)
            throw new Exception("Preview did not restore the debug panel.");
        return new { passed = true, buttons = artifacts.Length, preview = artifacts[2].displayName,
            progressUnchanged = true, debugPanelRemainedOpen = true };
    }
}
