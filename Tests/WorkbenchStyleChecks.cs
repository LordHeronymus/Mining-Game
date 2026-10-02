using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class WorkbenchStyleChecks
{
    public static object Main()
    {
        if(!Application.isPlaying || LoadingProgress.Active) throw new Exception("Run after the game finishes loading.");
        new GameObject("Workbench checks").AddComponent<WorkbenchStyleProbe>();
        return "Running workshop layout, filters, five ingredients, glow and batch crafting checks.";
    }
}
public sealed class WorkbenchStyleProbe : MonoBehaviour
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    IEnumerator Start()
    {
        var panel=Object.FindFirstObjectByType<WorkbenchPanel>();
        var inventory=InventoryManager.Instance;
        var snapshot=inventory.GetSnapshot().ToDictionary(x=>x.Key,x=>x.Value);
        var keys=new Dictionary<string,int?>();
        foreach(var field in typeof(RecipeUnlocks).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
            if(field.IsLiteral && field.FieldType==typeof(string))
            {
                string key=(string)field.GetRawConstantValue();
                keys[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetInt(key):(int?)null;
                PlayerPrefs.SetInt(key,1);
            }
        var rope=panel.recipes.First(x=>x.output.item==Item.Rope);
        bool favoriteExisted=PlayerPrefs.HasKey(rope.FavoriteKey);
        int originalFavorite=PlayerPrefs.GetInt(rope.FavoriteKey);
        string result="FAILED: incomplete";
        try
        {
            inventory.ResetAll();
            panel.ShowPanel(true); panel.SetCategory(CraftingRecipe.RecipeCategory.Automatic);
            panel.SetSearch(""); panel.SetFavoritesOnly(false); panel.SetFilter(false);
            Check(panel.IsOpen && GameplayInputBlocker.IsBlocked,"Workshop blocks gameplay.");
            var content=(RectTransform)panel.transform.Find("Layout/Recipes/Content");
            var cards=content.Cast<Transform>().Where(x=>x.gameObject.activeSelf).Select(x=>(RectTransform)x).ToArray();
            Check(cards.Length==panel.VisibleRecipeCount && cards.Length>12,"Unlocked recipe grid includes overflow.");
            Check(content.childCount==panel.recipes.Count(x=>x && x.output),"Recipe cards stay pooled.");
            Check(cards[3].anchoredPosition.x==576 && cards[4].anchoredPosition.y==-195,"Four columns with correct spacing.");
            Check(cards[11].anchoredPosition.y- cards[11].rect.height>=-570,"Twelve whole cards fit in viewport.");
            var scroll=content.parent.GetComponent<ScrollRect>();
            scroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases();
            Check(content.anchoredPosition.y>0,"All remaining recipes reachable by scrolling.");
            var bridge=panel.recipes.First(x=>x.output.item==Item.BridgePart);
            panel.SetSearch(" BRUCKEN ");
            Check(panel.VisibleRecipeCount==1 && panel.SelectedRecipe==bridge,"Search normalizes whitespace and diacritics.");
            panel.SetSearch("no-such-recipe");
            Check(panel.VisibleRecipeCount==0 && !panel.transform.Find("Layout/Details").gameObject.activeSelf,"Empty filter hides actionable details.");
            panel.SetSearch("");
            var boots=panel.recipes.First(x=>x.output.item==Item.HeavyDutyBoots);
            panel.SelectRecipe(boots); boots.TryGetCosts(out var bootCosts);
            Check(bootCosts.Count==4,"Boots use four actual ingredients.");
            yield return null;
            var ingredients=(RectTransform)panel.transform.Find("Layout/Details/Ingredients/Content");
            Check(ingredients.childCount==4,"Four ingredients appear in a 2 by 2 grid.");
            Check(((RectTransform)ingredients.GetChild(1)).anchoredPosition.x==230 && ((RectTransform)ingredients.GetChild(2)).anchoredPosition.y==-72,"Ingredient grid positions.");
            Check(!panel.transform.Find("Layout/Details/QuantityControls").gameObject.activeSelf,"Powerups preserve one-time crafting rule.");
            Check(ingredients.GetComponentsInChildren<TextMeshProUGUI>().Any(x=>x.text.Contains("<color=#E88665>")),"Missing counts are coral.");
            var glow=panel.transform.Find("Layout/Details/Item Glow").GetComponent<Image>();
            var icon=panel.transform.Find("Layout/Details/Icon").GetComponent<Image>();
            Check(glow.material.shader.name=="UI/Workbench Item Glow" && !glow.raycastTarget,"Procedural gold glow does not intercept input.");
            Check(glow.transform.GetSiblingIndex()<icon.transform.GetSiblingIndex() && icon.rectTransform.rect.height>200,"Glow lies behind enlarged selected item.");
            float time=glow.material.GetFloat("_AnimationTime");
            yield return new WaitForSecondsRealtime(.12f);
            Check(glow.material.GetFloat("_AnimationTime")>time,"Shimmer animates with unscaled time.");
            var greaves=panel.recipes.First(x=>x.output.item==Item.SpringGreaves);
            panel.SelectRecipe(greaves); greaves.TryGetCosts(out var greaveCosts);
            yield return null;
            Check(ingredients.childCount==greaveCosts.Count && greaveCosts.Count>4,"Recipes with more than four ingredients retain every ingredient.");
            Check(ingredients.rect.height>((RectTransform)ingredients.parent).rect.height,"Extra ingredient rows scroll inside material viewport.");
            var ingredientScroll=ingredients.parent.GetComponent<ScrollRect>();
            ingredientScroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases();
            Check(ingredients.anchoredPosition.y>0,"Fifth ingredient reachable.");
            panel.SelectRecipe(bridge);
            var favorite=content.Find(rope.name+"/Favorite").GetComponent<Button>();
            Canvas.ForceUpdateCanvases();
            var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,
                favorite.transform.TransformPoint(((RectTransform)favorite.transform).rect.center))};
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0 && hits[0].gameObject==favorite.gameObject,"Favorite button remains above card in raycasts.");
            if(!panel.IsFavorite(rope)) favorite.onClick.Invoke();
            Check(panel.IsFavorite(rope) && panel.SelectedRecipe==bridge,"Favorite toggle preserves selection.");
            panel.SetFavoritesOnly(true); panel.SetSearch("Seil"); panel.SetFilter(true);
            Check(panel.VisibleRecipeCount==0,"Filters combine and use actual inventory.");
            rope.TryGetCosts(out var ropeCosts);
            foreach(var cost in ropeCosts) inventory.Add(cost.Key,cost.Value*3);
            Check(panel.VisibleRecipeCount==1 && panel.SelectedRecipe==rope,"Inventory change refreshes craftable favorite.");
            panel.SetQuantity(3); Check(panel.Quantity==3,"Batch quantity preserved.");
            Check(panel.CraftSelected(),"Craft action starts transaction.");
            float started=Time.realtimeSinceStartup;
            while(inventory.GetCount(rope.output)!=rope.outputAmount*3 && Time.realtimeSinceStartup-started<12) yield return null;
            Check(inventory.GetCount(rope.output)==rope.outputAmount*3,"Async craft produces exact batch output.");
            Check(ropeCosts.All(x=>inventory.GetCount(x.Key)==0),"Craft consumes exact ingredients.");
            Check(panel.VisibleRecipeCount==0,"Spent materials refresh filtered recipes.");
            result="PASS: 12 visible cards, overflow scrolling, search, favorites, inventory filters, 2x2 and five ingredients, unscaled gold shimmer, raycasts, async batch crafting.";
        }
        finally
        {
            panel.SetSearch(""); panel.SetFilter(false); panel.SetFavoritesOnly(false);
            panel.SetCategory(CraftingRecipe.RecipeCategory.Automatic); panel.ShowPanel(false);
            inventory.ResetAll(); foreach(var item in snapshot) inventory.Add(item.Key,item.Value);
            foreach(var pair in keys) if(pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key,pair.Value.Value); else PlayerPrefs.DeleteKey(pair.Key);
            if(favoriteExisted) PlayerPrefs.SetInt(rope.FavoriteKey,originalFavorite); else PlayerPrefs.DeleteKey(rope.FavoriteKey);
            PlayerPrefs.Save();
            File.WriteAllText("Temp/WorkbenchStyleChecks-result.txt",result);
            Destroy(gameObject);
        }
    }
}
