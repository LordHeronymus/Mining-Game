using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PickaxeWorkbenchSetup
{
    const string ItemFolder = "Assets/GameObjects/Items/Tools/";
    const string IconFolder = "Assets/GameObjects/Items/Sprites/Pickaxes/";
    const string RecipeFolder = "Assets/GameObjects/Workbench/Recipes/";

    [MenuItem("Tools/Workbench/Install Pickaxes")]
    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode.");
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene.");
        AssetDatabase.Refresh();
        var recipes = CreateRecipes();
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (!panel) throw new InvalidOperationException("WorkbenchPanel is missing.");
        var existing = panel.recipes ?? Array.Empty<CraftingRecipe>();
        var added = recipes.Where(recipe => !existing.Contains(recipe)).ToArray();
        if (added.Length > 0)
        {
            Undo.RecordObject(panel, "Add pickaxe recipes");
            panel.recipes = existing.Concat(added).ToArray();
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            EditorSceneManager.SaveScene(panel.gameObject.scene);
        }
        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        return "Eight pickaxe recipes installed; added " + added.Length + " to the workbench.";
    }

    public static CraftingRecipe[] CreateRecipes()
    {
        var wood = Existing("Materials/Wood");
        var nails = Existing("Materials/Nails");
        var coal = Existing("Ores/Coal");
        var copper = Existing("Ores/Copper");
        var iron = Existing("Ores/Iron");
        var silver = Existing("Ores/Silver");
        var gold = Existing("Ores/Gold");
        var platinum = Existing("Ores/Platinum");

        return new[]
        {
            Create("CopperPickaxe", Item.CopperPickaxe, "Kupferspitzhacke", "Copper-Pickaxe.png",
                new CraftingIngredient(wood, 3), new CraftingIngredient(copper, 5)),
            Create("IronPickaxe", Item.IronPickaxe, "Eisenspitzhacke", "Iron-Pickaxe-512.png",
                new CraftingIngredient(wood, 3), new CraftingIngredient(iron, 6), new CraftingIngredient(nails, 2)),
            Create("SteelPickaxe", Item.SteelPickaxe, "Stahlspitzhacke", "Steel-Pickaxe-512.png",
                new CraftingIngredient(wood, 3), new CraftingIngredient(iron, 10), new CraftingIngredient(coal, 4)),
            Create("TitaniumPickaxe", Item.TitaniumPickaxe, "Titanspitzhacke", "Titanium-Pickaxe-512.png",
                new CraftingIngredient(wood, 4), new CraftingIngredient(iron, 6), new CraftingIngredient(platinum, 4)),
            Create("TungstenPickaxe", Item.TungstenPickaxe, "Wolframspitzhacke", "Tungsten-Pickaxe-Crystal-Variant-1-512.png",
                new CraftingIngredient(wood, 4), new CraftingIngredient(iron, 4), new CraftingIngredient(coal, 5), new CraftingIngredient(platinum, 6)),
            Create("ObsidianPickaxe", Item.ObsidianPickaxe, "Obsidianspitzhacke", "Obsidian-Pickaxe-512.png",
                new CraftingIngredient(wood, 4), new CraftingIngredient(copper, 4), new CraftingIngredient(iron, 4), new CraftingIngredient(coal, 8)),
            Create("MythrilPickaxe", Item.MythrilPickaxe, "Mithrilspitzhacke", "Mythril-Pickaxe-512.png",
                new CraftingIngredient(wood, 4), new CraftingIngredient(silver, 8), new CraftingIngredient(platinum, 5)),
            Create("DiamondPickaxe", Item.DiamondPickaxe, "Diamantspitzhacke", "Diamond-Pickaxe-512.png",
                new CraftingIngredient(wood, 4), new CraftingIngredient(silver, 5), new CraftingIngredient(gold, 8), new CraftingIngredient(platinum, 10))
        };
    }

    static ItemSO Existing(string path)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/" + path + ".asset");
        if (!item) throw new InvalidOperationException("Missing material: " + path);
        return item;
    }

    static CraftingRecipe Create(string file, Item id, string displayName, string iconFile, params CraftingIngredient[] ingredients)
    {
        string iconPath = IconFolder + iconFile;
        AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Missing pickaxe icon: " + iconPath);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
            importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        if (!icon) throw new InvalidOperationException("Cannot load pickaxe icon: " + iconPath);
        string itemPath = ItemFolder + file + ".asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            item.item = id;
            item.category = ItemCategory.Tool;
            item.displayName = displayName;
            item.icon = icon;
            AssetDatabase.CreateAsset(item, itemPath);
        }
        string recipePath = RecipeFolder + file + ".asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(recipePath);
        if (!recipe)
        {
            recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.output = item;
            recipe.outputAmount = 1;
            recipe.pluralName = displayName.Replace("spitzhacke", "spitzhacken");
            recipe.ingredients = ingredients;
            AssetDatabase.CreateAsset(recipe, recipePath);
        }
        return recipe;
    }
}
