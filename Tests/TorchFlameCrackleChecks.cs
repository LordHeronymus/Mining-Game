using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Checks for the torch rework from 23.10.2026 (variant C2): holder sits low in its cell, the new flame layers
// start above the holder, the crackle loop is loaded and its volume falls to silence at the GPS range.
// Edit Mode. Look, flicker and sound are accepted by the user in the game.
public static class TorchFlameCrackleChecks
{
    const string Report = "Temp/TorchFlameCrackleChecks.txt";
    const float Cell = 1.1f;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + label + "\n");
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "");
        try
        {
            var placement = PlacementChecks();
            var audio = CrackleChecks();
            File.AppendAllText(Report, "PASS all checks\n");
            return new { placement, audio };
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error.Message + "\n");
            throw;
        }
    }

    static object PlacementChecks()
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Tools/Torche.asset");
        Check(item && item.item == Item.Torche, "Torch item asset found");
        var profileMap = new GameObject("Torch profile probe") { hideFlags = HideFlags.HideAndDontSave };
        var root = new GameObject("Torch check grid") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var probe = profileMap.AddComponent<MapGenerator>();
            GpsSettings.ApplyComponent(probe);
            Check(Mathf.Approximately(probe.torchHolderOffsetY, -.22f), "GPS holder offset is -0.22 (" + probe.torchHolderOffsetY + ")");
            Check(Mathf.Approximately(probe.torchHolderOffsetY + probe.torchFlameOffsetY, .25f),
                "GPS flame emitter sits 0.25 above the cell centre (" + (probe.torchHolderOffsetY + probe.torchFlameOffsetY) + ")");
            Check(Mathf.Approximately(probe.torchSoundRangeTiles, 4f), "GPS crackle range is 4 tiles (" + probe.torchSoundRangeTiles + ")");
            Check(probe.torchSoundVolume > 0f, "GPS crackle volume is above zero (" + probe.torchSoundVolume + ")");

            root.AddComponent<Grid>().cellSize = new Vector3(Cell, Cell, 0f);
            var mapObject = new GameObject("Torch check map") { hideFlags = HideFlags.HideAndDontSave };
            mapObject.transform.SetParent(root.transform, false);
            mapObject.AddComponent<Tilemap>();
            var map = mapObject.AddComponent<MapGenerator>();
            map.torchFlameOffsetX = 0f;
            map.torchFlameOffsetY = probe.torchFlameOffsetY;
            map.torchHolderOffsetY = probe.torchHolderOffsetY;
            map.torchFlameSize = 1.7f;
            map.torchFlameFrequency = 30f;
            map.torchBrightness = 2f;

            var cell = new Vector3Int(2, -6, 0);
            PlacedTorch.CreateAt(map, item, cell);
            var torch = mapObject.GetComponentInChildren<PlacedTorch>();
            Check(torch, "Torch was created");
            var holder = torch.transform.Find("Torch Holder");
            Check(holder && Mathf.Approximately(holder.localPosition.y, -.22f), "Holder sits 0.22 below the cell centre");
            // The opaque part of TorchHolderSprite reaches 0.318 below the sprite centre (pixel row 1081 of 1254, scale 0.07).
            float holderBottom = holder.localPosition.y - .318f;
            Check(holderBottom >= -Cell * .5f, "Holder stays inside its cell (bottom " + holderBottom + ")");

            var flame = torch.GetComponent<TorchFlame>();
            Check(flame && flame.LayerCount == 5, "Flame has 5 layers (smoke, outer, core, blue base, embers)");
            string[] names = { "Smoke", "Outer Flame", "Flame Core", "Blue Base", "Embers" };
            for (int i = 0; i < names.Length; i++)
            {
                var layer = flame.GetLayer(i);
                Check(layer && layer.name == names[i], "Layer " + i + " is " + names[i]);
                Check(Mathf.Abs(layer.transform.localPosition.y - .25f) < .0001f, names[i] + " emits 0.25 above the cell centre");
            }
            Check(Mathf.Approximately(flame.GetLayer(1).emission.rateOverTime.constant, 22f), "Outer flame at 22/s with GPS frequency 30");
            Check(Mathf.Approximately(flame.GetLayer(1).main.startSizeY.constantMax, .644f), "Outer flame height matches C2 with GPS size 1.7");
            Check(flame.GetLayer(0).GetComponent<ParticleSystemRenderer>().sortingOrder <
                holder.GetComponent<SpriteRenderer>().sortingOrder, "Smoke renders behind the holder");
            Check(flame.GetLayer(4).GetComponent<ParticleSystemRenderer>().sortingOrder >
                flame.GetLayer(1).GetComponent<ParticleSystemRenderer>().sortingOrder, "Embers render in front of the flame");

            Check(torch.FlameCell == cell, "Torch light now lies in the torch's own cell");
            map.torchFlameSize = 3.4f;
            map.torchFlameFrequency = 60f;
            map.torchHolderOffsetY = -.1f;
            // Edit Mode runs no OnEnable, so the torch is not in the live set: apply the GPS change directly.
            typeof(PlacedTorch).GetMethod("PlaceHolder", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(torch, null);
            flame.ApplySettings();
            Check(Mathf.Approximately(holder.localPosition.y, -.1f), "GPS holder offset applies live");
            Check(Mathf.Approximately(flame.GetLayer(1).emission.rateOverTime.constant, 44f) &&
                Mathf.Approximately(flame.GetLayer(1).main.startSizeY.constantMax, 1.288f), "GPS size and frequency scale the layers");
            return new { holderBottom };
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(profileMap);
        }
    }

    static object CrackleChecks()
    {
        var clip = AudioManager.TorchCrackleClip;
        Check(clip, "Crackle loop clip is assigned");
        Check(clip.length > 5f, "Crackle loop is long enough to loop calmly (" + clip.length + " s)");
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
        Check(importer && importer.forceToMono, "Crackle loop is imported as mono (panned in code)");

        float near = TorchCrackleAudio.GainAt(0f, Cell, 4f);
        float half = TorchCrackleAudio.GainAt(Cell * .5f, Cell, 4f);
        float two = TorchCrackleAudio.GainAt(Cell * 2f, Cell, 4f);
        float almost = TorchCrackleAudio.GainAt(Cell * 3.9f, Cell, 4f);
        float four = TorchCrackleAudio.GainAt(Cell * 4f, Cell, 4f);
        float five = TorchCrackleAudio.GainAt(Cell * 5f, Cell, 4f);
        Check(Mathf.Approximately(near, 1f) && Mathf.Approximately(half, 1f), "Full volume right beside the torch");
        Check(two > .15f && two < .6f, "About a third of the volume two tiles away (" + two + ")");
        Check(almost > 0f && almost < .01f, "Nearly silent just before four tiles (" + almost + ")");
        Check(four == 0f && five == 0f, "Silent from four tiles on");
        float previous = 1f;
        for (float d = 0f; d <= Cell * 5f; d += .05f)
        {
            float g = TorchCrackleAudio.GainAt(d, Cell, 4f);
            if (g > previous + 1e-6f) throw new Exception("Crackle volume rises again at distance " + d);
            previous = g;
        }
        Check(true, "Crackle volume only falls with distance");
        return new { two, almost };
    }
}
