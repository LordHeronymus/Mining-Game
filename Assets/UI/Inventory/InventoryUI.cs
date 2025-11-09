using System.Linq;
using UnityEngine;
using System.Collections;

public class InventoryUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform content;           
    [SerializeField] private InventorySlot slotPrefab;  
    [SerializeField] private CanvasGroup panel;           
    [SerializeField] float fadeDuration = 0.1f;

    bool panelVisible = false;
    bool subscribed = false;
    Coroutine fadeCo, subCo;

    private void Awake()
    {
        subCo = StartCoroutine(TrySubscribe());
        Rebuild();
    }

    void OnEnable()
    {
        if (subCo == null) subCo = StartCoroutine(TrySubscribe());
    }

    void OnDisable()
    {
        if (InventoryManager.Instance) InventoryManager.Instance.OnInventoryChanged -= Rebuild;
        subscribed = false;
    }

    void Update()
    {
        if (!panelVisible && Input.GetKeyDown(KeyCode.I)) { ShowPanel(); return; }
        if (panelVisible && (Input.GetKeyDown(KeyCode.I) 
                          || Input.GetKeyDown(KeyCode.Escape))) HidePanel();
    }

    IEnumerator TrySubscribe()
    {
        while (!subscribed)
        {
            var inv = InventoryManager.Instance;
            yield return null;
            if (inv == null) continue;

            inv.OnInventoryChanged += Rebuild;
            subscribed = true;
        }
    }

    private void Rebuild()
    {
        if (!content || InventoryManager.Instance == null) return;
        ClearChildren();

        var snap = InventoryManager.Instance.GetSnapshot();
        foreach (var kv in snap.Where(k => k.Key != null).OrderBy(k => k.Key.displayName))
        {
            var slot = Instantiate(slotPrefab, content);
            slot.Set(kv.Key.icon, kv.Value);
            slot.name = $"Slot_{kv.Key.displayName}";
        }
    }

    private void ClearChildren()
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    public void ShowPanel()
    {
        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadePanel(true));
    }

    public void HidePanel()
    {
        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadePanel(false));
    }

    public IEnumerator FadePanel(bool enabled)
    {
        panelVisible = enabled;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            panel.alpha = enabled
                ? Mathf.Clamp01(elapsed / fadeDuration)
                : 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        panel.alpha = enabled ? 1f : 0f;
        panel.blocksRaycasts = enabled;
        panel.interactable = enabled;
    }
}
