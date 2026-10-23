using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Checks for the mining fixes: tapping keeps the hit cooldown, a drop over the carry limit stalls
// without energy drain, deep ores use the deep ore hit, and hotbar drags never mine.
public static class BughuntTileMinerChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var created = new List<Object>();
        T Asset<T>() where T : ScriptableObject { var asset = ScriptableObject.CreateInstance<T>(); asset.hideFlags = HideFlags.HideAndDontSave; created.Add(asset); return asset; }
        var root = new GameObject("Bughunt TileMiner checks", typeof(Grid)) { hideFlags = HideFlags.HideAndDontSave };
        GameObject Child(string name)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.SetActive(false); child.transform.SetParent(root.transform, false); return child;
        }
        var audio = AudioManager.Instance;
        var inventoryField = typeof(InventoryManager).GetField("<Instance>k__BackingField", HiddenStatic);
        var inventory = InventoryManager.Instance;
        try
        {
            // Tapping after every hit must not beat holding the button.
            var hitMethod = typeof(TileMiner).GetMethod("TryBeginMiningHit", HiddenStatic, null,
                new[] { typeof(float).MakeByRefType(), typeof(float), typeof(float), typeof(bool) }, null);
            Check(hitMethod != null, "Treffertakt als testbare Hilfsmethode vorhanden");
            int CountHits(bool tapping)
            {
                const float frame = 1f / 60f, interval = .5f;
                float next = 0f; bool active = false, released = false; int hits = 0;
                for (float now = 0f; now < 5f; now += frame)
                {
                    if (released) { released = false; active = false; continue; }
                    var args = new object[] { next, now, interval, active };
                    bool hit = (bool)hitMethod.Invoke(null, args);
                    next = (float)args[0]; active = true;
                    if (hit) { hits++; released = tapping; }
                }
                return hits;
            }
            int held = CountHits(false), tapped = CountHits(true);
            Check(held >= 9 && held <= 11, "Halten trifft einmal pro Intervall (" + held + " Treffer in 5 s)");
            Check(tapped <= held, "Antippen trifft nicht haeufiger als Halten (" + tapped + " gegen " + held + ")");
            var first = new object[] { 0f, 10f, .5f, false };
            Check(!(bool)hitMethod.Invoke(null, first) && Mathf.Approximately((float)first[0], 10.25f), "Nach einer Pause kommt der erste Treffer nach einem halben Intervall");
            var cooldown = new object[] { 10.5f, 10.1f, .5f, false };
            Check(!(bool)hitMethod.Invoke(null, cooldown) && Mathf.Approximately((float)cooldown[0], 10.5f), "Neuer Druck kuerzt die laufende Abklingzeit nicht");

            // Deep ores take their layer from the host stone; shallow ores keep their own dig sound.
            var soundMethod = typeof(TileMiner).GetMethod("GetMiningHitSound", HiddenStatic);
            Check(soundMethod != null, "Trefferklang-Auswahl als testbare Hilfsmethode vorhanden");
            var layers = new MapLayer[4];
            for (int i = 0; i < layers.Length; i++) { var stone = Asset<Block>(); stone.digSound = SoundType.DigMedium; layers[i] = new MapLayer { stone = stone }; }
            var oreBlock = Asset<Block>(); oreBlock.digSound = SoundType.DigOre;
            SoundType Sound(Block block, bool isOre, Block substrate, out int layer)
            {
                var args = new object[] { layers, block, isOre, substrate, 0 };
                var sound = (SoundType)soundMethod.Invoke(null, args); layer = (int)args[4]; return sound;
            }
            Check(Sound(oreBlock, true, layers[2].stone, out int layer) == SoundType.DigDeepOreHit && layer == 2, "Erz in Schicht 3 spielt PickaxeHitL3Plus mit Schichtabmischung");
            Check(Sound(oreBlock, true, layers[3].stone, out layer) == SoundType.DigDeepOreHit && layer == 3, "Erz in Schicht 4 spielt PickaxeHitL3Plus");
            Check(Sound(oreBlock, true, layers[1].stone, out layer) == SoundType.DigOre && layer == -1, "Erz in Schicht 2 behaelt seinen bisherigen Klang");
            Check(Sound(oreBlock, true, layers[0].stone, out layer) == SoundType.DigOre && layer == -1, "Erz in Schicht 1 behaelt seinen bisherigen Klang");
            Check(Sound(oreBlock, true, null, out layer) == SoundType.DigOre && layer == -1, "Erz ohne bekanntes Gestein behaelt seinen bisherigen Klang");
            Check(Sound(layers[2].stone, false, null, out layer) == SoundType.DigDeepStone && layer == 2, "Tiefer Stein klingt unveraendert");
            Check(Sound(layers[0].stone, false, null, out layer) == SoundType.DigMedium && layer == 0, "Flacher Stein klingt unveraendert");
            Check(Sound(null, false, null, out layer) == SoundType.DigMedium && layer == -1, "Fehlender Block klingt unveraendert");

            // A final hit whose drop no longer fits keeps the block and stops the mining energy drain.
            AudioManager.Instance = null;
            Check(inventoryField != null, "Inventar-Instanz fuer den Test austauschbar");
            inventoryField.SetValue(null, null);
            var terrain = Child("Terrain").AddComponent<Tilemap>();
            var drop = Asset<ItemSO>();
            var heavyOre = Asset<Block>(); heavyOre.itemDrop = drop; heavyOre.hardness = 1f;
            var tile = Asset<StoneTestTile>(); tile.block = heavyOre; tile.colliderType = Tile.ColliderType.None;
            var cell = new Vector3Int(3, -2, 0);
            terrain.SetTile(cell, tile);
            var registry = Asset<BlockRegistry>();
            var minerObject = Child("Miner");
            minerObject.AddComponent<BoxCollider2D>();
            var stats = minerObject.AddComponent<StatsManager>();
            var miner = minerObject.AddComponent<TileMiner>();
            typeof(TileMiner).GetField("tilemap", Hidden).SetValue(miner, terrain);
            typeof(TileMiner).GetField("stats", Hidden).SetValue(miner, stats);
            typeof(TileMiner).GetField("blockRegistry", Hidden).SetValue(miner, registry);
            var progress = (Dictionary<Vector3Int, float>)typeof(TileMiner).GetField("progress", Hidden).GetValue(miner);
            progress[cell] = .99f;
            typeof(TileMiner).GetField("mining", Hidden).SetValue(miner, true);
            float mineTime = (float)typeof(TileMiner).GetMethod("GetTargetMineTime", Hidden).Invoke(miner, new object[] { cell });
            Check(mineTime > 0f && !float.IsInfinity(mineTime), "Abbaudauer des Testblocks ist gueltig (" + mineTime + " s)");
            var previousHit = TileMiner.OnBlockHit; var previousMined = TileMiner.OnBlockMined;
            bool hitRaised = false, minedRaised = false;
            TileMiner.OnBlockHit = _ => hitRaised = true; TileMiner.OnBlockMined = (_, _) => minedRaised = true;
            try
            {
                typeof(TileMiner).GetMethod("ApplyMiningHit", Hidden).Invoke(miner, new object[] { cell, mineTime });
                Check(terrain.HasTile(cell) && !minedRaised, "Zu schwere Beute laesst den Block stehen");
                Check(Mathf.Approximately(progress[cell], .99f) && !hitRaised, "Fortschritt bleibt knapp vor dem Abbau stehen");
                Check(miner.IsMining, "Spitzhacke schwingt weiter");
                Check(!miner.IsMingin, "Volle Traglast verbraucht keine Abbauenergie");
                Check((Vector3Int?)typeof(TileMiner).GetField("stalledMiningCell", Hidden).GetValue(miner) == cell, "Blockierte Zelle wird gemerkt");
                typeof(TileMiner).GetMethod("ClearMiningProgress", Hidden).Invoke(miner, null);
                Check(typeof(TileMiner).GetField("stalledMiningCell", Hidden).GetValue(miner) == null, "Neue Karte setzt die Blockade zurueck");
            }
            finally { TileMiner.OnBlockHit = previousHit; TileMiner.OnBlockMined = previousMined; }

            // Dragging a hotbar item keeps the pickaxe selected but must block mining.
            var hudObject = Child("Hud");
            var hud = hudObject.AddComponent<CompactHud>();
            var blocks = typeof(TileMiner).GetMethod("HotbarBlocksMining", HiddenStatic);
            Check(blocks != null, "Hotbar-Sperre als testbare Hilfsmethode vorhanden");
            bool Blocks(CompactHud value) => (bool)blocks.Invoke(null, new object[] { value });
            var dragged = typeof(CompactHud).GetField("draggedSlot", Hidden);
            Check(!Blocks(null) && !Blocks(hud) && hud.SelectedSlot == 0, "Gewaehlte Spitzhacke ohne Ziehen darf abbauen");
            dragged.SetValue(hud, 2);
            Check(hud.IsDraggingItem && Blocks(hud), "Ziehen eines Hotbar-Items sperrt den Abbau");
            dragged.SetValue(hud, -1);
            Check(!hud.IsDraggingItem && !Blocks(hud), "Nach dem Ziehen wird wieder abgebaut");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/BughuntTileMinerChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/BughuntTileMinerChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw;
        }
        finally
        {
            AudioManager.Instance = audio;
            inventoryField?.SetValue(null, inventory);
            Object.DestroyImmediate(root);
            foreach (var asset in created) if (asset) Object.DestroyImmediate(asset);
        }
    }
}
