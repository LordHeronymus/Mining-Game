using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Edit Mode: every blueprint picture on a shop card shows its visible parchment at the same absolute size
// (BuyPage.BlueprintSize) and stays clear of the card frame, whatever transparent margin the art has.
public static class ShopBlueprintCardChecks
{
    const float CardWidth = 296, CardHeight = 322;
    public static object Main()
    {
        if (Application.isPlaying) return "Requires Edit Mode.";
        var fit = typeof(BuyPage).GetMethod("FitBlueprint", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("FAIL: BuyPage.FitBlueprint not found");
        var sprites = Resources.LoadAll<Sprite>("Shop").Where(s => s.name.EndsWith("Recipe")).ToArray();
        if (sprites.Length < 13) throw new Exception("FAIL: only " + sprites.Length + " blueprint sprites");
        var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
        float smallest = float.MaxValue, largest = 0, trimmed = 0;
        try
        {
            ShopVisualTheme.Place(card, 0, 0, CardWidth, CardHeight);
            foreach (var sprite in sprites)
            {
                var rect = new GameObject(sprite.name, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(card, false); ShopVisualTheme.Place(rect, 18, 22, 260, 176);
                var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite;
                fit.Invoke(null, new object[] { image });
                if (sprite.vertices.Length == 0) throw new Exception("FAIL: no mesh on " + sprite.name);
                Vector2 lo = sprite.vertices[0], hi = lo;
                foreach (var v in sprite.vertices) { lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
                var px = sprite.rect.size;
                lo = (lo * sprite.pixelsPerUnit + sprite.pivot) / px; hi = (hi * sprite.pixelsPerUnit + sprite.pivot) / px;
                trimmed = Mathf.Max(trimmed, 1 - Mathf.Max(hi.x - lo.x, hi.y - lo.y));
                var size = Vector2.Scale(hi - lo, rect.rect.size) * rect.localScale.x;
                // Visible parchment in card space (top-left origin, y down).
                float left = rect.anchoredPosition.x + (lo.x - .5f) * rect.rect.width;
                float top = -rect.anchoredPosition.y - (hi.y - .5f) * rect.rect.height;
                float extent = Mathf.Max(size.x, size.y);
                smallest = Mathf.Min(smallest, extent); largest = Mathf.Max(largest, extent);
                if (Mathf.Abs(extent - 160) > .5f) throw new Exception($"FAIL: {sprite.name} visible size {size}");
                if (top < 30 || left < 30 || left + size.x > CardWidth - 30 || top + size.y > 196)
                    throw new Exception($"FAIL: {sprite.name} overlaps frame or name ({left:0.#},{top:0.#} {size})");
                if (Mathf.Abs(left + size.x / 2 - CardWidth / 2) > .5f) throw new Exception($"FAIL: {sprite.name} not centred");
            }
        }
        finally { Object.DestroyImmediate(card.gameObject); }
        // Pickaxe/medkit art has ~4 % transparent margin; if the mesh were the full rect, sizes would differ in game.
        if (trimmed < .03f) throw new Exception("FAIL: sprite meshes are not tight, margins not detected");
        return $"PASS: {sprites.Length} blueprints, visible size {smallest:0.##}..{largest:0.##}, inside frame, largest trimmed margin {trimmed:P1}";
    }
}
