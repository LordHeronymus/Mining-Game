using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class DebugWindowChecks
{
    static void Check(bool result,string message) { if(!result) throw new Exception(message); }
    public static object Main()
    {
        var panel=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if(!GameplayDebugPanel.IsOpen) panel.Toggle();
        Check(panel.GetComponents<GameplayDebugWindow>().Length==1,"Debug panel initialized more than one runtime window.");
        var window=panel.GetComponent<GameplayDebugWindow>();
        var card=(RectTransform)panel.transform.Find("Card");
        var root=(RectTransform)panel.transform;
        Canvas.ForceUpdateCanvases();
        Check(card.anchorMin==Vector2.zero && card.anchorMax==Vector2.one,"Debug panel is not fullscreen.");
        Check(card.rect.size==root.rect.size,"Debug card does not fill the canvas.");
        float expectedBackdropAlpha=PlayerPrefs.GetFloat("GameplayDebugPanel.BackdropAlpha",.55f);
        Check(Mathf.Approximately(panel.GetComponent<Image>().color.a,expectedBackdropAlpha),"Fullscreen background alpha does not match its saved setting.");
        foreach(var name in new[]{"GameplayTab","TestsTab","MiscTab","ItemsTab","PowerupsTab","WorldTab","AudioTab","IconsTab","RecipesTab"})
            Check(card.Find(name),"Missing navigation tab: "+name);
        var content=card.Find("WindowViewport/WindowContent");
        window.SwitchTab("Gameplay");
        foreach(var name in new[]{"StartSection","StartingMoneyInput","StartingResourceAdd"})
            Check(content.Find(name) && content.Find(name).gameObject.activeInHierarchy,"Starting resources are missing from Gameplay: "+name);
        window.SwitchTab(true);
        var altarTeleport=content.Find("TeleportToUltroniumAltar") as RectTransform;
        var spawnTeleport=content.Find("TeleportToSpawn") as RectTransform;
        Check(altarTeleport && spawnTeleport && altarTeleport.gameObject.activeInHierarchy && spawnTeleport.gameObject.activeInHierarchy,
            "Test teleport buttons are missing.");
        Check(Mathf.Approximately(altarTeleport.anchoredPosition.y,spawnTeleport.anchoredPosition.y),
            "Altar and spawn teleport buttons are not on the same row.");
        var drainEnergy=content.Find("EnergyDrain10") as RectTransform;
        var maxEnergy=content.Find("EnergyMax") as RectTransform;
        Check(drainEnergy && maxEnergy && drainEnergy.gameObject.activeInHierarchy && maxEnergy.gameObject.activeInHierarchy,
            "Energy test buttons are missing.");
        Check(Mathf.Approximately(drainEnergy.anchoredPosition.y,maxEnergy.anchoredPosition.y),
            "Energy test buttons are not on the same row.");
        foreach(var name in new[]{"TestActive","TestMultiplier","MovementMultiplier","GodMode","NoEnergy","EnergyDrain10","FlyMode","NoClip","NoWeight","GlobalLighting"})
            Check(content.Find(name) && content.Find(name).gameObject.activeInHierarchy,"Missing test control: "+name);
        foreach(var name in new[]{"TestLabel/DiggingMultiplierToggle","MovementLabel/MovementMultiplierToggle"})
            Check(content.Find(name) && content.Find(name).gameObject.activeInHierarchy,"Missing inline factor toggle: "+name);
        Check(!content.Find("MiningHitOffsetInput").gameObject.activeInHierarchy,"Hit offset is still on the test page.");
        window.SwitchTab("Misc");
        Check(content.Find("MiningHitOffsetInput") && content.Find("MiningHitOffsetInput").gameObject.activeInHierarchy,"Hit offset is missing from Misc.");
        Check(!content.Find("DayNightRow").gameObject.activeInHierarchy,"Day/Night control is still on the test page.");
        Check(!content.Find("GiftCard0").gameObject.activeInHierarchy,"Items are visible on test page.");
        window.SwitchTab("Gameplay");
        Check(content.Find("DayNightRow").gameObject.activeInHierarchy,"Day/Night control is missing from Gameplay.");
        window.SwitchTab("Items");
        Check(content.Find("GiftCard0").gameObject.activeInHierarchy,"Items page did not open.");
        window.SwitchTab("Powerups");
        Check(content.Cast<Transform>().Any(item => item.name.StartsWith("PowerupSection") && item.gameObject.activeInHierarchy),"Powerups page is empty.");
        Check(content.Cast<Transform>().Any(item => item.name.StartsWith("PowerupCard") && item.gameObject.activeInHierarchy),"Powerups page has no item cards.");
        window.SwitchTab("World");
        Check(content.Find("LightingSection").gameObject.activeInHierarchy,"World page did not open.");
        window.SwitchTab("Audio");
        var audio=content.Find("AudioBrowserRoot");
        Check(audio && audio.gameObject.activeInHierarchy,"Audio page did not open.");
        var search=audio.Find("AudioBrowserSearch")?.GetComponent<TMP_InputField>();
        Check(search && string.IsNullOrEmpty(search.text),"Audio search is not empty at startup.");
        Check(audio.GetComponentsInChildren<Button>(true).Length>20,"Audio clip controls are missing.");
        window.SwitchTab("Icons");
        Check(content.Find("IconRecipeDropdown").gameObject.activeInHierarchy,"Icon page did not open.");
        window.SwitchTab(true);
        return new {passed=true,fullscreen=true,tabs=9,testControls=true,miscHitOffset=true,startingResourcesInGameplay=true,powerups=true,singleRuntimeWindow=true};
    }
}
