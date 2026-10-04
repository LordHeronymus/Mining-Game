using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class GpsSchema
{
    static readonly Dictionary<string, string> ChildLabels = new()
    {
        ["name"] = "Name", ["startDepth"] = "Starttiefe (Blöcke)", ["transitionWidth"] = "Übergang (Blöcke)",
        ["backgroundSprite"] = "Hintergrundsprite", ["stone"] = "Gesteinsart", ["layerIndices"] = "Layer",
        ["ore"] = "Erz", ["baseWeight"] = "Basisgewicht", ["weightCurve"] = "Gewichtungskurve (×)",
        ["baseVeinSize"] = "Adergrößenindex", ["veinSizeCurve"] = "Adergrößenkurve (×)", ["minimumVeinSize"] = "Mindestadergröße (Blöcke)",
        ["enabled"] = "Aktiv", ["minimumDepth"] = "Mindesttiefe (Blöcke)", ["walkerCount"] = "Walker-Anzahl",
        ["walkerStartRandomness"] = "Startpunkt-Zufälligkeit", ["densityByDepth"] = "Verteilung nach Tiefe",
        ["minimumWalkerLength"] = "Walker-Länge min.", ["maximumWalkerLength"] = "Walker-Länge max.",
        ["minimumTunnelRadius"] = "Tunnelradius min.", ["maximumTunnelRadius"] = "Tunnelradius max.",
        ["caveAversionPercent"] = "Höhlenaversion (%)", ["directionChange"] = "Richtungsänderung", ["verticalityIndex"] = "Vertikalität",
        ["branchChancePercent"] = "Verzweigungschance (%)", ["splitBranchChanceFactorPercent"] = "Folge-Split-Faktor (%)",
        ["branchChanceByDepth"] = "Verzweigung nach Tiefe", ["chamberChancePercent"] = "Kammerchance an Knoten (%)",
        ["minimumChamberRadius"] = "Kammerradius min.", ["maximumChamberRadius"] = "Kammerradius max.",
        ["pickaxe"] = "Spitzhacke", ["progressPerHitMultiplier"] = "Abbaufortschritt pro Treffer (×)", ["maximumHardness"] = "Max. HärteIndex",
        ["item"] = "Item", ["itemId"] = "Item", ["amount"] = "Anzahl", ["money"] = "Startgeld", ["items"] = "Startitems",
        ["scale"] = "Icon-Skalierung X/Y", ["offset"] = "Icon-Versatz X/Y", ["flipX"] = "Horizontal spiegeln", ["flipY"] = "Vertikal spiegeln",
        ["tile"] = "Sprite-Tile", ["chancePercent"] = "Fundchance pro Zelle (%)", ["cash"] = "Geld ($)", ["artifactPoints"] = "Artefaktpunkte",
        ["clip"] = "Clip", ["volume"] = "Lautstärke (%)", ["pitch"] = "Pitch (%)", ["pitchSpread"] = "Pitch-Streuung ± (%)",
        ["volumeSpread"] = "Lautstärkestreuung ± (%)",
        ["xpMultiplier"] = "XP-Multiplikator", ["depthStep"] = "Tiefenstufe (Blöcke)",
        ["depthBaseXp"] = "Tiefen-XP am Anfang", ["depthIncrementXp"] = "XP-Zuwachs pro Tiefenstufe",
        ["depthMaxXp"] = "Maximale XP pro Tiefenstufe", ["explorationRegionSize"] = "Erkundungsgebiet (Blöcke)",
        ["explorationCellThreshold"] = "Entdeckte Zellen pro Gebiet", ["explorationXp"] = "Erkundungs-XP",
        ["resourceXpMultiplier"] = "Ressourcen-XP-Multiplikator", ["discoveryXp"] = "Entdeckungs-XP",
        ["efficiencyMaxBonus"] = "Maximaler Effizienzbonus", ["efficiencyReferenceXpPerMinute"] = "Effizienzreferenz (XP/min)",
        ["layerIndex"] = "Layer", ["breaking"] = "Bruch", ["soundType"] = "Soundtyp", ["clipIndex"] = "Clip", ["tuning"] = "Klang",
    };
    public static GpsFieldSpec ChildSpec(GpsValue node, Type parent)
    {
        var field = parent == null ? null : GpsCodec.Field(parent, node.name);
        string label = ChildLabels.TryGetValue(node.name, out var text) ? text :
            field?.GetCustomAttributes(typeof(InspectorNameAttribute), true).Cast<InspectorNameAttribute>().FirstOrDefault()?.displayName ?? node.name;
        var result = new GpsFieldSpec(node.name, label);
        if (field?.GetCustomAttributes(typeof(RangeAttribute), true).FirstOrDefault() is RangeAttribute range)
        { result.min = range.min; result.max = range.max; }
        if (field?.GetCustomAttributes(typeof(MinAttribute), true).FirstOrDefault() is MinAttribute min) result.min = Mathf.Max(result.min, min.min);
        if (node.name == "volume" || node.name == "pitch" || node.name == "pitchSpread" || node.name == "volumeSpread") result.factor = 100f;
        if (node.name == "amount") result.min = 1;
        if (node.name == "money" || node.name == "cash" || node.name == "artifactPoints" || node.name == "layerIndex" || node.name == "clipIndex") result.min = 0;
        return result;
    }
    public static bool HideChild(Type type, string name) =>
        (type == typeof(MapLayer) && name == "ores") || name == "version";
    public static GpsFieldSpec ArraySpec(GpsValue child, GpsFieldSpec parent, int index)
    {
        var result = new GpsFieldSpec(child.name, child.kind == GpsValueKind.Object ? NodeLabel(child,GpsSettings.Profile) : "Level " + (index+1), parent.min,parent.max,parent.factor);
        if (parent.name=="energyCapacityMultipliers") result.min=1;
        if (parent.name=="detailVolumeMultipliers") { result.label="Clip " + (index+1); result.factor=100; result.min=0; result.max=1; }
        if (parent.name=="detailsPerMinuteByLayer") { result.label="Layer " + (index+1); result.min=0; }
        return result;
    }
    public static IEnumerable<(GpsRecord record, GpsFieldSpec field)> SectionFields(string tab, string title)
    {
        foreach (var section in Sections.Where(section => section.tab == tab && section.title == title))
            foreach (var record in GpsSettings.Records(section))
            {
                if (section.source == typeof(ItemSO) && section.title == "Erzverkaufspreise" && GpsSettings.Profile.Resolve(record.assetKey) is ItemSO item && item.category != ItemCategory.Ore) continue;
                foreach (var field in section.fields)
                {
                    if (section.source==typeof(Block) && field.name=="itemDrop" && GpsSettings.Profile.Resolve(record.assetKey) is Block block && !block.HasOreOverlays) continue;
                    if (!field.hideInUi && record.fields.Exists(node => node.name == field.name)) yield return (record, field);
                }
            }
    }
    public static List<GpsAssetReference> Choices(Type type) => GpsSettings.Profile.assets
        .Where(entry => entry.asset && type.IsInstanceOfType(entry.asset)).OrderBy(entry => DisplayName(entry.asset), StringComparer.CurrentCultureIgnoreCase).ToList();
    public static int VectorDimension(GpsValue value)
    {
        Type type = GpsCodec.ResolveType(value.type);
        return type == typeof(Vector2) || type == typeof(Vector2Int) ? 2 : type == typeof(Vector3) || type == typeof(Vector3Int) ? 3 : 4;
    }
    public static void CurveDepthRange(string recordKey, string root, GpsValue parent, out int start, out int end)
    {
        start = 0;
        var map = GpsSettings.Document.records.Find(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
        end = Mathf.Max(1, (int)(map?.fields.Find(field => field.name == "mapHeight")?.number ?? 1000));
        if (root != "oreSettings" || parent == null) return;
        var selected = parent.children.Find(field => field.name == "layerIndices")?.children.Select(value => (int)value.number).OrderBy(index => index).ToArray();
        var layers = map?.fields.Find(field => field.name == "layers")?.children;
        if (selected == null || selected.Length == 0 || layers == null) return;
        int first = selected[0], last = selected[selected.Length - 1];
        if (first >= 0 && first < layers.Count) start = (int)layers[first].children.Find(field => field.name == "startDepth").number;
        if (last + 1 >= 0 && last + 1 < layers.Count) end = (int)layers[last + 1].children.Find(field => field.name == "startDepth").number;
    }
}
