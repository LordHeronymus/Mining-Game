using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InstallScythePowerup
{
    public static string Apply()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene in Edit Mode.");

        var scythe = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Tools/Scythe.asset");
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>("Assets/GameObjects/Workbench/Recipes/Scythe.asset");
        var grass = UnityEngine.Object.FindFirstObjectByType<SurfaceTallGrass>(FindObjectsInactive.Include);
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include);
        if (!scythe || !recipe || !grass || !hud)
            throw new InvalidOperationException("Scythe, recipe, grass, or HUD is missing.");

        scythe.category = ItemCategory.Powerup;
        recipe.category = CraftingRecipe.RecipeCategory.Tools;
        EditorUtility.SetDirty(scythe);
        EditorUtility.SetDirty(recipe);

        var grassData = new SerializedObject(grass);
        grassData.FindProperty("scythePowerup").objectReferenceValue = scythe;
        grassData.ApplyModifiedProperties();

        Undo.RecordObject(hud, "Remove scythe from hotbar");
        for (int i = 0; i < hud.slots.Length; i++)
            if (hud.slots[i] == scythe) hud.slots[i] = null;
        EditorUtility.SetDirty(hud);

        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(grass.gameObject.scene);
        EditorSceneManager.SaveScene(grass.gameObject.scene);
        return "Scythe powerup linked; recipe retained under tools; hotbar cleared.";
    }
}
