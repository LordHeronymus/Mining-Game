using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GpsEditorView
{
    readonly HashSet<string> expanded = new();
    readonly GameplaySettingsWindow window;
    GpsEditorTheme Theme=>window.Theme;
    public GpsEditorView(GameplaySettingsWindow owner) => window = owner;
    public void Draw(string tab)
    {
        foreach (string title in GpsSchema.Sections.Where(section => section.tab == tab).Select(section => section.title).Distinct())
        {
            using (new EditorGUILayout.VerticalScope(Theme.Card))
            {
                if (!Foldout(tab + "/" + title, title)) continue;
                var fields = GpsSchema.SectionFields(tab, title).ToArray();
                foreach (var group in fields.GroupBy(pair => pair.record.key))
                {
                    var record = group.First().record;
                    if (!string.IsNullOrEmpty(record.name) && fields.Select(pair => pair.record.key).Distinct().Count() > 1 && group.Count() > 1 && !Foldout(tab + "/" + title + "/" + record.key, record.name)) continue;
                    foreach (var pair in group)
                    {
                        var root = GpsSettings.GetValue(record.key, pair.field.name)?.Copy(); if (root == null) continue;
                        string label = !string.IsNullOrEmpty(record.name) && group.Count() == 1 && fields.Select(p => p.record.key).Distinct().Count() > 1 &&
                            fields.Select(p=>p.record.type).Distinct().Count()==1 ? record.name : pair.field.label;
                        EditorGUI.BeginChangeCheck();
                        DrawValue(record.key, root, root, pair.field, label, record.key + "/" + root.name, null);
                        if (EditorGUI.EndChangeCheck())
                        { if (!GpsSettings.SetValue(record.key, root, out string error)) window.SetStatus(error); }
                    }
                }
            }
        }
        if (tab == "Map")
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Kartenübersicht",Theme.Button)) MapOverviewWindow.Open();
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("Map generieren",Theme.Primary))
                    {
                        if (!GpsSettings.ValidateDocument(GpsSettings.Document,out var error)) { window.SetStatus(error); return; }
                        var map = Object.FindFirstObjectByType<MapGenerator>();
                        if (map) { GpsSettings.ApplyGeneration(map); MapEditorGeneration.Generate(map); }
                    }
            }
        }
    }
    bool Foldout(string key, string label)
    {
        bool before = expanded.Contains(key), next = Theme.Foldout(before,label);
        if (next) expanded.Add(key); else expanded.Remove(key); return next;
    }
    void DrawValue(string record, GpsValue root, GpsValue node, GpsFieldSpec spec, string label, string path, GpsValue parent)
    {
        if (node.name == "homeClips" && node.kind == GpsValueKind.Array)
        {
            foreach (var entry in node.children)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool open = Foldout(path + "/" + entry.name, GpsSchema.NodeLabel(entry, GpsSettings.Profile));
                    if (GUILayout.Button("Play",Theme.Button,GUILayout.Width(52)))
                        HomeAudioEditorPreview.Play((HomeAudioClipTuning)GpsCodec.Write(entry, GpsSettings.Profile.Resolve));
                    if (!open) continue;
                }
                using (new EditorGUI.IndentLevelScope())
                    foreach (var child in entry.children)
                        if (child.name != "clip")
                        {
                            var childSpec = GpsSchema.ChildSpec(child, typeof(HomeAudioClipTuning));
                            DrawValue(record, root, child, childSpec, childSpec.label, path + "/" + entry.name + "/" + child.name, entry);
                        }
            }
            return;
        }
        if (node.name == "layerIndices" && node.kind == GpsValueKind.Array)
        {
            GUILayout.Label(label,Theme.Label,GUILayout.Height(28));
            var map = GpsSettings.Document.records.Find(entry => entry.type == typeof(MapGenerator).AssemblyQualifiedName);
            var layers = map?.fields.Find(field => field.name == "layers")?.children;
            if (layers == null) return;
            using (new EditorGUI.IndentLevelScope())
                for (int i=0;i<layers.Count;i++)
                {
                    bool has = node.children.Any(entry => entry.number == i);
                    string name = layers[i].children.Find(entry => entry.name == "name")?.text;
                    bool next = Theme.Check(string.IsNullOrEmpty(name) ? "Layer " + (i+1) : name,has);
                    if (next && !has) node.children.Add(GpsCodec.Read(node.children.Count.ToString(), typeof(int), i, GpsSettings.Profile.Key));
                    if (!next && has) node.children.RemoveAll(entry => entry.number == i);
                }
            return;
        }
        if (node.kind == GpsValueKind.Object || node.kind == GpsValueKind.Array)
        {
            if (!Foldout(path, label)) return;
            Type type = GpsCodec.ResolveType(node.type);
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i=0;i<node.children.Count;i++)
                {
                    var child=node.children[i]; if (GpsSchema.HideChild(type,child.name)) continue;
                    var childSpec=node.kind==GpsValueKind.Array ? GpsSchema.ArraySpec(child,spec,i) : GpsSchema.ChildSpec(child,type);
                    DrawValue(record,root,child,childSpec,childSpec.label,path+"/"+i,node);
                    if (node.kind==GpsValueKind.Array)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("↑",Theme.Button,GUILayout.Width(36)) && i>0) { (node.children[i-1],node.children[i])=(node.children[i],node.children[i-1]); GUI.changed=true; }
                            if (GUILayout.Button("↓",Theme.Button,GUILayout.Width(36)) && i+1<node.children.Count) { (node.children[i+1],node.children[i])=(node.children[i],node.children[i+1]); GUI.changed=true; }
                            if (GUILayout.Button("−",Theme.Button,GUILayout.Width(36))) { node.children.RemoveAt(i--); GUI.changed=true; }
                        }
                    }
                }
                if (node.kind==GpsValueKind.Array && GUILayout.Button("+",Theme.Button,GUILayout.Width(42)))
                {
                    Type element=type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                    var child=node.children.Count>0 ? node.children.Last().Copy() : GpsCodec.Read("0",element,
                        element==typeof(string) ? "" : typeof(Object).IsAssignableFrom(element) ? null : Activator.CreateInstance(element), GpsSettings.Profile.Key);
                    child.name=node.children.Count.ToString(); node.children.Add(child); GUI.changed=true;
                }
            }
            return;
        }
        if(node.kind==GpsValueKind.Boolean){node.flag=Theme.Check(label,node.flag);return;}
        Theme.Row(label,out var control);
        switch (node.kind)
        {
            case GpsValueKind.Text: node.text=EditorGUI.TextField(control,node.text,Theme.Field); break;
            case GpsValueKind.Enum:
                var type=GpsCodec.ResolveType(node.type); var values=Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32).ToArray();
                int index=Theme.Select(control,Mathf.Max(0,Array.IndexOf(values,(int)node.number)),Enum.GetNames(type)); node.number=values[index]; break;
            case GpsValueKind.Reference:
                bool audio=typeof(AudioClip).IsAssignableFrom(GpsCodec.ResolveType(node.type));
                Rect assetRect=control;if(audio)assetRect.width-=60;
                var asset=Theme.AssetField(assetRect,GpsSettings.Profile.Resolve(node.text),GpsCodec.ResolveType(node.type));
                if (asset!=GpsSettings.Profile.Resolve(node.text)) node.text=GpsProfileEditor.Register(asset,GpsSettings.Profile);
                if (asset is AudioClip clip && GUI.Button(new Rect(control.xMax-52,control.y,52,28),"Play",Theme.Button)) Preview(clip);
                break;
            case GpsValueKind.Color:
                Theme.PaintControl(control);node.color=EditorGUI.ColorField(new Rect(control.x+4,control.y+5,control.width-8,control.height-10),GUIContent.none,node.color,true,true,false);break;
            case GpsValueKind.Vector:
                int size=GpsSchema.VectorDimension(node);
                Vector4 vector=node.vector;
                for(int i=0;i<size;i++)
                {
                    var component=new Rect(control.x+i*(control.width+8)/size,control.y,(control.width+8)/size-8,control.height);
                    GUI.Label(new Rect(component.x,component.y,18,component.height),new[]{"X","Y","Z","W"}[i],Theme.Small);component.x+=20;component.width-=20;
                    vector[i]=Mathf.Clamp(EditorGUI.FloatField(component,vector[i],Theme.Field),spec.min,spec.max);
                }
                node.vector=vector;break;
            case GpsValueKind.Curve:
                Theme.PaintControl(control);
                var curve=EditorGUI.CurveField(new Rect(control.x+3,control.y+3,control.width-6,control.height-6),node.curve.ToCurve(),GpsEditorTheme.Brass,Rect.zero);
                if (root.name=="oreDensityCurve" || root.name=="oreSettings")
                {
                    GpsSchema.CurveDepthRange(record,root.name,parent,out int start,out int end);
                    curve=window.DrawProfileCurve(path,curve,start,end,parent);
                }
                node.curve=GpsCurve.From(curve); break;
            default:
                if (node.name=="itemId")
                {
                    var choices=GpsSchema.Choices(typeof(ItemSO));
                    int selected=choices.FindIndex(entry=>(int)((ItemSO)entry.asset).item==(int)node.number);
                    int next=Theme.Select(control,Mathf.Max(0,selected),choices.Select(entry=>GpsSchema.DisplayName(entry.asset)).ToArray());
                    if (next>=0 && next<choices.Count) node.number=(int)((ItemSO)choices[next].asset).item;
                }
                else if (node.kind==GpsValueKind.Integer) node.number=Mathf.Clamp(EditorGUI.IntField(control,(int)node.number,Theme.Field),spec.min,spec.max);
                else node.number=Mathf.Clamp(EditorGUI.FloatField(control,(float)node.number*spec.factor,Theme.Field)/spec.factor,spec.min,spec.max);
                break;
        }
    }
    static void Preview(AudioClip clip)
    {
        if (Application.isPlaying) { GpsTestActions.PreviewClip(clip); return; }
        var util=typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        util?.GetMethod("PlayPreviewClip",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static,null,new[]{typeof(AudioClip),typeof(int),typeof(bool)},null)?.Invoke(null,new object[]{clip,0,false});
    }
}
