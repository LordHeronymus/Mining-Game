using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

public static class SetupInventoryDesign
{
    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Use Edit Mode");
        MakeTile("InventoryTile", false);
        MakeTile("InventorySelection", true);
        const string path = "Assets/UI/Inventory/InventoryFrame.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100; importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048; importer.SaveAndReimport();
        var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var workbench = UnityEngine.Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var oldParent = ui.transform.parent;
        Undo.RegisterFullObjectHierarchyUndo(oldParent.gameObject, "Inventory design");
        if (oldParent.name == "Inventory")
        {
            ui.transform.SetParent(oldParent.parent, false);
            Undo.DestroyObjectImmediate(oldParent.gameObject);
        }
        ui.name = "InventoryUI";
        while (ui.transform.childCount > 0) Undo.DestroyObjectImmediate(ui.transform.GetChild(0).gameObject);
        if (ui.GetComponent<ScrollRect>()) Undo.DestroyObjectImmediate(ui.GetComponent<ScrollRect>());
        if (ui.GetComponent<Image>()) Undo.DestroyObjectImmediate(ui.GetComponent<Image>());
        var rect = (RectTransform)ui.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        rect.pivot = new Vector2(.5f, .5f); ui.gameObject.layer = LayerMask.NameToLayer("UI");
        var group = ui.GetComponent<CanvasGroup>(); if (!group) group = ui.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0; group.interactable = group.blocksRaycasts = false;
        ui.frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        ui.slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Inventory/InventoryTile.png");
        ui.selectionSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Inventory/InventorySelection.png");
        ui.badgeSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Shop/OreCountBadge.png");
        ui.rowSprite = workbench.rowSprite; ui.actionSprite = workbench.actionSprite; ui.font = workbench.font;
        const string materialPath = "Assets/UI/Inventory/InventoryFont.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material) { material = new Material(workbench.fontMaterial); AssetDatabase.CreateAsset(material, materialPath); }
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.09f,.045f,.025f,1));
        material.SetFloat(ShaderUtilities.ID_OutlineWidth,.12f); material.EnableKeyword("OUTLINE_ON");
        ui.fontMaterial = material;
        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
        return "Inventory configured: live scene backdrop, grid design, shared shop assets.";
    }
    static void MakeTile(string name, bool selection)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            // Rounded rectangle signed distance, with a fine bevel and optional outer glow.
            var q = new Vector2(Mathf.Abs(x - 63.5f) - 53.5f, Mathf.Abs(y - 63.5f) - 53.5f);
            float d = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - 6;
            Color c;
            if (selection)
            {
                float glow = Mathf.Exp(-Mathf.Abs(d) * .65f) * .5f;
                float core = 1 - Mathf.Clamp01((Mathf.Abs(d) - .9f) / .8f);
                c = Color.Lerp(new Color(1,.53f,.06f,glow), new Color(1,.77f,.29f,1), core);
            }
            else
            {
                c = Color.Lerp(new Color(.15f,.09f,.055f), new Color(.25f,.16f,.10f), y / 127f);
                if (d > -2.2f) c = Color.Lerp(new Color(.37f,.25f,.17f),new Color(.62f,.44f,.29f),y / 127f);
                c.a = 1 - Mathf.Clamp01(d + .4f);
            }
            pixels[y * size + x] = c;
        }
        texture.SetPixels(pixels); texture.Apply();
        string path = "Assets/UI/Inventory/" + name + ".png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = Vector4.zero; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }
}
