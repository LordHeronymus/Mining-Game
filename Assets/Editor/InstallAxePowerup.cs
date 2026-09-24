using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InstallAxePowerup
{
    const string IconPath = "Assets/GameObjects/Items/Sprites/Crafting/Axe.png";
    const string ItemPath = "Assets/GameObjects/Items/Tools/Axe.asset";
    const string RecipePath = "Assets/GameObjects/Workbench/Recipes/Axe.asset";

    [MenuItem("Tools/Workbench/Install Axe Powerup")]
    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode.");
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene.");

        var recipe = CreateRecipe();
        var trees = UnityEngine.Object.FindFirstObjectByType<SurfaceTrees>(FindObjectsInactive.Include);
        var panel = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (!trees || !panel) throw new InvalidOperationException("SurfaceTrees or WorkbenchPanel is missing.");

        var treesData = new SerializedObject(trees);
        treesData.FindProperty("axePowerup").objectReferenceValue = recipe.output;
        var multiplier = treesData.FindProperty("axeHitMultiplier");
        if (multiplier.floatValue < 1f) multiplier.floatValue = 4f;
        treesData.ApplyModifiedProperties();

        if (panel.recipes == null || !panel.recipes.Contains(recipe))
        {
            Undo.RecordObject(panel, "Add axe recipe");
            panel.recipes = (panel.recipes ?? Array.Empty<CraftingRecipe>()).Append(recipe).ToArray();
            EditorUtility.SetDirty(panel);
        }
        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(trees.gameObject.scene);
        EditorSceneManager.SaveScene(trees.gameObject.scene);
        return "Axe powerup and workbench recipe installed.";
    }

    public static CraftingRecipe CreateRecipe()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (!icon) throw new InvalidOperationException("High-resolution axe icon is missing.");

        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(ItemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            item.item = Item.Axe;
            item.category = ItemCategory.Powerup;
            item.displayName = "Axt";
            item.icon = icon;
            AssetDatabase.CreateAsset(item, ItemPath);
        }
        else if (item.icon != icon)
        {
            item.icon = icon;
            EditorUtility.SetDirty(item);
        }

        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(RecipePath);
        if (!recipe)
        {
            var wood = Material("Materials/Wood");
            var iron = Material("Ores/Iron");
            var nails = Material("Materials/Nails");
            recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.output = item;
            recipe.outputAmount = 1;
            recipe.pluralName = "Äxte";
            recipe.category = CraftingRecipe.RecipeCategory.Tools;
            recipe.ingredients = new[]
            {
                new CraftingIngredient(wood, 3),
                new CraftingIngredient(iron, 5),
                new CraftingIngredient(nails, 2)
            };
            AssetDatabase.CreateAsset(recipe, RecipePath);
        }
        return recipe;
    }

    static ItemSO Material(string relativePath)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/" + relativePath + ".asset");
        if (!item) throw new InvalidOperationException("Missing axe material: " + relativePath);
        return item;
    }
}
