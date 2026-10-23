// Run in the connected editor during a running game (Play → Neues Spiel), never in Edit Mode:
// unity command run_script --file Tests/SurfaceHeadlampChecks.cs
// Checks that the helmet light also shines above the terrain at night (surface lamp) and hands over
// to the underground light field in the shaft. The visual look is accepted by the user.
using System;
using UnityEditor;
using UnityEngine;

public static class SurfaceHeadlampChecks
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

        var tiles = lighting.GetComponent<MapGenerator>().Terrain;
        var lamp = miner.headlamp;
        Vector3 oldPosition = lamp.transform.position;
        bool wasNight = sky.IsNight;
        Vector3 up = tiles.transform.TransformVector(Vector3.up * tiles.layoutGrid.cellSize.y);
        // Underground check: an open cell just below the surface; a fresh map has none, so one is opened temporarily.
        var air = new Vector3Int(0, -2, 0);
        var oldTile = tiles.GetTile(air);
        Vector3 top = tiles.CellToWorld(new Vector3Int(0, 1, 0)) + tiles.transform.TransformVector(Vector3.right * tiles.layoutGrid.cellSize.x * .5f);
        try
        {
            void Place(Vector3 position) { lamp.transform.position = position; lighting.SendMessage("UpdateHeadlamp"); }
            void Night(bool value) { sky.SetNight(value); sky.AdvanceFade(1000f); }

            Night(true);
            Place(top + up * 1.2f);
            Check(lighting.SurfaceHeadlampActive, "Surface lamp must shine above the terrain at night.");
            Check(Mathf.Abs(lighting.SurfaceHeadlampIntensity - lamp.intensity) < .001f,
                $"Surface lamp must use the helmet brightness: {lighting.SurfaceHeadlampIntensity} vs {lamp.intensity}");
            Check(!lighting.HeadlampFieldActive, "The underground light field cannot cover the sky.");

            Place(top + up * .25f);
            float halfway = lighting.SurfaceHeadlampIntensity;
            Check(halfway > .3f * lamp.intensity && halfway < .7f * lamp.intensity,
                $"Surface lamp must fade out just above the terrain: {halfway}");

            Night(false);
            Place(top + up * 1.2f);
            Check(!lighting.SurfaceHeadlampActive, "Surface lamp must stay off during the day.");

            Night(true);
            if (oldTile) tiles.SetTile(air, null);
            Place(tiles.GetCellCenterWorld(air));
            Check(!lighting.SurfaceHeadlampActive, "Surface lamp must be off underground.");
            Check(lighting.HeadlampFieldActive, $"Underground light field must take over in air cell {air}.");
            return new { surface = lamp.intensity, halfway, underground = air.ToString() };
        }
        finally
        {
            sky.SetNight(wasNight);
            sky.AdvanceFade(1000f);
            if (oldTile && !tiles.HasTile(air)) tiles.SetTile(air, oldTile);
            lamp.transform.position = oldPosition;
            lighting.SendMessage("UpdateHeadlamp");
        }
    }
}
