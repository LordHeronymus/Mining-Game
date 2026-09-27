#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class InstallTitaniumItem
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");

        const string source = "Assets/Design/TitaniumOre/Titanium-InventoryIcon-A-Shards.png";
        const string destination = "Assets/GameObjects/Items/Sprites/Ores/Titanium_ItemIcon.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(destination))
        {
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(destination);
        if (!icon) throw new InvalidOperationException("Titanium icon could not be imported.");

        const string itemPath = "Assets/GameObjects/Items/Ores/Titanium.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, itemPath);
        }
        item.item = Item.Titanium;
        item.category = ItemCategory.Ore;
        item.displayName = "Titan";
        item.icon = icon;
        item.themeColor = new Color(.62f, .71f, .78f, 1f);
        item.worth = 0;
        item.weight = 1f;
        EditorUtility.SetDirty(item);

        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(
            "Assets/GameObjects/Workbench/Recipes/TitaniumPickaxe.asset");
        var wood = AssetDatabase.LoadAssetAtPath<ItemSO>(
            "Assets/GameObjects/Items/Materials/Wood.asset");
        if (!recipe || !wood || !recipe.output || recipe.output.item != Item.TitaniumPickaxe)
            throw new InvalidOperationException("Titanium pickaxe recipe or wood item is missing.");
        if (!recipe.SetRecipeSettings(recipe.category, recipe.outputAmount,
                new[] { new CraftingIngredient(wood, 10), new CraftingIngredient(item, 20) }))
            throw new InvalidOperationException("Titanium pickaxe costs were rejected.");
        recipe.SaveRecipeSettings();
        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();

        return new { item = itemPath, icon = destination,
            iconGuid = AssetDatabase.AssetPathToGUID(destination),
            wood = recipe.ingredients[0].amount, titanium = recipe.ingredients[1].amount };
    }
}
#endif
