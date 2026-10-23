using System;
using System.Linq;
using UnityEngine;

// F1 → Erzverteilung: ore pickers ([OreOnly]) offer only blocks with ore overlays, by their display name; "Faktor je Erz"
// lists every ore with its factor. Docs/Erz-Reichhaltigkeit.md.
public sealed partial class GpsRuntimePanel
{
    const string NoOre = "— Erz wählen —";
    bool rebuildPending;

    // Ores, gems and Ultronium carry the Small/Medium/Rich overlay groups; stone layers and dirt do not.
    public static Block[] OreChoices() => GpsSettings.Profile.assets.Select(entry => entry.asset).OfType<Block>()
        .Where(block => block && block.HasOreOverlays).GroupBy(block => block.id).Select(group => group.First())
        .OrderBy(block => block.displayName, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public static bool IsOreOnly(GpsValue parent, string name) => parent != null && GpsCodec.ResolveType(parent.type) is Type type &&
        GpsCodec.Field(type, name)?.IsDefined(typeof(OreOnlyAttribute), true) == true;

    // A value that is no ore (e.g. an entry added before this picker existed) keeps its value and shows NoOre until one is chosen.
    void OreDropdown(Vector2 position, GpsValue node, Action commit, float width)
    {
        int current = (int)node.number;
        var ores = OreChoices();
        int selected = Array.FindIndex(ores, block => (int)block.id == current), offset = selected < 0 ? 1 : 0;
        var labels = ores.Select(block => block.displayName);
        Dropdown(position, (offset == 1 ? labels.Prepend(NoOre) : labels).ToArray(), selected + offset, index =>
        {
            if (index < offset) return;
            node.number = (int)ores[index - offset].id; commit();
            // Labels (e.g. the header of the entry) change; rebuilding inside the dropdown callback would break its closing.
            rebuildPending = true;
        }, width);
    }

    void LateUpdate()
    {
        if (!rebuildPending) return;
        rebuildPending = false;
        float position = scroll ? scroll.verticalNormalizedPosition : 1f;
        BuildPage();
        if (scroll) scroll.verticalNormalizedPosition = position;
    }

    static int OreOf(GpsValue entry) => (int)(entry.children.Find(child => child.name == "ore")?.number ?? -1);

    // VeinRichnessSettings.ores: every ore has a fixed row "Name | × Faktor"; nothing to add or remove. The data stays a list of
    // (ore, factor): a row without an entry shows 1 and gets one on its first edit. Entries on no ore (older lists) stay untouched.
    bool DrawOreFactors(GpsValue node, string label, string path, int indent, Action commit)
    {
        if (node.kind != GpsValueKind.Array || GpsCodec.ResolveType(node.type) != typeof(VeinRichnessOre[])) return false;
        var ores = OreChoices();
        GpsValue Entry(Block ore) => node.children.Find(entry => OreOf(entry) == (int)ore.id);
        static GpsValue FactorOf(GpsValue entry) => entry?.children.Find(child => child.name == "factor");
        int changed = ores.Count(ore => FactorOf(Entry(ore)) is GpsValue factor && Math.Abs(factor.number - 1) > 1e-6);
        if (!Header(path, label + (changed > 0 ? " (" + changed + " geändert)" : ""), indent)) return true;
        var spec = GpsSchema.ChildSpec(new GpsValue { name = "factor" }, typeof(VeinRichnessOre));
        float x = 20 + Indent(indent + 1), inputX = 650;
        foreach (var ore in ores)
        {
            var block = ore;
            double current = FactorOf(Entry(block))?.number ?? 1;
            var name = Label(content, block.displayName, new Vector2(x, -rowY), new Vector2(inputX - x - 20, 30), 21);
            if (Math.Abs(current - 1) > 1e-6) name.color = Accent;
            Label(content, "×", new Vector2(inputX, -rowY), new Vector2(30, 30), 21);
            Input(content, new Vector2(inputX + 34, -rowY), new Vector2(240, 30), current.ToString("0.###", Culture), text =>
            {
                var entry = Entry(block);
                double shown = FactorOf(entry)?.number ?? 1;
                // Leaving the field fires onEndEdit too; only a changed text is a new value.
                if (text == shown.ToString("0.###", Culture) || !TryNumber(text, out float value)) return;
                if (entry == null)
                    node.children.Add(entry = GpsCodec.Read(node.children.Count.ToString(), typeof(VeinRichnessOre),
                        new VeinRichnessOre { ore = block.id, factor = 1f }, GpsSettings.Profile.Key));
                FactorOf(entry).number = Mathf.Clamp(value, spec.min, spec.max); commit();
                rebuildPending = true;
            });
            rowY += 37;
        }
        Label(content, "Multipliziert Medium und Rich des Erzes (1 = Tabelle, 0 = nur Small).", new Vector2(x, -rowY), new Vector2(1200, 30), 19)
            .color = new Color(.6f, .62f, .65f);
        rowY += 37;
        return true;
    }
}
