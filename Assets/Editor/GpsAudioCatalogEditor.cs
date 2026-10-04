using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GpsAudioCatalogEditor
{
    public static void Populate(GpsProfile profile,GpsDocument data)
    {
        var record=data.records.Find(r=>r.type==typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);
        if(record==null)return;
        var root=record.fields.Find(v=>v.name=="clips");
        var version=record.fields.Find(v=>v.name=="catalogVersion");
        bool migrate=version==null || version.number<1;
        var home=data.records.Find(r=>r.type==typeof(LoadingAudioSettingsAsset).AssemblyQualifiedName)?.fields.Find(v=>v.name=="homeClips");
        foreach(string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            if(path.Contains("_SceneBackups"))continue;
            foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AudioClip>())
            {
                string key=GpsProfileEditor.Register(clip,profile);
                var old=root.children.Find(e=>e.children.Find(v=>v.name=="clip")?.text==key);
                var tune=old==null ? new AudioClipTuningEntry{clip=clip} : (AudioClipTuningEntry)GpsCodec.Write(old,profile.Resolve);
                tune.category=Category(path);tune.displayName=Display(clip.name);
                if(migrate)
                {
                    var legacy=home?.children.Find(e=>e.children.Find(v=>v.name=="clip")?.text==key);
                    if(legacy!=null)
                    {
                        var previous=(HomeAudioClipTuning)GpsCodec.Write(legacy,profile.Resolve);
                        tune.volume*=previous.volume;tune.pitch*=previous.pitch;
                        tune.volumeSpread=previous.volumeSpread;
                        tune.pitchSpread=Mathf.Max(tune.pitchSpread,previous.pitchSpread);
                    }
                }
                var node=GpsCodec.Read(old?.name??root.children.Count.ToString(),typeof(AudioClipTuningEntry),tune,asset=>GpsProfileEditor.Register(asset,profile));
                if(old==null)root.children.Add(node);else root.children[root.children.IndexOf(old)]=node;
            }
        }
        if(version==null){version=GpsCodec.Read("catalogVersion",typeof(int),1,profile.Key);record.fields.Add(version);}else version.number=1;
    }
    static string Category(string path)
    {
        string p=path.ToLowerInvariant();
        if(p.Contains("/archive/"))return "Archiv";
        if(p.Contains("/home"))return p.Contains("homeclick")?"Shop und UI":"Homescreen";
        if(p.Contains("/loading"))return "Ladebildschirm";
        if(p.Contains("workshopfavorite")||p.Contains("workshopunfavorite"))return "Shop und UI";
        if(p.Contains("ultronium"))return "Altar";
        if(p.Contains("frog")||p.Contains("bird")||p.Contains("walkongrass"))return "Tiere";
        if(p.Contains("light rubble")||p.Contains("lightrubble"))return "Geröll";
        if(p.Contains("layer1details")||p.Contains("ghostwhisper"))return "Untergrund-Details";
        if(p.Contains("/ambience/"))return "Umgebung";
        if(p.Contains("/wood")||p.Contains("/trees/")||p.Contains("/torches/")||p.Contains("/grass/"))return "Bauen und Pflanzen";
        if(p.Contains("hurt")||p.Contains("bonebreaking")||p.Contains("heartbeat")||p.Contains("lowenergy")||p.Contains("gameover")||p.Contains("cloth")||p.Contains("recharge"))return "Spieler und Gesundheit";
        if(p.Contains("/mining")||p.Contains("pickaxe")||p.Contains("ore")||p.Contains("metalhit"))return "Abbau";
        if(p.Contains("/ui/")||p.Contains("click")||p.Contains("ding")||p.Contains("paper")||p.Contains("money")||p.Contains("alert")||p.Contains("door")||p.Contains("hotbar")||p.Contains("artifactcollected"))return "Shop und UI";
        return "Weitere Clips";
    }
    static string Display(string name)
    {
        return name switch
        {
            "HomescreenAmbience"=>"Homescreen-Musik", "LoadingYogaAmbience"=>"Lade-Ambiente", "LoadingPickaxeHit"=>"Lade-Spitzhacke",
            "HomeClick"=>"Menüklick", "SingleHeartBeat"=>"Herzschlag", "LowEnergyBip"=>"Energiewarnung", "GameOverMusic"=>"Game-over-Musik",
            "WorkshopFavorite"=>"Workshop-Favorit",
            "WorkshopUnfavorite"=>"Workshop-Favorit entfernen",
            "CaveAmbience"=>"Höhlen-Ambiente", "CaveTribalSong"=>"Höhlen-Tribal-Musik", "MeadowAmbience"=>"Wiesen-Ambiente", "UndergroundClay"=>"Untergrund-Ambiente", "WindAmbience"=>"Wind",
            "dragon-studio-creepy-ghost-sound-487677"=>"Geisterflüstern 01", "ghost whisper"=>"Geisterflüstern 02", "ghost1"=>"Geisterflüstern 03",
            "freesound_community-rocks-6129"=>"Fallende Steine 01", "freesound_community-stones-falling-6375"=>"Fallende Steine 02",
            "gravel slide"=>"Kiesrutsch 01", "gravel_slide_trocken_variante"=>"Kiesrutsch 02",
            "Sandrieseln_02_Weich_Laenger_weicher_Ausklang"=>"Sandrieseln 01", "Sandrieseln_03_Zwei_Schuebe_weicher_Ausklang_laut"=>"Sandrieseln 02",
            "UltroniumAltarAmbience"=>"Altar-Ambiente", "UltroniumChargeUp"=>"Altar aufladen", "UltroniumFullCharge"=>"Altar vollständig geladen",
            _=>name.Replace("HomeDistantPickaxe","Entfernte Spitzhacke ").Replace("HomeWaterdrop","Wassertropfen ")
                .Replace("BirdChirp_","Vogelruf ").Replace("Frog_","Froschquaken ").Replace("LightRubble","Geröll ").Replace('_',' ')
        };
    }
}
