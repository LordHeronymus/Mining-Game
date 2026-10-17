using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;

public static class CopperCombinedRuntimeChecks
{
    public static string Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("Free editor at homescreen required");
        if(GameSaveSystem.TestDirectory!=null || MetaProgression.TestDirectory!=null)
            throw new Exception("Another test owns the save scope");
        var root=new GameObject("Copper earth tile runtime checks");Object.DontDestroyOnLoad(root);
        root.AddComponent<CopperCombinedRuntimeProbe>();return "Started; report Temp/CopperCombinedRuntimeChecks.txt";
    }
}

public sealed class CopperCombinedRuntimeProbe:MonoBehaviour
{
    const string Report="Temp/CopperCombinedRuntimeChecks.txt";
    const string Output="Design/OreVeins/CopperL1Branches/Implemented";
    string previousSave,previousMeta,directory;bool restored,previousBackground;
    void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(Report,"PASS "+message+"\n");}
    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/CopperEarthTileRun-"+DateTime.UtcNow.Ticks);
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
    IEnumerator Run()
    {
        RunNavigation.NewGame("Kupfer-Kacheln-Test");yield return Ready();
        Check(GameSaveSystem.ActiveSlot>0 && GameSaveSystem.InitialSaveError==null,"Fresh isolated run saved");
        CameraCheck("Fresh start");
        var map=Object.FindFirstObjectByType<MapGenerator>();
        var visual=map.GetComponent<OreOverlayAppearance>();visual.PrepareVeins(Camera.main);
        CheckBindings(map,"Fresh generation");
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        int playerX=map.Terrain.WorldToCell(player.transform.position).x;
        var offsets=new[]{new Vector2Int(2,9),new Vector2Int(3,9),new Vector2Int(4,8),new Vector2Int(5,8),
            new Vector2Int(6,7),new Vector2Int(7,7),new Vector2Int(7,8),new Vector2Int(7,9),
            new Vector2Int(6,6),new Vector2Int(7,6),new Vector2Int(8,6),new Vector2Int(9,5),
            new Vector2Int(8,5),new Vector2Int(7,5),new Vector2Int(10,4),new Vector2Int(11,4),
            new Vector2Int(12,3),new Vector2Int(13,3),new Vector2Int(10,3),new Vector2Int(9,2)};
        var copper=map.registry.GetById(BlockType.CopperOre);
        var cells=new List<Vector3Int>();
        for(int x=playerX-8;x<playerX+8;x++)for(int y=-20;y<-8;y++)
        {
            var cell=new Vector3Int(x,y,0);map.Terrain.SetTile(cell,map.surfaceDirtTile);
            map.OreOverlay.SetTile(cell,null);map.ArtifactOverlay?.SetTile(cell,null);
        }
        for(int n=0;n<offsets.Length;n++)
        {
            var cell=new Vector3Int(playerX-8+offsets[n].x,-20+offsets[n].y,0);cells.Add(cell);
            map.OreOverlay.SetTile(cell,n%4==0?copper.smallOre[0]:n%3==0?copper.mediumOre[0]:copper.richOre[0]);
        }
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        Capture(map,new Vector3Int(playerX,-14,0),"Runtime-L1-before");
        var remaining=cells[9];var removed=cells[10];var expected=map.GetOreAt(remaining);
        var miner=Object.FindFirstObjectByType<TileMiner>();
        var hit=typeof(TileMiner).GetMethod("ApplyMiningHit",BindingFlags.NonPublic|BindingFlags.Instance);
        int hits=0;while(map.Terrain.HasTile(removed) && hits++<100)hit.Invoke(miner,new object[]{removed,.5f});
        Check(!map.Terrain.HasTile(removed) && !map.OreOverlay.HasTile(removed),"Real mining removes terrain and copper tile");
        Check(map.GetOreAt(remaining)==expected,"Real mining preserves neighbouring ore identity and richness");
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        Capture(map,new Vector3Int(playerX,-14,0),"Runtime-L1-mined");
        using(var field=new OreVeinField())
        {
            var view=new GameObject("Copper metadata view",typeof(Camera)).GetComponent<Camera>();
            try
            {
                view.enabled=false;view.orthographic=true;view.orthographicSize=6.6f;
                view.transform.position=map.Terrain.GetCellCenterWorld(remaining)+Vector3.back*10;
                field.Prepare(map,view);int revision=field.Revision;
                Check(field.TypeAt(removed)==-1 && field.TypeAt(remaining)==(int)BlockType.CopperOre,"GPU lookup removes mined copper and preserves neighbour");
                for(int n=0;n<60;n++)field.Prepare(map,view);
                Check(field.Revision==revision,"Unchanged views reuse cached metadata");
            }
            finally{Object.Destroy(view.gameObject);}
        }
        CameraCheck("After mining");
        int slot=GameSaveSystem.ActiveSlot;bool saved=false;string saveError=null;
        yield return GameSaveSystem.Save(slot,this,(ok,error)=>{saved=ok;saveError=error;});
        Check(saved,"Modified world saved: "+saveError);
        RunNavigation.MainMenu();yield return Ready();
        Check(RunNavigation.LoadGame(slot,out string error),"Isolated save loads: "+error);yield return Ready();
        map=Object.FindFirstObjectByType<MapGenerator>();CheckBindings(map,"Save restore");
        Check(map.GetOreAt(remaining)==expected,"Copper richness survives save/load");
        Check(!map.Terrain.HasTile(removed) && !map.OreOverlay.HasTile(removed),"Mined cell remains empty after loading");
        Capture(map,new Vector3Int(playerX,-14,0),"Runtime-L1-restored");
        CameraCheck("After restore");
        RunNavigation.MainMenu();yield return Ready();Check(MainMenuController.IsVisible,"Returned to homescreen");
    }
    void CheckBindings(MapGenerator map,string phase)
    {
        map.GetComponent<OreOverlayAppearance>().PrepareVeins(Camera.main);
        var p=new MaterialPropertyBlock();map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(p);
        Check(p.GetFloat("_CopperEarthTilesEnabled")==1 && p.GetTexture("_CopperEarthCombined"),
            phase+": combined copper/earth tiles are bound");
        Check(p.GetFloat("_UseTerrainMasks")<.5f || p.GetTexture("_TerrainEdgeMasks"),
            phase+": active terrain edge masks are bound");
    }
    void Capture(MapGenerator map,Vector3Int centre,string name)
    {
        Directory.CreateDirectory(Output);
        var view=new GameObject("Copper earth verification camera",typeof(Camera)).GetComponent<Camera>();
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
