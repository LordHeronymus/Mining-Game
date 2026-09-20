using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class WorkbenchBuilding : MonoBehaviour
{
    [SerializeField] CanvasGroup entryButton;
    [SerializeField, Min(0)] float fadeDuration = .1f;
    readonly HashSet<Collider2D> visitors = new();
    WorkbenchPanel panel;

    void Awake()
    {
        panel = FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
        HideButton();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMovement>()) visitors.Add(other);
    }

    void OnTriggerExit2D(Collider2D other) => visitors.Remove(other);

    void LateUpdate()
    {
        visitors.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
        if (!entryButton) return;
        bool visible = visitors.Count > 0 && panel && !GameplayInputBlocker.IsBlocked;
        entryButton.alpha = Mathf.MoveTowards(entryButton.alpha, visible ? 1 : 0,
            fadeDuration > 0 ? Time.unscaledDeltaTime / fadeDuration : 1);
        entryButton.interactable = entryButton.blocksRaycasts = visible;
    }

    public void Open()
    {
        if (visitors.Count == 0 || !panel || GameplayInputBlocker.IsBlocked) return;
        panel.ShowPanel(true);
        if (!panel.IsOpen) return;
        HideButton();
        AudioManager.Instance?.Play(SoundType.UI_Click);
        AudioManager.Instance?.Play(SoundType.DoorOpen);
    }

    void OnDisable()
    {
        visitors.Clear();
        HideButton();
    }

    void HideButton()
    {
        if (!entryButton) return;
        entryButton.alpha = 0;
        entryButton.interactable = entryButton.blocksRaycasts = false;
    }
}
