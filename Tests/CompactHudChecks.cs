using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public static class CompactHudChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static string Main()
    {
        Check(Application.isPlaying,"Use Play Mode");
        var hud=UnityEngine.Object.FindFirstObjectByType<CompactHud>();var inv=InventoryManager.Instance;var stats=StatsManager.Instance;
        int[] amounts={24,8,5,3,18,245};
        for(int i=0;i<6;i++)inv.Add(hud.slots[i],amounts[i]);
        stats.AddMoney(12450-stats.Money);
        typeof(StatsManager).GetProperty("Points").SetValue(stats,86500);
        hud.energy.consumptionMultiplier=0;hud.energy.energy=hud.energy.stats.MaxEnergy*.72f;
        var update=typeof(CompactHud).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);update.Invoke(hud,null);
        var top=hud.transform.Find("Status Strip");var bar=hud.transform.Find("Hotbar");
        Check(bar.childCount==9,"Pickaxe plus eight item slots required");
        Check(bar.GetChild(0).Find("Pickaxe").GetComponent<Image>().sprite==hud.pickaxeSprite,"Equipped pickaxe icon");
        Check(bar.GetChild(0).Find("Key").GetComponent<TMP_Text>().text=="1","Pickaxe key");
        Check(((RectTransform)bar.GetChild(1)).anchoredPosition.x-((RectTransform)bar.GetChild(0)).anchoredPosition.x>72,"Pickaxe slot gap");
        Check(top.Find("Money").GetComponent<TMP_Text>().text=="12.450","Live money formatting");
        Check(top.Find("Points").GetComponent<TMP_Text>().text=="Punkte  86.500","Live score");
        Check(Mathf.Abs(top.Find("Energy Fill").GetComponent<HudGemBar>().FillAmount-.72f)<.001f,"Energy fill");
        Check(top.Find("Health Value").GetComponent<TMP_Text>().text=="100 / 100","Health placeholder");
        Check(bar.GetChild(1).Find("Count Badge/Count").GetComponent<TMP_Text>().text=="24","Inventory count");
        inv.TryRemove(hud.slots[0],4);
        Check(bar.GetChild(1).Find("Count Badge/Count").GetComponent<TMP_Text>().text=="20","Live inventory update");
        inv.Add(hud.slots[0],4);
        int ladderSlot=Array.FindIndex(hud.slots,item=>item&&item.item==Item.Ladder);
        Check(ladderSlot>=0,"Ladder must be assigned to the hotbar");
        bar.GetChild(ladderSlot+1).GetComponent<Button>().onClick.Invoke();
        Check(hud.SelectedSlot==ladderSlot+1&&hud.player.GetComponent<PlayerLadder>().BuildMode,"Ladder hotbar selection");
        bar.GetChild(0).GetComponent<Button>().onClick.Invoke();
        Check(hud.SelectedSlot==0&&hud.SelectedItem==null&&!hud.player.GetComponent<PlayerLadder>().BuildMode,"Pickaxe selection leaves ladder mode");
        var inventory=UnityEngine.Object.FindFirstObjectByType<InventoryUI>();inventory.ShowPanel();
        Check(!hud.SelectSlot(ladderSlot+1),"Hotbar must respect modal input blocker");
        inventory.StopAllCoroutines();var hide=inventory.FadePanel(false);while(hide.MoveNext()){}
        var oldPosition=hud.player.transform.position;
        try
        {
            float surface=hud.map.Terrain.CellToWorld(Vector3Int.up).y;
            hud.player.transform.position=new Vector3(oldPosition.x,surface-124.2f,oldPosition.z);update.Invoke(hud,null);
            Check(hud.DepthMeters==124,"Depth must follow real player position");
        }
        finally{hud.player.transform.position=oldPosition;update.Invoke(hud,null);}
        Canvas.ForceUpdateCanvases();
        var hits=new List<RaycastResult>();var data=new PointerEventData(EventSystem.current);
        data.position=RectTransformUtility.WorldToScreenPoint(null,bar.GetChild(0).TransformPoint(new Vector3(33,-33,0)));
        EventSystem.current.RaycastAll(data,hits);Check(hits.Any(h=>h.gameObject==bar.GetChild(0).gameObject),"Hotbar must intercept clicks");
        Check(TileMiner.IsPointerOverUi(data.position),"Hotbar click must block mining in the same frame");
        var root=(RectTransform)hud.transform;float width=root.rect.width,height=root.rect.height;
        var topRect=(RectTransform)top;var bottomRect=(RectTransform)bar;
        Check(topRect.rect.height*top.localScale.y/height<.06f,"Status strip too tall");
        Check(bottomRect.rect.width*bar.localScale.x/width<.36f,"Hotbar too wide");
        hud.SelectSlot(0);
        return "Passed: HUD values, separate pickaxe slot 1, item slots 2–9, counts, click selection, ladder mode, modal guard, raycast and compact dimensions.";
    }
}
