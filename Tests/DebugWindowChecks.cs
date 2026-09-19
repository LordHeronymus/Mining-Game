using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class DebugWindowChecks
{
    static void Check(bool result,string message) { if(!result) throw new Exception(message); }
    public static object Main()
    {
        var panel=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        if(!GameplayDebugPanel.IsOpen) panel.Toggle();
        var window=panel.GetComponent<GameplayDebugWindow>();
        var card=(RectTransform)panel.transform.Find("Card");
        var canvas=panel.GetComponentInParent<Canvas>();
        var originalSize=card.sizeDelta;
        var originalPosition=card.anchoredPosition;
        var e=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=new Vector2(700,700)};
        window.Begin(e,Vector2.zero);
        e.position+=new Vector2(80,40)*canvas.scaleFactor; window.Drag(e);
        Check(Vector2.Distance(card.anchoredPosition,originalPosition+new Vector2(80,40))<1,"Canvas-scaled dragging failed.");
        window.Begin(e,new Vector2(1,-1));
        e.position+=new Vector2(-420,300)*canvas.scaleFactor;window.Drag(e);
        Check(card.sizeDelta.x<originalSize.x && card.sizeDelta.y<originalSize.y,"Corner resizing failed.");
        var content=card.Find("WindowViewport/WindowContent");
        Check(((RectTransform)content.Find("LightingSection")).anchoredPosition.y < -300,"Narrow layout did not stack sections.");
        var scroll=card.GetComponentInChildren<ScrollRect>();
        Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition=0;
        Canvas.ForceUpdateCanvases();
        Check(scroll.content.rect.height>scroll.viewport.rect.height,"Small window must scroll.");
        Check(scroll.verticalScrollbar && scroll.verticalScrollbar.gameObject.activeSelf,"Scrollbar missing.");
        var size=card.sizeDelta;var pos=card.anchoredPosition;
        panel.Close();panel.Toggle();
        Check(card.sizeDelta==size && card.anchoredPosition==pos,"Reopening reset window geometry.");
        ScreenCapture.CaptureScreenshot("C:/Users/jlang/AppData/Local/Temp/DebugWindowSmall.png");
        return new {passed=true,drag=true,resize=true,stacked=true,scroll=true,reopen=true};
    }
}
