using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ShopSellStyleChecks
{
    public static object Main()
    {
        new GameObject("Sell style checks").AddComponent<ShopSellStyleProbe>();
        return "Checking shared chrome, wide cards, search, live sale counts, revenue and tab preservation.";
    }
}
public sealed class ShopSellStyleProbe : MonoBehaviour
{
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    IEnumerator Start()
    {
        while(LoadingProgress.Active || GameObject.Find("Sell shop preview")) yield return null;
        var shop=Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        var sell=Object.FindFirstObjectByType<SellPage>(FindObjectsInactive.Include);
        var inventory=InventoryManager.Instance; var stats=StatsManager.Instance;
        var snapshot=inventory.GetSnapshot().ToDictionary(x=>x.Key,x=>x.Value); int money=stats.Money;
        var noWeight=typeof(GameplayTestSettings).GetField("noWeight",BindingFlags.NonPublic|BindingFlags.Static);
        var infinite=typeof(GameplayTestSettings).GetField("infiniteMoney",BindingFlags.NonPublic|BindingFlags.Static);
        bool previousWeight=(bool)noWeight.GetValue(null),previousInfinite=(bool)infinite.GetValue(null);
        string result="FAILED: incomplete";
        try
        {
            noWeight.SetValue(null,true); infinite.SetValue(null,false); inventory.ResetAll();
            var catalog=Resources.Load<ItemCatalog>("ItemCatalog").items;
            var ores=catalog.Where(x=>x && x.category==ItemCategory.Ore && x.worth>0).Take(10).ToArray();
            Check(ores.Length==10,"Need ten actual ores for overflow test.");
            foreach(var ore in ores) inventory.AddStartingItem(ore,25);
            var iron=catalog.First(x=>x && x.item==Item.Iron);
            var wood=catalog.First(x=>x && x.item==Item.Wood);
            inventory.AddStartingItem(wood,3);
            shop.ShowPanel(true); shop.ShowSellPage(); sell.SetSearch("");
            yield return new WaitForSecondsRealtime(.4f);
            var root=sell.transform.Find("Ore Shop"); var content=(RectTransform)root.Find("Ores/Content");
            var cards=content.GetComponentsInChildren<ShopSlot>().OrderBy(x=>x.Item.displayName).ToArray();
            Check(cards.Length==10 && cards.All(x=>x.Item.category==ItemCategory.Ore && x.Count>0),"Only owned ores appear.");
            Check(((RectTransform)cards[1].transform).anchoredPosition.x==642 && ((RectTransform)cards[2].transform).anchoredPosition.y==-256,"Two-column wide-card layout.");
            Check(((RectTransform)cards[7].transform).anchoredPosition.y-232>=-1002,"Eight whole cards fit.");
            var scroll=content.parent.GetComponent<ScrollRect>(); scroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases();
            Check(content.rect.height>1002 && content.anchoredPosition.y>0,"Later ores are scrollable.");
            var names=new[]{"ShopTitle","Tabs","SellTabFrame","BuyTabFrame","CurrentMoneyFrame"};
            var chrome=names.Select(x=>(RectTransform)shop.transform.Find(x)).Select(x=>(x.anchoredPosition,x.sizeDelta)).ToArray();
            var background=shop.GetComponent<Image>().sprite;
            shop.ShowBuyPage(); yield return null;
            Check(shop.GetComponent<Image>().sprite==background,"Identical outer frame across tabs.");
            for(int i=0;i<names.Length;i++)
            {
                var rect=(RectTransform)shop.transform.Find(names[i]);
                Check(rect.anchoredPosition==chrome[i].anchoredPosition && rect.sizeDelta==chrome[i].sizeDelta,"Shared chrome positions: "+names[i]);
            }
            var buy=Object.FindFirstObjectByType<BuyPage>(); buy.SetSearch("Eisenspitzhacke");
            Check(buy.transform.Find("Blueprint Shop/Blueprints/Content").Cast<Transform>().Count(x=>x.gameObject.activeSelf)==1,"Buy search still works.");
            buy.SetSearch(""); shop.ShowSellPage(); yield return null;
            Check(root.Find("Search").GetComponent<TMP_InputField>().GetComponentInChildren<TextMeshProUGUI>().font==buy.GetComponentInChildren<TextMeshProUGUI>().font,"Shared serif font.");
            sell.SetSearch("  eIsEn  ");
            Check(sell.SelectedItem==iron && content.GetComponentsInChildren<ShopSlot>().Length==1,"Search ignores case/whitespace.");
            var ironCard=content.GetComponentsInChildren<ShopSlot>().Single(); int count=inventory.GetCount(iron);
            ironCard.GetComponent<Button>().onClick.Invoke();
            Check(inventory.GetCount(iron)==count && sell.SelectedItem==iron,"Plain card click only selects.");
            var details=root.Find("Ore Details"); var action=details.Find("Sell Selected").GetComponent<Button>();
            Check(action.GetComponentInChildren<TextMeshProUGUI>().text=="1 "+iron.displayName+" verkaufen","Default sells one.");
            Check(details.Find("Proceeds").GetComponentsInChildren<TextMeshProUGUI>()[1].text==ShopMoneyFormatter.Format(iron.worth),"Revenue follows one-item action.");
            int beforeMoney=stats.Money; action.onClick.Invoke();
            Check(inventory.GetCount(iron)==count-1 && stats.Money==beforeMoney+iron.worth,"One-item button sells exact amount/value.");
            typeof(SellPage).GetMethod("Sell",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(sell,new object[]{10});
            Check(inventory.GetCount(iron)==count-11 && stats.Money==beforeMoney+iron.worth*11,"Ten-item transaction preserved.");
            typeof(SellPage).GetMethod("SellMax",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(sell,null);
            Check(inventory.GetCount(iron)==0 && stats.Money==beforeMoney+iron.worth*count,"All of selected ore sells exact remainder.");
            Check(!details.gameObject.activeSelf,"Exhausted filtered stock clears actionable details.");
            sell.SetSearch(""); Check(sell.SelectedItem && sell.SelectedItem!=iron,"Selection advances to another owned ore.");
            details.Find("Sell All").GetComponent<Button>().onClick.Invoke();
            Check(ores.All(x=>inventory.GetCount(x)==0),"Sell-all action preserved.");
            sell.SetSearch("no-match"); Check(!details.gameObject.activeSelf,"Empty search stays safe.");
            result="PASS: shared panel/tab bounds, 8 visible wide cards, overflow, owned ores, search/empty state, selection-only click, live revenue, one/ten/all-selected/all sales and buy search.";
        }
        finally
        {
            sell.SetSearch(""); shop.ShowPanel(false); inventory.ResetAll();
            foreach(var entry in snapshot) inventory.AddStartingItem(entry.Key,entry.Value);
            stats.AddMoney(money-stats.Money); noWeight.SetValue(null,previousWeight); infinite.SetValue(null,previousInfinite);
            File.WriteAllText("Temp/ShopSellStyleChecks-result.txt",result); Destroy(gameObject);
        }
    }
}
