using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public sealed class ItemCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
{
    const string Path = "Assets/Resources/ItemCatalog.asset";
    public int callbackOrder => 0;

    static ItemCatalogBuilder() => EditorApplication.delayCall += Refresh;

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    {
        if (imported.Concat(deleted).Concat(moved).Any(p => p != Path && p.EndsWith(".asset")))
            EditorApplication.delayCall += Refresh;
    }

    public void OnPreprocessBuild(BuildReport report) => Refresh();

    public static void Refresh()
    {
        var entries = AssetDatabase.FindAssets("t:ItemSO", new[] { "Assets" })
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i).OrderBy(i => i.category).ThenBy(i => i.displayName).ThenBy(i => (int)i.item).ToArray();
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Path);
        if (!catalog)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AssetDatabase.CreateAsset(catalog, Path);
        }
        if (catalog.items.SequenceEqual(entries)) return;
        catalog.items = entries;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
    }
}
