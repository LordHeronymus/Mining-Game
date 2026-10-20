using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class ShopBuilding : MonoBehaviour
{
    // Legacy "Enter" sign from the scene; replaced at runtime by the shared gold EntrySign,
    // which forwards its click to the legacy button so the scene wiring (ShopPanel.ShowPanel) stays.
    [SerializeField] private CanvasGroup panel;

    readonly HashSet<Collider2D> visitors = new();
    EntrySign entrySign;
    ShopPanel shopPanel;

    public EntrySign EntrySign => entrySign;

    static bool IsPlayer(Collider2D other) => other && other.GetComponentInParent<PlayerMovement>();

    void Awake()
    {
        var legacyButton = panel ? panel.GetComponent<Button>() : null;
        var legacyCanvas = panel ? panel.GetComponentInParent<Canvas>(true) : null;
        int layer = legacyCanvas ? legacyCanvas.gameObject.layer : gameObject.layer;
        if (legacyCanvas) legacyCanvas.gameObject.SetActive(false);
        shopPanel = FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
        var sprite = EntrySign.MainSprite(transform);
        if (sprite) entrySign = EntrySign.Create(transform, sprite, "Shop öffnen",
            () => { if (visitors.Count > 0 && !GameplayInputBlocker.IsBlocked && legacyButton) legacyButton.onClick.Invoke(); }, layer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPlayer(other)) visitors.Add(other);
    }

    void OnTriggerExit2D(Collider2D other) => visitors.Remove(other);

    void LateUpdate()
    {
        visitors.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
        if (entrySign) entrySign.Visible = visitors.Count > 0 && !GameplayInputBlocker.IsBlocked && !(shopPanel && shopPanel.IsOpen);
    }

    void OnDisable() => visitors.Clear();
}
