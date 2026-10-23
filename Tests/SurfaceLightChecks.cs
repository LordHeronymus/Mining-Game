// Run in the connected editor during a running game (Play → Neues Spiel), never in Edit Mode:
// unity command run_script --file Tests/SurfaceLightChecks.cs
// Checks that headlamp light from a shaft continues softly above the terrain (no hard edge at
// the top terrain row), fades with distance, and by day only reaches a few cells above the shaft.
// The visual look is accepted by the user.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

public static class SurfaceLightChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires a running game (Play → Neues Spiel).";
        var lighting = UnityEngine.Object.FindFirstObjectByType<MapLighting>();
        var miner = UnityEngine.Object.FindFirstObjectByType<MinerPlayerVisual>();
        var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
        if (!lighting || !lighting.IsReady || !miner || !miner.headlamp || !sky)
            return "Requires a running game (Play → Neues Spiel).";
        if (GameplayTestSettings.GlobalLighting) return "Requires Debug → Globale Beleuchtung aus.";

        var map = lighting.GetComponent<MapGenerator>();
        Tilemap tiles = map.Terrain;
        var lamp = miner.headlamp;
        Vector3 oldPosition = lamp.transform.position;
        bool wasNight = sky.IsNight;
        // A short shaft through the top terrain rows; a fresh map has none, so it is opened temporarily.
        var shaft = new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, -2, 0) };
        var oldTiles = new TileBase[shaft.Length];
        for (int i = 0; i < shaft.Length; i++) oldTiles[i] = tiles.GetTile(shaft[i]);
        try
        {
            void Refresh() { lighting.SendMessage("LateUpdate"); lighting.SendMessage("UpdateHeadlamp"); }
            void Night(bool value) { sky.SetNight(value); sky.AdvanceFade(1000f); Refresh(); }
            float Light(int y) => lighting.GetDrawnLocalLight(new Vector3Int(0, y, 0));

            foreach (var cell in shaft) { tiles.SetTile(cell, null); lighting.NotifyTileChanged(cell); }
            lamp.transform.position = tiles.GetCellCenterWorld(shaft[1]);
            Night(true);
            Check(lighting.HeadlampFieldActive, "Headlamp field must be active in the shaft.");

            var torchObject = GameObject.Find("Torch field light");
            var torchLight = torchObject ? torchObject.GetComponent<Light2D>() : null;
            Check(torchLight && torchLight.lightCookieSprite, "Torch field light is missing.");
            float spriteRows = torchLight.lightCookieSprite.rect.height;
            Check(Mathf.Approximately(spriteRows, map.GeneratedHeight + MapLighting.SurfaceLightRows),
                $"Light sprite must cover the surface rows: {spriteRows} rows for map height {map.GeneratedHeight}");

            Check(lighting.SurfaceGlowActive, "The surface background must receive the glow while the field shines.");
            var glowObject = GameObject.Find("Surface light glow");
            var glow = glowObject ? glowObject.GetComponent<MeshRenderer>() : null;
            Check(glow && glow.sortingLayerName == "Background", "Surface glow must draw on the background layer.");
            Color glowColor = glow.sharedMaterial.GetColor("_GlowColor");
            Check(glowColor.r > torchLight.color.r * torchLight.intensity,
                $"At night the glow must make up for the darkened background: {glowColor}");

            float top = Light(0), above = Light(1), higher = Light(4), far = Light(MapLighting.SurfaceLightRows + 2);
            Check(top > .05f, $"Top shaft cell must be lit: {top}");
            Check(above > .3f * top, $"Light must continue above the terrain without a hard edge: top {top}, above {above}");
            Check(higher < above && higher > 0f, $"Light must fade softly above the shaft: above {above}, 4 cells up {higher}");
            Check(far == 0f, $"Outside the surface rows no field light is drawn: {far}");
            // No light through the ground: a solid cell two columns beside the shaft below the surface stays dark.
            var beside = new Vector3Int(3, -2, 0);
            if (tiles.HasTile(beside) && tiles.HasTile(new Vector3Int(2, -2, 0)) && tiles.HasTile(new Vector3Int(1, -2, 0)) &&
                tiles.HasTile(new Vector3Int(3, -1, 0)) && tiles.HasTile(new Vector3Int(3, -3, 0)))
                Check(lighting.GetDrawnLocalLight(beside) == 0f, $"Light must not pass through the ground: {lighting.GetDrawnLocalLight(beside)}");

            Night(false);
            float dayTop = Light(0), dayAbove = Light(1), dayHigher = Light(4);
            Check(Mathf.Abs(dayTop - top) < .01f, $"Underground light must not depend on the time of day: {dayTop} vs {top}");
            Check(dayAbove < above && dayAbove > .2f * dayTop,
                $"By day the surface light must be weaker but still fade softly: day {dayAbove}, night {above}, top {dayTop}");
            Check(dayHigher < .02f, $"By day the surface light must stay close to the shaft: {dayHigher}");
            return new { top, above, higher, dayAbove, dayHigher };
        }
        finally
        {
            sky.SetNight(wasNight);
            sky.AdvanceFade(1000f);
            for (int i = 0; i < shaft.Length; i++)
                if (oldTiles[i] && !tiles.HasTile(shaft[i])) { tiles.SetTile(shaft[i], oldTiles[i]); lighting.NotifyTileChanged(shaft[i]); }
            lamp.transform.position = oldPosition;
            lighting.SendMessage("LateUpdate");
            lighting.SendMessage("UpdateHeadlamp");
        }
    }
}
