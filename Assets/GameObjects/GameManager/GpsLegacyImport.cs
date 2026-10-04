using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class GpsLegacyImport
{
    [Serializable] sealed class RecipeData { public int version, category, outputAmount; public int[] ingredientItems, ingredientAmounts; }
    public static void Import(GpsDocument data,GpsProfile profile,string directory)
    {
        string gps=Path.Combine(directory,"gameplay-settings.json");
        if (File.Exists(gps))
        {
            var old=GameplaySettingsStore.Load(gps,1.5f,out string error);
            if (error==null)
            {
                Set(data,profile,typeof(PlayerBaseStats),"miningSpeed",old.baseDiggingSpeed);
                if (old.hasLightingOverride)
                {
                    string[] fields={"lightingEnabled","daylightStrength","ambientBrightness","downwardLoss","sidewaysLoss","blockLoss","exponentialStrength"};
                    object[] values={old.lighting.enabled,old.lighting.daylight,old.lighting.ambient,old.lighting.downLoss,old.lighting.sideLoss,old.lighting.blockLoss,old.lighting.strength};
                    for(int i=0;i<fields.Length;i++) Set(data,profile,typeof(MapLighting),fields[i],values[i]);
                }
            }
        }
        string tests=Path.Combine(directory,"gameplay-test-settings.json");
        if (File.Exists(tests))
        {
            try
            {
                var old=JsonUtility.FromJson<GameplayTestSettingsData>(File.ReadAllText(tests));
                if (old!=null && old.version>=1 && old.version<=9 && GameplayTestSettings.IsValid(old.diggingMultiplier))
                {
                    if (old.version<3) old.movementMultiplier=1;
                    if (old.version<5) { old.diggingMultiplierEnabled=true; old.movementMultiplierEnabled=true; }
                    if (old.version<2) old.globalLighting=true;
                    if (old.version<6) old.noWeight=false;
                    if (old.version<7) old.infiniteMoney=false;
                    if (old.version<8) old.cameraFollow=true;
                    if (old.version<9) old.smartCursorStrokeWidth=.5f;
                    if (!GameplayTestSettings.IsValidMovementMultiplier(old.movementMultiplier)) old.movementMultiplier=1;
                    if (!GameplayTestSettings.IsValidSmartCursorStrokeWidth(old.smartCursorStrokeWidth)) old.smartCursorStrokeWidth=.5f;
                    if (old.hasMiningHitOffsetOverride && GameplayTestSettings.IsValidMiningHitOffset(old.miningHitOffsetMs)) Set(data,profile,typeof(MinerPlayerVisual),"miningHitOffsetMs",old.miningHitOffsetMs);
                    if (old.dayNightMode!=GameplayDayNightMode.Automatic && Enum.IsDefined(typeof(GameplayDayNightMode),old.dayNightMode))
                    { Set(data,profile,typeof(SkyController),"automaticCycle",false); Set(data,profile,typeof(SkyController),"isNight",old.dayNightMode==GameplayDayNightMode.Night); }
                    old.dayNightMode=GameplayDayNightMode.Automatic; old.hasMiningHitOffsetOverride=false; data.tests=old;
                }
            }
            catch (Exception ex) { Debug.LogWarning("Alte Testeinstellungen konnten nicht importiert werden: "+ex.Message); }
        }
        try
        {
            if(PlayerPrefs.HasKey("debug.starting-resources.v1"))
            {
                var start=JsonUtility.FromJson<StartingResourcesData>(PlayerPrefs.GetString("debug.starting-resources.v1"));
                if(start!=null && start.money>=0 && start.items!=null)
                { start.items=start.items.Where(item=>item.amount>0 && StartingResourcesSettings.Resolve(item.itemId)).ToArray(); data.preferences.startingResources=start; }
            }
            data.preferences.panelBackdropAlpha=Mathf.Clamp01(PlayerPrefs.GetFloat("GameplayDebugPanel.BackdropAlpha",data.preferences.panelBackdropAlpha));
            data.preferences.panelElementAlpha=Mathf.Clamp01(PlayerPrefs.GetFloat("GameplayDebugPanel.ElementAlpha",data.preferences.panelElementAlpha));
            data.preferences.hotbarVerticalOffset=Mathf.Clamp(PlayerPrefs.GetFloat("workbench.hotbarIconVerticalOffset",data.preferences.hotbarVerticalOffset),-48,48);
            data.preferences.hotbarHoldDuration=Mathf.Clamp(PlayerPrefs.GetFloat("hotbar.dragHoldDuration",data.preferences.hotbarHoldDuration),0,2);
            data.preferences.masterVolume=Mathf.Clamp01(PlayerPrefs.GetFloat("settings.audio.master",data.preferences.masterVolume));
            foreach(var record in data.records)
            {
                if(profile.Resolve(record.assetKey) is ArtifactTile artifact && PlayerPrefs.HasKey("artifact.discovery.yOffset."+artifact.name))
                    SetField(record,profile,"discoveryIconYOffset",Mathf.Clamp(PlayerPrefs.GetFloat("artifact.discovery.yOffset."+artifact.name),-300,300));
                if(!(profile.Resolve(record.assetKey) is CraftingRecipe recipe)) continue;
                string iconKey=recipe.FavoriteKey+".icon";
                if(PlayerPrefs.HasKey(iconKey))
                {
                    var layout=JsonUtility.FromJson<CraftingRecipe.RecipeIconLayout>(PlayerPrefs.GetString(iconKey));
                    if(layout.scale.x>0 && layout.scale.y>0) { SetField(record,profile,"customCardIconLayout",true); SetField(record,profile,"cardIconLayout",layout); }
                }
                string savedKey=(string)typeof(CraftingRecipe).GetProperty("SavedSettingsKey",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.GetValue(recipe);
                if(string.IsNullOrEmpty(savedKey) || !PlayerPrefs.HasKey(savedKey)) continue;
                var saved=JsonUtility.FromJson<RecipeData>(PlayerPrefs.GetString(savedKey));
                if(saved==null || saved.version!=1 || saved.outputAmount<1 || saved.ingredientItems==null || saved.ingredientAmounts==null ||
                    saved.ingredientItems.Length==0 || saved.ingredientItems.Length!=saved.ingredientAmounts.Length || !Enum.IsDefined(typeof(CraftingRecipe.RecipeCategory),saved.category)) continue;
                var ingredients=saved.ingredientItems.Select((item,index)=>new CraftingIngredient(StartingResourcesSettings.Resolve(item),saved.ingredientAmounts[index])).ToArray();
                if(ingredients.Any(entry=>!entry.item || entry.item==recipe.output || entry.amount<1)) continue;
                SetField(record,profile,"category",(CraftingRecipe.RecipeCategory)saved.category); SetField(record,profile,"outputAmount",saved.outputAmount); SetField(record,profile,"ingredients",ingredients);
            }
        }
        catch(Exception ex) { Debug.LogWarning("Alte GPS-Werte konnten nicht vollständig importiert werden: "+ex.Message); }
        data.legacyMigrated=true;
    }
    static void Set(GpsDocument data,GpsProfile profile,Type type,string name,object value)
    { foreach(var record in data.records.Where(record=>record.type==type.AssemblyQualifiedName)) SetField(record,profile,name,value); }
    static void SetField(GpsRecord record,GpsProfile profile,string name,object value)
    {
        int index=record.fields.FindIndex(field=>field.name==name); if(index<0)return;
        record.fields[index]=GpsCodec.Read(name,GpsCodec.ResolveType(record.fields[index].type),value,profile.Key);
    }
}
