using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class ShopSellStylePreview
{
    public static object Main()
    {
        new GameObject("Sell shop preview").AddComponent<ShopSellStyleCapture>();
        return "Capturing V2 sell screen with actual ore sprites, then restoring inventory and money.";
    }
}
public sealed class ShopSellStyleCapture : MonoBehaviour
{
    IEnumerator Start()
    {
        while(LoadingProgress.Active) yield return null;
        var shop=Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        var page=Object.FindFirstObjectByType<SellPage>(FindObjectsInactive.Include);
        var inventory=InventoryManager.Instance; var stats=StatsManager.Instance;
        var snapshot=inventory.GetSnapshot().ToDictionary(x=>x.Key,x=>x.Value);
        int money=stats.Money;
        var noWeight=typeof(GameplayTestSettings).GetField("noWeight",BindingFlags.NonPublic|BindingFlags.Static);
        bool previousWeight=(bool)noWeight.GetValue(null);
        var feed=Object.FindFirstObjectByType<ItemFeed>(); bool feedVisible=feed && feed.gameObject.activeSelf;
        try
        {
            if(feed) feed.gameObject.SetActive(false);
            noWeight.SetValue(null,true); inventory.ResetAll();
            var items=Resources.Load<ItemCatalog>("ItemCatalog").items;
            Item[] ores={Item.Iron,Item.Coal,Item.Copper,Item.Gold,Item.Titanium,Item.Tungsten,Item.Ruby,Item.Emerald};
            int[] counts={128,64,40,18,12,8,4,3};
            for(int i=0;i<ores.Length;i++) inventory.AddStartingItem(items.First(x=>x && x.item==ores[i]),counts[i]);
            stats.AddMoney(100-stats.Money);
            shop.ShowPanel(true); shop.ShowSellPage(); page.SetSearch("");
            page.SelectItem(items.First(x=>x && x.item==Item.Iron));
            yield return new WaitForSecondsRealtime(.8f);
            Canvas.ForceUpdateCanvases(); yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Assets/Design/ShopSell-v2-implemented.png",texture.EncodeToPNG()); Destroy(texture);
        }
        finally
        {
            shop.ShowPanel(false); inventory.ResetAll(); foreach(var entry in snapshot) inventory.Add(entry.Key,entry.Value);
            stats.AddMoney(money-stats.Money); noWeight.SetValue(null,previousWeight);
            if(feed) feed.gameObject.SetActive(feedVisible); Destroy(gameObject);
        }
    }
}
