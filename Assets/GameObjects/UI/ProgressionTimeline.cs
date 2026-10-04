using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Small reusable art pieces; all labels, rewards and the filled distance are live data.
public static class ProgressionArt
{
    static readonly Dictionary<int, Sprite> sprites = new();
    static readonly Rect[] regions = {
        new(82,521,284,281), new(558,550,216,215), new(1001,551,215,215), new(1417,510,262,299),
        new(31,178,385,128), new(462,206,409,68), new(922,220,375,41), new(1405,55,294,372)
    };
    public static readonly Color Gold = new Color32(255, 201, 92, 255);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { foreach (var sprite in sprites.Values) if (sprite) UnityEngine.Object.Destroy(sprite); sprites.Clear(); }
    public static Sprite Sprite(int part)
    {
        if (sprites.TryGetValue(part, out var sprite) && sprite) return sprite;
        var texture = Resources.Load<Texture2D>("Progression/TimelineAtlas");
        if (!texture) return null;
        var rect = regions[part];
        rect = new Rect(rect.x * texture.width / 1774f, rect.y * texture.height / 887f,
            rect.width * texture.width / 1774f, rect.height * texture.height / 887f);
        sprite = UnityEngine.Sprite.Create(texture, rect, new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect);
        sprite.name = "Progression Part " + part; sprites[part] = sprite; return sprite;
    }
    public static Image Image(string name, Transform parent, Vector2 position, Vector2 size, int part)
    {
        var image = HomeUi.Image(name, parent, position, size); image.sprite = Sprite(part); return image;
    }
    public static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float font = 28)
    {
        var label = HomeUi.Label(name, parent, value, position, size, font);
        label.textWrappingMode = TextWrappingModes.NoWrap; label.enableAutoSizing = true;
        label.fontSizeMin = 16; label.fontSizeMax = font; label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    public static void Navigation(Transform root)
    {
        var buttons = new List<Button>();
        foreach (var b in root.GetComponentsInChildren<Button>()) if (b.IsInteractable()) buttons.Add(b);
        for (int i=0; i<buttons.Count; i++) {
            var previous = buttons[(i+buttons.Count-1)%buttons.Count]; var next = buttons[(i+1)%buttons.Count];
            buttons[i].navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnLeft=previous, selectOnUp=previous, selectOnRight=next, selectOnDown=next };
        }
    }
}

public sealed class ProgressionTimeline : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
{
    RectTransform rewards;
    TextMeshProUGUI levelLabel;
    Image xpFill;
    Button previous, next;
    readonly List<int> visibleLevels = new();
    readonly List<int> milestones = new();
    long displayedXp = -1;
    int displayedLevel = -1, focusLevel, newlyFromLevel = 1000;
    bool resultMode;
    Vector2 dragPoint;
    public int DisplayedLevel => displayedLevel;
    public float XpFraction => xpFill ? xpFill.fillAmount : 0;
    public IReadOnlyList<int> VisibleLevels => visibleLevels;

