using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class WorkbenchStylePreview
{
    public static object Main()
    {
        new GameObject("Workbench style preview").AddComponent<WorkbenchStyleCapture>();
        return "Capturing workshop with four real ingredients; restoring preview inventory and unlocks afterwards.";
    }
}
public sealed class WorkbenchStyleCapture : MonoBehaviour
{
    IEnumerator Start()
    {
        while (LoadingProgress.Active) yield return null;
        var panel=Object.FindFirstObjectByType<WorkbenchPanel>();
        var feed=Object.FindFirstObjectByType<ItemFeed>();
        if(feed) feed.gameObject.SetActive(false);
        var inventory=InventoryManager.Instance;
        var snapshot=inventory.GetSnapshot().ToDictionary(x=>x.Key,x=>x.Value);
        var keys=new Dictionary<string,int?>();
        foreach (var field in typeof(RecipeUnlocks).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
            if (field.IsLiteral && field.FieldType==typeof(string))
            {
                string key=(string)field.GetRawConstantValue();
                keys[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetInt(key):(int?)null;
                PlayerPrefs.SetInt(key,1);
            }
        try
        {
            inventory.ResetAll();
            var boots=panel.recipes.First(x=>x.output.item==Item.HeavyDutyBoots);
            boots.TryGetCosts(out var costs);
            foreach(var cost in costs) inventory.Add(cost.Key, cost.Key.item==Item.Iron?2:cost.Value+2);
            panel.ShowPanel(true); panel.SetSearch(""); panel.SetCategory(CraftingRecipe.RecipeCategory.Automatic);
            panel.SetFavoritesOnly(false); panel.SetFilter(false); panel.SelectRecipe(boots);
            yield return new WaitForSecondsRealtime(.6f);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            var capture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Assets/Design/Workbench-v2-implemented.png",capture.EncodeToPNG());
            Destroy(capture);
        }
        finally
        {
            panel.ShowPanel(false);
            inventory.ResetAll(); foreach(var item in snapshot) inventory.Add(item.Key,item.Value);
            foreach(var pair in keys) if(pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key,pair.Value.Value); else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save(); Destroy(gameObject);
            if(feed) feed.gameObject.SetActive(true);
        }
    }
}
