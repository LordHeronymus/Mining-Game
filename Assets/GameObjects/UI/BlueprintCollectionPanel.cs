using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A collection view reached through the timeline; the owner keeps pause/input ownership.
public sealed class BlueprintCollectionPanel : MonoBehaviour
{
    RectTransform layout, content;
    ScrollRect scroll;
    TextMeshProUGUI level;
    Action onBack;
    bool readOnly, refresh, closing;
    float height;
    static readonly Color Muted = new Color32(193, 178, 152, 255);

    internal static BlueprintCollectionPanel Create(Transform owner, bool readOnly, Action onBack)
    {
        var root=HomeUi.Panel("Blueprint Collection",owner);
        var panel=root.gameObject.AddComponent<BlueprintCollectionPanel>();
        panel.readOnly=readOnly; panel.onBack=onBack; panel.Build(); return panel;
    }

    void Build()
    {
        layout=HomeUi.Rect("Blueprints Layout",transform,Vector2.zero,new Vector2(1540,940));
        HomeUi.Image("Board Backing",layout,Vector2.zero,new Vector2(1410,830)).color=new Color32(23,12,7,255);
        var board=HomeUi.Image("Board",layout,Vector2.zero,new Vector2(1500,900),"Panel");
        board.pixelsPerUnitMultiplier=1.5f; board.raycastTarget=true;
        Text("Title",layout,"Baupläne",new Vector2(-180,356),new Vector2(935,70),48,TextAlignmentOptions.Left);
        level=Text("Level",layout,"",new Vector2(492,350),new Vector2(318,40),27,TextAlignmentOptions.Right);
        level.color=ProgressionArt.Gold;
        HomeUi.Image("Header Rule",layout,new Vector2(0,307),new Vector2(1310,1)).color=new Color32(132,89,43,180);
        var viewport=HomeUi.Rect("Blueprints Viewport",layout,new Vector2(0,-3),new Vector2(1320,580));
        viewport.gameObject.AddComponent<RectMask2D>().softness=new Vector2Int(0,12);
        viewport.gameObject.AddComponent<Image>().color=Color.clear;
        content=HomeUi.Rect("Blueprint Tiles",viewport,Vector2.zero,Vector2.zero);
        content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1);
        scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.content=content;scroll.viewport=viewport;
        scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;
        var track=HomeUi.Image("Scrollbar",layout,new Vector2(677,-3),new Vector2(7,580));
        track.color=new Color(0,0,0,.4f);track.raycastTarget=true;
        var area=HomeUi.Rect("Sliding Area",track.transform,Vector2.zero,Vector2.zero);HomeUi.Stretch(area);
        var handle=HomeUi.Image("Handle",area,Vector2.zero,Vector2.zero);HomeUi.Stretch(handle.rectTransform);
        handle.color=ProgressionArt.Gold;handle.raycastTarget=true;
        var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle.rectTransform;bar.targetGraphic=handle;
        bar.direction=Scrollbar.Direction.BottomToTop;bar.navigation=new Navigation {mode=Navigation.Mode.None};
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        var back=HomeUi.Button("Blueprints Back",layout,"Zurück",new Vector2(-483,-370),new Vector2(340,70),Close);
        MetaProgression.Changed+=OnProgressChanged;
        Rebuild();scroll.verticalNormalizedPosition=1;
        HomeUi.Fit(layout,new Vector2(1600,980));ProgressionArt.Navigation(transform);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }

    void Rebuild()
    {
        float position=scroll.verticalNormalizedPosition;
        foreach(Transform child in content){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        level.text="Level "+MetaProgression.Level; height=12;
        var essentials=new List<CraftingRecipe>();
        foreach(var recipe in ExoticCatalog.AllRecipes) if(recipe && recipe.output && !recipe.exotic &&
            recipe.output.item!=Item.Nails && recipe.output.item!=Item.Fabric &&
            recipe.output.item!=Item.Rope && recipe.output.item!=Item.Steel && !essentials.Contains(recipe)) essentials.Add(recipe);
        var pickaxes=new List<CraftingRecipe>();var energy=new List<CraftingRecipe>();
        var carrying=new List<CraftingRecipe>();var other=new List<CraftingRecipe>();
        foreach(var recipe in essentials)
        {
            var item=recipe.output;
            if((int)item.item>=(int)Item.CopperPickaxe && (int)item.item<=(int)Item.DiamondPickaxe) pickaxes.Add(recipe);
            else if(item.energyCapacityUpgradeLevel>0) energy.Add(recipe);
            else if(item.carryingCapacityUpgradeLevel>0) carrying.Add(recipe);
            else other.Add(recipe);
        }
        pickaxes.Sort((a,b)=>a.output.item.CompareTo(b.output.item));
        energy.Sort((a,b)=>a.output.energyCapacityUpgradeLevel.CompareTo(b.output.energyCapacityUpgradeLevel));
        carrying.Sort((a,b)=>a.output.carryingCapacityUpgradeLevel.CompareTo(b.output.carryingCapacityUpgradeLevel));
        other.Sort((a,b)=>string.Compare(a.output.displayName,b.output.displayName,StringComparison.OrdinalIgnoreCase));
        ExoticSection();
        Section("Spitzhacken",pickaxes); Section("Energie-Upgrades",energy);
        Section("Traglast-Upgrades",carrying); Section("Weitere essenzielle Baupläne",other);
        content.sizeDelta=new Vector2(0,Mathf.Max(scroll.viewport.rect.height,height));
        Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=position;
    }

    void ExoticSection()
    {
        var entries=ExoticBlueprintCatalog.Entries;
        var heading=At("Section Exotische Baupläne",new Vector2(0,-height-18),new Vector2(1310,36));
        Text("Heading",heading,"Exotische Baupläne",new Vector2(-375,0),new Vector2(550,36),27,TextAlignmentOptions.Left).color=ExoticDesign.Cyan;
        height+=48;
        for(int index=0;index<entries.Count;index++)
        {
            var entry=entries[index];
            var tile=At("Blueprint "+entry.Id,new Vector2((index%3-1)*446,-height-index/3*240-110),new Vector2(420,220));
            if(!entry.Preview)BuildTile(tile,entry.Recipe);
            else
            {
                var frame=HomeUi.Image("Frame",tile,Vector2.zero,tile.sizeDelta,"SelectionCardNormal");frame.pixelsPerUnitMultiplier=5.5f;frame.color=new Color(.52f,.79f,.9f,1);
                var icon=HomeUi.Image("Icon",tile,new Vector2(0,40),new Vector2(103,91));icon.sprite=entry.Icon;icon.preserveAspect=true;
                icon.color=MetaProgression.Level>=entry.Level?Color.white:new Color(.46f,.55f,.6f,1);
                Text("Unlock Level",tile,"Level "+entry.Level,new Vector2(-100,73),new Vector2(150,31),23,TextAlignmentOptions.Left).color=ExoticDesign.Cyan;
                ExoticDesign.AddSeal(tile,new Vector2(166,73),26);
                var name=Text("Name",tile,entry.Name,new Vector2(0,-30),new Vector2(350,54),29,TextAlignmentOptions.Center);name.textWrappingMode=TextWrappingModes.Normal;name.fontSizeMin=22;
                Text("State",tile,"Vorschau",new Vector2(0,-79),new Vector2(350,29),22,TextAlignmentOptions.Center).color=Muted;
            }
            ExoticBlueprintTooltip.Attach(tile.gameObject,entry);
        }
        height+=((entries.Count+2)/3)*240+14;
    }
    void Section(string title,List<CraftingRecipe> recipes)
    {
        if(recipes.Count==0)return;
        var heading=At("Section "+title,new Vector2(0,-height-18),new Vector2(1310,36));
        Text("Heading",heading,title,new Vector2(-375,0),new Vector2(550,36),27,TextAlignmentOptions.Left).color=title.StartsWith("Exotische")?ExoticDesign.Cyan:ProgressionArt.Gold;
        height+=48;
        for(int index=0;index<recipes.Count;index++){
            var recipe=recipes[index];
            var tile=At((recipe.exotic?"Blueprint "+recipe.exoticId:"Essential "+recipe.name),new Vector2((index%3-1)*446,-height-index/3*240-110),new Vector2(420,220));
            BuildTile(tile,recipe);
        }
        height+=((recipes.Count+2)/3)*240+14;
    }

    RectTransform At(string name,Vector2 position,Vector2 size)
    {
        var rect=HomeUi.Rect(name,content,position,size);rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);return rect;
    }

    void BuildTile(RectTransform tile,CraftingRecipe recipe)
    {
        bool available=!recipe.exotic || ExoticCatalog.IsAvailable(recipe);
        bool learned=recipe.exotic && readOnly && RecipeUnlocks.IsUnlocked(recipe);
        var frame=HomeUi.Image("Frame",tile,Vector2.zero,tile.sizeDelta,"SelectionCardNormal");frame.pixelsPerUnitMultiplier=5.5f;
        if(recipe.exotic) frame.color=new Color(.52f,.79f,.9f,1);
        var icon=HomeUi.Image("Icon",tile,new Vector2(0,40),new Vector2(103,91));
        icon.sprite=recipe.output.icon;icon.preserveAspect=true;
        icon.color=available || learned?Color.white:new Color(.46f,.55f,.6f,1);
        if(recipe.exotic){
            Text("Unlock Level",tile,"Level "+recipe.metaUnlockLevel,new Vector2(-100,73),new Vector2(150,31),23,TextAlignmentOptions.Left).color=ExoticDesign.Cyan;
            ExoticDesign.AddSeal(tile,new Vector2(166,73),26);
        }
        var name=Text("Name",tile,recipe.output.displayName,new Vector2(0,-30),new Vector2(350,54),29,TextAlignmentOptions.Center);
        name.textWrappingMode=TextWrappingModes.Normal;name.fontSizeMin=22;
        string state=recipe.exotic ? learned?"Im Run gelernt":available?"Im Fundpool":"Gesperrt" : "Ohne Levelbindung";
        Text("State",tile,state,new Vector2(0,-79),new Vector2(350,29),22,TextAlignmentOptions.Center).color=recipe.exotic && (available || learned)?ExoticDesign.Cyan:Muted;
    }

    static TextMeshProUGUI Text(string name,Transform parent,string value,Vector2 position,Vector2 size,float font,TextAlignmentOptions alignment)
    {
        var text=ProgressionArt.Text(name,parent,value,position,size,font);text.richText=false;text.alignment=alignment;return text;
    }
    void OnProgressChanged()=>refresh=true;
    void OnRectTransformDimensionsChange()=>HomeUi.Fit(layout,new Vector2(1600,980));
    void Update()
    {
        if(refresh){refresh=false;Rebuild();}
        float distance=Mathf.Max(0,content.rect.height-scroll.viewport.rect.height);
        if(distance>0){
            float move=(Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0);
            scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+move*400*Time.unscaledDeltaTime/distance);
            if(Input.GetKeyDown(KeyCode.PageDown))scroll.verticalNormalizedPosition-=scroll.viewport.rect.height/distance;
            if(Input.GetKeyDown(KeyCode.PageUp))scroll.verticalNormalizedPosition+=scroll.viewport.rect.height/distance;
        }
        if(Input.GetKeyDown(KeyCode.Escape) && RunPauseMenu.InputConsumedFrame!=Time.frameCount){RunPauseMenu.ConsumeInput();Close();}
    }
    public void Close()
    {
        if(closing)return;closing=true;gameObject.SetActive(false);Destroy(gameObject);onBack?.Invoke();
    }
    void OnDestroy()=>MetaProgression.Changed-=OnProgressChanged;
}
