using System;
using UnityEngine;

// F1 → Artefakte → "Fundchance je Artefakt": every artifact of MapGenerator.artifactSettings gets its own row
// "Chance <Name> (%)" instead of hiding the chance inside the nested list. Layer, money and points stay in the
// collapsible list below the rows. Docs/Gameplay-Einstellungen.md (Artefakte).
public sealed partial class GpsRuntimePanel
{
    bool drawingArtifactDetails;

    public static string ArtifactChanceLabel(GpsValue entry) => "Chance " + GpsSchema.NodeLabel(entry, GpsSettings.Profile) + " (%)";

    bool DrawArtifactChances(string record, GpsValue root, GpsValue node, GpsFieldSpec spec, string path, int indent, Action commit)
    {
        if (drawingArtifactDetails || node.kind != GpsValueKind.Array ||
            GpsCodec.ResolveType(node.type) != typeof(ArtifactDistributionSetting[])) return false;
        var chanceSpec = GpsSchema.ChildSpec(new GpsValue { name = "chancePercent" }, typeof(ArtifactDistributionSetting));
        float x = 20 + Indent(indent), inputX = 650;
        foreach (var item in node.children)
        {
            var entry = item;
            var chance = entry.children.Find(child => child.name == "chancePercent");
            if (chance == null) continue;
            Label(content, ArtifactChanceLabel(entry), new Vector2(x, -rowY), new Vector2(inputX - x - 20, 30), 21);
            Input(content, new Vector2(inputX, -rowY), new Vector2(240, 30), chance.number.ToString("0.####", Culture), text =>
            {
                // Leaving the field fires onEndEdit too; only a changed text is a new value.
                if (text == chance.number.ToString("0.####", Culture) || !TryNumber(text, out float value)) return;
                chance.number = Mathf.Clamp(value, chanceSpec.min, chanceSpec.max); commit();
                rebuildPending = true;
            });
            rowY += 37;
        }
        double factor = GpsSettings.GetValue(record, "artifactDropChanceMultiplier")?.number ?? 1;
        Label(content, "Chance pro Höhlenzelle im Layer des Artefakts, mal Faktor für alle Artefakte (×" + factor.ToString("0.##", Culture) +
            "). Wirkt auf einer neuen Karte.", new Vector2(x, -rowY), new Vector2(1500, 30), 19).color = new Color(.6f, .62f, .65f);
        rowY += 37;
        drawingArtifactDetails = true;
        try { DrawValue(record, root, node, spec, "Layer, Geld und Punkte je Artefakt", path, indent, null, commit); }
        finally { drawingArtifactDetails = false; }
        return true;
    }
}
