using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// This theme belongs to the Editor GPS only; shared settings and runtime UI remain independent.
public sealed class GpsEditorTheme : IDisposable
{
    public static readonly Color Background=new(.105f,.115f,.12f), Brass=new(.78f,.61f,.32f), Ink=new(.88f,.88f,.85f);
    public static readonly Color Plot=new(.065f,.075f,.085f), Grid=new(.28f,.30f,.32f,.55f);
    readonly List<Texture2D> textures=new();
    public readonly GUIStyle Title, Label, Small, Card, Navigation, Header, Button, Primary, Tab, ActiveTab, Field, Popup, ToggleOn, ToggleOff, ScrollView, VerticalScrollbar, HorizontalScrollbar;
    readonly GUISkin skin;
    public GpsEditorTheme()
    {
        Title=new GUIStyle(EditorStyles.largeLabel){fontSize=21,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};Title.normal.textColor=Ink;
        Label=new GUIStyle(EditorStyles.label){fontSize=13,alignment=TextAnchor.MiddleLeft};Label.normal.textColor=Ink;
        Small=new GUIStyle(Label){fontSize=11};Small.normal.textColor=new Color(.62f,.65f,.66f);
        Card=Surface(new Color(.145f,.157f,.166f),new Color(.23f,.25f,.26f));Card.padding=new RectOffset(14,14,10,12);Card.margin=new RectOffset(0,0,0,10);
        Navigation=Surface(new Color(.095f,.105f,.113f),new Color(.20f,.22f,.23f));Navigation.padding=new RectOffset(10,10,10,10);
        Header=Interactive(new Color(.18f,.195f,.205f),new Color(.28f,.30f,.31f));Header.alignment=TextAnchor.MiddleLeft;Header.fontStyle=FontStyle.Bold;Header.padding.left=34;Header.fixedHeight=34;
        Button=Interactive(new Color(.16f,.175f,.185f),new Color(.30f,.32f,.33f));Button.padding=new RectOffset(14,14,4,4);Button.fixedHeight=30;
        Primary=Interactive(new Color(.20f,.19f,.155f),Brass);Primary.padding=new RectOffset(40,14,4,4);Primary.fixedHeight=34;
        Tab=new GUIStyle(Button){fixedHeight=44,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(44,10,4,4)};
        ActiveTab=Interactive(new Color(.235f,.225f,.19f),Brass);ActiveTab.fixedHeight=44;ActiveTab.padding=new RectOffset(44,10,4,4);ActiveTab.alignment=TextAnchor.MiddleLeft;
        Field=new GUIStyle(EditorStyles.textField){fontSize=13,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,8,3,3),border=new RectOffset(7,7,7,7)};
        Field.normal.background=Texture(new Color(.08f,.09f,.10f),new Color(.245f,.265f,.28f));Field.normal.textColor=Ink;
        Field.hover.background=Texture(new Color(.105f,.115f,.125f),new Color(.38f,.37f,.32f));Field.hover.textColor=Ink;
        Field.focused.background=Texture(new Color(.11f,.12f,.13f),Brass);Field.focused.textColor=Ink;Field.active=Field.focused;
        Popup=new GUIStyle(Button){fixedHeight=28,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,27,3,3),fontStyle=FontStyle.Normal};
        ToggleOn=new GUIStyle(ActiveTab){fixedHeight=26,padding=new RectOffset(12,12,2,2),alignment=TextAnchor.MiddleCenter};
        ToggleOff=new GUIStyle(Button){fixedHeight=26,padding=new RectOffset(12,12,2,2),alignment=TextAnchor.MiddleCenter};
        skin=UnityEngine.Object.Instantiate(GUI.skin);skin.hideFlags=HideFlags.HideAndDontSave;
        skin.button=new GUIStyle(Button);skin.label=new GUIStyle(Label);skin.textField=new GUIStyle(Field);skin.box=new GUIStyle(Card);
        skin.verticalScrollbar=new GUIStyle(GUI.skin.verticalScrollbar);skin.verticalScrollbar.normal.background=Texture(new Color(.07f,.08f,.085f),new Color(.14f,.16f,.17f));skin.verticalScrollbar.fixedWidth=12;
        skin.verticalScrollbarThumb=new GUIStyle(GUI.skin.verticalScrollbarThumb);skin.verticalScrollbarThumb.normal.background=Texture(new Color(.35f,.33f,.27f),new Color(.48f,.43f,.33f));skin.verticalScrollbarThumb.hover.background=Texture(new Color(.47f,.41f,.30f),Brass);skin.verticalScrollbarThumb.active=skin.verticalScrollbarThumb.hover;
        skin.horizontalScrollbar=new GUIStyle(GUI.skin.horizontalScrollbar);skin.horizontalScrollbar.normal.background=skin.verticalScrollbar.normal.background;
        skin.horizontalScrollbarThumb=new GUIStyle(GUI.skin.horizontalScrollbarThumb);skin.horizontalScrollbarThumb.normal.background=skin.verticalScrollbarThumb.normal.background;
        VerticalScrollbar=skin.verticalScrollbar;HorizontalScrollbar=skin.horizontalScrollbar;ScrollView=GUIStyle.none;
    }
    GUIStyle Surface(Color fill,Color border)
    {
        var style=new GUIStyle{border=new RectOffset(7,7,7,7)};style.normal.background=Texture(fill,border);return style;
    }
    GUIStyle Interactive(Color fill,Color border)
    {
        var style=Surface(fill,border);style.font=EditorStyles.label.font;style.fontSize=13;style.alignment=TextAnchor.MiddleCenter;style.normal.textColor=Ink;
        style.hover.background=Texture(fill+new Color(.04f,.04f,.035f,0),Color.Lerp(border,Brass,.45f));style.hover.textColor=Color.white;
        style.active.background=Texture(fill-new Color(.025f,.025f,.025f,0),Brass);style.active.textColor=Ink;style.focused=style.hover;
        return style;
    }
    Texture2D Texture(Color fill,Color border)
    {
        const int width=64,height=48;const float radius=6;
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};
        var pixels=new Color[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            float px=x+.5f,py=y+.5f;
            float dx=Mathf.Max(Mathf.Max(radius-px,px-(width-radius)),0),dy=Mathf.Max(Mathf.Max(radius-py,py-(height-radius)),0);
            float edge=Mathf.Min(Mathf.Min(px,width-px),Mathf.Min(py,height-py));
            if(dx>0 && dy>0)edge=radius-Mathf.Sqrt(dx*dx+dy*dy);
            Color tint=Color.Lerp(fill*.91f,fill*1.06f,(float)y/(height-1));tint.a=1;
            tint=Color.Lerp(border,tint,Mathf.Clamp01(edge-1));tint.a=Mathf.Clamp01(edge+.25f);pixels[y*width+x]=tint;
        }
        texture.SetPixels(pixels);texture.Apply(false,true);textures.Add(texture);return texture;
    }
    public IDisposable Scope()=>new SkinScope(skin);
    sealed class SkinScope:IDisposable
    {
        readonly GUISkin before;public SkinScope(GUISkin skin){before=GUI.skin;GUI.skin=skin;}public void Dispose()=>GUI.skin=before;
    }
    public bool Foldout(bool open,string label)
    {
        var rect=EditorGUI.IndentedRect(GUILayoutUtility.GetRect(new GUIContent(label),Header,GUILayout.ExpandWidth(true)));
        bool click=GUI.Button(rect,label,Header);Chevron(new Rect(rect.x+13,rect.y+11,12,12),open?90:0,open?Brass:Ink);
        return click?!open:open;
    }
    public void Row(string label,out Rect control)
    {
        var row=EditorGUI.IndentedRect(GUILayoutUtility.GetRect(1,34,GUILayout.ExpandWidth(true)));
        float labelWidth=Mathf.Min(EditorGUIUtility.labelWidth,row.width*.48f);
        GUI.Label(new Rect(row.x,row.y,labelWidth-12,row.height),label,Label);
        control=new Rect(row.x+labelWidth,row.y+3,row.width-labelWidth,28);
    }
    public bool Check(string label,bool value)
    {
        Row(label,out var rect);rect.width=66;
        if(GUI.Button(rect,value?"An":"Aus",value?ToggleOn:ToggleOff)){value=!value;GUI.changed=true;}return value;
    }
    public int Select(Rect rect,int index,string[] options)
    {
        int selected=EditorGUI.Popup(rect,index,options,Popup);Chevron(new Rect(rect.xMax-18,rect.y+9,10,10),90,Ink);return selected;
    }
    public void PaintControl(Rect rect){if(Event.current.type==EventType.Repaint)Field.Draw(rect,GUIContent.none,false,false,false,false);}
    public UnityEngine.Object AssetField(Rect rect,UnityEngine.Object value,Type type)
    {
        int id=GUIUtility.GetControlID("GPSAsset".GetHashCode(),FocusType.Keyboard,rect);
        var style=new GUIStyle(Popup){padding=new RectOffset(34,27,3,3)};
        if(GUI.Button(rect,value?value.name:"—",style))EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(value,false,"t:"+type.Name,id);
        if(Event.current.type==EventType.Repaint)
        {
            if(value){var thumbnail=AssetPreview.GetMiniThumbnail(value);if(thumbnail)GUI.DrawTexture(new Rect(rect.x+8,rect.y+5,18,18),thumbnail,ScaleMode.ScaleToFit);}
            var before=Handles.color;Handles.BeginGUI();Handles.color=Ink;Handles.DrawWireDisc(new Vector3(rect.xMax-14,rect.center.y),Vector3.forward,4,1.5f);Handles.EndGUI();Handles.color=before;
        }
        var evt=Event.current;
        if(evt.commandName=="ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID()==id)
        {
            var selected=EditorGUIUtility.GetObjectPickerObject();
            if(!selected || type.IsInstanceOfType(selected)){if(selected!=value){value=selected;GUI.changed=true;}}
            evt.Use();
        }
        if(rect.Contains(evt.mousePosition) && (evt.type==EventType.DragUpdated || evt.type==EventType.DragPerform))
        {
            var candidate=Array.Find(DragAndDrop.objectReferences,asset=>asset && type.IsInstanceOfType(asset));
            if(candidate){DragAndDrop.visualMode=DragAndDropVisualMode.Copy;if(evt.type==EventType.DragPerform){DragAndDrop.AcceptDrag();value=candidate;GUI.changed=true;}evt.Use();}
        }
        return value;
    }
    public void DrawTab(Rect rect,string caption,bool active)
    {
        if(Event.current.type!=EventType.Repaint)return;
        Icon(caption,new Rect(rect.x+14,rect.y+12,20,20),active?new Color(.91f,.83f,.66f):new Color(.7f,.73f,.73f));
        if(active)EditorGUI.DrawRect(new Rect(rect.x+8,rect.yMax-3,rect.width-16,2),Brass);
    }
    public static void Chevron(Rect rect,float degrees,Color color)
    {
        if(Event.current.type!=EventType.Repaint)return;var before=Handles.color;Handles.color=color;Handles.BeginGUI();
        Vector2 center=rect.center;float r=degrees*Mathf.Deg2Rad;
        Vector3 Turn(float x,float y)=>center+new Vector2(x*Mathf.Cos(r)-y*Mathf.Sin(r),x*Mathf.Sin(r)+y*Mathf.Cos(r));
        Handles.DrawAAPolyLine(2,Turn(-2,-4),Turn(2,0),Turn(-2,4));Handles.EndGUI();Handles.color=before;
    }
    public static void Icon(string name,Rect rect,Color color)
    {
        if(Event.current.type!=EventType.Repaint)return;
        var before=Handles.color;Handles.BeginGUI();Handles.color=color;
        Vector3 P(float x,float y)=>new(rect.x+x*rect.width,rect.y+y*rect.height);
        void Line(params Vector2[] points){var vertices=new Vector3[points.Length];for(int i=0;i<points.Length;i++)vertices[i]=P(points[i].x,points[i].y);Handles.DrawAAPolyLine(1.8f,vertices);}
        void Dot(float x,float y,float size)=>Handles.DrawSolidDisc(P(x,y),Vector3.forward,rect.width*size);
        void Circle(float x,float y,float size)=>Handles.DrawWireDisc(P(x,y),Vector3.forward,rect.width*size,1.8f);
        Vector2 V(float x,float y)=>new(x,y);
        switch(name)
        {
            case "Spieler":Dot(.5f,.25f,.17f);Line(V(.18f,.9f),V(.18f,.68f),V(.32f,.52f),V(.68f,.52f),V(.82f,.68f),V(.82f,.9f),V(.18f,.9f));break;
            case "Bewegung":Dot(.65f,.13f,.12f);Line(V(.55f,.3f),V(.42f,.55f),V(.66f,.65f),V(.58f,.95f));Line(V(.52f,.34f),V(.3f,.31f),V(.13f,.51f));Line(V(.47f,.5f),V(.25f,.79f),V(.05f,.8f));Line(V(.54f,.31f),V(.75f,.48f),V(.93f,.46f));break;
            case "Upgrades":Line(V(.08f,.8f),V(.35f,.52f),V(.51f,.68f),V(.9f,.2f),V(.9f,.51f));Line(V(.9f,.2f),V(.6f,.2f));break;
            case "Map":Line(V(.08f,.2f),V(.34f,.08f),V(.65f,.23f),V(.92f,.1f),V(.92f,.83f),V(.65f,.96f),V(.34f,.81f),V(.08f,.93f),V(.08f,.2f));Line(V(.34f,.08f),V(.34f,.81f));Line(V(.65f,.23f),V(.65f,.96f));break;
            case "Erzverteilung":Line(V(.06f,.87f),V(.37f,.22f),V(.59f,.58f),V(.73f,.32f),V(.97f,.87f),V(.06f,.87f));Line(V(.27f,.43f),V(.38f,.54f),V(.48f,.42f));break;
            case "Partikel":Dot(.5f,.5f,.17f);Dot(.18f,.15f,.07f);Dot(.83f,.22f,.09f);Dot(.2f,.81f,.08f);Dot(.81f,.84f,.065f);break;
            case "Licht":Circle(.5f,.5f,.23f);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Line(V(.5f+Mathf.Cos(a)*.35f,.5f+Mathf.Sin(a)*.35f),V(.5f+Mathf.Cos(a)*.48f,.5f+Mathf.Sin(a)*.48f));}break;
            case "Werkbank":Line(V(.15f,.9f),V(.64f,.4f),V(.45f,.22f),V(.56f,.11f),V(.91f,.43f),V(.8f,.55f),V(.64f,.4f));break;
            case "Shop":Line(V(.02f,.13f),V(.2f,.13f),V(.35f,.68f),V(.82f,.68f),V(.94f,.28f),V(.25f,.28f));Dot(.4f,.9f,.08f);Dot(.8f,.9f,.08f);break;
            case "Audio":Line(V(.08f,.36f),V(.3f,.36f),V(.55f,.14f),V(.55f,.86f),V(.3f,.64f),V(.08f,.64f),V(.08f,.36f));Line(V(.7f,.29f),V(.8f,.42f),V(.8f,.58f),V(.7f,.71f));Line(V(.84f,.13f),V(.98f,.35f),V(.98f,.65f),V(.84f,.87f));break;
            case "Pflanzen":Line(V(.14f,.94f),V(.7f,.3f));Line(V(.3f,.76f),V(.15f,.45f),V(.34f,.16f),V(.92f,.08f),V(.82f,.58f),V(.54f,.78f),V(.3f,.76f));break;
            case "Tiere":Dot(.5f,.7f,.24f);Dot(.13f,.38f,.13f);Dot(.35f,.16f,.13f);Dot(.65f,.16f,.13f);Dot(.87f,.38f,.13f);break;
            case "Health":Line(V(.5f,.91f),V(.12f,.54f),V(.06f,.31f),V(.19f,.13f),V(.37f,.13f),V(.5f,.29f),V(.63f,.13f),V(.81f,.13f),V(.94f,.31f),V(.88f,.54f),V(.5f,.91f));break;
            case "Artefakte":Line(V(.5f,.02f),V(.81f,.27f),V(.85f,.71f),V(.5f,.99f),V(.15f,.71f),V(.19f,.27f),V(.5f,.02f));Line(V(.5f,.02f),V(.5f,.99f));Line(V(.19f,.27f),V(.5f,.41f),V(.81f,.27f));Line(V(.15f,.71f),V(.5f,.58f),V(.85f,.71f));break;
            case "UI":Line(V(.04f,.13f),V(.96f,.13f),V(.96f,.74f),V(.04f,.74f),V(.04f,.13f));Line(V(.5f,.74f),V(.5f,.96f));Line(V(.26f,.96f),V(.74f,.96f));break;
            case "Speichern":Line(V(.1f,.05f),V(.8f,.05f),V(.95f,.2f),V(.95f,.95f),V(.1f,.95f),V(.1f,.05f));Line(V(.3f,.05f),V(.3f,.39f),V(.75f,.39f),V(.75f,.05f));Line(V(.28f,.95f),V(.28f,.59f),V(.78f,.59f),V(.78f,.95f));break;
            case "Aktualisieren":Handles.DrawWireArc(P(.5f,.5f),Vector3.forward,Vector3.right,300,rect.width*.35f,1.8f);Line(V(.85f,.5f),V(.98f,.35f));Line(V(.85f,.5f),V(.68f,.36f));break;
        }
        Handles.EndGUI();Handles.color=before;
    }
    public void Dispose(){foreach(var texture in textures)if(texture)UnityEngine.Object.DestroyImmediate(texture);if(skin)UnityEngine.Object.DestroyImmediate(skin);}
}
