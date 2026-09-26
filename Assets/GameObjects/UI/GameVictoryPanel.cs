using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class GameVictoryPanel : MonoBehaviour
{
    static GameVictoryPanel current;
    CanvasGroup group;
    Button firstButton;

    public static void Show()
    {
        if (current) return;
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (!canvas) return;
        var root = new GameObject("Victory Panel", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        current = root.AddComponent<GameVictoryPanel>();
    }

    void Awake()
    {
        current = this;
        group = GetComponent<CanvasGroup>();
        Build();
        group.alpha = 1f;
        group.blocksRaycasts = group.interactable = true;
        GameplayInputBlocker.SetBlocked(this, true);
        Time.timeScale = 0f;
        if (EventSystem.current && firstButton) EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    void OnDestroy()
    {
        GameplayInputBlocker.SetBlocked(this, false);
        if (current == this) current = null;
    }

    void Build()
    {
        var root = (RectTransform)transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        var shade = MakeRect("Shade", root, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        shade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, .72f);

        var card = MakeRect("Card", root, Vector2.zero, new Vector2(720f, 390f));
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.gameObject.AddComponent<Image>().color = new Color32(36, 29, 48, 255);
        var title = MakeText("Title", card, new Vector2(0f, 92f), new Vector2(620f, 100f), 64f);
        title.text = "Gewonnen";
        firstButton = MakeButton("Retry", card, new Vector2(-152f, -110f), "Nochmal spielen", Restart);
        MakeButton("Menu", card, new Vector2(152f, -110f), "Zum Hauptmenü", ReturnToMainMenu);
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static TextMeshProUGUI MakeText(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = MakeRect(name, parent, position, size);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color32(255, 211, 108, 255);
        return text;
    }

    Button MakeButton(string name, Transform parent, Vector2 position, string label, UnityEngine.Events.UnityAction action)
    {
        var rect = MakeRect(name, parent, position, new Vector2(270f, 72f));
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(82, 60, 108, 255);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var text = MakeText("Label", rect, Vector2.zero, rect.sizeDelta, 25f);
        text.text = label;
        text.color = Color.white;
        text.raycastTarget = false;
        return button;
    }

    void Restart()
    {
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneIndex);
    }

    void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded("MainMenu"))
        {
            SceneManager.LoadScene("MainMenu");
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
