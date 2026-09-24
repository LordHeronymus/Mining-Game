using System;
using UnityEngine;
using UnityEngine.UI;

public static class DebugWindowChecks
{
    static void Check(bool result,string message) { if(!result) throw new Exception(message); }
    public static object Main()
    {
        var panel=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if(!GameplayDebugPanel.IsOpen) panel.Toggle();
        var window=panel.GetComponent<GameplayDebugWindow>();
        var card=(RectTransform)panel.transform.Find("Card");
        var root=(RectTransform)panel.transform;
        Canvas.ForceUpdateCanvases();
        Check(card.anchorMin==Vector2.zero && card.anchorMax==Vector2.one,"Debug panel is not fullscreen.");
        Check(card.rect.size==root.rect.size,"Debug card does not fill the canvas.");
        Check(panel.GetComponent<Image>().color.a>.5f,"Fullscreen background is not darkened.");
        foreach(var name in new[]{"GameplayTab","TestsTab","ItemsTab","WorldTab","AudioTab","IconsTab"})
            Check(card.Find(name),"Missing navigation tab: "+name);
        var content=card.Find("WindowViewport/WindowContent");
        window.SwitchTab(true);
        foreach(var name in new[]{"TestActive","TestMultiplier","MovementMultiplier","GodMode","NoEnergy","FlyMode","NoClip","GlobalLighting","DayNightRow"})
            Check(content.Find(name) && content.Find(name).gameObject.activeInHierarchy,"Missing test control: "+name);
        Check(!content.Find("GiftCard0").gameObject.activeInHierarchy,"Items are visible on test page.");
        window.SwitchTab("Items");
        Check(content.Find("GiftCard0").gameObject.activeInHierarchy,"Items page did not open.");
        window.SwitchTab("World");
        Check(content.Find("LightingSection").gameObject.activeInHierarchy,"World page did not open.");
        window.SwitchTab("Audio");
        Check(content.Find("AudioSection").gameObject.activeInHierarchy,"Audio page did not open.");
        window.SwitchTab("Icons");
        Check(content.Find("IconRecipeDropdown").gameObject.activeInHierarchy,"Icon page did not open.");
        window.SwitchTab(true);
        return new {passed=true,fullscreen=true,tabs=6,testControls=true};
    }
}
