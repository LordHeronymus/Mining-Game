using UnityEngine;

public sealed class GameplayDebugPanel : MonoBehaviour
{
    public static bool IsOpen => GpsRuntimePanel.IsOpen;
    void Awake()
    {
        var group=GetComponent<CanvasGroup>();
        if (group) { group.alpha=0; group.interactable=false; group.blocksRaycasts=false; }
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        enabled=false;
    }
    public void Toggle() { if (IsOpen) Close(); else GpsRuntimePanel.Open(); }
    public void Close() => GpsRuntimePanel.Close();
    public bool TryApplyAll() => GpsSettings.ValidateDocument(GpsSettings.Document,out _);
}
