using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class GameplaySettingsWindow : EditorWindow
{
    public static IReadOnlyList<string> TabLabels => GpsSchema.Tabs;
    [SerializeField] int tab;
    Vector2 scroll;
    GpsEditorView view;
    GpsEditorTheme theme;
    public GpsEditorTheme Theme => theme ??= new GpsEditorTheme();
    string status;
    MapGenerator map;
    int draggingDensityKey = -1, densityCurveControl, panningCurveControl;
    float draggingCurveMaximum;
    string selectedCurvePath;
    int selectedCurveKey = -1;
    readonly Dictionary<string, Vector2> curveViews = new();
    readonly Dictionary<string, GpsEditorCurve> curves = new();
    [MenuItem("Mining Game/Gameplay Settings")]
    public static void Open()
    { var window = GetWindow<GameplaySettingsWindow>("Gameplay Settings"); window.minSize = new Vector2(760,640); window.Show(); }
    void OnEnable() { view = new GpsEditorView(this); GpsSettings.Changed += Repaint; EditorApplication.playModeStateChanged += PlayModeChanged; }
    void PlayModeChanged(PlayModeStateChange state) { GpsSettings.Changed -= Repaint; GpsSettings.Changed += Repaint; Repaint(); }
    void OnDisable()
    {
        HomeAudioEditorPreview.Stop();
        GpsSettings.Changed -= Repaint;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        foreach (var curve in curves.Values) if (curve) DestroyImmediate(curve);
        curves.Clear();
        theme?.Dispose();theme=null;
    }
    public void SetStatus(string value) => status=value;
    void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0,0,position.width,position.height),GpsEditorTheme.Background);
        using var themed=Theme.Scope();
        GUILayout.BeginArea(new Rect(16,14,position.width-32,position.height-28));
        float previousLabelWidth=EditorGUIUtility.labelWidth;
        try { DrawSettings(); }
        finally { EditorGUIUtility.labelWidth=previousLabelWidth;GUILayout.EndArea(); }
    }
    void DrawSettings()
    {
        if (!Resources.Load<GpsProfile>(GpsSettings.ResourceName))
        { if (GUILayout.Button("GPS initialisieren",Theme.Button)) GpsProfileEditor.Install(); return; }
        GpsSettings.EnsureLoaded(); map=Object.FindFirstObjectByType<MapGenerator>();
        EditorGUIUtility.labelWidth=Mathf.Min(330,position.width*.39f);
        var header=GUILayoutUtility.GetRect(1,44,GUILayout.ExpandWidth(true));
        GUI.Label(new Rect(header.x,header.y,header.width-245,38),"Gameplay Settings"+(GpsSettings.HasUnsavedChanges ? " *" : ""),Theme.Title);
        var save=new Rect(header.xMax-224,header.y,224,34);
        if(GUI.Button(save,"Einstellungen speichern",Theme.Primary))
        { GUI.FocusControl(null);status=GpsSettings.Save(out string error)?"Gespeichert":error; }
        GpsEditorTheme.Icon("Speichern",new Rect(save.x+12,save.y+8,18,18),GpsEditorTheme.Brass);
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("Aktualisieren",Theme.Tab,GUILayout.Width(148),GUILayout.Height(30)))Repaint();
            var refresh=GUILayoutUtility.GetLastRect();GpsEditorTheme.Icon("Aktualisieren",new Rect(refresh.x+13,refresh.y+7,17,17),GpsEditorTheme.Ink);
            GUILayout.FlexibleSpace();
        }
        GUILayout.Space(10);
        int columns=Mathf.Clamp((int)((position.width-52)/165),1,8);
        using(new EditorGUILayout.VerticalScope(Theme.Navigation))
            for (int row=0;row*columns<TabLabels.Count;row++)
            {
                if(row>0)GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                for (int column=0;column<columns && row*columns+column<TabLabels.Count;column++)
                {
                    if(column>0)GUILayout.Space(8);
                    int index=row*columns+column;
                    bool active=tab==index;
                    int count=Mathf.Min(columns,TabLabels.Count-row*columns);
                    float width=(position.width-52-(count-1)*8)/count;
                    var rect=GUILayoutUtility.GetRect(new GUIContent(TabLabels[index]),active?Theme.ActiveTab:Theme.Tab,GUILayout.Width(width));
                    if(GUI.Button(rect,TabLabels[index],active?Theme.ActiveTab:Theme.Tab) && !active) { GUI.FocusControl(null);tab=index;scroll=Vector2.zero; }
                    Theme.DrawTab(rect,TabLabels[index],active);
                }
            }
        GUILayout.Space(12);
        scroll=GUILayout.BeginScrollView(scroll,false,false,Theme.HorizontalScrollbar,Theme.VerticalScrollbar,Theme.ScrollView);
        (view??=new GpsEditorView(this)).Draw(TabLabels[Mathf.Clamp(tab,0,TabLabels.Count-1)]);
        GUILayout.EndScrollView();
        if (!string.IsNullOrEmpty(status))GUILayout.Label(status,Theme.Small,GUILayout.Height(24));
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public AnimationCurve DrawProfileCurve(string path,AnimationCurve curve,int start,int end,GpsValue parent)
    {
        if (!curves.TryGetValue(path,out var holder)) { holder=ScriptableObject.CreateInstance<GpsEditorCurve>(); holder.hideFlags=HideFlags.HideAndDontSave; curves[path]=holder; }
        holder.curve=curve;
        var serialized=new SerializedObject(holder); serialized.Update();
        var selected=parent?.children.Find(node=>node.name=="layerIndices")?.children.Select(node=>(int)node.number);
        var weight=parent?.children.Find(node=>node.name=="weightCurve")?.curve?.ToCurve();
        float baseWeight=(float)(parent?.children.Find(node=>node.name=="baseWeight")?.number??1);
        DrawDensityCurvePreview(serialized.FindProperty("curve"),start,end,selected==null ? null : new HashSet<int>(selected),weight,baseWeight);
        serialized.ApplyModifiedPropertiesWithoutUndo(); return holder.curve;
    }
    public static CraftingRecipe[] SortCraftableRecipes(IEnumerable<CraftingRecipe> candidates)
    {
        return candidates
            .Where(recipe => recipe && recipe.TryGetCosts(out _))
            .OrderBy(recipe => RecipeCategoryOrder(recipe.Category))
            .ThenBy(recipe => recipe.output.displayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(recipe => recipe.name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string[] GetRecipeLabels(IEnumerable<CraftingRecipe> source)
    {
        return source.Select(recipe => RecipeCategoryLabel(recipe.Category) + " · " + recipe.output.displayName).ToArray();
    }

    static int RecipeCategoryOrder(CraftingRecipe.RecipeCategory category) => category switch
    {
        CraftingRecipe.RecipeCategory.Building => 0,
        CraftingRecipe.RecipeCategory.Materials => 1,
        CraftingRecipe.RecipeCategory.Tools => 2,
        _ => 3
    };

    static string RecipeCategoryLabel(CraftingRecipe.RecipeCategory category) => category switch
    {
        CraftingRecipe.RecipeCategory.Building => "Bauen",
        CraftingRecipe.RecipeCategory.Materials => "Materialien",
        CraftingRecipe.RecipeCategory.Tools => "Werkzeuge",
        _ => "Sonstiges"
    };

    internal static float DensityLayerX(int startDepth, int mapHeight) =>
        CurveLayerX(startDepth, 0, mapHeight);

    internal static float CurveLayerX(int depth, int startDepth, int endDepth) =>
        Mathf.Clamp01((float)(depth - startDepth) / Mathf.Max(1, endDepth - startDepth - 1));

    internal static int CurveDepthAtX(float x, int startDepth, int endDepth) =>
        startDepth + Mathf.RoundToInt(Mathf.Clamp01(x) * Mathf.Max(0, endDepth - startDepth - 1));

    internal static float CurveDepthValueAtX(float x, int startDepth, int endDepth) =>
        startDepth + Mathf.Clamp01(x) * Mathf.Max(0, endDepth - startDepth - 1);

    internal static int DepthTickStep(float visibleDepth, float plotWidth)
    {
        float target = visibleDepth / Mathf.Max(1f, plotWidth / 80f);
        if (target <= 1f) return 1;
        float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(target)));
        float scaled = target / power;
        float nice = scaled < 1.5f ? 1f : scaled < 3.5f ? 2f : scaled < 7.5f ? 5f : 10f;
        return Mathf.Max(1, Mathf.RoundToInt(nice * power));
    }

    internal static int OreAbsenceAtX(float x, int rangeStart, int rangeEnd, MapLayer[] layers,
        HashSet<int> selectedLayers, AnimationCurve weightCurve, float baseWeight)
    {
        if (layers == null || selectedLayers == null) return 0;
        float depth = CurveDepthValueAtX(x, rangeStart, rangeEnd);
        int layerIndex = -1;
        int layerStart = int.MinValue;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && layers[i].startDepth <= depth && layers[i].startDepth > layerStart)
            {
                layerIndex = i;
                layerStart = layers[i].startDepth;
            }
        if (layerIndex < 0 || !selectedLayers.Contains(layerIndex)) return 1;
        float factor = weightCurve == null || weightCurve.length == 0 ? 1f : weightCurve.Evaluate(x);
        float weight = baseWeight * factor;
        return !Finite(weight) || weight <= 0f ? 2 : 0;
    }

    internal static Vector2 ZoomCurveView(Vector2 view, float anchor, float factor, float minimumSpan)
    {
        float span = view.y - view.x;
        float nextSpan = Mathf.Clamp(span * factor, minimumSpan, 1f);
        float left = Mathf.Clamp(view.x + Mathf.Clamp01(anchor) * (span - nextSpan), 0f, 1f - nextSpan);
        return new Vector2(left, left + nextSpan);
    }

    internal static Vector2 PanCurveView(Vector2 view, float movement)
    {
        float span = view.y - view.x;
        float left = Mathf.Clamp(view.x + movement, 0f, 1f - span);
        return new Vector2(left, left + span);
    }

    void DrawDensityCurvePreview(SerializedProperty property, int rangeStart, int rangeEnd,
        HashSet<int> selectedLayers = null, AnimationCurve presenceCurve = null, float baseWeight = 1f)
    {
        var curve = property.animationCurveValue;
        if (curve == null || curve.length == 0) curve = AnimationCurve.Constant(0f, 1f, 1f);
        Rect outer = GUILayoutUtility.GetRect(10f, 288f, GUILayout.ExpandWidth(true));
        Theme.PaintControl(outer);
        Rect plot = new Rect(outer.x + 46f, outer.y + 35f, outer.width - 58f, 192f);
        Rect strip = new Rect(plot.x, plot.yMax + 24f, plot.width, 24f);
        string viewKey = property.serializedObject.targetObject.GetInstanceID() + ":" + property.propertyPath;
        if (!curveViews.TryGetValue(viewKey, out Vector2 view)) view = new Vector2(0f, 1f);
        float minimumSpan = Mathf.Min(1f, 4f / Mathf.Max(1, rangeEnd - rangeStart));
        float maximum = 2f;
        foreach (var key in curve.keys) if (Finite(key.value)) maximum = Mathf.Max(maximum, key.value * 1.2f);
        for (int i = 0; i <= 32; i++)
        {
            float value = curve.Evaluate(i / 32f);
            if (Finite(value)) maximum = Mathf.Max(maximum, value * 1.2f);
        }
        maximum = Mathf.Ceil(maximum * 2f) / 2f;
        int control = GUIUtility.GetControlID(FocusType.Passive, plot);
        if (GUIUtility.hotControl == control && densityCurveControl == control && draggingDensityKey >= 0)
            maximum = draggingCurveMaximum;
        Event evt = Event.current;

        if (GUI.Button(new Rect(outer.xMax - 72f, outer.y+4, 68f, 24f), "Gesamt", Theme.Button))
        {
            view = new Vector2(0f, 1f);
            curveViews[viewKey] = view;
            Repaint();
        }
        if (evt.type == EventType.ScrollWheel && (plot.Contains(evt.mousePosition) || strip.Contains(evt.mousePosition)))
        {
            float anchor = Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width);
            view = ZoomCurveView(view, anchor, Mathf.Pow(1.15f, evt.delta.y), minimumSpan);
            curveViews[viewKey] = view;
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseDown && evt.button == 2 && plot.Contains(evt.mousePosition))
        {
            panningCurveControl = control;
            GUIUtility.hotControl = control;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control &&
            panningCurveControl == control)
        {
            view = PanCurveView(view, -evt.delta.x / plot.width * (view.y - view.x));
            curveViews[viewKey] = view;
            evt.Use();
            Repaint();
        }

        if (evt.type == EventType.MouseDown && evt.button == 0 && plot.Contains(evt.mousePosition))
        {
            int nearest = -1;
            float distance = 11f * 11f;
            for (int i = 0; i < curve.length; i++)
            {
                if (curve.keys[i].time < view.x || curve.keys[i].time > view.y) continue;
                Vector2 point = CurvePoint(plot, maximum, view, curve.keys[i].time, curve.keys[i].value);
                float candidate = (point - evt.mousePosition).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = i;
                distance = candidate;
            }
            if (nearest < 0 && evt.clickCount >= 2)
            {
                float x = Mathf.Lerp(view.x, view.y, Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width));
                float y = Mathf.Max(0f, (plot.yMax - evt.mousePosition.y) / plot.height * maximum);
                nearest = curve.AddKey(new Keyframe(x, y));
                if (nearest >= 0)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, nearest, AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(curve, nearest, AnimationUtility.TangentMode.Auto);
                    property.animationCurveValue = curve;
                }
            }
            if (nearest >= 0)
            {
                draggingDensityKey = nearest;
                densityCurveControl = control;
                draggingCurveMaximum = maximum;
                selectedCurvePath = viewKey;
                selectedCurveKey = nearest;
                GUIUtility.hotControl = control;
                evt.Use();
                Repaint();
            }
            else if (selectedCurvePath == viewKey)
            {
                selectedCurveKey = -1;
                Repaint();
            }
        }
        else if (evt.type == EventType.MouseDown && evt.button == 1 && plot.Contains(evt.mousePosition) && curve.length > 2)
        {
            for (int i = 1; i < curve.length - 1; i++)
            {
                if (curve.keys[i].time < view.x || curve.keys[i].time > view.y ||
                    (CurvePoint(plot, maximum, view, curve.keys[i].time, curve.keys[i].value) - evt.mousePosition).sqrMagnitude > 100f)
                    continue;
                curve.RemoveKey(i);
                property.animationCurveValue = curve;
                if (selectedCurvePath == viewKey) selectedCurveKey = -1;
                evt.Use();
                break;
            }
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control &&
            densityCurveControl == control && draggingDensityKey >= 0 && draggingDensityKey < curve.length)
        {
            var keys = curve.keys;
            int index = draggingDensityKey;
            float x = Mathf.Lerp(view.x, view.y, Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width));
            if (index == 0) x = 0f;
            else if (index == keys.Length - 1) x = 1f;
            else x = Mathf.Clamp(x, keys[index - 1].time + .001f, keys[index + 1].time - .001f);
            float y = Mathf.Max(0f, (plot.yMax - evt.mousePosition.y) / plot.height * maximum);
            var key = keys[index];
            key.time = x;
            key.value = y;
            draggingDensityKey = curve.MoveKey(index, key);
            selectedCurveKey = draggingDensityKey;
            property.animationCurveValue = curve;
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == control)
        {
            GUIUtility.hotControl = 0;
            draggingDensityKey = -1;
            panningCurveControl = 0;
            evt.Use();
        }

        if (evt.type != EventType.Repaint) return;
        EditorGUI.DrawRect(plot,GpsEditorTheme.Plot);
        if (selectedLayers != null)
            DrawOreAbsenceBands(plot, view, rangeStart, rangeEnd, selectedLayers, presenceCurve, baseWeight);
        for (int i = 0; i <= 4; i++)
        {
            float y = Mathf.Lerp(plot.yMax, plot.y, i / 4f);
            EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1f),GpsEditorTheme.Grid);
            GUI.Label(new Rect(outer.x+6, y - 8f, 36f, 16f), (maximum * i / 4f).ToString("0.##"),Theme.Small);
        }
        float depthSpan = Mathf.Max(0, rangeEnd - rangeStart - 1);
        float firstDepth = rangeStart + view.x * depthSpan;
        float lastDepth = rangeStart + view.y * depthSpan;
        int tickStep = DepthTickStep(lastDepth - firstDepth, plot.width);
        int firstTick = Mathf.CeilToInt(firstDepth / tickStep) * tickStep;
        for (int depth = firstTick; depth <= lastDepth + .0001f; depth += tickStep)
        {
            float normalized = CurveLayerX(depth, rangeStart, rangeEnd);
            float x = plot.x + plot.width * (normalized - view.x) / (view.y - view.x);
            EditorGUI.DrawRect(new Rect(x, plot.y, 1f, plot.height),GpsEditorTheme.Grid*.7f);
            GUI.Label(new Rect(Mathf.Clamp(x - 24f, plot.x, plot.xMax - 48f),
                plot.yMax + 3f, 48f, 16f), depth.ToString(),Theme.Small);
        }
        if (map && map.layers != null)
        {
            var ordered = map.layers.Select((layer, index) => (layer, index))
                .Where(item => item.layer != null && item.layer.startDepth < rangeEnd)
                .OrderBy(item => item.layer.startDepth).ToArray();
            Color[] colors = { new Color(.55f, .32f, .27f), new Color(.54f, .49f, .31f),
                new Color(.31f, .43f, .54f), new Color(.45f, .33f, .53f) };
            for (int i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].layer.startDepth < rangeStart) continue;
                float start = CurveLayerX(ordered[i].layer.startDepth, rangeStart, rangeEnd);
                float end = i + 1 < ordered.Length ?
                    CurveLayerX(ordered[i + 1].layer.startDepth, rangeStart, rangeEnd) : 1f;
                if (end <= view.x || start >= view.y) continue;
                float left = strip.x + strip.width * Mathf.Clamp01((start - view.x) / (view.y - view.x));
                float right = strip.x + strip.width * Mathf.Clamp01((end - view.x) / (view.y - view.x));
                Color color = selectedLayers == null || selectedLayers.Contains(ordered[i].index) ?
                    colors[ordered[i].index % colors.Length] : new Color(.23f, .25f, .28f);
                EditorGUI.DrawRect(new Rect(left, strip.y, Mathf.Max(0f, right - left), strip.height), color);
                string name = string.IsNullOrWhiteSpace(ordered[i].layer.name) ?
                    "Layer " + (ordered[i].index + 1) : ordered[i].layer.name;
                if (right - left > 26f) GUI.Label(new Rect(left + 4f, strip.y + 3f, right - left - 7f, 18f),
                    right - left > 86f ? name : "L" + (ordered[i].index + 1),Theme.Small);
                if (start < view.x || start > view.y) continue;
                float marker = left;
                EditorGUI.DrawRect(new Rect(marker, plot.y, 1.5f, plot.height),GpsEditorTheme.Brass);
                float labelX = Mathf.Clamp(marker + 3f, plot.x, plot.xMax - 105f);
                float previous = i > 0 ? CurveLayerX(ordered[i - 1].layer.startDepth, rangeStart, rangeEnd) : -1f;
                float labelY = outer.y + (i > 0 && start - previous < .11f * (view.y - view.x) ? 15f : 0f);
                GUI.Label(new Rect(labelX, labelY+4, 105f, 16f), name + " · " + ordered[i].layer.startDepth,Theme.Small);
            }
        }
        var points = new Vector3[129];
        for (int i = 0; i < points.Length; i++)
        {
            float x = Mathf.Lerp(view.x, view.y, i / 128f);
            points[i] = CurvePoint(plot, maximum, view, x, curve.Evaluate(x));
        }
        Handles.BeginGUI();
        for (int i = 0; i < points.Length - 1; i++)
        {
            int absence = selectedLayers == null ? 0 : OreAbsenceAtX(
                Mathf.Lerp(view.x, view.y, (i + .5f) / (points.Length - 1)), rangeStart, rangeEnd,
                map.layers, selectedLayers, presenceCurve, baseWeight);
            Handles.color = absence == 1 ? new Color(.55f, .59f, .64f) :
                absence == 2 ? new Color(.92f, .49f, .34f) : GpsEditorTheme.Brass;
            Handles.DrawAAPolyLine(2.5f, points[i], points[i + 1]);
        }
        for (int i = 0; i < curve.length; i++)
        {
            var key = curve.keys[i];
            if (key.time < view.x || key.time > view.y) continue;
            int absence = selectedLayers == null ? 0 : OreAbsenceAtX(key.time, rangeStart,
                rangeEnd, map.layers, selectedLayers, presenceCurve, baseWeight);
            Handles.color = selectedCurvePath == viewKey && i == selectedCurveKey ? Color.white :
                absence == 1 ? new Color(.7f, .74f, .78f) :
                absence == 2 ? new Color(1f, .58f, .4f) : GpsEditorTheme.Brass;
            Handles.DrawSolidDisc(CurvePoint(plot, maximum, view, key.time, key.value), Vector3.forward, 4.5f);
        }
        Handles.EndGUI();
        if (selectedCurvePath == viewKey && selectedCurveKey >= 0 && selectedCurveKey < curve.length)
        {
            var key = curve.keys[selectedCurveKey];
            if (key.time >= view.x && key.time <= view.y)
            {
                Vector2 point = CurvePoint(plot, maximum, view, key.time, key.value);
                const float width = 170f;
                float left = point.x + 10f;
                if (left + width > plot.xMax) left = point.x - width - 10f;
                Rect badge = new Rect(Mathf.Clamp(left, plot.x, plot.xMax - width),
                    Mathf.Clamp(point.y - 28f, plot.y, plot.yMax - 23f), width, 22f);
                Theme.PaintControl(badge);
                GUI.Label(new Rect(badge.x + 7f, badge.y + 2f, width - 12f, 18f),
                    "X/Tiefe " + CurveDepthValueAtX(key.time, rangeStart, rangeEnd).ToString("0.##") +
                    "   Y " + key.value.ToString("0.###"),Theme.Small);
            }
        }
    }

    static Vector2 CurvePoint(Rect plot, float maximum, Vector2 view, float x, float y) => new Vector2(
        plot.x + plot.width * Mathf.Clamp01(Finite(x) ? (x - view.x) / (view.y - view.x) : 0f),
        plot.yMax - plot.height * Mathf.Clamp01(Finite(y) ? y / maximum : 0f));

    void DrawOreAbsenceBands(Rect plot, Vector2 view, int rangeStart, int rangeEnd,
        HashSet<int> selectedLayers, AnimationCurve weightCurve, float baseWeight)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(plot.width / 2f));
        int previous = 0, runStart = 0;
        for (int column = 0; column <= columns; column++)
        {
            int kind = column == columns ? 0 : OreAbsenceAtX(
                Mathf.Lerp(view.x, view.y, (column + .5f) / columns), rangeStart, rangeEnd,
                map.layers, selectedLayers, weightCurve, baseWeight);
            if (kind == previous) continue;
            if (previous != 0)
            {
                float left = plot.x + plot.width * runStart / columns;
                float width = plot.width * (column - runStart) / columns;
                EditorGUI.DrawRect(new Rect(left, plot.y, width, plot.height), previous == 1 ?
                    new Color(.32f, .34f, .38f, .58f) : new Color(.48f, .23f, .16f, .52f));
                if (width > 90f)
                    GUI.Label(new Rect(left + 6f, plot.y + 5f, width - 12f, 18f),
                        previous == 1 ? "Nicht in Layer" : "Gewicht 0",Theme.Small);
            }
            previous = kind;
            runStart = column;
        }
    }

}

public sealed class GpsEditorCurve : ScriptableObject { public AnimationCurve curve; }
