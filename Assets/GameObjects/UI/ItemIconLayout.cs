using System.Linq;
using UnityEngine;

public static class ItemIconLayout
{
    public static readonly string[] Fields = { "iconScale", "iconOffset", "iconFlipX", "iconFlipY" };
    public static readonly string[] Categories = { "Alle", "Erze", "Werkzeuge", "Verbrauchsgegenstände", "Powerups", "Materialien und Sonstiges" };
    public static bool Matches(ItemSO item, string search, int category) => item &&
        (category == 0 || Category(item) == Categories[category]) &&
        (string.IsNullOrWhiteSpace(search) || item.displayName.IndexOf(search.Trim(), System.StringComparison.CurrentCultureIgnoreCase) >= 0);
    public static string Category(ItemSO item) => item.category switch {
        ItemCategory.Ore => "Erze", ItemCategory.Tool => "Werkzeuge", ItemCategory.Consumable => "Verbrauchsgegenstände",
        ItemCategory.Powerup => "Powerups", _ => "Materialien und Sonstiges" };
    public static void Apply(ItemSO item, RectTransform rect, Vector2 center)
    {
        if (!rect) return;
        rect.pivot = new Vector2(.5f, .5f);
        var scale = item ? item.iconScale : Vector2.one;
        var offset = item ? item.iconOffset : Vector2.zero;
        rect.anchoredPosition = center + new Vector2(offset.x * rect.rect.width / 260f, offset.y * rect.rect.height / 194f);
        rect.localScale = new Vector3(Mathf.Clamp(scale.x, .1f, 3f) * (item && item.iconFlipX ? -1 : 1),
            Mathf.Clamp(scale.y, .1f, 3f) * (item && item.iconFlipY ? -1 : 1), 1);
    }
    public static void Migrate(GpsDocument data, GpsProfile profile)
    {
        if (data == null || !profile) return;
        foreach (var record in data.records.Where(r => r.type == typeof(ItemSO).AssemblyQualifiedName))
        {
            var item = profile.Resolve(record.assetKey) as ItemSO;
            if (!item || record.fields.Any(f => f.name == "iconLayoutVersion" && f.number >= 1)) continue;
            Vector2 scale = Vector2.one, offset = Vector2.zero; bool flipX = false, flipY = false;
            if (item.category == ItemCategory.Ore)
            {
                scale = Read(record, "shopIconScale", item.shopIconScale, profile);
                offset = Read(record, "shopIconOffset", item.shopIconOffset, profile);
                flipX = Read(record, "shopIconFlipX", item.shopIconFlipX, profile);
                flipY = Read(record, "shopIconFlipY", item.shopIconFlipY, profile);
            }
            else
            {
                var recipeRecord = data.records.FirstOrDefault(r => profile.Resolve(r.assetKey) is CraftingRecipe recipe && recipe.output == item);
                if (recipeRecord != null && profile.Resolve(recipeRecord.assetKey) is CraftingRecipe recipe)
                {
                    var layout = recipe.CardIconLayout;
                    if (Read(recipeRecord, "customCardIconLayout", false, profile)) layout = Read(recipeRecord, "cardIconLayout", layout, profile);
                    scale = layout.scale; offset = Vector2.Scale(layout.offset, new Vector2(260f / 134f, 194f / 106f));
                    flipX = layout.flipX; flipY = layout.flipY;
                }
            }
            Set(record, "iconScale", scale, profile); Set(record, "iconOffset", offset, profile);
            Set(record, "iconFlipX", flipX, profile); Set(record, "iconFlipY", flipY, profile); Set(record, "iconLayoutVersion", 1, profile);
        }
    }
    static T Read<T>(GpsRecord r, string name, T fallback, GpsProfile p) => r.fields.Find(f => f.name == name) is GpsValue value ? (T)GpsCodec.Write(value, p.Resolve) : fallback;
    static void Set<T>(GpsRecord r, string name, T value, GpsProfile p)
    {
        var node = GpsCodec.Read(name, typeof(T), value, p.Key); int index = r.fields.FindIndex(f => f.name == name);
        if (index < 0) r.fields.Add(node); else r.fields[index] = node;
    }
}
