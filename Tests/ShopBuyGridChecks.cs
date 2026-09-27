if (!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Play mode required");
UnityEngine.Application.runInBackground=true;
var debug=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>(UnityEngine.FindObjectsInactive.Include);
if(debug) debug.gameObject.SetActive(false);
var shop=UnityEngine.Object.FindFirstObjectByType<ShopPanel>(UnityEngine.FindObjectsInactive.Include);
shop.ShowPanel(true);
shop.ShowBuyPage();
var page=UnityEngine.Object.FindFirstObjectByType<BuyPage>();
var stats=StatsManager.Instance;
var root=page.transform.Find("Blueprint Shop");
var cards=root.Find("Blueprints/Content");
if(cards.childCount!=9) throw new System.Exception("Expected nine blueprints");
var titanium=cards.Find("Blueprint TitaniumPickaxe");
titanium.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
var purchase=root.Find("Buy Blueprint").GetComponent<UnityEngine.UI.Button>();
var money=page.transform.parent.Find("CurrentMoney/Amount").GetComponent<TMPro.TextMeshProUGUI>();
const string key="workbench.recipe.titanium-pickaxe.unlocked";
bool existed=UnityEngine.PlayerPrefs.HasKey(key);
int oldUnlock=UnityEngine.PlayerPrefs.GetInt(key), oldMoney=stats.Money;
try {
    UnityEngine.PlayerPrefs.DeleteKey(key);
    stats.AddMoney(-stats.Money);
    if(purchase.interactable) throw new System.Exception("Unaffordable purchase enabled");
    purchase.onClick.Invoke();
    if(RecipeUnlocks.IsTitaniumPickaxeUnlocked || stats.Money!=0) throw new System.Exception("Unaffordable purchase succeeded");
    stats.AddMoney(5000);
    if(!purchase.interactable || money.text!=ShopMoneyFormatter.Format(5000)) throw new System.Exception("Money display or affordable button stale");
    purchase.onClick.Invoke();
    if(!RecipeUnlocks.IsTitaniumPickaxeUnlocked || stats.Money!=3800 || purchase.interactable) throw new System.Exception("Purchase state incorrect");
    purchase.onClick.Invoke();
    if(stats.Money!=3800) throw new System.Exception("Duplicate purchase charged");
    if(((UnityEngine.RectTransform)titanium).anchoredPosition.y!=-960) throw new System.Exception("Owned blueprint did not move to end");
    shop.ShowSellPage();
    if(page.transform.parent.GetComponent<UnityEngine.UI.Image>().sprite.name=="BuyBackground") throw new System.Exception("Sell background not restored");
    shop.ShowBuyPage();
    if(!root.Find("Selected Blueprint").GetComponent<UnityEngine.UI.Image>().sprite.name.Contains("Titanium")) throw new System.Exception("Selection lost on tab switch");
    if(money.text!=ShopMoneyFormatter.Format(3800)) throw new System.Exception("Money stale on tab switch");
    var bench=UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(UnityEngine.FindObjectsInactive.Include);
    var recipe=System.Array.Find(bench.recipes,r=>r && r.output && r.output.item==Item.TitaniumPickaxe);
    recipe.TryGetCosts(out var costs);
    var materials=root.Find("Materials/Content");
    foreach(var cost in costs) {
        UnityEngine.Transform row=null; foreach(UnityEngine.Transform child in materials) if(child.name=="Material "+cost.Key.name) row=child;
        if(!row) throw new System.Exception("Missing actual recipe ingredient: "+cost.Key.name);
        var texts=row.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
        if(texts[0].text!=cost.Key.displayName || texts[1].text!=cost.Value.ToString()) throw new System.Exception("Incorrect ingredient text");
    }
    return "PASS: 9 cards, selection, actual costs, affordability, money update, purchase, duplicate protection, owned sorting and both tab backgrounds.";
} finally {
    if(existed) UnityEngine.PlayerPrefs.SetInt(key,oldUnlock); else UnityEngine.PlayerPrefs.DeleteKey(key);
    UnityEngine.PlayerPrefs.Save();
    stats.AddMoney(oldMoney-stats.Money);
}

