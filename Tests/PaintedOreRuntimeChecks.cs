using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;

// Painted ore tiles (all fourteen ores incl. glowing ultronium; Small/Medium/Large): generation, all host rocks, overhang clipping at mined cells, mining and save/load.
public static class PaintedOreRuntimeChecks
{
    public static string Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("Requires a fresh Play start at the homescreen");
        if(GameSaveSystem.TestDirectory!=null || MetaProgression.TestDirectory!=null)
            throw new Exception("Another test owns the save scope");
        var root=new GameObject("Copper Large painted runtime checks");Object.DontDestroyOnLoad(root);
        root.AddComponent<PaintedOreRuntimeProbe>();return "Started; report Temp/PaintedOreRuntimeChecks.txt";
    }
}

public sealed class PaintedOreRuntimeProbe:MonoBehaviour
{
    const string Report="Temp/PaintedOreRuntimeChecks.txt";
    const string Output="Design/OreVeins/PaintedOres/Runtime";
    const float Marker=254f/255f, GlowMarker=253f/255f; // lit painted ore, lit and pulsing (ultronium)
    static bool IsMarker(float a)=>Mathf.Abs(a-Marker)<.001f || Mathf.Abs(a-GlowMarker)<.001f;
    string previousSave,previousMeta,directory;bool restored,previousBackground;
    void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(Report,"PASS "+message+"\n");}
    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/PaintedOreRun-"+DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);GameSaveSystem.TestDirectory=directory;
        MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        File.WriteAllText(Report,"Isolated save directory: "+directory+"\n");
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            bool more=false;object current=null;Exception error=null;
            try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){error=e;}
            if(error!=null)
            {
                File.AppendAllText(Report,"FAIL "+error);RunNavigation.MainMenu();
                while(RunNavigation.IsTransitioning)yield return null;
                Cleanup();yield break;
            }
            if(!more){stack.Pop();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        File.AppendAllText(Report,"ALL PASSED\n");Cleanup();
    }
    IEnumerator Ready()
    {
        float deadline=Time.realtimeSinceStartup+150;
        while(RunNavigation.IsTransitioning || LoadingProgress.Active)
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Loading timeout");yield return null;}
        yield return null;
    }
    void CameraCheck(string phase)
    {
        var player=Object.FindFirstObjectByType<PlayerMovement>();var view=Camera.main;
        Check(view && player && view.GetComponent<CameraFollow>().enabled &&
            view.GetComponent<CameraFollow>().target==player.transform &&
            view.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,
            phase+": player follow and world clamp active");
    }
    static bool Painted(Tilemap tiles,Vector3Int cell)=>IsMarker(tiles.GetColor(cell).a);
    IEnumerator Run()
    {
        RunNavigation.NewGame("Erz-Test");yield return Ready();
        Check(GameSaveSystem.ActiveSlot>0 && GameSaveSystem.InitialSaveError==null,"Fresh isolated run saved");
        CameraCheck("Fresh start");
        var map=Object.FindFirstObjectByType<MapGenerator>();
        var copper=map.registry.GetById(BlockType.CopperOre);var iron=map.registry.GetById(BlockType.IronOre);var silver=map.registry.GetById(BlockType.SilverOre);var gold=map.registry.GetById(BlockType.GoldOre);var coal=map.registry.GetById(BlockType.Coal);var platinum=map.registry.GetById(BlockType.PlatinumOre);var titanium=map.registry.GetById(BlockType.TitaniumOre);var tungsten=map.registry.GetById(BlockType.TungstenOre);var mythril=map.registry.GetById(BlockType.MythrilOre);var garnet=map.registry.GetById(BlockType.OrangeGarnetOre);var emerald=map.registry.GetById(BlockType.EmeraldOre);var ruby=map.registry.GetById(BlockType.RubyOre);var diamond=map.registry.GetById(BlockType.DiamondOre);var ultronium=map.registry.GetById(BlockType.UltroniumOre);
        foreach(var block in new[]{copper,iron,silver,gold,coal,platinum,titanium,tungsten,mythril,garnet,emerald,ruby,diamond,ultronium})
        {
            Check(block.smallOre.Length==2 && block.mediumOre.Length==2 && block.richOre.Length==3,block.displayName+" keeps 2 Small, 2 Medium and 3 Large variants");
            foreach(var (tiles,richness,suffix) in new[]{(block.smallOre,OreRichness.Small,"_small"),(block.mediumOre,OreRichness.Medium,"_medium"),(block.richOre,OreRichness.Rich,"_large")})
                foreach(var tile in tiles)
                    Check(tile.name.EndsWith(suffix) && Mathf.Abs(tile.color.a-(block.id==BlockType.UltroniumOre?GlowMarker:Marker))<.001f && tile.richness==richness,
                        tile.name+" is a painted "+richness+" tile");
            // The freshly generated world must already use painted tiles for every cell of this ore.
            var bounds=map.OreOverlay.cellBounds;var generated=new int[3];var painted=new int[3];
            foreach(var cell in bounds.allPositionsWithin)
            {
                var ore=map.OreOverlay.GetTile<OreTile>(cell);
                if(!ore || ore.block!=block)continue;
                int r=(int)ore.richness;generated[r]++;
                if(Array.IndexOf(block.GetOreVariants(ore.richness),ore)>=0 && Painted(map.OreOverlay,cell))painted[r]++;
            }
            for(int r=0;r<3;r++)
                // Rare ores (e.g. mythril) may not appear in a test world; the placed test cells below cover them.
                Check(painted[r]==generated[r],"Generated world: "+painted[r]+"/"+generated[r]+" "+(OreRichness)r+" "+block.displayName+" cells painted");
        }

        var player=Object.FindFirstObjectByType<PlayerMovement>();
        int playerX=map.Terrain.WorldToCell(player.transform.position).x;
        var hostTiles=new TileBase[]{map.surfaceDirtTile,map.layerOneTile,map.uniformTestTile,map.layerThreeTile,map.layerFourTile};
        for(int x=playerX-8;x<playerX+8;x++)for(int y=-24;y<-7;y++)
        {
            var cell=new Vector3Int(x,y,0);map.Terrain.SetTile(cell,hostTiles[Mathf.Min(4,(x-playerX+8)/3)]);
            map.OreOverlay.SetTile(cell,null);map.ArtifactOverlay?.SetTile(cell,null);
        }
        var rich=new List<Vector3Int>();var airNeighbours=new List<Vector3Int>();var placed=new Dictionary<Vector3Int,OreTile>();
        void Place(Vector3Int cell,OreTile ore)
        {
            map.OreOverlay.SetTile(cell,ore);map.OreOverlay.SetTileFlags(cell,TileFlags.None);placed[cell]=ore;
            map.OreOverlay.SetTransformMatrix(cell,ore.transform);
        }
        for(int h=0;h<5;h++)
        {
            int bx=playerX-8+h*3;
            var single=new Vector3Int(bx+1,-18,0);Place(single,copper.richOre[h%3]);rich.Add(single);
            airNeighbours.Add(new Vector3Int(bx+2,-18,0));
            var a=new Vector3Int(bx,-14,0);var b=new Vector3Int(bx+1,-14,0);
            Place(a,copper.richOre[(h+1)%3]);Place(b,copper.richOre[(h+2)%3]);rich.Add(a);rich.Add(b);
            var c=new Vector3Int(bx+1,-11,0);Place(c,copper.richOre[h%3]);rich.Add(c);
            var s=new Vector3Int(bx,-11,0);var m=new Vector3Int(bx+2,-11,0);
            Place(s,copper.smallOre[h%2]);Place(m,copper.mediumOre[(h+1)%2]);rich.Add(s);rich.Add(m);
            // Iron row: Small, Large, Medium.
            var fs=new Vector3Int(bx,-16,0);var fl=new Vector3Int(bx+1,-16,0);var fm=new Vector3Int(bx+2,-16,0);
            Place(fs,iron.smallOre[h%2]);Place(fl,iron.richOre[h%3]);Place(fm,iron.mediumOre[(h+1)%2]);rich.Add(fs);rich.Add(fl);rich.Add(fm);
            // Silver row: Medium, Small, Large.
            var ss=new Vector3Int(bx,-9,0);var sl=new Vector3Int(bx+1,-9,0);var sm=new Vector3Int(bx+2,-9,0);
            Place(ss,silver.mediumOre[h%2]);Place(sl,silver.smallOre[(h+1)%2]);Place(sm,silver.richOre[h%3]);rich.Add(ss);rich.Add(sl);rich.Add(sm);
            // Gold row: Large, Medium, Small.
            var gl=new Vector3Int(bx,-13,0);var gm=new Vector3Int(bx+1,-12,0);var gs=new Vector3Int(bx+2,-13,0);
            Place(gl,gold.richOre[(h+2)%3]);Place(gm,gold.mediumOre[h%2]);Place(gs,gold.smallOre[(h+1)%2]);rich.Add(gl);rich.Add(gm);rich.Add(gs);
            // Coal row: Large, Small, Medium.
            var kl=new Vector3Int(bx,-20,0);var ks=new Vector3Int(bx+1,-20,0);var km=new Vector3Int(bx+2,-20,0);
            Place(kl,coal.richOre[h%3]);Place(ks,coal.smallOre[h%2]);Place(km,coal.mediumOre[(h+1)%2]);rich.Add(kl);rich.Add(ks);rich.Add(km);
            // Platinum row: Medium, Large, Small.
            var pm=new Vector3Int(bx,-15,0);var pl=new Vector3Int(bx+1,-15,0);var ps=new Vector3Int(bx+2,-15,0);
            Place(pm,platinum.mediumOre[h%2]);Place(pl,platinum.richOre[(h+1)%3]);Place(ps,platinum.smallOre[(h+1)%2]);rich.Add(pm);rich.Add(pl);rich.Add(ps);
            // Titanium row: Small, Medium, Large.
            var ts=new Vector3Int(bx,-17,0);var tm=new Vector3Int(bx+1,-17,0);var tl=new Vector3Int(bx+2,-17,0);
            Place(ts,titanium.smallOre[h%2]);Place(tm,titanium.mediumOre[(h+1)%2]);Place(tl,titanium.richOre[(h+2)%3]);rich.Add(ts);rich.Add(tm);rich.Add(tl);
            // Tungsten row: Large, Small, Medium; mythril row: Medium, Large, Small.
            var wl=new Vector3Int(bx,-19,0);var ws=new Vector3Int(bx+1,-19,0);var wm=new Vector3Int(bx+2,-19,0);
            Place(wl,tungsten.richOre[h%3]);Place(ws,tungsten.smallOre[h%2]);Place(wm,tungsten.mediumOre[(h+1)%2]);rich.Add(wl);rich.Add(ws);rich.Add(wm);
            var mm=new Vector3Int(bx,-10,0);var ml=new Vector3Int(bx+1,-10,0);var ms=new Vector3Int(bx+2,-10,0);
            Place(mm,mythril.mediumOre[h%2]);Place(ml,mythril.richOre[(h+1)%3]);Place(ms,mythril.smallOre[(h+1)%2]);rich.Add(mm);rich.Add(ml);rich.Add(ms);
            // Garnet row: Small, Large, Medium; emerald row: Large, Medium, Small.
            var gs2=new Vector3Int(bx,-12,0);var gl2=new Vector3Int(bx+2,-12,0);var gm2=new Vector3Int(bx+2,-14,0);
            Place(gs2,garnet.smallOre[h%2]);Place(gl2,garnet.richOre[(h+1)%3]);Place(gm2,garnet.mediumOre[(h+1)%2]);rich.Add(gs2);rich.Add(gl2);rich.Add(gm2);
            var el=new Vector3Int(bx,-21,0);var em=new Vector3Int(bx+1,-21,0);var es=new Vector3Int(bx+2,-21,0);
            Place(el,emerald.richOre[h%3]);Place(em,emerald.mediumOre[(h+1)%2]);Place(es,emerald.smallOre[h%2]);rich.Add(el);rich.Add(em);rich.Add(es);
            // Ruby row: Medium, Small, Large; diamond row: Small, Large, Medium.
            var rm=new Vector3Int(bx,-22,0);var rs=new Vector3Int(bx+1,-22,0);var rl=new Vector3Int(bx+2,-22,0);
            Place(rm,ruby.mediumOre[h%2]);Place(rs,ruby.smallOre[(h+1)%2]);Place(rl,ruby.richOre[(h+1)%3]);rich.Add(rm);rich.Add(rs);rich.Add(rl);
            var ds=new Vector3Int(bx,-23,0);var dl=new Vector3Int(bx+1,-23,0);var dm=new Vector3Int(bx+2,-23,0);
            Place(ds,diamond.smallOre[h%2]);Place(dl,diamond.richOre[h%3]);Place(dm,diamond.mediumOre[(h+1)%2]);rich.Add(ds);rich.Add(dl);rich.Add(dm);
            // Ultronium row: Large, Medium, Small.
            var ul=new Vector3Int(bx,-8,0);var um=new Vector3Int(bx+1,-8,0);var us=new Vector3Int(bx+2,-8,0);
            Place(ul,ultronium.richOre[(h+2)%3]);Place(um,ultronium.mediumOre[h%2]);Place(us,ultronium.smallOre[(h+1)%2]);rich.Add(ul);rich.Add(um);rich.Add(us);
        }
        foreach(var cell in rich)Check(Painted(map.OreOverlay,cell),"Placed painted tile keeps its marker at "+cell);
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        Capture(map,new Vector3Int(playerX,-14,0),"Painted-all-hosts");

        var miner=Object.FindFirstObjectByType<TileMiner>();
        var hit=typeof(TileMiner).GetMethod("ApplyMiningHit",BindingFlags.NonPublic|BindingFlags.Instance);
        void Mine(Vector3Int cell){int hits=0;while(map.Terrain.HasTile(cell) && hits++<100)hit.Invoke(miner,new object[]{cell,.5f});}
        // Hard host rock needs a better pickaxe; open the neighbouring cells directly to test overhang clipping.
        foreach(var cell in airNeighbours){map.Terrain.SetTile(cell,null);map.OreOverlay.SetTile(cell,null);Check(!map.Terrain.HasTile(cell),"Neighbour of a Large tile opened at "+cell);}
        Check(map.GetOreAt(rich[0])!=null && map.GetOreAt(rich[0]).richness==OreRichness.Rich,"Large tile survives mining its neighbour");
        var minedRich=rich[1];Mine(minedRich);
        Check(!map.Terrain.HasTile(minedRich) && !map.OreOverlay.HasTile(minedRich),"Mining a Large tile removes terrain and ore");
        Check(map.GetOreAt(rich[2])!=null && Painted(map.OreOverlay,rich[2]),"Paired Large neighbour keeps identity and marker");
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        Capture(map,new Vector3Int(playerX,-14,0),"Painted-mined");
        CameraCheck("After mining");

        int slot=GameSaveSystem.ActiveSlot;bool saved=false;string saveError=null;
        yield return GameSaveSystem.Save(slot,this,(ok,error)=>{saved=ok;saveError=error;});
        Check(saved,"Modified world saved: "+saveError);
        RunNavigation.MainMenu();yield return Ready();
        Check(RunNavigation.LoadGame(slot,out string loadError),"Isolated save loads: "+loadError);yield return Ready();
        map=Object.FindFirstObjectByType<MapGenerator>();
        foreach(var cell in rich)
        {
            if(cell==minedRich){Check(!map.OreOverlay.HasTile(cell),"Mined Large cell stays empty after loading");continue;}
            var ore=map.GetOreAt(cell);
            Check(ore==placed[cell] && Painted(map.OreOverlay,cell),ore.name+" and marker restored at "+cell);
        }
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        Capture(map,new Vector3Int(playerX,-14,0),"Painted-restored");
        CameraCheck("After restore");
        RunNavigation.MainMenu();yield return Ready();Check(MainMenuController.IsVisible,"Returned to homescreen");
    }
    void Capture(MapGenerator map,Vector3Int centre,string name)
    {
        Directory.CreateDirectory(Output);
        var view=new GameObject("Copper Large verification camera",typeof(Camera)).GetComponent<Camera>();
        view.CopyFrom(Camera.main);view.enabled=false;view.orthographic=true;view.orthographicSize=6.6f;
        view.transform.position=map.Terrain.GetCellCenterWorld(centre)+Vector3.back*10;
        var lights=Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None);
        var states=new bool[lights.Length];for(int n=0;n<lights.Length;n++){states[n]=lights[n].enabled;lights[n].enabled=false;}
        var lamp=new GameObject("Copper verification lamp",typeof(Light2D)).GetComponent<Light2D>();
        lamp.lightType=Light2D.LightType.Global;lamp.intensity=1;
        var darkness=map.transform.Find("Daylight Overlay")?.GetComponent<MeshRenderer>();
        bool darknessEnabled=darkness && darkness.enabled;
        var target=new RenderTexture(1280,960,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(1280,960,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
        try
        {
            if(darkness)darkness.enabled=false;
            view.targetTexture=target;map.GetComponent<OreOverlayAppearance>().PrepareVeins(view);view.Render();
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,960),0,0);pixels.Apply();
            File.WriteAllBytes(Output+"/"+name+".png",pixels.EncodeToPNG());
            int visible=0;foreach(var c in pixels.GetPixels32())if(c.r>30 || c.g>30 || c.b>30)visible++;
            Check(visible>pixels.width*pixels.height*.7,"Captured actual gameplay scene "+name);
        }
        finally
        {
            view.targetTexture=null;RenderTexture.active=previous;if(darkness)darkness.enabled=darknessEnabled;
            lamp.enabled=false;for(int n=0;n<lights.Length;n++)if(lights[n])lights[n].enabled=states[n];
            Object.Destroy(view.gameObject);Object.Destroy(lamp.gameObject);Object.Destroy(target);Object.Destroy(pixels);
        }
    }
    void Cleanup()
    {
        if(restored)return;restored=true;
        if(GameSaveSystem.TestDirectory==directory)GameSaveSystem.TestDirectory=previousSave;
        if(directory!=null && MetaProgression.TestDirectory==Path.Combine(directory,"Profile"))MetaProgression.TestDirectory=previousMeta;
        Application.runInBackground=previousBackground;Object.Destroy(gameObject);
    }
    void OnDestroy(){if(!restored)Cleanup();}
}
