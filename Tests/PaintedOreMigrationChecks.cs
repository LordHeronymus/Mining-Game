using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;

// Old saves with vein ore tiles (_01.._07) load with the painted tiles of the same ore and size (PaintedOreMigration).
// Builds an old-style world in an isolated profile, saves, loads twice, saves again and loads once more.
public static class PaintedOreMigrationChecks
{
    public static string Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("Requires a fresh Play start at the homescreen");
        if(GameSaveSystem.TestDirectory!=null || MetaProgression.TestDirectory!=null)
            throw new Exception("Another test owns the save scope");
        var root=new GameObject("Painted ore migration checks");Object.DontDestroyOnLoad(root);
        root.AddComponent<PaintedOreMigrationProbe>();return "Started; report Temp/PaintedOreMigrationChecks.txt";
    }
}

public sealed class PaintedOreMigrationProbe:MonoBehaviour
{
    const string Report="Temp/PaintedOreMigrationChecks.txt";
    string previousSave,previousMeta,directory;bool restored,previousBackground;
    void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(Report,"PASS "+message+"\n");}
    void Note(string message)=>File.AppendAllText(Report,"INFO "+message+"\n");
    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/PaintedOreMigration-"+DateTime.UtcNow.Ticks);
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
    static bool Current(OreTile tile)=>tile.block && (Array.IndexOf(tile.block.smallOre,tile)>=0 ||
        Array.IndexOf(tile.block.mediumOre,tile)>=0 || Array.IndexOf(tile.block.richOre,tile)>=0);
    static Dictionary<Vector3Int,OreTile> Snapshot(Tilemap tiles)
    {
        var result=new Dictionary<Vector3Int,OreTile>();
        foreach(var cell in tiles.cellBounds.allPositionsWithin){var ore=tiles.GetTile<OreTile>(cell);if(ore)result[cell]=ore;}
        return result;
    }
    void VerifyLoaded(Tilemap tiles,Dictionary<Vector3Int,OreTile> expected,string phase)
    {
        var loaded=Snapshot(tiles);
        Check(loaded.Count==expected.Count,phase+": "+loaded.Count+"/"+expected.Count+" ore cells at the same positions");
        int wrong=0,unmarked=0;string first=null;
        foreach(var pair in expected)
        {
            loaded.TryGetValue(pair.Key,out var ore);
            if(ore!=pair.Value){wrong++;first??=pair.Key+": "+(ore?ore.name:"none")+" instead of "+pair.Value.name;continue;}
            var color=tiles.GetColor(pair.Key);
            if(Mathf.Abs(color.a-ore.color.a)>.0005f || tiles.GetTileFlags(pair.Key)!=ore.flags || tiles.GetTransformMatrix(pair.Key)!=ore.transform)
            {unmarked++;first??=pair.Key+": "+ore.name+" alpha "+color.a+" flags "+tiles.GetTileFlags(pair.Key);}
        }
        Check(wrong==0,phase+": every cell holds the expected tile ("+wrong+" wrong; "+first+")");
        Check(unmarked==0,phase+": every cell has the painted marker, flags and transform ("+unmarked+" wrong; "+first+")");
    }
    IEnumerator Run()
    {
        RunNavigation.NewGame("Erz-Migration");yield return Ready();
        Check(GameSaveSystem.ActiveSlot>0 && GameSaveSystem.InitialSaveError==null,"Fresh isolated run saved");
        CameraCheck("Fresh start");
        var map=Object.FindFirstObjectByType<MapGenerator>();var ores=map.OreOverlay;
        var generated=Snapshot(ores);
        Check(generated.Count>500 && generated.Values.All(Current),"Fresh world: "+generated.Count+" ore cells, all current painted tiles");

        // Old tiles from the save catalog, grouped by ore and size.
        var catalog=Resources.Load<SaveAssetCatalog>("SaveAssetCatalog");
        var old=catalog.tiles.Select(x=>x.tile as OreTile).Where(x=>x && x.block && !Current(x)).Distinct()
            .GroupBy(x=>(x.block,x.richness)).ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.name).ToArray());
        var blocks=generated.Values.Select(x=>x.block).Distinct().ToArray();
        foreach(var block in blocks)foreach(OreRichness r in Enum.GetValues(typeof(OreRichness)))
            if(old.TryGetValue((block,r),out var group))Note(block.displayName+" "+r+": old "+string.Join(",",group.Select(x=>x.name)));
        Check(blocks.All(b=>old.Keys.Any(k=>k.block==b)),"Every generated ore has old catalog tiles: "+string.Join(", ",blocks.Select(b=>b.displayName)));

        // Rebuild an old world: most cells get an old tile of the same ore and size; some keep a different painted variant.
        var expected=new Dictionary<Vector3Int,OreTile>();int replaced=0,kept=0,n=0;
        var perOre=new Dictionary<string,int>();
        foreach(var pair in generated.OrderBy(x=>x.Key.y).ThenBy(x=>x.Key.x))
        {
            var cell=pair.Key;var ore=pair.Value;var variants=ore.block.GetOreVariants(ore.richness);
            if(n++%9==0 && variants.Length>1)
            {
                var other=variants[(Array.IndexOf(variants,ore)+1)%variants.Length];
                ores.SetTile(cell,other);expected[cell]=other;kept++;continue;
            }
            if(!old.TryGetValue((ore.block,ore.richness),out var group)){expected[cell]=ore;continue;}
            var legacy=group[(Math.Abs(cell.x*31+cell.y*17))%group.Length];
            ores.SetTile(cell,legacy);expected[cell]=ore;replaced++;
            perOre[ore.block.displayName]=perOre.TryGetValue(ore.block.displayName,out int c)?c+1:1;
            if(Mathf.Abs(ores.GetColor(cell).a-legacy.color.a)>.0005f)throw new Exception("Old tile colour not applied at "+cell);
        }
        Note("Old cells per ore: "+string.Join(", ",perOre.Select(x=>x.Key+" "+x.Value)));
        Check(replaced>generated.Count/2 && kept>0,"Old-style world: "+replaced+" old tiles, "+kept+" other painted variants kept");
        Check(Snapshot(ores).Values.Count(x=>!Current(x))==replaced,"Tilemap really holds the old tiles before saving");

        // Direct timing of the migration on this layer (the same call PrepareLoad makes).
        var bounds=ores.cellBounds;var cells=ores.GetTilesBlock(bounds);var flags=new TileFlags[cells.Length];
        var custom=new Dictionary<int,(Color color,Matrix4x4 matrix)>();
        var apply=Type.GetType("PaintedOreMigration, Assembly-CSharp").GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        var watch=Stopwatch.StartNew();
        int changed=(int)apply.Invoke(null,new object[]{cells,flags,custom,bounds,map.ActiveSeed,map.GeneratedWidth});
        watch.Stop();
        Check(changed==replaced,"Migration replaces exactly the "+replaced+" old tiles ("+changed+")");
        Note("Migration of "+cells.Length+" cells: "+watch.Elapsed.TotalMilliseconds.ToString("0.00")+" ms");
        Check(watch.Elapsed.TotalMilliseconds<50,"Migration is fast ("+watch.Elapsed.TotalMilliseconds.ToString("0.00")+" ms)");

        int slot=GameSaveSystem.ActiveSlot;bool saved=false;string saveError=null;
        yield return GameSaveSystem.Save(slot,this,(ok,error)=>{saved=ok;saveError=error;});
        Check(saved,"Old-style world saved: "+saveError);

        for(int pass=1;pass<=3;pass++)
        {
            RunNavigation.MainMenu();yield return Ready();
            Check(RunNavigation.LoadGame(slot,out string loadError),"Load "+pass+": isolated save loads "+loadError);yield return Ready();
            map=Object.FindFirstObjectByType<MapGenerator>();
            VerifyLoaded(map.OreOverlay,expected,"Load "+pass);
            CameraCheck("Load "+pass);
            if(pass==2)
            {
                saved=false;yield return GameSaveSystem.Save(slot,this,(ok,error)=>{saved=ok;saveError=error;});
                Check(saved,"Migrated world saved again: "+saveError);
            }
        }
        RunNavigation.MainMenu();yield return Ready();Check(MainMenuController.IsVisible,"Returned to homescreen");
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
