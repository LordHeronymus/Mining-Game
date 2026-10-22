using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class WorkbenchBuilding : MonoBehaviour
{
    // Legacy "Enter" sign from the prefab; replaced at runtime by the shared gold EntrySign.
    [SerializeField] CanvasGroup entryButton;
    readonly HashSet<Collider2D> visitors = new();
    WorkbenchPanel panel;
    EntrySign entrySign;

    public EntrySign EntrySign => entrySign;

    void Awake()
    {
        panel = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        var legacyCanvas = entryButton ? entryButton.GetComponentInParent<Canvas>(true) : null;
        int layer = legacyCanvas ? legacyCanvas.gameObject.layer : gameObject.layer;
        if (legacyCanvas) legacyCanvas.gameObject.SetActive(false);
        var sprite = EntrySign.MainSprite(transform);
        if (sprite) entrySign = EntrySign.Create(transform, sprite, "Werkbank öffnen", Open, layer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMovement>()) visitors.Add(other);
    }

    void OnTriggerExit2D(Collider2D other) => visitors.Remove(other);

    void LateUpdate()
    {
        visitors.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
        if (entrySign) entrySign.Visible = visitors.Count > 0 && panel && !GameplayInputBlocker.IsBlocked;
    }

    public void Open()
    {
        if (visitors.Count == 0 || !panel || GameplayInputBlocker.IsBlocked) return;
        panel.ShowPanel(true);
        if (!panel.IsOpen) return;
        AudioManager.Instance?.Play(SoundType.UI_Click);
        AudioManager.Instance?.Play(SoundType.DoorOpen);
    }

    void OnDisable() => visitors.Clear();
}
