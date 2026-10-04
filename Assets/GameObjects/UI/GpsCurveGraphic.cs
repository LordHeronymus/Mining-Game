using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Curve keys can be dragged; double click adds a key, right click removes one.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GpsCurveGraphic : MaskableGraphic, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    GpsValue value;
    Action changed;
    int selected = -1;
    float maximum = 1, lastClick;
    public Action<int> KeySelected;
    public void Bind(GpsValue node, int startDepth, int endDepth, Action commit)
    {
        value = node; changed = commit; maximum = 1; color = new Color(.8f, .66f, .35f); raycastTarget = true;
        foreach (var key in node.curve.keys) maximum = Mathf.Max(maximum, key.value * 1.1f);
        SetVerticesDirty();
    }
    Vector2 Point(float time, float amount) => new(Mathf.Lerp(rectTransform.rect.xMin, rectTransform.rect.xMax, time), Mathf.Lerp(rectTransform.rect.yMin, rectTransform.rect.yMax, amount / maximum));
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if (value?.curve == null) return;
        var rect = rectTransform.rect;
        Quad(mesh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Color(.055f,.06f,.07f,1));
        for (int i = 0; i <= 10; i++) Line(mesh, Point(i / 10f, 0), Point(i / 10f, maximum), new Color(.3f,.32f,.35f,.5f), 1);
        for (int i = 0; i <= 4; i++) Line(mesh, Point(0, i * maximum / 4), Point(1, i * maximum / 4), new Color(.3f,.32f,.35f,.5f), 1);
        var curve = value.curve.ToCurve(); Vector2 previous = Point(0, Mathf.Clamp(curve.Evaluate(0), 0, maximum));
        for (int i = 1; i <= 200; i++) { var next = Point(i / 200f, Mathf.Clamp(curve.Evaluate(i / 200f), 0, maximum)); Line(mesh, previous, next, color, 2); previous = next; }
        foreach (var key in value.curve.keys) { var point = Point(key.time, key.value); Quad(mesh, point - Vector2.one * 5, point + Vector2.one * 5, color); }
    }
    static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Color tint)
    {
        int count = mesh.currentVertCount;
        mesh.AddVert(new Vector3(a.x,a.y), tint, Vector2.zero); mesh.AddVert(new Vector3(a.x,b.y), tint, Vector2.zero);
        mesh.AddVert(new Vector3(b.x,b.y), tint, Vector2.zero); mesh.AddVert(new Vector3(b.x,a.y), tint, Vector2.zero);
        mesh.AddTriangle(count,count+1,count+2); mesh.AddTriangle(count,count+2,count+3);
    }
    static void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color tint, float width)
    {
        var normal = new Vector2(-(b-a).y,(b-a).x).normalized * width / 2; int count = mesh.currentVertCount;
        mesh.AddVert(a-normal,tint,Vector2.zero); mesh.AddVert(a+normal,tint,Vector2.zero); mesh.AddVert(b+normal,tint,Vector2.zero); mesh.AddVert(b-normal,tint,Vector2.zero);
        mesh.AddTriangle(count,count+1,count+2); mesh.AddTriangle(count,count+2,count+3);
    }
    bool Local(PointerEventData e, out Vector2 point) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out point);
    public void OnPointerDown(PointerEventData e)
    {
        if (value?.curve == null || !Local(e,out var point)) return;
        selected = -1; float best = 18;
        for (int i=0;i<value.curve.keys.Count;i++) { float distance = Vector2.Distance(point,Point(value.curve.keys[i].time,value.curve.keys[i].value)); if (distance<best) { best=distance; selected=i; } }
        if (selected >= 0) KeySelected?.Invoke(selected);
        if (e.button == PointerEventData.InputButton.Right)
        { if (selected>=0 && value.curve.keys.Count>1) { value.curve.keys.RemoveAt(selected); changed(); } selected=-1; SetVerticesDirty(); return; }
        if (selected<0 && Time.unscaledTime-lastClick<.35f)
        {
            var rect = rectTransform.rect; value.curve.keys.Add(new GpsCurve.Key { time=Mathf.Clamp01((point.x-rect.xMin)/rect.width),value=Mathf.Clamp01((point.y-rect.yMin)/rect.height)*maximum });
            value.curve.keys.Sort((a,b)=>a.time.CompareTo(b.time)); changed(); SetVerticesDirty();
        }
        lastClick=Time.unscaledTime;
    }
    public void OnDrag(PointerEventData e)
    {
        if (selected<0 || !Local(e,out var point)) return;
        var rect = rectTransform.rect; var key=value.curve.keys[selected];
        float min=selected>0 ? value.curve.keys[selected-1].time+.0001f : 0;
        float max=selected+1<value.curve.keys.Count ? value.curve.keys[selected+1].time-.0001f : 1;
        key.time=Mathf.Clamp((point.x-rect.xMin)/rect.width,min,max); key.value=Mathf.Clamp01((point.y-rect.yMin)/rect.height)*maximum;
        value.curve.keys[selected]=key; changed(); SetVerticesDirty();
    }
    public void OnPointerUp(PointerEventData e) => selected=-1;
}
