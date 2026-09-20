using UnityEngine;

public class InfoPanel : MonoBehaviour
{
    public static InfoPanel Instance;

    [SerializeField] CanvasGroup panel;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GetComponent<CanvasGroup>().alpha = 1;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ShowPanel(bool show)
    {
        if (show)
        {
            panel.alpha = 1;
            panel.blocksRaycasts = true;
            panel.interactable = true;
        }
        else
        {
            panel.alpha = 0;
            panel.blocksRaycasts = false;
            panel.interactable = false;
        }
    }
}
