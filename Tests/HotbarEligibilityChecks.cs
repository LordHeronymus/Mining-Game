using System;
using System.Linq;
using UnityEngine;

public static class HotbarEligibilityChecks
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Use Play Mode");
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        if (!hud || !catalog) throw new Exception("HUD or item catalog missing");

        var wood = catalog.items.First(item => item && item.item == Item.Wood);
        var fiber = catalog.items.First(item => item && item.item == Item.PlantFiber);
        var copper = catalog.items.First(item => item && item.item == Item.Copper);
        var ladder = catalog.items.First(item => item && item.item == Item.Ladder);
        if (CompactHud.IsHotbarItem(wood) || CompactHud.IsHotbarItem(fiber) || CompactHud.IsHotbarItem(copper))
            throw new Exception("Resources must not be eligible for the hotbar");
        if (!CompactHud.IsHotbarItem(ladder)) throw new Exception("Placeable tools must stay eligible");

        try { hud.AssignSlot(1, wood); }
        catch (ArgumentException) { return "PASS: resources are rejected and placeable tools remain eligible for the hotbar."; }
        throw new Exception("Wood assignment must be rejected");
    }
}
