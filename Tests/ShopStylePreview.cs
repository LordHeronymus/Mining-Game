using System.Collections;
using System.IO;
using UnityEngine;
public static class ShopStylePreview
{
    public static object Main()
    {
        var shop = Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        shop.ShowPanel(true); shop.ShowBuyPage();
        var page = Object.FindFirstObjectByType<BuyPage>();
        page.transform.Find("Blueprint Shop/Blueprints/Content/Blueprint HeavyDutyBoots").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        new GameObject("Shop preview capture").AddComponent<ShopStyleCapture>();
        return "Capturing approved four-ingredient shop layout.";
    }
}
public static class ShopSellStylePreview
{
    public static object Main()
    {
        Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include).ShowSellPage();
        new GameObject("Shop sell preview").AddComponent<ShopStyleCapture>().path="Assets/Design/ShopSell-v2-implemented.png";
        return "Capturing sell view.";
    }
}
public sealed class ShopStyleCapture : MonoBehaviour
{
    public string path="Assets/Design/ShopBuy-v2-implemented.png";
    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(.7f);
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var capture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path, capture.EncodeToPNG());
        Destroy(capture); Destroy(gameObject);
    }
}