    public static ProgressionTimeline Create(Transform parent, Vector2 position, long xp, bool result = false, int newFrom = 1000)
    {
        var root = HomeUi.Rect("Progression Timeline", parent, position, new Vector2(1570,570));
        var hit = root.gameObject.AddComponent<Image>(); hit.color=Color.clear; hit.raycastTarget=true;
        var timeline = root.gameObject.AddComponent<ProgressionTimeline>();
        timeline.resultMode=result; timeline.newlyFromLevel=newFrom; timeline.Build(); timeline.SetExperience(xp,true);
        return timeline;
    }
    void Build()
    {
        // Track first, badge second: the badge's right lip overlaps the track's left cap.
        var track = ProgressionArt.Image("XP Track",transform,new Vector2(-355,230),new Vector2(405,38),5);
        xpFill=ProgressionArt.Image("XP Fill",transform,new Vector2(-355,230),new Vector2(379,20),6);
        xpFill.type=Image.Type.Filled; xpFill.fillMethod=Image.FillMethod.Horizontal; xpFill.fillOrigin=0;
        var badge=ProgressionArt.Image("Level Badge",transform,new Vector2(-642,230),new Vector2(215,72),4);
        badge.raycastTarget=true;
        var home=badge.gameObject.AddComponent<Button>(); home.targetGraphic=badge; home.transition=Selectable.Transition.None;
        home.onClick.AddListener(()=>Focus(displayedLevel));
        levelLabel=ProgressionArt.Text("Current Level",badge.transform,"",Vector2.zero,new Vector2(176,52),34);
        // Center the entire heading in the space from the XP tip to the board's inner content edge.
        float trackEnd = track.rectTransform.anchoredPosition.x + track.rectTransform.rect.xMax;
        float titleCenter = (trackEnd + ((RectTransform)transform).rect.xMax) * .5f;
        var title = ProgressionArt.Text("Title",transform,"Fortschritt",new Vector2(titleCenter,230),new Vector2(540,90),64);
        title.enableAutoSizing = false;
        title.fontSize = title.fontSizeMin = title.fontSizeMax = 64;
        float ruleOffset = title.GetPreferredValues("Fortschritt", Mathf.Infinity, Mathf.Infinity).x * .5f + 28 + 50;
        foreach (float x in new[]{titleCenter-ruleOffset,titleCenter+ruleOffset})
            HomeUi.Image("Title Rule",transform,new Vector2(x,230),new Vector2(100,2)).color=ProgressionArt.Gold;
        rewards=HomeUi.Rect("Rewards",transform,new Vector2(0,-15),new Vector2(1430,430));
        previous=HomeUi.Button("Earlier Levels",transform,"‹",new Vector2(-774,-200),new Vector2(72,62),()=>Browse(-1));
        next=HomeUi.Button("Later Levels",transform,"›",new Vector2(774,-200),new Vector2(72,62),()=>Browse(1));
        StyleArrow(previous,-1); StyleArrow(next,1);
    }
    static void StyleArrow(Button button,int direction)
    {
        button.GetComponentInChildren<TMP_Text>().text="";
        var feedback=button.GetComponent<HomeButtonFeedback>();
        feedback.normalPart="SelectionCardNormal";feedback.activePart="SelectionCardHover";
        feedback.pressOnlySelection=true;
        var image=button.GetComponent<Image>();image.sprite=HomeUi.Sprite("SelectionCardNormal");image.pixelsPerUnitMultiplier=image.sprite.rect.height/62;
        var symbol=HomeUi.Rect("Arrow",button.transform,Vector2.zero,new Vector2(24,26)).gameObject.AddComponent<ProgressionSymbol>();
        symbol.direction=direction;symbol.raycastTarget=false;symbol.color=ProgressionArt.Gold;
    }
    public void SetExperience(long xp, bool recenter=false)
    {
        xp=Math.Max(0,xp); int level=MetaProgressionCatalog.LevelForXp(xp);
        if (xp==displayedXp && !recenter) return;
        bool changed=level!=displayedLevel; displayedXp=xp; displayedLevel=level;
        levelLabel.text="Level "+level;
        long cost=MetaProgressionCatalog.LevelCost(level);
        xpFill.fillAmount=cost==0 ? 1 : Mathf.Clamp01((float)((double)(xp-MetaProgressionCatalog.XpForLevel(level))/cost));
        if (changed || recenter) Focus(level);
    }
    public void Focus(int level)
    {
        focusLevel=Mathf.Clamp(level,1,MetaProgressionCatalog.MaxLevel);
        milestones.Clear(); milestones.Add(1);
        for(int l=2;l<=MetaProgressionCatalog.MaxLevel;l++)
            if(PowerAt(l)>0 || ComfortAt(l)>0 || RecipesAt(l).Count>0 || l==focusLevel || l==displayedLevel || l==1000) milestones.Add(l);
        visibleLevels.Clear();
        int index=milestones.BinarySearch(focusLevel);
        int past=index>0 ? milestones[index-1] : 0;
        if(!resultMode) foreach(var recipe in ExoticBlueprintCatalog.Entries)
            if(recipe.Level<focusLevel) past=recipe.Level;
        if(resultMode && index>1) visibleLevels.Add(milestones[index-2]);
        if(past>0) visibleLevels.Add(past);
        visibleLevels.Add(focusLevel);
        for(int i=index+1;i<milestones.Count && visibleLevels.Count<5;i++) visibleLevels.Add(milestones[i]);
        int last=visibleLevels[visibleLevels.Count-1], future=0;
        foreach(var recipe in ExoticBlueprintCatalog.Entries) if(recipe.Level>last) { future=recipe.Level; break; }
        if(future==0) { int p=milestones.BinarySearch(last)+1; if(p<milestones.Count) future=milestones[p]; }
        if(future>last) visibleLevels.Add(future);
        previous.interactable=focusLevel>1; next.interactable=focusLevel<1000;
        Draw();
        var profile=GetComponentInParent<MetaProgressionPanel>(); var result=GetComponentInParent<ExpeditionResultPanel>();
        ProgressionArt.Navigation(profile?profile.transform:result?result.transform:transform);
    }
    public static int PowerAt(int level)=>MetaProgressionCatalog.EarnedPowerPoints(level)-MetaProgressionCatalog.EarnedPowerPoints(level-1);
    public static int ComfortAt(int level)=>MetaProgressionCatalog.EarnedComfortPoints(level)-MetaProgressionCatalog.EarnedComfortPoints(level-1);
    static List<ExoticBlueprintInfo> RecipesAt(int level)
    {
        var result=new List<ExoticBlueprintInfo>(); foreach(var recipe in ExoticBlueprintCatalog.Entries) if(recipe.Level==level) result.Add(recipe); return result;
    }
    void Draw()
    {
        for(int i=rewards.childCount-1;i>=0;i--) { var child=rewards.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
        const float railY=-185, width=1320;
        float step=visibleLevels.Count>1 ? width/(visibleLevels.Count-1) : 0;
        for(int i=0;i<visibleLevels.Count-1;i++) {
            int left=visibleLevels[i], right=visibleLevels[i+1]; float x=-width*.5f+i*step;
            // Large gaps are deliberately dotted so spacing never suggests proportional levels.
            if(right-left>1) {
                for(int dot=1;dot<8;dot++) ProgressionArt.Image("Gap",rewards,new Vector2(x+step*dot/8,railY),Vector2.one*7,right<=displayedLevel?1:2);
            } else {
                ProgressionArt.Image("Rail",rewards,new Vector2(x+step*.5f,railY),new Vector2(step+4,18),5);
                float fill=right<=displayedLevel?1:0;
                if(fill>0) ProgressionArt.Image("Reached Rail",rewards,new Vector2(x+step*.5f,railY),new Vector2(step+4,8),6);
            }
        }
        for(int i=0;i<visibleLevels.Count;i++) {
            int level=visibleLevels[i]; float x=visibleLevels.Count==1?0:-width*.5f+i*step;
            var root=HomeUi.Rect("Milestone "+level,rewards,new Vector2(x,0),new Vector2(220,400));
            bool reached=level<=displayedLevel, active=level==displayedLevel;
            ProgressionArt.Image("Stem",root,new Vector2(0,-159),new Vector2(5,42),6);
            ProgressionArt.Image("Marker",root,new Vector2(0,railY),active?new Vector2(70,78):new Vector2(48,48),active?3:reached?1:2);
            if(reached && !active) {
                var check=HomeUi.Rect("Reached",root,new Vector2(0,railY),new Vector2(23,23)).gameObject.AddComponent<ProgressionSymbol>();
                check.color=HomeUi.Cream;check.raycastTarget=false;
            }
            ProgressionArt.Text("Level",root,"Level "+level,new Vector2(0,-126),new Vector2(213,40),28);
            var recipes=RecipesAt(level);
            if(recipes.Count>0) {
                // Current catalog has one recipe per level; stack additional entries if extended.
                for(int r=0;r<recipes.Count;r++) {
                    var recipe=recipes[r];
                    var card=ProgressionArt.Image("Blueprint "+recipe.Id,root,new Vector2(0,34+r*8),new Vector2(207,261),7);
                    if(!reached && level!=NextBlueprint()) card.color=new Color(.63f,.69f,.72f,1);
                    ExoticBlueprintTooltip.Attach(card.gameObject,recipe);
                    ExoticDesign.AddSeal(card.transform,new Vector2(-74,99),24);
                    ProgressionArt.Text("Rarity",card.transform,"EXOTISCH",new Vector2(16,98),new Vector2(144,27),19).color=ExoticDesign.Cyan;
                    var icon=HomeUi.Image("Item",card.transform,new Vector2(0,18),new Vector2(143,137)); icon.sprite=recipe.Icon; icon.preserveAspect=true;
                    ProgressionArt.Text("Name",card.transform,recipe.Name,new Vector2(0,-66),new Vector2(181,35),26);
                    ProgressionArt.Text("Kind",card.transform,"Bauplan",new Vector2(0,-94),new Vector2(164,27),21);
                    if(resultMode && reached && level>newlyFromLevel) {
                        ProgressionArt.Text("New",card.transform,"NEU",new Vector2(61,65),new Vector2(61,25),20).color=ProgressionArt.Gold;
                        ProgressionArt.Text("Pool Unlock",root,recipe.Preview?"Vorschau":"Im Fundpool",new Vector2(0,-103),new Vector2(200,24),20).color=ExoticDesign.Cyan;
                    }
                }
                if(PowerAt(level)>0 || ComfortAt(level)>0)
                    ProgressionArt.Text("Point Reward",root,PowerAt(level)>0?"+1 Powerup-Punkt":"+1 Komfortpunkt",new Vector2(0,187),new Vector2(220,29),21).color=ProgressionArt.Gold;
            } else if(PowerAt(level)>0 || ComfortAt(level)>0) {
                ProgressionArt.Image("Point",root,new Vector2(0,7),new Vector2(94,94),0);
                ProgressionArt.Text("Point Amount",root,"+1",new Vector2(0,-57),new Vector2(120,34),28);
                ProgressionArt.Text("Point Kind",root,PowerAt(level)>0?"Powerup-Punkt":"Komfortpunkt",new Vector2(0,-84),new Vector2(216,31),24);
            } else if(level==1000) ProgressionArt.Text("Maximum",root,"Maximum",new Vector2(0,0),new Vector2(213,40),31).color=ProgressionArt.Gold;
        }
    }
    int NextBlueprint() { foreach(var recipe in ExoticBlueprintCatalog.Entries) if(recipe.Level>displayedLevel) return recipe.Level; return -1; }
    public void Browse(int direction)
    {
        Focus(Mathf.Clamp(focusLevel + Math.Sign(direction) * 4, 1, MetaProgressionCatalog.MaxLevel));
    }
    public void OnScroll(PointerEventData e) { if(Mathf.Abs(e.scrollDelta.y)>.01f) Browse(e.scrollDelta.y<0?1:-1); }
    public void OnBeginDrag(PointerEventData e) { RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,e.position,e.pressEventCamera,out dragPoint); }
    public void OnDrag(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,e.position,e.pressEventCamera,out var point);
        if(Mathf.Abs(point.x-dragPoint.x)<85) return; Browse(point.x<dragPoint.x?1:-1); dragPoint=point;
    }
}

