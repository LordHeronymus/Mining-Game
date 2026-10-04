using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class UpgradeTileArt
{
    static readonly Dictionary<int, Sprite> icons = new();
    static Material darkWood, darkHover;
    static readonly List<Texture2D> rankTextures = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var icon in icons.Values) if (icon) Object.Destroy(icon);
        icons.Clear();
        foreach(var texture in rankTextures) if(texture) Object.Destroy(texture);
        rankTextures.Clear();
        if(darkWood) Object.Destroy(darkWood);
        if(darkHover) Object.Destroy(darkHover);
    }

    public static Sprite Icon(string id)
    {
        if (id == "money")
        {
            var coins = Resources.LoadAll<Sprite>("GameOverCoin");
            return coins.Length > 0 ? coins[0] : null;
        }
        if (id == "ladders" || id == "torches" || id == "capacity" || id == "reach") {
            var item = id switch { "ladders" => Item.Ladder, "torches" => Item.Torche, "capacity" => Item.Backpack, _ => Item.BridgePart };
            foreach (var recipe in ExoticCatalog.AllRecipes)
                if (recipe && recipe.output && recipe.output.item == item) return recipe.output.icon;
        }
        int part = id switch {
            "health" => 0, "energy" => 1, "money" => 2, "movement" => 3,
            "jump" => 4, "mining" => 5, "capacity" => 2, _ => 5
        };
        if (icons.TryGetValue(part, out var sprite) && sprite) return sprite;
        var texture = Resources.Load<Texture2D>("Progression/UpgradeIcons");
        if (!texture) return null;
        float width = texture.width / 3f, height = texture.height / 2f;
        sprite = Sprite.Create(texture, new Rect(part % 3 * width, (1 - part / 3) * height, width, height),
            new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        sprite.name = "Upgrade Icon " + part;
        icons[part] = sprite;
        return sprite;
    }

    public static void DarkenCard(Button card)
    {
        if(!darkWood) {
            darkWood=CardMaterial("SelectionCardNormal");
            darkHover=CardMaterial("SelectionCardHover");
        }
        card.GetComponent<Image>().material=darkWood;
        card.transform.Find("Highlight").GetComponent<Image>().material=darkHover;
    }

    static Material CardMaterial(string part)
    {
        var sprite=HomeUi.Sprite(part);var rect=sprite.rect;var texture=sprite.texture;
        var material=new Material(Resources.Load<Shader>("Progression/UpgradeCardWood")){name="Upgrade Dark Wood "+part,hideFlags=HideFlags.DontSave};
        material.SetVector("_WoodUvRect",new Vector4(rect.x/texture.width,rect.y/texture.height,rect.width/texture.width,rect.height/texture.height));
        material.SetColor("_WoodTone",new Color(.48f,.52f,.5f,1));
        return material;
    }

    public static void StyleTab(Button tab)
    {
        tab.GetComponent<HomeButtonFeedback>().enabled=false;
        tab.gameObject.AddComponent<UpgradeTabVisual>();
        var text=tab.GetComponentInChildren<TMPro.TextMeshProUGUI>();text.rectTransform.sizeDelta=new Vector2(210,34);text.fontSizeMax=text.fontSize=26;
    }

    public static void PointIcon(Transform parent,Vector2 position,bool comfort)
    {
        var icon=HomeUi.Image(comfort?"Comfort Crystal":"Power Crystal",parent,position,new Vector2(20,36));
        icon.sprite=AtlasSprite(comfort?203:202);icon.preserveAspect=true;
    }

    public static Sprite AtlasSprite(int key)
    {
        if(icons.TryGetValue(key,out var cached)&&cached)return cached;
        bool gem=key>=202;
        var texture=Resources.Load<Texture2D>(gem?"Progression/PointCrystals":"Progression/UpgradeTabs");
        if(!texture)return null;
        Rect region;
        if(gem)region=new Rect(key==202?153:731, 174, 440, 866);
        else region=new Rect(50,1284-(key==199?116:key==200?504:902)-268,1120,268);
        float sx=texture.width/(gem?1323f:1225f),sy=texture.height/(gem?1189f:1284f);
        region=new Rect(region.x*sx,region.y*sy,region.width*sx,region.height*sy);
        cached=Sprite.Create(texture,region,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
        icons[key]=cached;return cached;
    }

    public static Sprite RankSprite(bool filled)
    {
        int key=filled?101:100;
        if(icons.TryGetValue(key,out var sprite)&&sprite)return sprite;
        var texture=new Texture2D(24,16,TextureFormat.RGBA32,false){name=filled?"Gold Rank":"Steel Rank",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        for(int y=0;y<16;y++)for(int x=0;x<24;x++){
            bool edge=x==0||y==0||x==23||y==15;
            bool bevel=x<3||y<3||x>20||y>12;
            Color color=filled?Color.Lerp(new Color(.87f,.43f,.02f),new Color(1,.87f,.3f),y/15f):Color.Lerp(new Color(.13f,.15f,.15f),new Color(.27f,.29f,.29f),y/15f);
            if(bevel)color=filled?(y>12||x<3?new Color(1,.95f,.6f):new Color(.65f,.36f,.06f)):(y>12||x<3?new Color(.49f,.51f,.5f):new Color(.08f,.09f,.09f));
            if(edge)color=filled?new Color(.35f,.2f,.03f):new Color(.035f,.035f,.03f);
            texture.SetPixel(x,y,color);
        }
        texture.Apply();rankTextures.Add(texture);
        sprite=Sprite.Create(texture,new Rect(0,0,24,16),new Vector2(.5f,.5f),100);icons[key]=sprite;return sprite;
    }
}

public sealed class UpgradeTabVisual : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    Image normal,hover,selected; HomeButtonFeedback state; bool over;
    void Awake(){
        state=GetComponent<HomeButtonFeedback>();normal=GetComponent<Image>();
        hover=transform.Find("Highlight").GetComponent<Image>();selected=transform.Find("Selection").GetComponent<Image>();
        normal.sprite=UpgradeTileArt.AtlasSprite(199);hover.sprite=UpgradeTileArt.AtlasSprite(200);selected.sprite=UpgradeTileArt.AtlasSprite(201);
        normal.type=hover.type=selected.type=Image.Type.Simple;
    }
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data)=>over=true;
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data)=>over=false;
    void OnDisable(){over=false;if(hover)hover.color=Color.clear;}
    void Update(){
        float step=1-Mathf.Exp(-9*Time.unscaledDeltaTime);
        bool focus=UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject==gameObject;
        hover.color=new Color(1,1,1,Mathf.Lerp(hover.color.a,over||focus?1:0,step));
        selected.color=new Color(1,1,1,Mathf.Lerp(selected.color.a,state.primary?1:0,step));
    }
}
