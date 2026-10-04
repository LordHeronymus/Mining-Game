using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owns its overlay so scrolling, rebuilding, closing and domain reload cannot leave stale tips.
public sealed class ExoticBlueprintTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    public ExoticBlueprintInfo Entry { get; private set; }
    RectTransform tip;
    Canvas canvas;
    Vector2 pointer;
    public static void Attach(GameObject target, ExoticBlueprintInfo entry)
    {
        var graphic=target.GetComponent<Graphic>();
        if(!graphic){var hit=target.AddComponent<Image>();hit.color=Color.clear;graphic=hit;}
        graphic.raycastTarget=true;
        var handler=target.AddComponent<ExoticBlueprintTooltip>();handler.Entry=entry;
    }
    public void OnPointerEnter(PointerEventData e)
    {
        Hide(); if(!Application.isPlaying || Entry==null)return;
        canvas=GetComponentInParent<Canvas>()?.rootCanvas;if(!canvas)return;
        pointer=e.position;
        tip=HomeUi.Rect("Exotic Blueprint Tooltip",canvas.transform,Vector2.zero,new Vector2(420,220));
        var group=tip.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
        var overlay=tip.gameObject.AddComponent<Canvas>();overlay.overrideSorting=true;overlay.sortingOrder=32000;
        HomeUi.Image("Backing",tip,Vector2.zero,new Vector2(410,210)).color=new Color32(20,13,9,255);
        var frame=HomeUi.Image("Frame",tip,Vector2.zero,tip.sizeDelta,"SelectionCardNormal");frame.pixelsPerUnitMultiplier=5.5f;
        var title=ProgressionArt.Text("Name",tip,Entry.Name,new Vector2(0,74),new Vector2(365,40),29);title.color=ExoticDesign.Cyan;
        var level=ProgressionArt.Text("Level",tip,"Level "+Entry.Level+(Entry.Preview?" · Vorschau":" · Exotisch"),new Vector2(0,34),new Vector2(365,28),21);level.color=ProgressionArt.Gold;
        var description=ProgressionArt.Text("Description",tip,Entry.Description,new Vector2(0,-43),new Vector2(360,110),23);
        description.textWrappingMode=TextWrappingModes.Normal;description.alignment=TextAlignmentOptions.TopLeft;description.richText=false;
        Position();
    }
    void Position()
    {
        if(!tip||!canvas)return;
        var root=(RectTransform)canvas.transform;
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,pointer,camera,out var p);
        float halfW=tip.rect.width*.5f,halfH=tip.rect.height*.5f;
        p+=new Vector2(halfW+20,-halfH-20);
        p.x=Mathf.Clamp(p.x,root.rect.xMin+halfW+8,root.rect.xMax-halfW-8);
        p.y=Mathf.Clamp(p.y,root.rect.yMin+halfH+8,root.rect.yMax-halfH-8);
        tip.localPosition=new Vector3(p.x,p.y,0);
    }
    void Update(){if(tip){pointer=Input.mousePosition;Position();}}
    public void OnPointerExit(PointerEventData e)=>Hide();
    public void OnScroll(PointerEventData e)
    {
        Hide();
        // The hover target must not swallow scrolling intended for the timeline or collection.
        if(transform.parent)ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject,e,ExecuteEvents.scrollHandler);
    }
    void Hide(){if(tip){tip.gameObject.SetActive(false);Destroy(tip.gameObject);tip=null;}}
    void OnDisable()=>Hide();
    void OnDestroy()=>Hide();
}
