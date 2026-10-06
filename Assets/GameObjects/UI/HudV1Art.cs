using System.Collections.Generic;
using UnityEngine;

public static class HudV1Art
{
    static readonly Dictionary<Sprite, Material> wood = new();
    static readonly Dictionary<Sprite, Material> socketHover = new(), socketSelected = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var material in wood.Values) if (material) Object.Destroy(material);
        wood.Clear();
        foreach (var material in socketHover.Values) if (material) Object.Destroy(material);
        foreach (var material in socketSelected.Values) if (material) Object.Destroy(material);
        socketHover.Clear(); socketSelected.Clear();
    }
    public static Material DarkWood(Sprite sprite)
    {
        if (!sprite) return null;
        if (wood.TryGetValue(sprite, out var material) && material) return material;
        var shader = Resources.Load<Shader>("Progression/UpgradeCardWood");
        if (!shader) return null;
        material = new Material(shader) { name = "HUD V1 Dark Wood", hideFlags = HideFlags.DontSave };
        var rect = sprite.rect;
        material.SetVector("_WoodUvRect", new Vector4(rect.x / sprite.texture.width, rect.y / sprite.texture.height,
            rect.width / sprite.texture.width, rect.height / sprite.texture.height));
        material.SetColor("_WoodTone", new Color(.60f, .57f, .54f, 1));
        wood[sprite] = material; return material;
    }
    public static Material SocketHover(Sprite normal)
    {
        if (socketHover.TryGetValue(normal, out var material) && material) return material;
        material = new Material(DarkWood(normal)) { name = "HUD Aligned Socket Hover", hideFlags = HideFlags.DontSave };
        material.SetColor("_Color", new Color(1.35f, 1.22f, 1.05f, 1));
        socketHover[normal] = material; return material;
    }
    public static Material SocketSelected(Sprite normal, Sprite selected)
    {
        if (socketSelected.TryGetValue(normal, out var material) && material) return material;
        material = new Material(DarkWood(normal)) { name = "HUD Aligned Socket Selection", hideFlags = HideFlags.DontSave };
        var rect = normal.rect;
        material.SetVector("_GoldUvRect", new Vector4(rect.x / normal.texture.width, rect.y / normal.texture.height,
            rect.width / normal.texture.width, rect.height / normal.texture.height));
        material.SetVector("_GoldInsets", new Vector4(.095f, .10f, .095f, .10f));
        material.SetFloat("_GoldChamfer", .045f);
        material.SetFloat("_GoldFill", 1);
        material.SetFloat("_UseGoldArt", 1);
        material.SetTexture("_GoldArt", selected.texture);
        rect = selected.rect;
        material.SetVector("_GoldArtRect", new Vector4((rect.x + rect.width * .095f) / selected.texture.width,
            (rect.y + rect.height * .10f) / selected.texture.height,
            rect.width * .81f / selected.texture.width, rect.height * .80f / selected.texture.height));
        material.SetColor("_Color", new Color(1.2f, 1.12f, 1f, 1));
        socketSelected[normal] = material; return material;
    }
}