// Geometric navigation/check marks avoid missing glyphs in the game's title font.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ProgressionSymbol : MaskableGraphic
{
    public int direction;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); var r=rectTransform.rect;
        if(direction!=0) {
            mesh.AddVert(new Vector3(r.center.x+direction*r.width*.45f,r.center.y),color,Vector2.zero);
            mesh.AddVert(new Vector3(r.center.x-direction*r.width*.35f,r.yMax),color,Vector2.zero);
            mesh.AddVert(new Vector3(r.center.x-direction*r.width*.35f,r.yMin),color,Vector2.zero);mesh.AddTriangle(0,1,2);
        } else {
            Stroke(mesh,new Vector2(r.xMin,r.center.y),new Vector2(r.center.x-r.width*.1f,r.yMin+r.height*.17f),r.width*.16f);
            Stroke(mesh,new Vector2(r.center.x-r.width*.1f,r.yMin+r.height*.17f),new Vector2(r.xMax,r.yMax-r.height*.12f),r.width*.16f);
        }
    }
    void Stroke(VertexHelper mesh,Vector2 a,Vector2 b,float width)
    {
        var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;int i=mesh.currentVertCount;
        mesh.AddVert(a+n,color,Vector2.zero);mesh.AddVert(b+n,color,Vector2.zero);mesh.AddVert(b-n,color,Vector2.zero);mesh.AddVert(a-n,color,Vector2.zero);
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
}
