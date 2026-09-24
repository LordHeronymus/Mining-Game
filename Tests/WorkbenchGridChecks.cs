using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class WorkbenchGridChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static CraftingRecipe.RecipeCategory All => CraftingRecipe.RecipeCategory.Automatic;

    public static object Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>();
        var inventory = InventoryManager.Instance;
        var snapshot = inventory.GetSnapshot().ToDictionary(x => x.Key, x => x.Value);
        var favoriteState = panel.recipes.ToDictionary(x => x, x => PlayerPrefs.GetInt(x.FavoriteKey, 0));
        var bridge = panel.recipes.First(x => x.output.item == Item.BridgePart);
        var rope = panel.recipes.First(x => x.output.item == Item.Rope);
        try
        {
            inventory.ResetAll();
            panel.ShowPanel(true);
            Check(panel.IsOpen && GameplayInputBlocker.IsBlocked, "Modal must block gameplay.");
            panel.SetCategory(All); panel.SetSearch(""); panel.SetFilter(false); panel.SetFavoritesOnly(false);
            Check(panel.VisibleRecipeCount == 16, "All 16 actual recipes must be present.");
            var content = panel.transform.Find("Layout/Recipes/Content");
            Check(content.childCount == 16, "Cards should be pooled once.");
            var fourth = (RectTransform)content.GetChild(3);
            var fifth = (RectTransform)content.GetChild(4);
            Check(fourth.anchoredPosition.x == 588 && fifth.anchoredPosition.y == -174, "Four-column grid.");
            var input = panel.transform.Find("Layout/Search").GetComponent<TMP_InputField>();
            input.text = "  BRUCKEN  ";
            Check(panel.VisibleRecipeCount == 1 && panel.SelectedRecipe == bridge, "Search must handle case, whitespace and umlauts.");
            input.text = "xyz-no-match";
            Check(panel.VisibleRecipeCount == 0 && !panel.SelectedRecipe && !panel.transform.Find("Layout/Details").gameObject.activeSelf,
                "Empty search must clear actionable details.");
            input.text = "";
            panel.SetCategory(CraftingRecipe.RecipeCategory.Tools); Check(panel.VisibleRecipeCount == 12, "Tools category including axe and scythe.");
            panel.SetCategory(CraftingRecipe.RecipeCategory.Building); Check(panel.VisibleRecipeCount == 2, "Building category.");
            panel.SetCategory(CraftingRecipe.RecipeCategory.Materials); Check(panel.VisibleRecipeCount == 2, "Materials category.");
            panel.SetCategory(All);
            foreach (var recipe in panel.recipes) if (panel.IsFavorite(recipe)) panel.ToggleFavorite(recipe);
            panel.SelectRecipe(bridge);
            var favoriteButton = content.Find(rope.name + "/Favorite").GetComponent<Button>();
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null,
                favoriteButton.transform.TransformPoint(((RectTransform)favoriteButton.transform).rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject == favoriteButton.gameObject, "Favorite must receive clicks above recipe card.");
            ExecuteEvents.Execute(favoriteButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Check(panel.IsFavorite(rope) && panel.SelectedRecipe == bridge, "Favorite click must not change selection.");
            Check(PlayerPrefs.GetInt(rope.FavoriteKey) == 1, "Favorite persistence.");
            panel.SetFavoritesOnly(true); Check(panel.VisibleRecipeCount == 1 && panel.SelectedRecipe == rope, "Favorites filter.");
            panel.SetCategory(CraftingRecipe.RecipeCategory.Building); Check(panel.VisibleRecipeCount == 0, "Filters must intersect.");
            panel.SetCategory(All); panel.SetFilter(true);
            inventory.ResetAll(); Check(panel.VisibleRecipeCount == 0, "Craftable filter updates from inventory.");
            rope.TryGetCosts(out var costs);
            foreach (var cost in costs) inventory.Add(cost.Key, cost.Value * 3);
            Check(panel.VisibleRecipeCount == 1 && panel.SelectedRecipe == rope, "Live material gain must expose craftable favorite.");
            panel.SetQuantity(3); Check(panel.Quantity == 3, "Batch quantity.");
            Check(panel.CraftSelected() && inventory.GetCount(rope.output) == rope.outputAmount * 3, "Craft through filtered UI.");
            Check(panel.VisibleRecipeCount == 0 && !panel.SelectedRecipe, "Spent ingredients must remove uncraftable recipe.");
            panel.SetFilter(false); panel.ToggleFavorite(rope);
            Check(panel.VisibleRecipeCount == 0, "Removing last favorite gives empty state.");
            panel.SetFavoritesOnly(false); panel.SelectRecipe(bridge);
            panel.ShowPanel(false); Check(!GameplayInputBlocker.IsBlocked, "Closing releases gameplay.");
            panel.ShowPanel(true);
            Check(content.childCount == 16, "Repeated filtering/opening must reuse cards.");
            Canvas.ForceUpdateCanvases();
            var scroll = content.parent.GetComponent<ScrollRect>();
            Check(scroll.verticalScrollbar.handleRect.rect.height <= 508, "Scrollbar handle must fit its track.");
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Check(((RectTransform)content).anchoredPosition.y > 100, "Scroll must reach final row.");
            panel.SetSearch("spitzhacke"); Canvas.ForceUpdateCanvases();
            Check(panel.VisibleRecipeCount == 8 && Math.Abs(scroll.verticalNormalizedPosition - 1) < .01, "Filter resets scroll to top.");
            return "PASS: 16 recipes, 4-column layout, search, all categories, intersecting filters, favorite hit targets/persistence, empty states, live stock, batch crafting, modal guard, pooled cards and scrolling.";
        }
        finally
        {
            inventory.ResetAll(); foreach (var pair in snapshot) inventory.Add(pair.Key, pair.Value);
            foreach (var pair in favoriteState) if (panel.IsFavorite(pair.Key) != (pair.Value == 1)) panel.ToggleFavorite(pair.Key);
            panel.SetSearch(""); panel.SetCategory(All); panel.SetFilter(false); panel.SetFavoritesOnly(false); panel.SelectRecipe(bridge);
            panel.ShowPanel(true);
        }
    }

    public static object ManyRecipes()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var source = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>();
        source.ShowPanel(false);
        var recipes = new List<CraftingRecipe>();
        var items = new List<ItemSO>();
        var root = new GameObject("Temporary 60 Recipe Test", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        root.SetActive(false);
        root.transform.SetParent(source.transform.parent, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one; rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        var panel = root.AddComponent<WorkbenchPanel>();
        try
        {
            for (int i = 0; i < 60; i++)
            {
                var item = UnityEngine.Object.Instantiate(source.recipes[0].output); items.Add(item);
                item.displayName = "Rezept " + i.ToString("00");
                var recipe = ScriptableObject.CreateInstance<CraftingRecipe>(); recipes.Add(recipe);
                recipe.name = "TemporaryRecipe" + i; recipe.output = item;
                recipe.category = (CraftingRecipe.RecipeCategory)(i % 3 + 1);
            }
            panel.recipes = recipes.ToArray(); panel.font = source.font; panel.fontMaterial = source.fontMaterial;
            panel.rowSprite = source.rowSprite; panel.selectedRowSprite = source.selectedRowSprite; panel.actionSprite = source.actionSprite;
            root.SetActive(true); panel.ShowPanel(true); Canvas.ForceUpdateCanvases();
            Check(panel.VisibleRecipeCount == 60, "60 recipes must remain accessible.");
            var scroll = panel.transform.Find("Layout/Recipes").GetComponent<ScrollRect>();
            Check(scroll.content.rect.height == 2596, "15 grid rows.");
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Check(scroll.content.anchoredPosition.y > 2000, "Last of 60 recipes must be reachable.");
            panel.SetSearch("59"); Check(panel.VisibleRecipeCount == 1 && panel.SelectedRecipe == recipes[59], "Find recipe 60 directly.");
            panel.SetSearch(""); panel.SetCategory(CraftingRecipe.RecipeCategory.Tools);
            Check(panel.VisibleRecipeCount == 20, "Large category filtering.");
            panel.SetSearch("59"); Check(panel.VisibleRecipeCount == 0, "Search and category intersect for 60 recipes.");
            panel.SetCategory(All); Check(panel.SelectedRecipe == recipes[59], "Restore selection after empty results.");
            return "PASS: 60 temporary recipes, 15 rows, last-row reachability, direct search and combined category filters.";
        }
        finally
        {
            panel.ShowPanel(false); UnityEngine.Object.DestroyImmediate(root);
            foreach (var recipe in recipes) UnityEngine.Object.DestroyImmediate(recipe);
            foreach (var item in items) UnityEngine.Object.DestroyImmediate(item);
            source.ShowPanel(true);
        }
    }
}
