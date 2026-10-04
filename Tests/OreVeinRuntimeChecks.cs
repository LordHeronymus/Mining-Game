using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public static class OreVeinRuntimeChecks
{
    public static string Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("A free editor at the homescreen in Play Mode is required");
        if (GameSaveSystem.TestDirectory != null || MetaProgression.TestDirectory != null)
            throw new Exception("Another test owns the save/profile scope");
        var root = new GameObject("Ore vein runtime checks");
        Object.DontDestroyOnLoad(root); root.AddComponent<OreVeinRuntimeProbe>();
        return "Started; report Temp/OreVeinRuntimeChecks.txt";
    }
}

public sealed class OreVeinRuntimeProbe : MonoBehaviour
{
    const string Report = "Temp/OreVeinRuntimeChecks.txt";
    string previousSave, previousMeta, directory;
    bool restored, previousBackground;
    void Check(bool value,string message)
    { if(!value)throw new Exception(message); File.AppendAllText(Report,"PASS "+message+"\n"); }

    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory; previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/OreVeinRun-"+DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);GameSaveSystem.TestDirectory=directory;
        MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        File.WriteAllText(Report,"Isolated save directory: "+directory+"\n");
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            if(GameSaveSystem.TestDirectory!=directory)
            { File.AppendAllText(Report,"INTERRUPTED: save scope changed\n");Cleanup();yield break; }
            bool more=false;object current=null;Exception error=null;
            try { more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current; }
            catch(Exception e){error=e;}
            if(error!=null)
            {
                File.AppendAllText(Report,"FAIL "+error);
                // Keep the isolated paths until the failed test world is gone.
                RunNavigation.MainMenu();
                float deadline=Time.realtimeSinceStartup+30;
                while(RunNavigation.IsTransitioning && Time.realtimeSinceStartup<deadline)yield return null;
                if(!MainMenuController.IsVisible)
                {
                    File.AppendAllText(Report,"\nMenu cleanup timed out; isolated paths retained until Play stops\n");
                    yield break;
                }
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
        { if(Time.realtimeSinceStartup>deadline)throw new Exception("Loading timed out");yield return null; }
        yield return null;
    }

    void CameraCheck(string phase)
    {
        var player=Object.FindFirstObjectByType<PlayerMovement>();var camera=Camera.main;
        Check(camera && player && camera.GetComponent<CameraFollow>().enabled &&
            camera.GetComponent<CameraFollow>().target==player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,
            phase+": player follow and world clamp active");
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Erzadern-Test");yield return Ready();
        Check(GameSaveSystem.ActiveSlot>0 && GameSaveSystem.InitialSaveError==null,"Fresh isolated run created and saved");
        CameraCheck("Fresh start");
        var map=Object.FindFirstObjectByType<MapGenerator>();
        var visual=map.GetComponent<OreOverlayAppearance>();var camera=Camera.main;
        Check(visual && visual.isActiveAndEnabled,"Ore appearance is active after fresh generation");
        visual.PrepareVeins(camera);
        var props=new MaterialPropertyBlock();map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        Check(props.GetFloat("_VeinEnabled")==1 && props.GetTexture("_VeinCells"),"Real camera binds connected vein lookup");
        var origin=map.Terrain.WorldToCell(Object.FindFirstObjectByType<PlayerMovement>().transform.position);
        var cell=new Vector3Int(origin.x,-6,0);
        var ore=map.registry.GetById(BlockType.CopperOre).richOre[0];
        map.Terrain.SetTile(cell,map.uniformTestTile);
        map.Terrain.SetTile(cell+Vector3Int.right,map.uniformTestTile);
        map.OreOverlay.SetTile(cell,ore);map.OreOverlay.SetTile(cell+Vector3Int.right,ore);
        yield return null;
        visual.PrepareVeins(camera);
        using(var field=new OreVeinField())
        {
            Check(field.Prepare(map,camera) && field.TypeAt(cell)==(int)BlockType.CopperOre,"Live field detects added ore pair");
            int before=field.Revision;
            for(int n=0;n<120;n++)field.Prepare(map,camera);
            Check(field.Revision==before,"120 unchanged frames reuse metadata without rebuilding");
        }
        var miner=Object.FindFirstObjectByType<TileMiner>();
        var hit=typeof(TileMiner).GetMethod("ApplyMiningHit",BindingFlags.Instance|BindingFlags.NonPublic);
        int count=0;
        while(map.Terrain.HasTile(cell) && count++<100)hit.Invoke(miner,new object[]{cell,.5f});
        Check(!map.Terrain.HasTile(cell) && !map.OreOverlay.HasTile(cell),"Actual pickaxe hits remove terrain and ore");
        yield return null;visual.PrepareVeins(camera);
        map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        var bounds=props.GetVector("_VeinBounds");var texture=(Texture2D)props.GetTexture("_VeinCells");
        Check(texture.GetPixel(cell.x-(int)bounds.x,cell.y-(int)bounds.y).r==0,"Mined cell clears GPU connection field");
        Check(map.GetOreAt(cell+Vector3Int.right)==ore,"Neighbour ore and richness remain intact");
        CameraCheck("After mining");
        int slot=GameSaveSystem.ActiveSlot;
        RunNavigation.MainMenu();yield return Ready();
        Check(RunNavigation.LoadGame(slot,out string loadError),"Load isolated original save: "+loadError);yield return Ready();
        map=Object.FindFirstObjectByType<MapGenerator>();visual=map.GetComponent<OreOverlayAppearance>();
        visual.PrepareVeins(Camera.main);map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        Check(props.GetFloat("_VeinEnabled")==1 && props.GetTexture("_VeinCells"),"Save restore reconstructs visuals without new save fields");
        CameraCheck("After restore");
        RunNavigation.MainMenu();yield return Ready();
        Check(MainMenuController.IsVisible,"Returned to homescreen");
    }

    void Cleanup()
    {
        if(restored)return;restored=true;
        if(GameSaveSystem.TestDirectory==directory)
        { GameSaveSystem.TestDirectory=previousSave;MetaProgression.TestDirectory=previousMeta; }
        Application.runInBackground=previousBackground;Object.Destroy(gameObject);
    }
    void OnDestroy(){if(!restored)Cleanup();}
}
