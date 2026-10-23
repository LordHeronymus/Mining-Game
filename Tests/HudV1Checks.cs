using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class HudV1Checks
{
    const string Report="Temp/HudV1Checks.txt";
    static readonly List<string> passed=new();
    static void Check(bool condition,string text)
    {if(!condition)throw new Exception(text);passed.Add(text);File.WriteAllLines(Report,passed);}
    static void Tick(CompactHud hud)=>typeof(CompactHud).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,null);
    public static async Task<object> Main()
    {
        passed.Clear();
        Check(Application.isPlaying && GameSaveSystem.TestDirectory!=null && MetaProgression.TestDirectory!=null,"Isolated Play-mode scope");
        var hud=Object.FindFirstObjectByType<CompactHud>();var stats=StatsManager.Instance;
        var top=(RectTransform)hud.transform.Find("Status Strip");var counters=(RectTransform)hud.transform.Find("Counters");
        var bar=(RectTransform)hud.transform.Find("Hotbar");var root=(RectTransform)hud.transform;
        var inventory=InventoryManager.Instance;
        int money=stats.Money,points=stats.Points,selected=hud.SelectedSlot;
        float energy=hud.energy.energy;Vector3 playerPosition=hud.player.transform.position;
        var slots=(ItemSO[])hud.slots.Clone();
        Vector2 anchorMin=root.anchorMin,anchorMax=root.anchorMax,size=root.sizeDelta,position=root.anchoredPosition;
        float timeScale=Time.timeScale;
        try
        {
            Check(top.pivot==new Vector2(0,1)&&counters.pivot==new Vector2(1,1),"Separate left/right anchored status plaques");
            Check(bar.childCount==9&&bar.GetComponent<Image>().sprite.name.Contains("Tray"),"Nine interactive sockets on one shared tool tray");
            Check(bar.rect.width==1120&&bar.rect.height==144,"Reference hotbar dimensions");
            Check(top.GetComponentInChildren<Image>().sprite.name.Contains("Vitals"),"Dedicated V1 panel art");
            for(int i=0;i<9;i++)
            {
                var socket=(RectTransform)bar.GetChild(i);
                Check(socket.GetComponent<Button>()&&socket.GetComponent<GoldButtonFeedback>(),"Socket "+i+" preserves interaction/selection");
                Check(socket.rect.size==new Vector2(110,110),"Socket "+i+" has uniform protected geometry");
                if(i>0)Check(socket.Find("Key").GetComponent<TMP_Text>().text==i.ToString(),"Quickslot "+i+" key label");
            }
            Check(top.Find("Health Icon").GetComponent<Image>().sprite.name.Contains("Heart")&&top.Find("Energy Icon").GetComponent<Image>().sprite.name.Contains("Bolt"),"Reference-style glossy status symbols");
            Check(top.Find("Health Value").gameObject.activeSelf&&top.Find("Energy Value").gameObject.activeSelf,"Health and energy values shown right of the gem rows");
            var health=top.Find("Health Fill").GetComponent<HudGemBar>();var fill=top.Find("Energy Fill").GetComponent<HudGemBar>();
            var mesh=new VertexHelper();typeof(HudGemBar).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null).Invoke(health,new object[]{mesh});
            Check(mesh.currentVertCount>100,"Eight live gemstone segments render real mesh");mesh.Dispose();
            hud.energy.energy=hud.energy.stats.MaxEnergy*.42f;Tick(hud);
            Check(Mathf.Abs(fill.FillAmount-.42f)<.001f,"Partial energy tracks live gameplay value");
            Check(Mathf.Abs(health.FillAmount-stats.Health/stats.MaxHealth)<.001f,"Health tracks live stats");
            stats.AddMoney(123456789-stats.Money);typeof(StatsManager).GetProperty("Points").SetValue(stats,87654321);Tick(hud);Canvas.ForceUpdateCanvases();
            foreach(string name in new[]{"Money","Points"})
            {var text=counters.Find(name).GetComponent<TMP_Text>();text.ForceMeshUpdate();Check(!text.isTextTruncated&&text.renderedWidth<=text.rectTransform.rect.width+1,"Large "+name+" fits protected plaque");}
            int amount=inventory.GetCount(slots[0]);inventory.Add(slots[0],1);
            Check(bar.GetChild(1).Find("Count Badge/Count").GetComponent<TMP_Text>().text==ShopMoneyFormatter.Format(amount+1),"Inventory amount updates immediately");
            inventory.TryRemove(slots[0],1);
            bar.GetChild(3).GetComponent<Button>().onClick.Invoke();
            Check(hud.SelectedSlot==3&&hud.player.GetComponent<PlayerLadder>().BuildMode,"Ladder click enters build mode");
            bar.GetChild(0).GetComponent<Button>().onClick.Invoke();
            Check(hud.SelectedSlot==0&&!hud.player.GetComponent<PlayerLadder>().BuildMode,"Pickaxe click leaves build mode");
            EventSystem.current.SetSelectedGameObject(null);await Task.Delay(450);
            Check(bar.GetComponentsInChildren<GoldButtonFeedback>().Count(g=>g.Chosen)==1&&bar.GetChild(0).GetComponent<GoldButtonFeedback>().Chosen,"Exactly one logical gold selection");
            var feedback=bar.GetChild(1).GetComponent<GoldButtonFeedback>();var socketRect=(RectTransform)feedback.transform;
            Vector3 oldLocal=socketRect.localPosition,oldScale=socketRect.localScale;
            feedback.OnPointerEnter(new PointerEventData(EventSystem.current));Time.timeScale=0;await Task.Delay(150);
            Check(socketRect.localPosition==oldLocal&&socketRect.localScale==oldScale,"Hover remains stationary while paused");
            Check(feedback.transform.Find("Gold Feedback").GetComponent<Image>().color.a>0&&feedback.transform.Find("Gold Fill").GetComponent<Image>().color.a<.01f,"Hover shows border, preserves dark wood");
            feedback.OnPointerExit(new PointerEventData(EventSystem.current));Time.timeScale=timeScale;
            foreach(Vector2 dimensions in new[]{new Vector2(1920,1080),new Vector2(2560,1080),new Vector2(1440,1080)})
            {
                root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);root.sizeDelta=dimensions;root.anchoredPosition=Vector2.zero;
                typeof(CompactHud).GetMethod("Fit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,null);
                Canvas.ForceUpdateCanvases();
                Check(Inside(top,root)&&Inside(counters,root)&&Inside(bar,root),dimensions+": all HUD frames inside viewport");
                Check(top.rect.width*top.localScale.x+counters.rect.width*counters.localScale.x+36<dimensions.x,dimensions+": status plaques leave center free");
                Check(bar.rect.width*bar.localScale.x/dimensions.x<=.78f,dimensions+": hotbar remains bounded");
            }
            root.anchorMin=anchorMin;root.anchorMax=anchorMax;root.sizeDelta=size;root.anchoredPosition=position;Canvas.ForceUpdateCanvases();
            var data=new PointerEventData(EventSystem.current);
            var raycasts=new List<RaycastResult>();var first=(RectTransform)bar.GetChild(0);
            data.position=RectTransformUtility.WorldToScreenPoint(null,first.TransformPoint(first.rect.center));EventSystem.current.RaycastAll(data,raycasts);
            Check(raycasts.Any(r=>r.gameObject==first.gameObject)&&TileMiner.IsPointerOverUi(data.position),"Socket intercepts pointer before mining");
            data.position=RectTransformUtility.WorldToScreenPoint(null,bar.TransformPoint(new Vector3(bar.rect.xMin+16,bar.rect.center.y,0)));
            Check(TileMiner.IsPointerOverUi(data.position),"Tray border and gaps block mining clicks");
            Check(hud.TryGetAssignableSlotAt(RectTransformUtility.WorldToScreenPoint(null,bar.GetChild(3).TransformPoint(((RectTransform)bar.GetChild(3)).rect.center)),out int index)&&index==3,"Drag/drop still resolves the numbered quickslot");
            var source=bar.GetChild(1).GetComponent<HotbarSlotDrag>();
            var target=bar.GetChild(3).GetComponent<HotbarSlotDrag>();
            Vector2 sourcePoint=RectTransformUtility.WorldToScreenPoint(null,source.transform.TransformPoint(((RectTransform)source.transform).rect.center));
            Vector2 targetPoint=RectTransformUtility.WorldToScreenPoint(null,target.transform.TransformPoint(((RectTransform)target.transform).rect.center));
            hud.SelectSlot(1);
            source.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=sourcePoint});
            await Task.Delay(650);
            source.OnDrag(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=targetPoint});
            source.OnPointerUp(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=targetPoint});
            Check(hud.slots[2]==slots[0]&&hud.slots[0]==slots[2]&&hud.SelectedSlot==3,"Held pointer drag swaps items and carries logical selection");
            Check(!hud.transform.Find("Dragged Item").gameObject.activeSelf,"Drag graphic disappears after drop");
            hud.RestoreRunSlots(slots.Select(s=>s?(int)s.item:-1).ToArray(),0);
            var invPanel=Object.FindFirstObjectByType<InventoryUI>();invPanel.ShowPanel();await Task.Delay(450);
            Check(bar.parent==hud.GetComponentInParent<Canvas>().rootCanvas.transform,"Inventory keeps enlarged tray visible above modal");
            Check(!hud.SelectSlot(3),"Modal blocks gameplay slot input");
            invPanel.StopAllCoroutines();var hide=invPanel.FadePanel(false);while(hide.MoveNext()){}await Task.Delay(100);
            Check(bar.parent==hud.transform,"Tray returns to gameplay parent after inventory closes");
            float surface=hud.map.Terrain.CellToWorld(Vector3Int.up).y;hud.player.transform.position=new Vector3(playerPosition.x,surface-124.2f,playerPosition.z);Tick(hud);
            Check(hud.DepthMeters==124&&counters.Find("Depth").GetComponent<TMP_Text>().text=="124 m","Depth derives from actual player position");
            hud.player.transform.position=playerPosition;
            var camera=Camera.main;
            Check(camera.GetComponent<CameraFollow>().enabled&&camera.GetComponent<CameraFollow>().target==hud.player.transform&&camera.GetComponent<CameraWorldBorderClamp>().enabled,"Fresh gameplay camera follow/clamp remain active");
            File.AppendAllText(Report,"\nALL PASSED: "+passed.Count);return "PASS "+passed.Count+" HUD V1 runtime checks";
        }
        catch(Exception error){File.AppendAllText(Report,"\nFAIL: "+error);throw;}
        finally
        {
            root.anchorMin=anchorMin;root.anchorMax=anchorMax;root.sizeDelta=size;root.anchoredPosition=position;
            Time.timeScale=timeScale;hud.player.transform.position=playerPosition;hud.energy.energy=energy;
            stats.AddMoney(money-stats.Money);typeof(StatsManager).GetProperty("Points").SetValue(stats,points);
            hud.RestoreRunSlots(slots.Select(s=>s?(int)s.item:-1).ToArray(),selected);Tick(hud);Canvas.ForceUpdateCanvases();
        }
    }
    static bool Inside(RectTransform frame,RectTransform root)
    {
        var corners=new Vector3[4];frame.GetWorldCorners(corners);var rect=root.rect;
        return corners.All(c=>{var p=root.InverseTransformPoint(c);return p.x>=rect.xMin-.1f&&p.x<=rect.xMax+.1f&&p.y>=rect.yMin-.1f&&p.y<=rect.yMax+.1f;});
    }
}
