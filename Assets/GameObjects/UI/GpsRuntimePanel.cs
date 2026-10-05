using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class GpsRuntimeHost : MonoBehaviour
{
    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (GameBindings.Down(GameAction.DebugPanel) && (!GameplayInputBlocker.IsBlocked || GpsRuntimePanel.IsOpen))
        { if (GpsRuntimePanel.IsOpen) GpsRuntimePanel.Close(); else GpsRuntimePanel.Open(); }
        else if (GpsRuntimePanel.IsOpen && Input.GetKeyDown(KeyCode.Escape)) { RunPauseMenu.ConsumeInput(); GpsRuntimePanel.Close(); }
#endif
    }
}

public sealed partial class GpsRuntimePanel : MonoBehaviour
{
    static GpsRuntimePanel instance;
    public static bool IsOpen => instance && instance.gameObject.activeSelf;
    public static readonly IReadOnlyList<string> TabLabels = GpsSchema.Tabs.Concat(new[] { GpsSchema.TestTab }).ToArray();
    readonly HashSet<string> expanded = new();
    readonly List<Button> tabButtons = new();
    readonly List<Action> refreshValues = new();
    RectTransform content, card;
    ScrollRect scroll;
    TextMeshProUGUI status, title;
    Image backdrop;
    TMP_FontAsset font;
    string tab = GpsSchema.Tabs[0];
    string itemIconSearch = "";
    int itemIconCategory;
    float rowY;
    bool building, committing;
    static readonly Color Panel = new(.105f, .115f, .13f, 1f), Control = new(.17f, .18f, .2f, 1f), Accent = new(.78f, .63f, .35f, 1f);
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => instance = null;
    public static void Open()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Application.isPlaying) return;
        if (!instance)
        {
            HomeUi.EnsureEventSystem();
            var root = new GameObject("Debug Settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 29000;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            instance = root.AddComponent<GpsRuntimePanel>(); instance.Build();
        }
        instance.gameObject.SetActive(true); instance.transform.SetAsLastSibling();
        GameplayInputBlocker.SetBlocked(instance, true); instance.BuildPage(); instance.Refresh();
#endif
    }
    public static void Close()
    {
        GpsAudioPreview.Stop();
        if (!instance) return;
        if (EventSystem.current?.currentSelectedGameObject)
        { EventSystem.current.SetSelectedGameObject(null); }
        GameplayInputBlocker.SetBlocked(instance, false); instance.gameObject.SetActive(false);
    }
    void OnDestroy() { GameplayInputBlocker.SetBlocked(this, false); GpsSettings.Changed -= ConfigurationChanged; }
    void Build()
    {
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") ?? TMP_Settings.defaultFontAsset;
        backdrop = Rect("Backdrop", transform, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        Stretch(backdrop.rectTransform); backdrop.raycastTarget = true;
        card = Rect("Card", transform, Vector2.zero, new Vector2(1780, 990));
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f); card.anchoredPosition = Vector2.zero;
        card.gameObject.AddComponent<Image>().color = Panel;
        title = Label(card, "Debug Settings", new Vector2(28, -25), new Vector2(650, 38), 28);
        Button(card, "Speichern", new Vector2(1450, -26), new Vector2(145, 38), Save);
        Button(card, "Schließen", new Vector2(1600, -26), new Vector2(145, 38), Close);
        var tabs = TabLabels;
        for (int i = 0; i < tabs.Count; i++)
        {
            string label = tabs[i];
            var button = Button(card, label, new Vector2(28 + i % 8 * 216, -80 - i / 8 * 38), new Vector2(210, 34), () => SelectTab(label));
            tabButtons.Add(button);
        }
        var viewport = Rect("Viewport", card, new Vector2(28, -164), new Vector2(1708, 757));
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .03f);
        viewport.gameObject.AddComponent<RectMask2D>();
        content = Rect("Content", viewport, Vector2.zero, new Vector2(1708, 0));
        scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 42; scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var track = Rect("Scrollbar", card, new Vector2(1744, -164), new Vector2(10, 757));
        track.gameObject.AddComponent<Image>().color = Control;
        var thumb = Rect("Thumb", track, Vector2.zero, Vector2.zero); Stretch(thumb);
        var image = thumb.gameObject.AddComponent<Image>(); image.color = Accent;
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = thumb; bar.targetGraphic = image;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        status = Label(card, "", new Vector2(28, -938), new Vector2(1670, 30), 19);
        GpsSettings.Changed += ConfigurationChanged;
    }
    public void SelectTab(string name)
    {
        if (!TabLabels.Contains(name)) return;
        tab = name; BuildPage(); scroll.verticalNormalizedPosition = 1;
    }
    void Save()
    {
        EventSystem.current?.SetSelectedGameObject(null);
        status.text = GpsSettings.Save(out string error) ? "Gespeichert" : error;
    }
    void Refresh()
    {
        if (!card) return;
        backdrop.color = new Color(0, 0, 0, GpsSettings.Preferences.panelBackdropAlpha);
        title.text = "Debug Settings" + (GpsSettings.HasUnsavedChanges ? " *" : "");
        var group = card.GetComponent<CanvasGroup>(); if (!group) group=card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = GpsSettings.Preferences.panelElementAlpha;
        if (building) return;
        foreach (var refresh in refreshValues) refresh();
    }
    void ConfigurationChanged()
    {
        if (!committing && !building && IsOpen) { float position=scroll.verticalNormalizedPosition; BuildPage(); scroll.verticalNormalizedPosition=position; }
        Refresh();
    }
    void BuildPage()
    {
        if (!content || building) return;
        building = true; refreshValues.Clear();
        for (int i = content.childCount - 1; i >= 0; i--) { var child = content.GetChild(i); child.gameObject.SetActive(false); Object.Destroy(child.gameObject); }
        rowY = 0;
        bool showAudioOptions=true;
        if(tab=="Audio") { BuildAudioCatalog(); showAudioOptions=Header("Audio/Advanced","Ablauf und Mischung",0); }
        foreach (string section in GpsSchema.Sections.Where(section => section.tab == tab).Select(section => section.title).Distinct())
        {
            if(tab=="Audio" && (!showAudioOptions || section=="Einzelclips" || section=="Gesamtpegel"))continue;
            var fields = GpsSchema.SectionFields(tab, section).ToArray();
            if (fields.Length == 0) continue;
            string sectionKey = tab + "/" + section;
            if (!Header(sectionKey, section, 0)) continue;
            if (section == "Item-Icons")
            {
                Label(content, "Items suchen", new Vector2(44, -rowY), new Vector2(190, 32), 20);
                Input(content, new Vector2(250, -rowY), new Vector2(794, 32), itemIconSearch,
                    value => { itemIconSearch = value; BuildPage(); });
                Dropdown(new Vector2(1100, -rowY), ItemIconLayout.Categories, itemIconCategory,
                    value => { itemIconCategory = value; BuildPage(); }, 560);
                rowY += 46;
                fields = fields.Where(p => ItemIconLayout.Matches(GpsSettings.Profile.Resolve(p.record.assetKey) as ItemSO, itemIconSearch, itemIconCategory)).OrderBy(p => p.record.name).ToArray();
            }
            foreach (var group in fields.GroupBy(field => field.record.key))
            {
                var record = group.First().record;
                bool multiple = group.Count() > 1 && fields.Select(field => field.record.key).Distinct().Count() > 1 && !string.IsNullOrEmpty(record.assetKey);
                if (multiple && !Header(sectionKey + "/" + record.key, record.name, 1)) continue;
                foreach (var entry in group)
                {
                    var root = GpsSettings.GetValue(record.key, entry.field.name)?.Copy(); if (root == null) continue;
                    string label = !string.IsNullOrEmpty(record.assetKey) && group.Count() == 1 && fields.Length > 1 ? record.name : entry.field.label;
                    DrawValue(record.key, root, root, entry.field, label, sectionKey + "/" + record.key + "/" + root.name, 1, null,
                        () => { committing=true; try { if (!GpsSettings.SetValue(record.key, root, out string error)) status.text = error; } finally { committing=false; } });
                }
                if (section == "Item-Icons" && GpsSettings.Profile.Resolve(record.assetKey) is ItemSO ore) BuildShopIconPreview(ore);
            }
        }
        if (tab == GpsSchema.TestTab) BuildTests();
        if (tab == "Map")
        {
            Button(content, "Map generieren", new Vector2(20, -rowY), new Vector2(220, 35), GenerateMap);
            rowY += 45;
        }
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(rowY, scroll.viewport.rect.height));
        for (int i = 0; i < tabButtons.Count; i++) tabButtons[i].GetComponent<Image>().color = TabLabels[i] == tab ? Accent * .65f : Control;
        building = false; Refresh();
    }
    void BuildShopIconPreview(ItemSO ore)
    {
        var card = Rect("Icon Preview", content, new Vector2(44, -rowY), new Vector2(608, 232));
        var background = card.gameObject.AddComponent<Image>(); background.sprite = HomeUi.Sprite("SelectionCardNormal");
        background.type = Image.Type.Sliced; background.pixelsPerUnitMultiplier = 5.5f; background.raycastTarget = false;
        var icon = Rect("Ore", card, new Vector2(20, -18), new Vector2(260, 194)).gameObject.AddComponent<Image>();
        icon.sprite = ore.icon; icon.raycastTarget = false; ShopVisualTheme.CenterImage(icon);
        var center = icon.rectTransform.anchoredPosition;
        ShopVisualTheme.ApplyOreIconLayout(icon, ore, center);
        refreshValues.Add(() => { if (icon) ShopVisualTheme.ApplyOreIconLayout(icon, ore, center); });
        var name = Label(card, ore.displayName, new Vector2(300, -28), new Vector2(282, 72), 43);
        name.font = Resources.Load<TMP_FontAsset>("ArtifactDiscovery/TitleFont") ?? font;
        var badge = Rect("Count", card, new Vector2(482, -155), new Vector2(100, 56));
        var badgeImage = badge.gameObject.AddComponent<Image>(); badgeImage.sprite = HomeUi.Sprite("SelectionCardNormal");
        badgeImage.type = Image.Type.Sliced; badgeImage.pixelsPerUnitMultiplier = 8; badgeImage.raycastTarget = false;
        var count = Label(badge, "1", Vector2.zero, new Vector2(100, 56), 41); count.font = name.font;
        count.alignment = TextAlignmentOptions.Midline;
        rowY += 248;
    }

    bool Header(string key, string label, int indent)
    {
        bool open = expanded.Contains(key);
        Button(content, (open ? "− " : "+ ") + label, new Vector2(indent * 24, -rowY), new Vector2(1690 - indent * 24, 32), () =>
        { if (!expanded.Add(key)) expanded.Remove(key); float position = scroll.verticalNormalizedPosition; BuildPage(); scroll.verticalNormalizedPosition = position; });
        rowY += 40; return open;
    }
    void DrawValue(string record, GpsValue root, GpsValue node, GpsFieldSpec spec, string label, string path, int indent, GpsValue parent, Action commit)
    {
        if (node.name == "homeClips" && node.kind == GpsValueKind.Array)
        {
            foreach (var entry in node.children)
            {
                float headerY = rowY;
                bool open = Header(path + "/" + entry.name, GpsSchema.NodeLabel(entry, GpsSettings.Profile), indent);
                Button(content, "▶", new Vector2(1592, -headerY), new Vector2(90, 32),
                    () => LoadingAudio.PreviewHomeClip((HomeAudioClipTuning)GpsCodec.Write(entry, GpsSettings.Profile.Resolve)));
                if (!open) continue;
                foreach (var child in entry.children)
                    if (child.name != "clip")
                    {
                        var childSpec = GpsSchema.ChildSpec(child, typeof(HomeAudioClipTuning));
                        DrawValue(record, root, child, childSpec, childSpec.label,
                            path + "/" + entry.name + "/" + child.name, indent + 1, entry, commit);
                    }
            }
            return;
        }
        if (node.name=="layerIndices" && node.kind==GpsValueKind.Array)
        {
            Label(content,label,new Vector2(20+indent*24,-rowY),new Vector2(600,32),21); rowY+=37;
            var layers=GpsSettings.Document.records.Find(entry=>entry.type==typeof(MapGenerator).AssemblyQualifiedName)?.fields.Find(field=>field.name=="layers")?.children;
            if (layers!=null) for (int i=0;i<layers.Count;i++)
            {
                int index=i; bool has=node.children.Any(value=>value.number==index);
                string name=layers[i].children.Find(value=>value.name=="name")?.text;
                Button(content,(has ? "[x] " : "[ ] ")+(string.IsNullOrEmpty(name) ? "Layer "+(i+1) : name),new Vector2(44+indent*24,-rowY),new Vector2(950,30),()=>
                { if (has) node.children.RemoveAll(value=>value.number==index); else node.children.Add(GpsCodec.Read(node.children.Count.ToString(),typeof(int),index,GpsSettings.Profile.Key)); commit(); BuildPage(); }); rowY+=36;
            }
            return;
        }
        if (node.kind == GpsValueKind.Object || node.kind == GpsValueKind.Array)
        {
            if (!Header(path, label, indent)) return;
            Type type = GpsCodec.ResolveType(node.type);
            for (int i = 0; i < node.children.Count; i++)
            {
                var child = node.children[i];
                if (GpsSchema.HideChild(type, child.name)) continue;
                var childSpec = node.kind == GpsValueKind.Array ? GpsSchema.ArraySpec(child,spec,i) : GpsSchema.ChildSpec(child, type);
                DrawValue(record, root, child, childSpec, childSpec.label, path + "/" + i, indent + 1, node, commit);
                if (node.kind == GpsValueKind.Array)
                {
                    int index = i;
                    Button(content, "−", new Vector2(1620, -rowY), new Vector2(32, 26), () => { node.children.RemoveAt(index); commit(); BuildPage(); });
                    Button(content, "↑", new Vector2(1540, -rowY), new Vector2(32, 26), () => { if (index > 0) { (node.children[index - 1], node.children[index]) = (node.children[index], node.children[index - 1]); commit(); BuildPage(); } });
                    Button(content, "↓", new Vector2(1580, -rowY), new Vector2(32, 26), () => { if (index + 1 < node.children.Count) { (node.children[index + 1], node.children[index]) = (node.children[index], node.children[index + 1]); commit(); BuildPage(); } });
                    rowY += 33;
                }
            }
            if (node.kind == GpsValueKind.Array)
            {
                Button(content, "+", new Vector2(20 + indent * 24, -rowY), new Vector2(50, 28), () =>
                {
                    Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                    var child = node.children.Count > 0 ? node.children.Last().Copy() : GpsCodec.Read("0", element,
                        element == typeof(string) ? "" : typeof(Object).IsAssignableFrom(element) ? null : Activator.CreateInstance(element), GpsSettings.Profile.Key);
                    child.name = node.children.Count.ToString(); node.children.Add(child); commit(); BuildPage();
                });
                rowY += 36;
            }
            return;
        }
        float x = 20 + indent * 24, inputX = 650;
        Label(content, label, new Vector2(x, -rowY), new Vector2(inputX - x - 20, 32), 21);
        switch (node.kind)
        {
            case GpsValueKind.Boolean:
                var toggleButton = Button(content, (node.flag ^ spec.invertBoolean) ? "An" : "Aus", new Vector2(inputX, -rowY), new Vector2(140, 30), () => { node.flag = !node.flag; commit(); BuildPage(); });
                break;
            case GpsValueKind.Reference:
                var choices = GpsSchema.Choices(GpsCodec.ResolveType(node.type));
                Dropdown(new Vector2(inputX, -rowY), choices.Select(entry => GpsSchema.DisplayName(entry.asset)).Prepend("—").ToArray(),
                    choices.FindIndex(entry => entry.key == node.text) + 1, selected => { node.text = selected == 0 ? null : choices[selected - 1].key; commit(); });
                if (GpsSettings.Profile.Resolve(node.text) is AudioClip clip)
                    Button(content, "Play", new Vector2(1588, -rowY), new Vector2(90, 30), () => GpsTestActions.PreviewClip(clip));
                break;
            case GpsValueKind.Enum:
                Type enumType = GpsCodec.ResolveType(node.type); var names = Enum.GetNames(enumType); var values = Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32).ToArray();
                Dropdown(new Vector2(inputX, -rowY), names, Array.IndexOf(values, (int)node.number), selected => { node.number = values[selected]; commit(); });
                break;
            case GpsValueKind.Vector:
                int dimensions = GpsSchema.VectorDimension(node);
                for (int i = 0; i < dimensions; i++)
                {
                    int index = i;
                    Input(content, new Vector2(inputX + i * 240, -rowY), new Vector2(225, 30), node.vector[i].ToString("0.###", Culture), text =>
                    { if (TryNumber(text, out float value)) { node.vector[index] = Mathf.Clamp(value, spec.min, spec.max); commit(); } });
                }
                break;
            case GpsValueKind.Color:
                Input(content, new Vector2(inputX, -rowY), new Vector2(320, 30), "#" + ColorUtility.ToHtmlStringRGBA(node.color), text =>
                { if (ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out var color)) { node.color = color; commit(); } });
                var swatch = Rect("Color", content, new Vector2(990, -rowY), new Vector2(80, 30)).gameObject.AddComponent<Image>(); swatch.color = node.color;
                break;
            case GpsValueKind.Curve:
                rowY += 35;
                var graphic = Rect("Curve", content, new Vector2(x, -rowY), new Vector2(1670 - x, 210)).gameObject.AddComponent<GpsCurveGraphic>();
                GpsSchema.CurveDepthRange(record, root.name, parent, out int start, out int end);
                int keyCount=node.curve.keys.Count;
                Action curveCommit=()=>{commit();if(node.curve.keys.Count!=keyCount)BuildPage();else Refresh();};
                graphic.Bind(node, start, end, curveCommit);
                rowY += 218;
                int selectedKey=0;
                graphic.KeySelected=index=>{selectedKey=index;Refresh();};
                bool depth=root.name=="oreSettings" || root.name=="oreDensityCurve";
                Dropdown(new Vector2(x,-rowY),node.curve.keys.Select((key,index)=>"Punkt "+(index+1)).ToArray(),0,index=>{selectedKey=index;Refresh();});
                Button(content,"+",new Vector2(1250,-rowY),new Vector2(60,30),()=>
                { var curve=node.curve.ToCurve();float time=.5f;while(curve.keys.Any(key=>Mathf.Abs(key.time-time)<.0001f) && time<1)time+=.01f;curve.AddKey(Mathf.Clamp01(time),curve.Evaluate(time));node.curve=GpsCurve.From(curve);commit();BuildPage(); });
                Button(content,"−",new Vector2(1320,-rowY),new Vector2(60,30),()=>
                { if(node.curve.keys.Count>1 && selectedKey<node.curve.keys.Count){node.curve.keys.RemoveAt(selectedKey);commit();BuildPage();} });
                rowY+=38;
                Label(content,depth ? "Tiefe" : "X",new Vector2(x,-rowY),new Vector2(120,30),20);
                var xInput=Input(content,new Vector2(x+140,-rowY),new Vector2(280,30),"",text=>
                {
                    if(selectedKey>=node.curve.keys.Count || !TryNumber(text,out float value))return;
                    var key=node.curve.keys[selectedKey];float time=depth ? (value-start)/Mathf.Max(1,end-start-1) : value;
                    float lower=selectedKey>0 ? node.curve.keys[selectedKey-1].time+.0001f : 0;
                    float upper=selectedKey+1<node.curve.keys.Count ? node.curve.keys[selectedKey+1].time-.0001f : 1;
                    key.time=Mathf.Clamp(time,lower,upper);node.curve.keys[selectedKey]=key;commit();graphic.SetVerticesDirty();
                });
                Label(content,"Y",new Vector2(x+460,-rowY),new Vector2(70,30),20);
                var yInput=Input(content,new Vector2(x+540,-rowY),new Vector2(280,30),"",text=>
                { if(selectedKey<node.curve.keys.Count && TryNumber(text,out float value)){var key=node.curve.keys[selectedKey];key.value=Mathf.Max(0,value);node.curve.keys[selectedKey]=key;commit();graphic.Bind(node,start,end,curveCommit);} });
                Action refreshCurve=()=>
                {
                    if(selectedKey>=node.curve.keys.Count)return;var key=node.curve.keys[selectedKey];
                    if(!xInput.isFocused)xInput.SetTextWithoutNotify((depth ? start+key.time*Mathf.Max(1,end-start-1) : key.time).ToString("0.###",Culture));
                    if(!yInput.isFocused)yInput.SetTextWithoutNotify(key.value.ToString("0.###",Culture));
                };
                refreshValues.Add(refreshCurve);refreshCurve();rowY+=38;
                return;
            default:
                if (node.name == "itemId")
                {
                    var itemChoices = GpsSchema.Choices(typeof(ItemSO));
                    Dropdown(new Vector2(inputX, -rowY), itemChoices.Select(entry => GpsSchema.DisplayName(entry.asset)).ToArray(),
                        itemChoices.FindIndex(entry => (int)((ItemSO)entry.asset).item == (int)node.number), selected => { node.number = (int)((ItemSO)itemChoices[selected].asset).item; commit(); });
                    break;
                }
                var input = Input(content, new Vector2(inputX, -rowY), new Vector2(975, 30), node.kind == GpsValueKind.Text ? node.text :
                    (node.number * spec.factor).ToString("0.###", Culture), text =>
                {
                    if (node.kind == GpsValueKind.Text) { node.text = text; commit(); }
                    else if (TryNumber(text, out float value))
                    { node.number = Mathf.Clamp(value / spec.factor, spec.min, spec.max); if (node.kind == GpsValueKind.Integer) node.number = Math.Round(node.number); commit(); }
                });
                refreshValues.Add(() =>
                {
                    if (!input || input.isFocused || node != root) return;
                    var current = GpsSettings.GetValue(record, root.name);
                    if (current != null) input.SetTextWithoutNotify(current.kind == GpsValueKind.Text ? current.text : (current.number * spec.factor).ToString("0.###", Culture));
                });
                break;
        }
        rowY += 37;
    }
    void BuildTests()
    {
        if (Header("test-actions", "Spielzustand", 0))
        {
            ActionRow("Zum Spawn", GpsTestActions.TeleportSpawn); ActionRow("Zum Altar", GpsTestActions.TeleportAltar);
            Label(content, "Aktuelle HP", new Vector2(24, -rowY), new Vector2(600, 30), 21);
            Input(content, new Vector2(650, -rowY), new Vector2(350, 30), (StatsManager.Instance ? StatsManager.Instance.Health : 0).ToString("0.##", Culture),
                text => { if (TryNumber(text, out float health)) GpsTestActions.SetHealth(health); }); rowY += 37;
            foreach (int damage in new[] { 90, 40, 10, 1 }) { int amount = damage; ActionRow(amount + " Schaden", () => StatsManager.Instance?.ApplyDamage(amount)); }
            ActionRow("10 % Energie abziehen", GpsTestActions.DrainEnergy); ActionRow("Energie auffüllen", GpsTestActions.FillEnergy);
        }
        if (Header("test-items", "Items und Powerups", 0))
        {
            var choices = GpsSchema.Choices(typeof(ItemSO)); int selected = 0, amount = 10;
            Dropdown(new Vector2(24, -rowY), choices.Select(entry => GpsSchema.DisplayName(entry.asset)).ToArray(), 0, index => selected = index);
            Input(content, new Vector2(1020, -rowY), new Vector2(230, 30), "10", text => { if (int.TryParse(text, out int value)) amount = Mathf.Clamp(value, 1, 999999); });
            Button(content, "Geben", new Vector2(1280, -rowY), new Vector2(200, 30), () => { if (choices.Count > selected) GpsTestActions.GiveItem((ItemSO)choices[selected].asset, amount); }); rowY += 40;
        }
        if (Header("test-overlays", "Overlays", 0))
        {
            ActionRow("Level-up-Animation testen", () => { Close(); GpsTestActions.PreviewLevelUp(); });
            ActionRow("Endscreen testen", () => { Close(); GpsTestActions.EndScreen(); });
            ActionRow("Ladekalibrierung zurücksetzen", LoadingProgress.ResetCalibration);
            foreach (var artifact in GpsSchema.Choices(typeof(ArtifactTile)))
            { var tile = (ArtifactTile)artifact.asset; ActionRow(tile.displayName, () => { Close(); ArtifactDiscoveryView.ShowArtifact(tile); }); }
        }
#if UNITY_EDITOR
        if (Header("test-editor", "Editor", 0))
            ActionRow(PerformanceMonitorControl.IsEnabled ? "Performanceaufzeichnung stoppen" : "Performance aufzeichnen",
                () => { PerformanceMonitorControl.SetEnabled(!PerformanceMonitorControl.IsEnabled); BuildPage(); });
#endif
    }
    void ActionRow(string label, Action action) { Button(content, label, new Vector2(24, -rowY), new Vector2(720, 32), action); rowY += 40; }
    void GenerateMap()
    {
        var map = Object.FindFirstObjectByType<MapGenerator>(); if (!map) return;
        if (!GpsSettings.ValidateDocument(GpsSettings.Document,out var error)) { status.text=error; return; }
        GpsSettings.ApplyGeneration(map);
        map.GenerateMap();
    }
    static bool TryNumber(string text, out float value) => float.TryParse(text.Replace(',', '.'), NumberStyles.Float, Culture, out value) && GpsCodec.Finite(value);
    static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    TextMeshProUGUI Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect("Label", parent, position, size).gameObject.AddComponent<TextMeshProUGUI>(); text.font = font;
        text.text = value; text.fontSize = fontSize; text.color = new Color(.86f, .87f, .88f); text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; return text;
    }
    Button Button(Transform parent, string label, Vector2 position, Vector2 size, Action action)
    {
        var image = Rect(label, parent, position, size).gameObject.AddComponent<Image>(); image.color = Control;
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var text = Label(image.transform, label, new Vector2(12, 0), size - new Vector2(24, 0), 20);
        button.onClick.AddListener(() => action?.Invoke()); return button;
    }
    TMP_InputField Input(Transform parent, Vector2 position, Vector2 size, string value, Action<string> submit)
    {
        var image = Rect("Input", parent, position, size).gameObject.AddComponent<Image>(); image.color = new Color(.075f, .08f, .09f);
        var input = image.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image;
        var viewport = Rect("Text Area", image.transform, Vector2.zero, size); viewport.gameObject.AddComponent<RectMask2D>();
        input.textViewport = viewport; input.textComponent = Label(viewport, "", new Vector2(8, 0), size - new Vector2(16, 0), 20);
        HomeUi.StyleInputField(input);
        input.SetTextWithoutNotify(value ?? ""); input.onEndEdit.AddListener(text => submit(text)); return input;
    }
    void Dropdown(Vector2 position, string[] labels, int selected, Action<int> submit, float width = 975)
    {
        var image = Rect("Dropdown", content, position, new Vector2(width, 30)).gameObject.AddComponent<Image>(); image.color = Control;
        var dropdown = image.gameObject.AddComponent<TMP_Dropdown>(); dropdown.targetGraphic = image;
        dropdown.captionText = Label(image.transform, "", new Vector2(10, 0), new Vector2(width - 25, 30), 20);
        var template = Rect("Template", image.transform, new Vector2(0, -30), new Vector2(width, 270));
        template.gameObject.AddComponent<Image>().color = Panel;
        var viewport = Rect("Viewport", template, Vector2.zero, template.sizeDelta); viewport.gameObject.AddComponent<RectMask2D>();
        var container = Rect("Content", viewport, Vector2.zero, new Vector2(width, 30));
        var item = Rect("Item", container, Vector2.zero, new Vector2(width, 30));
        var itemImage = item.gameObject.AddComponent<Image>(); itemImage.color = Control;
        var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemImage;
        var check = Rect("Check", item, new Vector2(4, -8), new Vector2(12, 12)).gameObject.AddComponent<Image>(); check.color = Accent; toggle.graphic = check;
        dropdown.itemText = Label(item, "", new Vector2(24, 0), new Vector2(width - 35, 30), 20);
        var listScroll = template.gameObject.AddComponent<ScrollRect>(); listScroll.viewport = viewport; listScroll.content = container;
        listScroll.horizontal = false; listScroll.movementType = ScrollRect.MovementType.Clamped; listScroll.scrollSensitivity = 30;
        dropdown.template = template; template.gameObject.SetActive(false);
        dropdown.AddOptions(labels.ToList()); dropdown.SetValueWithoutNotify(Mathf.Max(0, selected)); dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(value => submit(value)); dropdown.interactable = labels.Length > 0;
    }
}
