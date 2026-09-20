using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class OreOverlayChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object Main()
    {
        Check(!Application.isPlaying && !InventoryManager.Instance, "Run ore checks in edit mode without a live inventory");
        // Edit-mode gameplay checks must not call a stale audio singleton left by play mode.
        var audio = AudioManager.Instance;
        AudioManager.Instance = null;
        try { return new[] { Run(BlockType.CopperOre), Run(BlockType.GoldOre), Run(BlockType.SilverOre), Run(BlockType.PlatinumOre), Run(BlockType.IronOre), Run(BlockType.Coal) }; }
        finally { AudioManager.Instance = audio; }
    }

    static object Run(BlockType oreType)
    {
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
        var oreBlock = registry.GetById(oreType);
        var stone = registry.GetById(BlockType.Stone);
        if (oreType == BlockType.PlatinumOre)
        {
            var gold = registry.GetById(BlockType.GoldOre);
            Check(oreBlock.itemDrop.item == Item.Platinum && oreBlock.itemDrop.icon, "Platinum item is incomplete");
            Check(oreBlock.itemDrop.worth == gold.itemDrop.worth * 2, "Platinum must sell for twice gold");
            Check(Mathf.Approximately(oreBlock.hardness, gold.hardness * 1.5f), "Platinum hardness must be 1.5 times gold");
            Check(OreSparkles.IsOre(oreBlock), "Platinum missing from ore particle handling");
            Check(oreBlock.variants.Length == 0, "Platinum must not reuse legacy gold tiles");
            foreach (var tile in gold.variants) Check(registry.FromTile(tile) == gold, "Platinum replaced legacy gold identity");
        }
        Check(oreBlock.smallOre.Length == 2 && oreBlock.mediumOre.Length == 2 && oreBlock.richOre.Length == 3, "Ore art groups incorrect");
        Check(registry.GetById(BlockType.IronOre).HasOreOverlays, "Iron overlay migration missing");
        if (oreType == BlockType.Coal)
        {
            Check(oreBlock.itemDrop.item == Item.Coal && oreBlock.itemDrop.worth == 3 && oreBlock.itemDrop.category == ItemCategory.Ore,
                "Coal item identity, category or price incorrect");
            Check(oreBlock.itemDrop.icon && OreSparkles.IsOre(oreBlock), "Coal icon or particle handling missing");
        }
        foreach (OreRichness r in Enum.GetValues(typeof(OreRichness)))
            foreach (var ore in oreBlock.GetOreVariants(r))
            {
                var s = ore.sprite;
                Check(ore.block == oreBlock && ore.richness == r && ore.colliderType == Tile.ColliderType.None, "Overlay metadata incorrect");
                Check((s.bounds.size - new Vector3(.5f,.5f,.2f)).sqrMagnitude < .0001f, "Sprite block size mismatch");
                Check(Vector2.Distance(s.pivot, s.rect.size * .5f) < .01f, "Sprite pivot mismatch");
                Check(s.texture.mipmapCount > 1 && s.texture.filterMode == FilterMode.Trilinear &&
                    s.texture.format == TextureFormat.RGBA32, "Incorrect texture quality or alpha format");
            }

        var shape = new Block[21 * 21];
        for (int y=0;y<21;y++) for(int x=0;x<21;x++)
            shape[y*21+x] = (x-10)*(x-10) + (y-10)*(y-10) < 80 ? oreBlock : stone;
        var richness = OreVeins.Build(shape,21,21,789);
        Check(richness[10*21+10] == OreRichness.Rich, "Vein center must be rich");
        var levels = new HashSet<OreRichness>();
        for(int y=1;y<20;y++)for(int x=1;x<20;x++)
        {
            int i=y*21+x;if(shape[i]!=oreBlock)continue;
            levels.Add(richness[i]);
            if(shape[i-1]!=oreBlock||shape[i+1]!=oreBlock||shape[i-21]!=oreBlock||shape[i+21]!=oreBlock)
                Check(richness[i]==OreRichness.Small,"A vein boundary is not small");
        }
        Check(levels.Count==3,"A large vein must contain all three richness levels");
        shape[10*21+10]=stone;
        richness=OreVeins.Build(shape,21,21,789);
        Check(richness[10*21+11]==OreRichness.Small,"Internal holes must also form a vein boundary");
        foreach(OreRichness r in Enum.GetValues(typeof(OreRichness)))
        {
            int total=0;
            for(int i=0;i<10000;i++)total+=OreTile.DropCount(r,(i+.5f)/10000);
            Check(total == (r==OreRichness.Small?5000:r==OreRichness.Medium?10000:15000),"Incorrect richness drop expectation");
        }
        Check(OreTile.DropCount(OreRichness.Small,.5f)==0 && OreTile.DropCount(OreRichness.Rich,.5f)==1,"50% boundary incorrect");

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var randomState = UnityEngine.Random.state;
        var originalMined = TileMiner.OnBlockMined;
        GameObject inventoryObject = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Test Grid",typeof(Grid));
            grid.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var terrain=new GameObject("Test Terrain",typeof(Tilemap),typeof(TilemapRenderer),typeof(MapGenerator));
            terrain.transform.SetParent(grid.transform,false);
            var map=terrain.GetComponent<MapGenerator>();map.enabled=false;
            map.registry=registry;map.mapWidth=128;map.mapHeight=128;map.seed=42319;map.randomizeSeed=false;
            terrain.GetComponent<TilemapRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat");
            map.GenerateMap();
            var sampler=new MapGenerationSampler(registry,map.seed,map.mapHeight);
            var bounds=map.Terrain.cellBounds;
            var terrainBefore=map.Terrain.GetTilesBlock(bounds);
            var oreBefore=map.OreOverlay.GetTilesBlock(bounds);
            var matrices=new Dictionary<Vector3Int,Matrix4x4>();
            var oreArt=new HashSet<TileBase>();var baseArt=new HashSet<TileBase>();var angles=new HashSet<int>();
            int oreCount=0;
            foreach(var cell in bounds.allPositionsWithin)
            {
                var ore=map.GetOreAt(cell);if(!ore || ore.block != oreBlock)continue;
                oreCount++;baseArt.Add(map.Terrain.GetTile(cell));oreArt.Add(ore);
                Check(registry.FromTile(map.Terrain.GetTile(cell))==sampler.GetBaseBlock(cell.x+map.mapWidth/2,-cell.y),"Ore has wrong substrate");
                Check(map.GetBlockAt(cell)==oreBlock,"Layered ore resolves as stone");
                var m=map.OreOverlay.GetTransformMatrix(cell);matrices[cell]=m;
                float angle=Mathf.Atan2(m.m10,m.m00)*Mathf.Rad2Deg;
                angles.Add((Mathf.RoundToInt(angle/90)+4)%4);
                Check(Mathf.Abs(angle/90-Mathf.Round(angle/90))<.001f,"Rotation is not a quarter-turn");
                Check(Mathf.Abs(m.MultiplyVector(Vector3.right).magnitude-1)<.001f,"Overlay scale differs from stone");
            }
            Check(oreCount>100 && oreArt.Count==7 && baseArt.Count>=stone.variants.Length && angles.Count==4,"Generation missed art or rotation variants");
            for(int i=0;i<100;i++)UnityEngine.Random.value.ToString();
            map.GenerateMap();
            var terrainAfter=map.Terrain.GetTilesBlock(bounds);var oreAfter=map.OreOverlay.GetTilesBlock(bounds);
            for(int i=0;i<terrainBefore.Length;i++)
                Check(terrainBefore[i]==terrainAfter[i] && oreBefore[i]==oreAfter[i],"Generation depends on global RNG state");
            foreach(var pair in matrices)Check(map.OreOverlay.GetTransformMatrix(pair.Key)==pair.Value,"Rotation changed on regeneration");
            Check(map.OreOverlay.GetComponent<TilemapRenderer>().sharedMaterial==terrain.GetComponent<TilemapRenderer>().sharedMaterial,
                "Stone and ore must use identical lighting");
            var glints=terrain.AddComponent<OreSparkles>();
            typeof(OreSparkles).GetField("map",Private).SetValue(glints,map);
            var canSparkle=typeof(OreSparkles).GetMethod("CanSparkle",Private);
            foreach(var cell in matrices.Keys)
                Check((bool)canSparkle.Invoke(glints,new object[]{cell}),"Layered ore did not sparkle");
            Check(!map.OreOverlay.GetComponent<Collider2D>() && map.OreOverlay.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>().Length==0,
                "Overlay must not add collisions or lights");

            // Exercise the actual save format, including ore identity, rotation and a mined hole.
            var first=new List<Vector3Int>(matrices.Keys)[0];
            var second=new List<Vector3Int>(matrices.Keys)[1];
            Check(map.RemoveBlock(first),"Failed to remove layered ore");
            Check(!map.Terrain.HasTile(first)&&!map.OreOverlay.HasTile(first),"Removal left an orphan layer");
            Check(!(bool)canSparkle.Invoke(glints,new object[]{first}),"Mined ore kept sparkling");
            UnityEngine.Object.DestroyImmediate(glints);
            RoundTrip(map);
            Check(!map.Terrain.HasTile(first)&&!map.OreOverlay.HasTile(first),"Mined cell came back after restore");
            Check(map.GetBlockAt(second)==oreBlock && map.OreOverlay.GetTransformMatrix(second)==matrices[second],"Overlay restore lost identity or rotation");

            // Run the same completion method used by the player's mining timer.
            Check(!InventoryManager.Instance,"Run edit-mode checks without a live player inventory");
            inventoryObject=new GameObject("Test Inventory",typeof(InventoryManager));
            var inventory=inventoryObject.GetComponent<InventoryManager>();
            typeof(InventoryManager).GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,inventory);
            var player=new GameObject("Test Miner",typeof(BoxCollider2D),typeof(AudioSource),typeof(TileMiner));
            var miner=player.GetComponent<TileMiner>();miner.enabled=false;
            typeof(TileMiner).GetField("tilemap",Private).SetValue(miner,map.Terrain);
            typeof(TileMiner).GetField("blockRegistry",Private).SetValue(miner,registry);
            if (oreType == BlockType.PlatinumOre)
            {
                var services = new GameObject("Test Platinum Balance");
                services.SetActive(false);
                var stats = services.AddComponent<StatsManager>();
                var shop = services.AddComponent<ShopManager>();
                typeof(TileMiner).GetField("stats",Private).SetValue(miner,stats);
                var gold = registry.GetById(BlockType.GoldOre);
                var mineTime = typeof(TileMiner).GetMethod("GetTargetMineTime",Private);
                map.Terrain.SetTile(first,stone.variants[0]);
                map.OreOverlay.SetTile(first,gold.mediumOre[0]);
                float goldTime = (float)mineTime.Invoke(miner,new object[]{first});
                map.OreOverlay.SetTile(first,oreBlock.mediumOre[0]);
                float platinumTime = (float)mineTime.Invoke(miner,new object[]{first});
                Check(goldTime > 0 && Mathf.Approximately(platinumTime / goldTime,1.5f),"Actual platinum mining time must be 50% longer");
                Check(shop.GetSellValue(oreBlock.itemDrop,3) == shop.GetSellValue(gold.itemDrop,3) * 2,"Shop platinum sale value incorrect");
                UnityEngine.Object.DestroyImmediate(services);
            }
            int events=0;
            OreRichness currentRichness=OreRichness.Small;
            TileMiner.OnBlockMined=(position,points)=> {
                events++; Check(map.GetBlockAt(map.Terrain.WorldToCell(position))==oreBlock,"Mining event lost ore identity");
                int expected=OreTile.ExpectedDropHalfUnits(currentRichness)*oreBlock.itemDrop.worth*5;
                Check(points==expected,"Mining points must use the richness tier's average drop");
            };
            var totals=new int[3];
            UnityEngine.Random.InitState(123456);
            foreach(OreRichness r in Enum.GetValues(typeof(OreRichness)))
            {
                currentRichness=r;
                int expected=OreTile.ExpectedDropHalfUnits(r)*oreBlock.itemDrop.worth*5;
                Check(registry.GetPoints(oreBlock.GetOreVariants(r)[0])==expected,"Registry points differ from mining points");
                int before=inventory.GetCount(oreBlock.itemDrop);
                for(int n=0;n<1000;n++)
                {
                    map.Terrain.SetTile(first,stone.variants[n%stone.variants.Length]);
                    map.OreOverlay.SetTile(first,oreBlock.GetOreVariants(r)[0]);
                    Check(miner.CompleteMining(first),"Mining transaction failed");
                    Check(!map.GetBlockAt(first)&&!map.OreOverlay.HasTile(first),"Mining failed to remove both layers");
                    Check(!miner.CompleteMining(first),"Double mining paid twice");
                }
                totals[(int)r]=inventory.GetCount(oreBlock.itemDrop)-before;
            }
            Check(events==3000,"Mining event count incorrect");
            if (oreType == BlockType.Coal)
            {
                var shopObject = new GameObject("Test Coal Shop");shopObject.SetActive(false);
                var shop = shopObject.AddComponent<ShopManager>();
                Check(shop.CanSell(oreBlock.itemDrop,3) && shop.GetSellValue(oreBlock.itemDrop,3)==9,"Mined coal cannot be sold for 3 per unit");
                UnityEngine.Object.DestroyImmediate(shopObject);
            }
            Check(Math.Abs(totals[0]-500)<65 && totals[1]==1000 && Math.Abs(totals[2]-1500)<65,"Inventory drop distribution incorrect");
            return new { success=true, oreType=oreType.ToString(), oreCount, sprites=oreArt.Count, stones=baseArt.Count, rotations=angles.Count,
                minedPerTier=1000, smallDrops=totals[0], mediumDrops=totals[1], richDrops=totals[2],
                checks="transparent imports, vein gradient, deterministic graphics, legacy ores, inventory, atomic removal, persistence, shared lighting" };
        }
        finally
        {
            TileMiner.OnBlockMined=originalMined;
            UnityEngine.Random.state=randomState;
            if(inventoryObject)UnityEngine.Object.DestroyImmediate(inventoryObject);
            typeof(InventoryManager).GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(previous);
        }
    }

    static void RoundTrip(MapGenerator map)
    {
        var write=typeof(LastPlayedMap).GetMethod("WriteLayer",BindingFlags.Static|BindingFlags.NonPublic);
        var read=typeof(LastPlayedMap).GetMethod("ReadLayer",BindingFlags.Static|BindingFlags.NonPublic);
        using(var stream=new MemoryStream())
        {
            using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {
                write.Invoke(null,new object[]{writer,map.Terrain});
                write.Invoke(null,new object[]{writer,map.OreOverlay});
            }
            map.Terrain.ClearAllTiles();map.OreOverlay.ClearAllTiles();stream.Position=0;
            using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))
            {
                read.Invoke(null,new object[]{reader,map.Terrain});
                read.Invoke(null,new object[]{reader,map.OreOverlay});
            }
        }
    }
}
