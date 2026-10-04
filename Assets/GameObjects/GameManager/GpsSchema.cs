using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum GpsApplyTime { Immediately, NewMap, NewRun }
public sealed class GpsFieldSpec
{
    public string name, label;
    public float min = -float.MaxValue, max = float.MaxValue, factor = 1;
    public GpsApplyTime applyTime;
    public bool hideInUi;
    public GpsFieldSpec(string name, string label = null, float min = -float.MaxValue,
        float max = float.MaxValue, float factor = 1, GpsApplyTime applyTime = GpsApplyTime.Immediately)
    { this.name = name; this.label = label ?? name; this.min = min; this.max = max; this.factor = factor; this.applyTime = applyTime; }
}
public sealed class GpsSectionSpec
{
    public string tab, title, role;
    public Type source;
    public GpsFieldSpec[] fields;
    public GpsSectionSpec(string tab, string title, Type source, string role, params GpsFieldSpec[] fields)
    { this.tab = tab; this.title = title; this.source = source; this.role = role; this.fields = fields; }
    public bool Matches(GpsRecord record) => record.type == source.AssemblyQualifiedName &&
        (string.IsNullOrEmpty(role) || record.key.EndsWith(":" + role, StringComparison.Ordinal));
}
public static partial class GpsSchema
{
    public static readonly string[] Tabs = { "Spieler", "Bewegung", "Upgrades", "Map", "Erzverteilung", "Partikel", "Licht", "Werkbank", "Shop", "Audio", "Pflanzen", "Tiere", "Health", "Artefakte", "UI" };
    public const string TestTab = "Testeinstellungen";
    public static readonly List<GpsSectionSpec> Sections = BuildValidated();
    static List<GpsSectionSpec> BuildValidated()
    {
        var sections=Build();
        var labels=new Dictionary<string,string> { ["randomizeSeed"]="Zufälliger Seed",["grassSizeMultiplier"]="Grasgröße (×)",
            ["healingHerbSizeMultiplier"]="Heilkrautgröße (×)",["oreScale"]="Erzgröße (×)",["hardness"]="Härte",["hardnessIndex"]="Härteindex",
            ["fallRotationCurve"]="Fallkurve",["clusterPercent"]="Cluster-Anteil (%)",["clusterSize"]="Clustergröße",["healingHerbRarityPercent"]="Seltenheit (%)" };
        foreach (var section in sections)
            foreach (var spec in section.fields)
            {
                var field=GpsCodec.Field(section.source,spec.name);
                if (field?.GetCustomAttributes(typeof(RangeAttribute),true).FirstOrDefault() is RangeAttribute range)
                { spec.min=Mathf.Max(spec.min,range.min); spec.max=Mathf.Min(spec.max,range.max); }
                if (field?.GetCustomAttributes(typeof(MinAttribute),true).FirstOrDefault() is MinAttribute min) spec.min=Mathf.Max(spec.min,min.min);
                if (spec.label==spec.name && labels.TryGetValue(spec.name,out string label)) spec.label=label;
                if (spec.name=="maximumDelaySeconds") { spec.min=0; spec.max=10; }
                if (spec.name=="miningSpeed") { spec.min=.01f; spec.max=100; }
                if (spec.name=="orthographicSize" || spec.name=="mass") spec.min=.01f;
                if (spec.name=="gravityScale" || spec.name=="weight" || spec.name=="worth" || spec.name=="hardness" || spec.name=="hardnessIndex" || spec.name=="rechargeCost") spec.min=0;
                if (spec.name=="maxEnergy") spec.min=1;
            }
        return sections;
    }
    public static IEnumerable<GpsFieldSpec> Fields(Type type, string role = null) => Sections
        .Where(section => section.source == type && (string.IsNullOrEmpty(section.role) || section.role == role))
        .SelectMany(section => section.fields).GroupBy(field => field.name).Select(group => group.First());
    public static string Role(UnityEngine.Object target) => target is SurfaceCritters critter ? critter.species.ToString() : "";
    public static string ComponentKey(UnityEngine.Object target) => "component:" + target.GetType().FullName +
        (string.IsNullOrEmpty(Role(target)) ? "" : ":" + Role(target));
    public static GpsFieldSpec Spec(GpsRecord record, string field) => Sections.Where(section => section.Matches(record))
        .SelectMany(section => section.fields).FirstOrDefault(spec => spec.name == field);
    public static bool IsNormal(GpsSectionSpec section) => section.tab != TestTab;
    public static string NodeLabel(GpsValue node, GpsProfile profile)
    {
        if (node.kind == GpsValueKind.Object)
        {
            if(GpsCodec.ResolveType(node.type)==typeof(LayerMiningAudioSettingEntry))
                return "Layer "+((int)(node.children.Find(field=>field.name=="layerIndex")?.number??0)+1)+" · "+
                    Enum.GetName(typeof(SoundType),(int)(node.children.Find(field=>field.name=="soundType")?.number??0))+" · Clip "+
                    ((int)(node.children.Find(field=>field.name=="clipIndex")?.number??0)+1);
            var name = node.children.FirstOrDefault(child => child.name == "name");
            if (!string.IsNullOrEmpty(name?.text)) return name.text;
            var asset = node.children.FirstOrDefault(child => child.name == "tile" || child.name == "clip" || child.name == "pickaxe" || child.name == "item");
            if (asset != null && profile.Resolve(asset.text) is UnityEngine.Object item && item) return DisplayName(item);
            var ore = node.children.FirstOrDefault(child => child.name == "ore");
            if (ore != null)
            {
                var block=profile.assets.Select(entry=>entry.asset).OfType<Block>().FirstOrDefault(block=>(int)block.id==(int)ore.number);
                return block ? block.displayName : Enum.GetName(GpsCodec.ResolveType(ore.type), (int)ore.number);
            }
            var itemId=node.children.FirstOrDefault(child=>child.name=="itemId");
            if(itemId!=null && StartingResourcesSettings.Resolve((int)itemId.number) is ItemSO startingItem) return startingItem.displayName;
        }
        return node.name;
    }
    public static string DisplayName(UnityEngine.Object target) => target is ItemSO item ? item.displayName : target is Block block ? block.displayName :
        target is CraftingRecipe recipe && recipe.output ? recipe.output.displayName : target is ArtifactTile artifact ? artifact.displayName : target.name;
}
