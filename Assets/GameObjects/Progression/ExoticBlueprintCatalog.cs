using System.Collections.Generic;
using UnityEngine;

// Presentation catalog: preview entries deliberately never become craftable recipes or loot.
public sealed class ExoticBlueprintInfo
{
    public readonly string Id, Name, Description, IconPath;
    public readonly int Level;
    public readonly CraftingRecipe Recipe;
    public bool Preview => !Recipe;
    public Sprite Icon => Recipe ? Recipe.output.icon : Resources.Load<Sprite>("Exotics/BlueprintIcons/" + IconPath);
    public ExoticBlueprintInfo(string id, string name, int level, string description, string iconPath = null, CraftingRecipe recipe = null)
    { Id=id; Name=name; Level=level; Description=description; IconPath=iconPath; Recipe=recipe; }
}

public static class ExoticBlueprintCatalog
{
    static List<ExoticBlueprintInfo> entries;
    public static IReadOnlyList<ExoticBlueprintInfo> Entries
    {
        get
        {
            if(entries!=null)return entries;
            entries=new List<ExoticBlueprintInfo>();
            foreach(var r in ExoticCatalog.Recipes)
            {
                string description=r.output.item switch {
                    Item.IronLadder => "Eine Leiter aus Eisen. Spart Holz und den Weg zurück zu den Bäumen an der Oberfläche.",
                    Item.LavaLamp => "Spendet mehr Licht als eine Fackel, ist aber wesentlich teurer in der Herstellung.",
                    _ => "Baut große Felsbrocken ab, für die du sonst Dynamit benötigst."
                };
                entries.Add(new ExoticBlueprintInfo(r.exoticId,r.output.displayName,r.metaUnlockLevel,description,recipe:r));
            }
            Add("StimShot","Stim-Shot",10,"Kurzer Energieschub, anschließend erhöhte Erschöpfung.");
            Add("Seilrolle","Seilrolle",20,"Lässt von einer Kante schnell ein Seil nach unten. Benötigt einen geeigneten Befestigungspunkt.");
            Add("Leuchtkristall","Leuchtkristall",25,"Beleuchtet ohne Brennstoff. Zur Herstellung werden seltene Kristalle benötigt.");
            Add("Sonar","Sonar-Modul",30,"Zeigt kurz nahe Hohlräume. Jeder Impuls kostet Energie.");
            Add("Faltbruecke","Faltbrücke",45,"Überbrückt mehrere Felder mit einem Bauteil. Die Herstellung ist teuer.");
            Add("Erzmagnet","Erzmagnet",55,"Zieht lose Rohstoffe heran und verbraucht dabei Energie.");
            Add("Sprungfeder","Sprungfeder",65,"Platzierbare Absprunghilfe für hohe Schächte. Muss auf festem Boden aufgebaut werden.");
            Add("Signalboje","Signalboje",75,"Markiert einen Fundort, damit du ihn später wiederfindest.");
            Add("Seilwinde","Seilwinde",85,"Erleichtert den Aufstieg in vorbereiteten Schächten. Erfordert ein verlegtes Seil.");
            Add("UltroniumKompass","Ultronium-Kompass",95,"Zeigt grob in Richtung Ultronium, verrät aber keine Entfernung.");
            Add("Sprengladung","Gezielte Sprengladung",110,"Sprengt einen schmalen Bereich gezielter als Dynamit.");
            Add("Kletterkrallen","Kletterkrallen",125,"Ermöglichen kurze Wandaufstiege mit hohem Energieverbrauch.");
            Add("Erzscanner","Erzscanner",140,"Prüft die direkte Umgebung auf eine ausgewählte Erzsorte.");
            Add("Feldschmiede","Feldschmiede",155,"Erlaubt begrenzte Verarbeitung unter Tage, benötigt aber viel Brennstoff.");
            Add("Lastenplattform","Lastenplattform",170,"Transportiert schwere Rohstoffe durch einen zuvor ausgebauten Schacht.");
            Add("Notfallakku","Notfallakku",185,"Einmalige Energiereserve. Aufwendig in der Herstellung.");
            Add("Rettungsanker","Rettungsanker",200,"Fängt einen gefährlichen Sturz ab und wird dabei verbraucht.");
            Add("Kartografensonde","Kartografensonde",300,"Erkundet einen schmalen Schacht vorab. Sammelt keine Rohstoffe.");
            Add("Versorgungsdepot","Versorgungsdepot",450,"Kompaktes Zwischenlager für Vorräte auf langen Expeditionen.");
            Add("Prismenlampe","Prismenlampe",650,"Einstellbare Lichtfarbe und Lichtverteilung für ausgebaute Höhlen.");
            entries.Sort((a,b)=>a.Level.CompareTo(b.Level));
            return entries;
        }
    }
    static void Add(string id,string name,int level,string description)=>entries.Add(new ExoticBlueprintInfo(id,name,level,description,id));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()=>entries=null;
}
