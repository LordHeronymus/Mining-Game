using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class NewGamePanel : MonoBehaviour
{
    RectTransform layout;
    TMP_InputField nameField;
    Button start;

    public static void Show(Transform parent)
    {
        if (!Application.isPlaying || FindFirstObjectByType<NewGamePanel>() || RunNavigation.IsTransitioning) return;
        var root = HomeUi.Panel("New Game Setup", parent);
        HomeUi.Stretch(root);
        root.gameObject.AddComponent<NewGamePanel>().Build();
    }
    void Build()
    {
        GameplayInputBlocker.SetBlocked(this, true);
        var shade = HomeUi.Image("Shade", transform, Vector2.zero, Vector2.zero);
        HomeUi.Stretch(shade.rectTransform); shade.color = new Color(0, 0, 0, .72f); shade.raycastTarget = true;
        layout = HomeUi.Rect("New Game Layout", transform, Vector2.zero, new Vector2(900, 600));
        HomeUi.Image("Board Backing", layout, Vector2.zero, new Vector2(800, 500)).color = new Color32(23, 12, 7, 255);
        HomeUi.Image("Board", layout, Vector2.zero, new Vector2(860, 560), "Panel");
        HomeUi.Label("Title", layout, "Neues Spiel", new Vector2(0, 180), new Vector2(680, 75), 48);
        HomeUi.Label("Name Label", layout, "Name", new Vector2(0, 68), new Vector2(660, 45), 30);
        var background = HomeUi.Image("Game Name", layout, new Vector2(0, 0), new Vector2(660, 76));
        background.color = new Color(.08f, .035f, .015f, .85f); background.raycastTarget = true;
        var viewport = HomeUi.Rect("Text Viewport", background.transform, Vector2.zero, new Vector2(614, 60));
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = HomeUi.Label("Text", viewport, "", Vector2.zero, new Vector2(604, 55), 30);
        text.richText = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        nameField = background.gameObject.AddComponent<TMP_InputField>();
        nameField.enabled = false;
        nameField.textViewport = viewport; nameField.textComponent = text;
        nameField.targetGraphic = background; nameField.characterLimit = 40;
        nameField.lineType = TMP_InputField.LineType.SingleLine;
        nameField.caretColor = new Color32(222, 174, 91, 255); nameField.customCaretColor = true;
        nameField.caretWidth = 2; nameField.caretBlinkRate = .8f; nameField.onFocusSelectAll = false;
        nameField.enabled = true;
        HomeUi.StyleInputField(nameField);
        HomeUi.Button("Cancel New Game", layout, "Abbrechen", new Vector2(-180, -162), new Vector2(310, 78), Close);
        start = HomeUi.Button("Start New Game", layout, "Starten", new Vector2(180, -162), new Vector2(310, 78), StartGame, true);
        nameField.onValueChanged.AddListener(_ => Refresh());
        Refresh(); HomeUi.Fit(layout, new Vector2(1020, 720));
        nameField.Select(); nameField.ActivateInputField();
    }
    void Refresh() => start.interactable = !string.IsNullOrWhiteSpace(nameField.text) && GameSaveSystem.NextFreeSlot() > 0 && !GameSaveSystem.IsBusy;
    void StartGame()
    {
        Refresh(); if (!start.interactable) return;
        RunNavigation.NewGame(nameField.text.Trim());
        if (RunNavigation.IsTransitioning) Close();
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }
    void Close() => Destroy(gameObject);
    void OnRectTransformDimensionsChange() => HomeUi.Fit(layout, new Vector2(1020, 720));
    void OnDestroy()
    {
        GameplayInputBlocker.SetBlocked(this, false);
        EventSystem.current?.SetSelectedGameObject(null);
    }
}
