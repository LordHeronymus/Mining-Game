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

        var root = shop.transform.parent.Find("Workshop");
        bool created = !root;
        if (created)
        {
            root = new GameObject("Workshop").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create workshop");
            root.SetParent(shop.transform.parent, false);
            root.position = new Vector3(13.5f, .45f, 0);
            var visual = new GameObject("Sprite", typeof(SpriteRenderer));
            visual.transform.SetParent(root, false);
            var sprite = visual.GetComponent<SpriteRenderer>();
            var shopSprite = shop.transform.Find("Sprite").GetComponent<SpriteRenderer>();
            sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            sprite.sharedMaterial = shopSprite.sharedMaterial;
            sprite.sortingLayerID = shopSprite.sortingLayerID;
            sprite.sortingOrder = shopSprite.sortingOrder;
            var trigger = root.gameObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(5.8f, 4.5f);
            trigger.offset = new Vector2(0, 2.1f);
            root.gameObject.AddComponent<WorkbenchBuilding>();
        }
        var building = root.GetComponent<WorkbenchBuilding>();
        var shopCanvas = shop.transform.Find("EnterButtonCanvas");
        var canvasTransform = root.Find("EnterButtonCanvas");
        var canvas = canvasTransform ? canvasTransform.gameObject : Object.Instantiate(shopCanvas.gameObject, root);
        canvas.name = "EnterButtonCanvas";
        var shopButtonWorld = shopCanvas.GetComponentInChildren<Button>(true).transform.position;
        var workshopButtonWorld = new Vector3(root.position.x, shopButtonWorld.y, root.position.z);
        canvas.transform.localPosition = root.InverseTransformPoint(workshopButtonWorld);
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
        PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, Folder + "/Workshop.prefab", InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        Selection.activeGameObject = root.gameObject;
        return created ? "Workshop installed with a shop-aligned entrance button." : "Workshop entrance button aligned to the shop.";
    }
}
