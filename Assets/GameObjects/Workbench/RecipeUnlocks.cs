using UnityEngine;

public static class RecipeUnlocks
{
    const string MedkitKey = "workbench.recipe.medkit.unlocked";
    public const int MedkitPrice = 150;

    public static bool IsUnlocked(CraftingRecipe recipe) =>
        !recipe || !recipe.output || recipe.output.item != Item.Medkit || PlayerPrefs.GetInt(MedkitKey, 0) == 1;

    public static bool IsMedkitUnlocked => PlayerPrefs.GetInt(MedkitKey, 0) == 1;

    public static void UnlockMedkit()
    {
        PlayerPrefs.SetInt(MedkitKey, 1);
        PlayerPrefs.Save();
    }
}
