using System;
using System.Linq;
using UnityEngine;

// F1 → Erzverteilung: ore pickers ([OreOnly]) offer only blocks with ore overlays, by their display name; "Faktor je Erz"
// shows one row per ore (ore, factor, buttons). Docs/Erz-Reichhaltigkeit.md.
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
    void OreDropdown(Vector2 position, GpsValue node, Action commit, float width, Func<int, bool> taken = null)
    {
        int current = (int)node.number;
        var ores = OreChoices().Where(block => (int)block.id == current || taken == null || !taken((int)block.id)).ToArray();
        int selected = Array.FindIndex(ores, block => (int)block.id == current), offset = selected < 0 ? 1 : 0;
        var labels = ores.Select(block => block.displayName);
        Dropdown(position, (offset == 1 ? labels.Prepend(NoOre) : labels).ToArray(), selected + offset, index =>
        {
            if (index < offset) return;
            node.number = (int)ores[index - offset].id; commit();
            // Labels and the ores left for other rows change; rebuilding inside the dropdown callback would break its closing.
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

    // VeinRichnessSettings.ores: header with the count, then "Erz | × Faktor | ↑ ↓ −" per row and "+ Erz" for the next unused ore.
    bool DrawOreFactors(GpsValue node, string label, string path, int indent, Action commit)
    {
        if (node.kind != GpsValueKind.Array || GpsCodec.ResolveType(node.type) != typeof(VeinRichnessOre[])) return false;
        if (!Header(path, label + " (" + node.children.Count + ")", indent)) return true;
        float x = 20 + Indent(indent + 1), inputX = 650;
        for (int i = 0; i < node.children.Count; i++)
        {
            var entry = node.children[i]; int index = i;
            var ore = entry.children.Find(child => child.name == "ore");
            var factor = entry.children.Find(child => child.name == "factor");
            if (ore != null)
                OreDropdown(new Vector2(x, -rowY), ore, commit, inputX - x - 20, id => node.children.Any(other => other != entry && OreOf(other) == id));
            if (factor != null)
            {
                var spec = GpsSchema.ChildSpec(factor, typeof(VeinRichnessOre));
                Label(content, "×", new Vector2(inputX, -rowY), new Vector2(30, 30), 21);
                Input(content, new Vector2(inputX + 34, -rowY), new Vector2(240, 30), factor.number.ToString("0.###", Culture), text =>
                {
                    // Leaving the field fires onEndEdit too; only a changed text is a new value.
                    if (text == factor.number.ToString("0.###", Culture) || !TryNumber(text, out float value)) return;
                    factor.number = Mathf.Clamp(value, spec.min, spec.max); commit();
                });
                Label(content, "Medium und Rich", new Vector2(inputX + 290, -rowY), new Vector2(400, 30), 19).color = new Color(.6f, .62f, .65f);
            }
            Button(content, "↑", new Vector2(1540, -rowY), new Vector2(32, 30), () => { if (index > 0) { (node.children[index - 1], node.children[index]) = (node.children[index], node.children[index - 1]); commit(); BuildPage(); } });
            Button(content, "↓", new Vector2(1580, -rowY), new Vector2(32, 30), () => { if (index + 1 < node.children.Count) { (node.children[index + 1], node.children[index]) = (node.children[index], node.children[index + 1]); commit(); BuildPage(); } });
            Button(content, "−", new Vector2(1620, -rowY), new Vector2(32, 30), () => { node.children.RemoveAt(index); commit(); BuildPage(); });
            rowY += 37;
        }
        var unused = OreChoices().FirstOrDefault(block => node.children.All(entry => OreOf(entry) != (int)block.id));
        if (unused)
        {
            Button(content, "+ Erz", new Vector2(x, -rowY), new Vector2(120, 30), () =>
            {
                var child = GpsCodec.Read(node.children.Count.ToString(), typeof(VeinRichnessOre),
                    new VeinRichnessOre { ore = unused.id, factor = 1f }, GpsSettings.Profile.Key);
                node.children.Add(child); commit(); BuildPage();
            });
            rowY += 37;
        }
        return true;
    }
}
