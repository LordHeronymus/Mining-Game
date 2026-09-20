using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class WorkbenchBuildingSetup
{
    const string Folder = "Assets/GameObjects/Map/Workbench";

    [MenuItem("Tools/Workbench/Install Building")]
    public static string Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Use Edit Mode.");
        var shop = Object.FindFirstObjectByType<ShopBuilding>();
        var panel = Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        if (!shop || !panel) throw new System.InvalidOperationException("Shop and WorkbenchPanel required.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameObjects/Map", "Workbench");
        string path = Folder + "/Workshop.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(path))
        {
            string error = AssetDatabase.MoveAsset("Assets/ZZZ New Assets/Workshop.png", path);
            if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException(error);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 204.8f;
        importer.maxTextureSize = 2048;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(.5f, .075f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var existing = shop.transform.parent.Find("Workshop");
        if (existing) throw new System.InvalidOperationException("Workshop already exists; edit the existing object.");
        var root = new GameObject("Workshop");
        Undo.RegisterCreatedObjectUndo(root, "Create workshop");
        root.transform.SetParent(shop.transform.parent, false);
        root.transform.position = new Vector3(13.5f, .45f, 0);
        var visual = new GameObject("Sprite", typeof(SpriteRenderer));
        visual.transform.SetParent(root.transform, false);
        var sprite = visual.GetComponent<SpriteRenderer>();
        var shopSprite = shop.transform.Find("Sprite").GetComponent<SpriteRenderer>();
        sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        sprite.sharedMaterial = shopSprite.sharedMaterial;
        sprite.sortingLayerID = shopSprite.sortingLayerID;
        sprite.sortingOrder = shopSprite.sortingOrder;
        var trigger = root.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(5.8f, 4.5f);
        trigger.offset = new Vector2(0, 2.1f);
        var building = root.AddComponent<WorkbenchBuilding>();
        var canvas = Object.Instantiate(shop.transform.Find("EnterButtonCanvas").gameObject, root.transform);
        canvas.name = "EnterButtonCanvas";
        canvas.transform.localPosition = new Vector3(0, .77f, 0);
        canvas.GetComponent<Canvas>().sortingLayerName = "UI";
        canvas.GetComponent<Canvas>().worldCamera = Camera.main;
        var button = canvas.GetComponentInChildren<Button>(true);
        button.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(button.onClick, building.Open);
        button.targetGraphic = button.GetComponent<Image>();
        var group = button.GetComponent<CanvasGroup>();
        group.alpha = 0; group.interactable = group.blocksRaycasts = false;
        var data = new SerializedObject(building);
        data.FindProperty("entryButton").objectReferenceValue = group;
        data.ApplyModifiedPropertiesWithoutUndo();
        Undo.RecordObject(panel, "Use workshop entrance");
        panel.allowKeyboardOpen = false;
        EditorUtility.SetDirty(panel);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, Folder + "/Workshop.prefab", InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        return "Workshop installed at x=13.5 with proximity button and prefab.";
    }
}
